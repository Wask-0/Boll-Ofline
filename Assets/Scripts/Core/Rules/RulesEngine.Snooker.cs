using System;
using System.Collections.Generic;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Physics;

namespace Boll.Core.Rules
{
    /// <summary>Снукер, правила WPBSA в объёме CONCEPT §3.5. Ид: биток 0, красные 1–15, цветные 16–21 (жёлтый…чёрный).</summary>
    public sealed partial class RulesEngine
    {
        public static readonly string[] SnookerColors = { "yellow", "green", "brown", "blue", "pink", "black" };
        public const int Yellow = 16, Black = 21;

        public static bool IsRed(int id) => id >= 1 && id <= 15;
        public static bool IsColour(int id) => id >= 16 && id <= 21;
        public static int BallValue(int id) => IsRed(id) ? 1 : IsColour(id) ? id - 14 : 0;

        int RedsLeft() { int n = 0; foreach (int id in St.OnTable) if (IsRed(id)) n++; return n; }

        int LowestColour()
        {
            for (int id = Yellow; id <= Black; id++) if (St.OnTable.Contains(id)) return id;
            return -1;
        }

        /// <summary>Шары «на» в начале следующего удара (без заявки: после красного — все цветные на столе).</summary>
        public List<int> OnBalls()
        {
            var l = new List<int>();
            if (St.ColourOn) { for (int id = Yellow; id <= Black; id++) if (St.OnTable.Contains(id)) l.Add(id); }
            else if (RedsLeft() > 0) { for (int id = 1; id <= 15; id++) if (St.OnTable.Contains(id)) l.Add(id); }
            else { int c = LowestColour(); if (c >= 0) l.Add(c); }
            return l;
        }

        /// <summary>Биток в snooker'е (нет прямого доступа ко всей ширине ни одного шара «на») — можно заявить свободный шар.</summary>
        public bool IsSnookered(Vec2 cue, IReadOnlyList<Ball> balls)
        {
            double R = map.BallDiameter / 2, rho = map.BallDiameter;
            foreach (int id in OnBalls())
            {
                Ball b = balls.FirstOrDefault(x => x.Id == id && !x.Pocketed);
                if (b == null) continue;
                Vec2 d = b.Pos - cue; double dist = d.Len;
                if (dist <= rho) return false;
                double alpha = Math.Asin(rho / dist), len = Math.Sqrt(dist * dist - rho * rho);
                bool visible = true;
                for (int sgn = -1; sgn <= 1 && visible; sgn += 2)
                {
                    double c = Math.Cos(sgn * alpha), s = Math.Sin(sgn * alpha);
                    Vec2 u = new Vec2((d.X * c - d.Y * s) / dist, (d.X * s + d.Y * c) / dist);
                    Vec2 t = cue + u * len;
                    foreach (Ball o in balls)
                        if (o.Id != id && o.Id != 0 && !o.Pocketed && Geo.DistToSegment(o.Pos, cue, t) < rho - 1e-6) { visible = false; break; }
                    if (!visible) break;
                    foreach (Obstacle ob in map.Obstacles)
                    {
                        if (ob.Type == ObstacleType.Mover) continue;
                        for (int k = 0; k <= 20 && visible; k++)
                        {
                            Vec2 p = cue + (t - cue) * (k / 20.0);
                            double dd = ob.IsCircle ? (p - ob.Center).Len - ob.Radius : Geo.DistToSegment(p, ob.A, ob.B);
                            if (dd < R) visible = false;
                        }
                    }
                }
                if (visible) return false;
            }
            return true;
        }

        Vec2 SnookerSpot(int id) => IsColour(id) ? map.Layout.Marks[SnookerColors[id - Yellow]] : map.Layout.RackPoint;

        ShotResult EvalSnooker(Analysis a, ShotInput s)
        {
            int me = St.Current, opp = 1 - me;
            var r = Res(me, HandMode.None);
            bool cueP = a.Pocketed.Any(p => p.id == 0);
            int reds = RedsLeft(), lowest = LowestColour();
            bool colourOn = St.ColourOn, seq = !colourOn && reds == 0;
            bool freeAllowed = St.FreeBallEligible && s.FreeBallId >= 0 && St.OnTable.Contains(s.FreeBallId);
            int free = freeAllowed ? s.FreeBallId : -1;
            int nominated = colourOn && IsColour(s.NominatedColor) ? s.NominatedColor : -1;
            if (colourOn && nominated < 0 && IsColour(a.FirstBall)) nominated = a.FirstBall;
            St.FreeBallEligible = false;

            bool IsOn(int id) => colourOn ? (nominated >= 0 ? id == nominated : IsColour(id)) : reds > 0 ? IsRed(id) : id == lowest;
            int onValue = colourOn ? (nominated >= 0 ? BallValue(nominated) : 4) : reds > 0 ? 1 : BallValue(lowest);

            var before = a.Pocketed.Where(p => p.id != 0 && (a.ContactIdx < 0 || p.idx < a.ContactIdx)).Select(p => p.id).ToList();
            var after = a.ContactIdx < 0 ? new List<int>() : a.Pocketed.Where(p => p.idx > a.ContactIdx && p.id != 0).Select(p => p.id).ToList();
            foreach (int id in before) r.Respots.Add(new Respot { BallId = id, Point = SnookerSpot(id) });

            if (a.ContactIdx < 0) // промах: не фол
            {
                St.ColourOn = false;
                r.NextPlayer = opp; if (cueP) r.Hand = HandMode.Home;
                return CheckSnookerEnd(r, a, false);
            }

            int F = a.FirstBall;
            bool firstOk = IsOn(F) || F == free;
            var legal = new List<int>(); var illegal = new List<int>(); bool freePotted = false;
            foreach (int p in after)
            {
                if (p == free) freePotted = true;
                else if (IsOn(p)) legal.Add(p);
                else illegal.Add(p);
            }
            bool foul = !firstOk || illegal.Count > 0 || cueP;

            if (foul)
            {
                int v = Math.Max(4, onValue);
                if (F != free) v = Math.Max(v, BallValue(F));
                foreach (int p in illegal) v = Math.Max(v, BallValue(p));
                St.Score[opp] += v;
                r.Foul = true; r.NextPlayer = opp; r.Hand = cueP ? HandMode.Home : HandMode.None;
                r.Reason = cueP ? FoulReason.CueBallPocketed : !firstOk ? FoulReason.WrongFirstBall : FoulReason.IllegalBreak;
                r.Message = (cueP ? "Фол: биток забит" : !firstOk ? "Фол: первым тронут не тот шар" : "Фол: забит шар не «на»") + ", " + v + " оч. сопернику";
                foreach (int p in after)
                {
                    if (IsColour(p) || p == free) r.Respots.Add(new Respot { BallId = p, Point = SnookerSpot(p) });
                    else St.OnTable.Remove(p);
                }
                St.ColourOn = false; St.FreeBallEligible = true;
                bool lastBlack = seq && lowest == Black && after.Contains(Black) && St.OnTable.Count(i => i >= 1) == 1;
                return CheckSnookerEnd(r, a, lastBlack);
            }

            // чистый удар
            int pts = 0; bool anyPot = false;
            foreach (int p in legal)
            {
                pts += BallValue(p); anyPot = true;
                if (IsRed(p)) St.OnTable.Remove(p);
                else if (colourOn) r.Respots.Add(new Respot { BallId = p, Point = SnookerSpot(p) }); // цветной после красного возвращается
                else St.OnTable.Remove(p);
            }
            if (freePotted) { pts += onValue; anyPot = true; r.Respots.Add(new Respot { BallId = free, Point = SnookerSpot(free) }); }
            St.Score[me] += pts;
            bool redPotted = legal.Any(IsRed) || (freePotted && reds > 0);
            St.ColourOn = redPotted;
            if (!anyPot) r.NextPlayer = opp;
            bool lastBlackPot = seq && legal.Contains(Black) && St.OnTable.Count(i => i >= 1) == 0;
            return CheckSnookerEnd(r, a, lastBlackPot);
        }

        /// <summary>Конец кадра: последний чёрный (счёт) и переигровка чёрного при равенстве.</summary>
        ShotResult CheckSnookerEnd(ShotResult r, Analysis a, bool lastBlack)
        {
            int s0 = St.Score[0], s1 = St.Score[1];
            if (St.Tiebreak) { if (s0 != s1) r.Winner = s0 > s1 ? 0 : 1; return r; }
            if (lastBlack)
            {
                if (s0 != s1) r.Winner = s0 > s1 ? 0 : 1;
                else
                {
                    St.Tiebreak = true; r.Tiebreak = true; St.ColourOn = false;
                    St.OnTable.Add(Black);
                    r.Respots.RemoveAll(x => x.BallId == Black);
                    r.Respots.Add(new Respot { BallId = Black, Point = SnookerSpot(Black) });
                    r.NextPlayer = Rng != null ? (Rng.Toss() ? 1 : 0) : 1 - St.Current;
                    r.Hand = HandMode.Home; r.Message = "Счёт равный: переигровка чёрного";
                }
            }
            return r;
        }
    }
}
