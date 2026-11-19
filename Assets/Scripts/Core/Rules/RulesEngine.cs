using System;
using System.Collections.Generic;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Physics;

namespace Boll.Core.Rules
{
    /// <summary>Оценка удара для «восьмёрки», «девятки», «Американки», «Сибирки» (CONCEPT §3.1–3.4).</summary>
    public sealed partial class RulesEngine
    {
        public const int PyramidWin = 8;
        readonly MapData map;
        public readonly MatchState St;

        public Rng Rng;   // для жребия в переигровке чёрного (необязателен)
        public RulesEngine(MapData map, MatchState state, Rng rng = null) { this.map = map; St = state; Rng = rng; }

        sealed class Analysis
        {
            public int ContactIdx = -1, FirstBall = -1;
            public Vec2 FirstBallPos;
            public List<(int id, int idx, int pocket)> Pocketed = new List<(int, int, int)>();
            public bool PocketAfter, CushionAfter, TwoCushionsOrCross;
            public HashSet<int> CushionBalls = new HashSet<int>();
        }

        // ---------- анализ ----------
        Analysis Analyze(ShotInput s)
        {
            var a = new Analysis();
            var ev = s.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                SolverEvent e = ev[i];
                if (e.Type == EventType.BallBall && a.ContactIdx < 0 && (e.A == s.CueId || e.B == s.CueId))
                {
                    a.ContactIdx = i;
                    bool cueIsA = e.A == s.CueId;
                    a.FirstBall = cueIsA ? e.B : e.A; a.FirstBallPos = cueIsA ? e.Pos2 : e.Pos;
                }
                else if (e.Type == EventType.Pocket) a.Pocketed.Add((e.A, i, e.Index));
                else if (e.Type == EventType.Cushion) a.CushionBalls.Add(e.A);
            }
            if (a.ContactIdx >= 0)
            {
                var keys = new Dictionary<int, HashSet<int>>();
                var crossed = new HashSet<int>();
                for (int i = a.ContactIdx + 1; i < ev.Count; i++)
                {
                    SolverEvent e = ev[i];
                    if (e.Type == EventType.Pocket) a.PocketAfter = true;
                    else if (e.Type == EventType.CenterCross) crossed.Add(e.A);
                    else if (e.Type == EventType.Cushion)
                    {
                        a.CushionAfter = true;
                        if (!keys.TryGetValue(e.A, out var set)) keys[e.A] = set = new HashSet<int>();
                        set.Add(SectionKey(e));
                    }
                }
                foreach (var kv in keys)
                    if (kv.Value.Count >= 2 || crossed.Contains(kv.Key)) a.TwoCushionsOrCross = true;
            }
            return a;
        }

        int SectionKey(SolverEvent e)
        {
            switch (e.Surface)
            {
                case Surface.Wall: return 10000 + e.Index;
                case Surface.Bumper: return 20000 + e.Index;
                case Surface.Mover: return 30000 + e.Index;
            }
            Vec2 a = map.Outline[e.Index], b = map.Outline[(e.Index + 1) % map.Outline.Length], ab = b - a;
            double t = (e.Pos - a).Dot(ab) / ab.Dot(ab);
            int n = 0;
            foreach (Pocket p in map.Pockets)
                if (Geo.DistToSegment(p.Pos, a, b) < p.Throat && (p.Pos - a).Dot(ab) / ab.Dot(ab) < t) n++;
            return e.Index * 100 + n;
        }

        public static string FoulText(FoulReason r)
        {
            switch (r)
            {
                case FoulReason.NoContact: return "Фол: биток никого не коснулся";
                case FoulReason.WrongFirstBall: return "Фол: первым тронут не тот шар";
                case FoulReason.NoRailAfterContact: return "Фол: после удара никто не коснулся борта";
                case FoulReason.CueBallPocketed: return "Фол: биток забит";
                case FoulReason.IllegalBreak: return "Фол: разбой незаконный";
                case FoulReason.HomeShot: return "Фол: первым тронут шар в доме";
                case FoulReason.ThreeFouls: return "Три фола подряд — поражение";
            }
            return "";
        }

        // ---------- общий вход ----------
        public ShotResult Evaluate(ShotInput s)
        {
            Analysis a = Analyze(s);
            ShotResult r;
            switch (St.Kind)
            {
                case GameKind.Eight: r = EvalEight(a, s); break;
                case GameKind.Nine: r = EvalNine(a, s); break;
                case GameKind.American:
                case GameKind.Siberian: r = EvalPyramid(a, s); break;
                default: r = EvalSnooker(a, s); break;
            }
            St.Current = r.NextPlayer; St.Hand = r.Hand;
            if (r.Winner >= 0) { St.Over = true; St.Winner = r.Winner; }
            return r;
        }

        static ShotResult Res(int next, HandMode hand) => new ShotResult { NextPlayer = next, Hand = hand };

        void SetFoul(ShotResult r, FoulReason f) { r.Foul = true; r.Reason = f; r.Message = FoulText(f); }

        // ---------- «восьмёрка» ----------
        static int GroupOf(int id) => id >= 1 && id <= 7 ? 1 : id >= 9 && id <= 15 ? 2 : 0;
        bool GroupOnTable(int g) { foreach (int id in St.OnTable) if (GroupOf(id) == g) return true; return false; }

        ShotResult EvalEight(Analysis a, ShotInput s)
        {
            int me = St.Current, opp = 1 - me;
            bool cueP = a.Pocketed.Any(p => p.id == 0), eightP = a.Pocketed.Any(p => p.id == 8);
            var objs = a.Pocketed.Where(p => p.id != 0 && p.id != 8).ToList();
            var r = Res(me, HandMode.None);
            FoulReason fr = FoulReason.None;

            if (St.BreakPending)
            {
                if (a.ContactIdx < 0) fr = FoulReason.NoContact; else if (cueP) fr = FoulReason.CueBallPocketed;
                bool legal = objs.Count > 0 || eightP || a.CushionBalls.Count(id => id >= 1) >= 4;
                if (eightP || !legal)
                {
                    St.ResetBalls(); St.Score = new int[2];
                    r.Rerack = true; r.NextPlayer = eightP && fr == FoulReason.None ? me : opp; r.Hand = HandMode.Home;
                    r.Message = eightP ? "«8» на разбое: перерасстановка" : "Разбой незаконный: перерасстановка";
                    return r;
                }
                St.BreakPending = false;
                foreach (var o in objs) St.OnTable.Remove(o.id);
                if (fr != FoulReason.None) { SetFoul(r, fr); r.NextPlayer = opp; r.Hand = HandMode.Anywhere; }
                else if (objs.Count == 0) r.NextPlayer = opp;
                return r;
            }

            int g = St.Group[me];
            bool cleared = !St.TableOpen && !GroupOnTable(g);
            if (a.ContactIdx < 0) fr = FoulReason.NoContact;
            else
            {
                int f = a.FirstBall;
                bool ok = St.TableOpen ? f != 8 : cleared ? f == 8 : GroupOf(f) == g;
                if (!ok) fr = FoulReason.WrongFirstBall;
                else if (!a.PocketAfter && !a.CushionAfter) fr = FoulReason.NoRailAfterContact;
            }
            if (fr == FoulReason.None && cueP) fr = FoulReason.CueBallPocketed;

            if (eightP)
            {
                bool win = cleared && fr == FoulReason.None && a.Pocketed.First(p => p.id == 8).pocket == s.CalledPocket;
                r.Winner = win ? me : opp; r.NextPlayer = me;
                if (!win) { r.Message = fr != FoulReason.None ? FoulText(fr) : "«8» забита не по правилам"; r.Foul = fr != FoulReason.None; r.Reason = fr; }
                St.OnTable.Remove(8);
            }
            else if (fr != FoulReason.None) { SetFoul(r, fr); r.NextPlayer = opp; r.Hand = HandMode.Anywhere; }
            else
            {
                if (St.TableOpen && objs.Count > 0)
                {
                    int ng = GroupOf(objs[0].id);
                    St.Group[me] = ng; St.Group[opp] = 3 - ng; St.TableOpen = false;
                }
                if (!objs.Any(o => GroupOf(o.id) == St.Group[me])) r.NextPlayer = opp;
            }
            foreach (var o in objs) St.OnTable.Remove(o.id);
            for (int p = 0; p < 2; p++)
            {
                int cnt = 0;
                if (St.Group[p] != 0)
                    for (int id = 1; id <= 15; id++) if (GroupOf(id) == St.Group[p] && !St.OnTable.Contains(id)) cnt++;
                St.Score[p] = cnt;
            }
            return r;
        }

        // ---------- «девятка» ----------
        ShotResult EvalNine(Analysis a, ShotInput s)
        {
            int me = St.Current, opp = 1 - me;
            bool cueP = a.Pocketed.Any(p => p.id == 0), nineP = a.Pocketed.Any(p => p.id == 9);
            var objs = a.Pocketed.Where(p => p.id != 0).ToList();
            int target = St.OnTable.Where(i => i >= 1).DefaultIfEmpty(9).Min();
            var r = Res(me, HandMode.None);
            FoulReason fr = FoulReason.None;
            if (a.ContactIdx < 0) fr = FoulReason.NoContact;
            else if (a.FirstBall != target) fr = FoulReason.WrongFirstBall;
            else if (St.BreakPending)
            {
                if (objs.Count == 0 && a.CushionBalls.Count(id => id >= 1) < 4) fr = FoulReason.IllegalBreak;
            }
            else if (!a.PocketAfter && !a.CushionAfter) fr = FoulReason.NoRailAfterContact;
            if (fr == FoulReason.None && cueP) fr = FoulReason.CueBallPocketed;
            St.BreakPending = false;

            foreach (var o in objs) { St.OnTable.Remove(o.id); St.Score[me]++; }
            if (fr == FoulReason.None)
            {
                St.FoulStreak[me] = 0;
                if (nineP) r.Winner = me;
                else if (objs.Count == 0) r.NextPlayer = opp;
                return r;
            }
            SetFoul(r, fr); r.NextPlayer = opp; r.Hand = HandMode.Anywhere;
            if (nineP) { St.OnTable.Add(9); St.Score[me]--; r.Respots.Add(new Respot { BallId = 9, Point = map.Layout.RackPoint }); }
            if (++St.FoulStreak[me] >= 3) { r.Winner = opp; r.Reason = FoulReason.ThreeFouls; r.Message = FoulText(FoulReason.ThreeFouls); }
            else if (St.FoulStreak[me] == 2) { r.Warning = true; r.Message += ". Ещё один фол — поражение"; }
            return r;
        }

        // ---------- «Американка» / «Сибирка» ----------
        ShotResult EvalPyramid(Analysis a, ShotInput s)
        {
            bool sib = St.Kind == GameKind.Siberian;
            int me = St.Current, opp = 1 - me, cue = s.CueId;
            Vec2 back = map.Layout.Marks["back"];
            var r = Res(me, HandMode.None);
            var scored = new List<int>(); var early = new List<int>();
            foreach (var p in a.Pocketed) (a.ContactIdx >= 0 && p.idx > a.ContactIdx ? scored : early).Add(p.id);
            bool cueP = a.Pocketed.Any(p => p.id == cue);

            FoulReason fr = FoulReason.None;
            if (a.ContactIdx < 0) fr = FoulReason.NoContact;
            else
            {
                if (sib && s.FromHome && Geo.PointInPolygon(a.FirstBallPos, map.Layout.Home)) fr = FoulReason.HomeShot;
                else if (scored.Count == 0 && !a.TwoCushionsOrCross) fr = FoulReason.NoRailAfterContact;
            }

            if (fr != FoulReason.None)
            {
                SetFoul(r, fr); r.NextPlayer = opp;
                foreach (var p in a.Pocketed)
                {
                    if (sib && p.id == 0) { St.OnTable.Add(0); continue; }
                    St.OnTable.Add(p.id); r.Respots.Add(new Respot { BallId = p.id, Point = back });
                }
                int give = -1;
                foreach (int id in St.Scored[me]) if (id >= 1 && (give < 0 || id < give)) give = id;
                if (give < 0 && !sib && St.Scored[me].Contains(0)) give = 0;
                if (give >= 0)
                {
                    St.Scored[me].Remove(give); St.OnTable.Add(give);
                    r.Respots.Add(new Respot { BallId = give, Point = back });
                }
                St.Score[me] = Math.Max(0, St.Score[me] - 1);
                if (sib) r.Hand = cueP ? HandMode.Home : HandMode.None;
                St.BreakPending = false;
                return r;
            }

            foreach (int id in early) r.Respots.Add(new Respot { BallId = id, Point = back });
            foreach (int id in scored)
            {
                St.Score[me]++;
                if (sib && id == 0) { St.OnTable.Add(0); r.Hand = HandMode.Home; continue; }
                St.Scored[me].Add(id); St.OnTable.Remove(id);
            }
            if (scored.Count == 0) r.NextPlayer = opp;
            if (St.Score[me] >= PyramidWin) r.Winner = me;
            St.BreakPending = false;
            return r;
        }

        /// <summary>Сибирка: удар из дома, если за линией дома нет белых — id белого, который надо поставить на заднюю отметку (иначе −1).</summary>
        public int HomeShotBallToMove(IReadOnlyList<Ball> balls)
        {
            Layout l = map.Layout;
            int best = -1; double bd = double.MaxValue;
            foreach (Ball b in balls)
            {
                if (b.Pocketed || b.Id == 0) continue;
                if (!Geo.PointInPolygon(b.Pos, l.Home)) return -1;
                Vec2 ab = l.HomeLineB - l.HomeLineA;
                double d = Math.Abs((b.Pos - l.HomeLineA).Cross(ab)) / ab.Len;
                if (d < bd) { bd = d; best = b.Id; }
            }
            return best;
        }
    }
}
