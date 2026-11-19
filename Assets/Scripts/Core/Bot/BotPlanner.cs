using System;
using System.Collections.Generic;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Physics;
using Boll.Core.Rules;

namespace Boll.Core.Bot
{
    /// <summary>Параметры перебора (подстраиваются; пороги из таблицы уровней не меняются).</summary>
    public sealed class BotTuning
    {
        public int MaxSims;        // бюджет оценочных прогонов на ход
        public int Width;          // сколько лучших ударов проверять
        public double SimSeconds = 6;

        public static BotTuning For(int level)
        {
            switch (level)
            {
                case 3: return new BotTuning { MaxSims = 8, Width = 4 };
                case 4: return new BotTuning { MaxSims = 14, Width = 4 };
                case 5: return new BotTuning { MaxSims = 30, Width = 6 };
                case 6: return new BotTuning { MaxSims = 44, Width = 8 };
                case 7: return new BotTuning { MaxSims = 60, Width = 8 };
                case 8: return new BotTuning { MaxSims = 72, Width = 10 };
                default: return new BotTuning { MaxSims = 0, Width = 1 };
            }
        }
    }

    /// <summary>
    /// Планировщик хода бота. Работает порциями: Work(sims) делает не больше sims оценочных прогонов
    /// (уровни 1–2 прогонов не делают). Ход анимируется снаружи: прицел ≥ 1 с, весь ход ≤ 3 с.
    /// </summary>
    public sealed class BotPlanner
    {
        public const double MinAimSeconds = 1, MaxTurnSeconds = 3;

        sealed class Cand
        {
            public double Angle, Power, Quality, Prob = 1;
            public int Target, Pocket = -1;
            public double SpinX, SpinY;
        }

        sealed class Box { public double Value; public ShotParams Best; }

        readonly Match m;
        readonly BotLevel lv;
        readonly BotTuning tune;
        readonly Rng rng;
        readonly int me;
        readonly double R;
        readonly IEnumerator<int> it;
        int simsLeft;
        double score0;

        public bool Done { get; private set; }
        public ShotParams Result { get; private set; }
        public Vec2? HandSpot { get; private set; }

        public BotPlanner(Match match, BotLevel level, BotTuning tuning = null)
        {
            m = match; lv = level; tune = tuning ?? BotTuning.For(level.Level);
            rng = match.Rng; me = match.St.Current; R = match.Map.BallDiameter / 2;
            simsLeft = tune.MaxSims;
            it = Plan().GetEnumerator();
        }

        /// <summary>Выполнить не больше sims оценочных прогонов. true — план готов (Result).</summary>
        public bool Work(int sims)
        {
            for (int i = 0; i < sims && !Done; i++)
                if (!it.MoveNext()) Done = true;
            return Done;
        }

        /// <summary>Закончить немедленно с лучшим найденным (по лимиту времени хода).</summary>
        public void Finish()
        {
            while (!Done && Result == null) { if (!it.MoveNext()) Done = true; }
            if (Result == null) Result = FallbackShot();
            Done = true;
        }

        // ================= план хода =================
        IEnumerable<int> Plan()
        {
            if (m.St.Hand != HandMode.None && !(m.St.BreakPending && m.Kind != GameKind.Snooker) && lv.Level >= 3)
                HandSpot = ChooseHandSpot(m);
            if (HandSpot.HasValue) m.TryPlaceCue(HandSpot.Value);

            score0 = m.St.Score[me];
            ShotParams ideal;
            if (lv.Level <= 2) ideal = Simple();
            else
            {
                var box = new Box { Value = double.NegativeInfinity };
                foreach (int _ in Search(m, lv.Lookahead, box, true)) yield return 0;
                ideal = box.Best ?? FallbackShot();
            }
            Result = ApplyError(ideal);
            Done = true;
        }

        ShotParams FallbackShot()
        {
            var t = Targets(m);
            return NearestTarget(t) ?? new ShotParams { Angle = 0, Power = 0.5 };
        }

        ShotParams NearestTarget(List<int> targets)
        {
            Ball cue = CueOf(m);
            Ball best = null; double bd = double.MaxValue;
            foreach (Ball b in m.Solver.Balls)
            {
                if (b.Pocketed || !targets.Contains(b.Id)) continue;
                double d = (b.Pos - cue.Pos).Len;
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null) return null;
            var d0 = best.Pos - cue.Pos;
            return Fill(new ShotParams { Angle = Math.Atan2(d0.Y, d0.X), Power = 0.15 + 0.85 * rng.NextDouble() }, best.Id, NearestPocket(best.Pos), cue.Id);
        }

        int NearestPocket(Vec2 p)
        {
            int bi = 0; double bd = double.MaxValue;
            for (int i = 0; i < m.Map.Pockets.Length; i++)
            {
                double d = (m.Map.Pockets[i].Pos - p).Len;
                if (d < bd) { bd = d; bi = i; }
            }
            return bi;
        }

        ShotParams Fill(ShotParams p, int target, int pocket, int cueId = 0)
        {
            p.CueId = cueId; p.CalledPocket = pocket;
            if (m.Kind == GameKind.Snooker && m.St.ColourOn) p.NominatedColor = RulesEngine.IsColour(target) ? target : 16;
            return p;
        }

        ShotParams Simple()
        {
            var t = Targets(m);
            if (lv.Level == 2)
            {
                var c = Geometric(m, CueOf(m).Pos, t, CueOf(m).Id).OrderByDescending(x => x.Quality * (0.15 + x.Prob)).FirstOrDefault();
                if (c != null) return Fill(new ShotParams { Angle = c.Angle, Power = Math.Min(0.9, c.Power) }, c.Target, c.Pocket, CueOf(m).Id);
            }
            return NearestTarget(t) ?? new ShotParams { Power = 0.5 };
        }

        // ================= ошибки =================
        ShotParams ApplyError(ShotParams p)
        {
            double spread = lv.AngleSpreadDeg, k = spread / 8.0;
            double sigma = spread * Math.PI / 180 / 1.96;
            p.Angle += rng.Gauss() * sigma;
            p.Power = Math.Max(0.05, Math.Min(1, p.Power * (1 + rng.Gauss() * 0.08 * k / 1.96 * 1.0)));
            if (p.SpinX != 0 || p.SpinY != 0)
            {
                p.SpinX = Clamp1(p.SpinX + rng.Gauss() * 0.1 * k);
                p.SpinY = Clamp1(p.SpinY + rng.Gauss() * 0.1 * k);
            }
            return p;
        }

        static double Clamp1(double v) => Math.Max(-0.8, Math.Min(0.8, v));

        // ================= поиск =================
        IEnumerable<int> Search(Match node, int depth, Box box, bool top)
        {
            box.Value = double.NegativeInfinity;
            if (node.St.Hand != HandMode.None && !top && !(node.St.BreakPending && node.Kind != GameKind.Snooker))
            {
                Vec2? hs = ChooseHandSpot(node);
                if (hs.HasValue) node.TryPlaceCue(hs.Value);
            }
            var targets = Targets(node);
            bool brk = node.St.BreakPending && node.Kind != GameKind.Snooker;
            List<Cand> cands = brk ? BreakCands(node, targets) : Geometric(node, CueOf(node).Pos, targets, CueOf(node).Id).OrderByDescending(x => x.Quality * (0.15 + x.Prob)).ToList();
            int width = top ? tune.Width : Math.Max(1, tune.Width >> Math.Min(4, lv.Lookahead - depth));
            cands = cands.Take(width).ToList();
            double before = node.St.Score[me];
            bool stopAtSuccess = depth <= 0;

            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    if (box.Value >= 50 || lv.Fallback == Fallback.None && !(lv.Clusters)) break;
                    cands = pass1(node, targets, brk);
                    if (cands.Count == 0) break;
                }
                foreach (Cand c in cands)
                {
                    foreach (var v in Variants(c))
                    {
                        if (simsLeft <= 0) yield break;
                        simsLeft--;
                        var p = new ShotParams { Angle = c.Angle, Power = v.power, SpinX = v.sx, SpinY = v.sy };
                        Fill(p, c.Target, c.Pocket, CueOf(node).Id);
                        Match sim = Sim(node, p);
                        yield return 0;
                        if (sim == null) continue;
                        double s = Score(sim, before);
                        if (depth > 0 && sim.Phase == Phase.Aiming && sim.St.Current == me && s >= 100 && simsLeft > 0)
                        {
                            var sub = new Box();
                            foreach (int _ in Search(sim, depth - 1, sub, false)) yield return 0;
                            if (!double.IsNegativeInfinity(sub.Value)) s += 0.8 * Math.Max(sub.Value, -150);
                        }
                        if (s >= 100 && depth <= 0 && lv.Lookahead > 0 && sim.Phase == Phase.Aiming && sim.St.Current == me)
                            s += 25 * Geometric(sim, CueOf(sim).Pos, Targets(sim), CueOf(sim).Id).Select(x => x.Quality).DefaultIfEmpty(0).Max();
                        bool raw = s >= 100;
                        double adj = raw ? s * c.Prob : s;
                        if (adj > box.Value) { box.Value = adj; box.Best = p; }
                        if (raw) break;     // удар подходит; пробовать другие силы/винты не нужно
                    }
                    if (stopAtSuccess && box.Value >= 100 * c.Prob && box.Value > 0 && box.Best != null && c.Prob >= 0.3) yield break;
                }
            }
        }

        List<Cand> pass1(Match node, List<int> targets, bool brk)
        {
            // запасные удары: скопления (уровни 7–8) и отыгрыш
            var l = new List<Cand>();
            Ball cue = CueOf(node);
            var tb = node.Solver.Balls.Where(b => !b.Pocketed && targets.Contains(b.Id)).OrderBy(b => (b.Pos - cue.Pos).Len).ToList();
            foreach (Ball b in tb.Take(3))
            {
                Vec2 d = b.Pos - cue.Pos; double a0 = Math.Atan2(d.Y, d.X);
                double span = Math.Asin(Math.Min(0.95, 2 * R / Math.Max(2 * R * 1.01, d.Len)));
                bool cluster = lv.Clusters && node.Solver.Balls.Count(o => !o.Pocketed && o.Id != b.Id && o.Id != 0 && (o.Pos - b.Pos).Len < 3.2 * R) > 0;
                foreach (double off in cluster ? new[] { 0.0, -0.5, 0.5 } : new[] { 0.0 })
                    foreach (double pw in cluster ? new[] { 0.85, 0.5 } : new[] { 0.35, 0.55 })
                        l.Add(new Cand { Angle = a0 + off * span, Power = pw, Target = b.Id, Pocket = NearestPocket(b.Pos), Quality = 0 });
            }
            return l;
        }

        IEnumerable<(double power, double sx, double sy)> Variants(Cand c)
        {
            double p0 = c.Power;
            yield return (p0, 0, 0);
            if (lv.Spin == SpinSkill.None) yield break;
            foreach (double sc in new[] { 1.2, 0.85 }) yield return (Math.Min(1, p0 * sc), 0, 0);
            yield return (p0, 0, 0.4); yield return (p0, 0, -0.4);
            if (lv.Spin == SpinSkill.Full) { yield return (p0, 0.3, 0); yield return (p0, -0.3, 0); }
        }

        /// <summary>Оценочный прогон на копии партии.</summary>
        Match Sim(Match node, ShotParams p)
        {
            Match c = Match.Restore(node.Map, node.Model, node.Snapshot());
            c.SafetySteps = (long)(tune.SimSeconds * 480);
            if (!c.Shoot(p)) return null;
            long n = 0, cap = c.SafetySteps + 4;
            while (c.Phase == Phase.Shooting && n < cap)
            {
                c.Step();
                if ((++n & 63) == 0 && c.Solver.MaxSpeed() < 0.15) c.Solver.StopAll();
            }
            return c.LastResult == null ? null : c;
        }

        double Score(Match c, double before)
        {
            ShotResult r = c.LastResult;
            if (c.St.Over) return c.St.Winner == me ? 1000 : -1000;
            if (r.Rerack) return -50;
            if (r.Foul) return -200;
            if (c.St.Current == me) return 100 + 15 * (c.St.Score[me] - before);
            double s = 0;
            if (lv.Fallback == Fallback.Tactical && c.Phase == Phase.Aiming)
            {
                var t = Targets(c);
                double ease = Geometric(c, CueOf(c).Pos, t, CueOf(c).Id).Select(x => x.Quality).DefaultIfEmpty(0).Max();
                s -= 40 * ease;
            }
            return s;
        }

        // ================= кандидаты =================
        /// <summary>Биток: шар 0; в «Американке», если он уже забит, — шар с лучшим геометрическим ударом.</summary>
        Ball CueOf(Match n)
        {
            Ball z = n.BallById(0);
            if (n.Kind != GameKind.American || (z != null && !z.Pocketed)) return z;
            Ball best = null; double bq = -1;
            foreach (Ball b in n.Solver.Balls)
            {
                if (b.Pocketed) continue;
                var tg = n.Solver.Balls.Where(o => !o.Pocketed && o.Id != b.Id).Select(o => o.Id).ToList();
                double q = Geometric(n, b.Pos, tg, b.Id).Select(c => c.Quality).DefaultIfEmpty(0).Max();
                if (q > bq || best == null) { bq = q; best = b; }
            }
            return best;
        }

        List<int> Targets(Match n)
        {
            int cid = CueOf(n).Id;
            var on = n.Solver.Balls.Where(b => !b.Pocketed && b.Id != cid).Select(b => b.Id).ToList();
            MatchState st = n.St;
            switch (n.Kind)
            {
                case GameKind.Eight:
                    if (st.BreakPending) return on;
                    if (st.TableOpen) return on.Where(i => i != 8).ToList();
                    int g = st.Group[st.Current];
                    var own = on.Where(i => (g == 1 ? i >= 1 && i <= 7 : i >= 9 && i <= 15)).ToList();
                    return own.Count > 0 ? own : on.Where(i => i == 8).ToList();
                case GameKind.Nine:
                    return on.Count == 0 ? on : new List<int> { on.Min() };
                case GameKind.Snooker:
                    return n.Rules.OnBalls().Where(on.Contains).ToList();
                default: return on;
            }
        }

        List<Cand> BreakCands(Match n, List<int> targets)
        {
            Ball cue = CueOf(n);
            Ball head = n.Solver.Balls.Where(b => !b.Pocketed && targets.Contains(b.Id)).OrderBy(b => (b.Pos - cue.Pos).Len).FirstOrDefault();
            var l = new List<Cand>();
            if (head == null) return l;
            Vec2 d = head.Pos - cue.Pos; double a0 = Math.Atan2(d.Y, d.X);
            foreach (var (da, pw) in new[] { (0.0, 1.0), (0.012, 1.0), (-0.012, 1.0), (0.0, 0.8), (0.0, 0.65) })
                l.Add(new Cand { Angle = a0 + da, Power = pw, Target = head.Id, Pocket = NearestPocket(head.Pos), Quality = 1 });
            return l;
        }

        List<Cand> Geometric(Match n, Vec2 cue, List<int> targets, int cueId = 0)
        {
            var res = new List<Cand>();
            MapData md = n.Map; double d2R = 2 * R;
            var balls = n.Solver.Balls;
            double aRoll = n.Model.RollingFriction * Solver.G;
            foreach (int tid in targets)
            {
                Ball T = balls.FirstOrDefault(b => b.Id == tid && !b.Pocketed);
                if (T == null) continue;
                for (int pi = 0; pi < md.Pockets.Length; pi++)
                {
                    Pocket P = md.Pockets[pi];
                    Vec2 tp = P.Pos - T.Pos; double d2 = tp.Len;
                    if (d2 < 1e-6) continue;
                    Vec2 u = tp * (1 / d2);
                    Vec2 G = T.Pos - u * d2R;
                    Vec2 cg = G - cue; double d1 = cg.Len;
                    if (d1 < 1e-4) continue;
                    double cos = cg.Dot(u) / d1;
                    if (cos < 0.17) continue;
                    if (!Inside(md, G)) continue;
                    if (!ClearBalls(balls, cue, G, d2R * 0.98, tid, cueId)) continue;
                    double pr = P.Throat * n.Model.PocketCapture / 2;
                    Vec2 stop = P.Pos - u * Math.Min(pr, d2);
                    if (!ClearBalls(balls, T.Pos, stop, d2R * 0.98, tid, cueId)) continue;
                    if (!ClearStatic(md, cue, G) || !ClearStatic(md, T.Pos, stop)) continue;
                    if (!PathInside(md, T.Pos, stop)) continue;
                    double vT = Math.Sqrt(2 * aRoll * d2) + 0.45;
                    double vc = vT / (0.975 * cos);
                    double v0 = Math.Sqrt(vc * vc + 2 * 0.3 * d1);
                    res.Add(new Cand
                    {
                        Angle = Math.Atan2(cg.Y, cg.X), Power = Math.Max(0.1, Math.Min(1, v0 * 1.1 / n.Model.CueSpeed)),
                        Quality = cos * cos / (0.4 + d1 + d2), Target = tid, Pocket = pi,
                        Prob = SuccessProb(d1, d2, pr)
                    });
                }
            }
            return res;
        }

        /// <summary>Вероятность забить с учётом разброса угла уровня: ошибка прицела усиливается длиной удара битка.</summary>
        double SuccessProb(double d1, double d2, double pocketRadius)
        {
            double sigma = lv.AngleSpreadDeg * Math.PI / 180 / 1.96;
            double sigT = Math.Max(1e-6, sigma * (d1 + 0.1) / (2 * R) * 0.6);
            double tol = Math.Atan(pocketRadius / Math.Max(0.05, d2)) * 0.8;
            return Erf(tol / (1.4142 * sigT));
        }

        static double Erf(double x)
        {
            double t = 1 / (1 + 0.3275911 * Math.Abs(x));
            double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return x >= 0 ? y : -y;
        }

        bool Inside(MapData md, Vec2 p) => Geo.PointInPolygon(p, md.Outline) && Geo.DistToPolygonEdge(p, md.Outline) >= R * 0.999;

        bool PathInside(MapData md, Vec2 a, Vec2 b)
        {
            for (int k = 1; k <= 12; k++)
            {
                Vec2 p = a + (b - a) * (k / 12.0);
                if (!Geo.PointInPolygon(p, md.Outline) || Geo.DistToPolygonEdge(p, md.Outline) < R * 0.9) return k >= 11;
            }
            return true;
        }

        bool ClearBalls(IList<Ball> balls, Vec2 a, Vec2 b, double minDist, int targetId, int cueId)
        {
            foreach (Ball o in balls)
            {
                if (o.Pocketed || o.Id == cueId || o.Id == targetId) continue;
                if (Geo.DistToSegment(o.Pos, a, b) < minDist) return false;
            }
            return true;
        }

        bool ClearStatic(MapData md, Vec2 a, Vec2 b)
        {
            foreach (Obstacle ob in md.Obstacles)
            {
                if (ob.Type == ObstacleType.Mover) continue;
                for (int k = 0; k <= 10; k++)
                {
                    Vec2 p = a + (b - a) * (k / 10.0);
                    double d = ob.IsCircle ? (p - ob.Center).Len - ob.Radius : Geo.DistToSegment(p, ob.A, ob.B);
                    if (d < R) return false;
                }
            }
            return true;
        }

        // ================= шар с руки =================
        Vec2? ChooseHandSpot(Match n)
        {
            MapData md = n.Map;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (Vec2 q in (n.St.Hand == HandMode.Home ? md.Layout.Home : md.Outline))
            { minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X); minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
            double step = n.St.Hand == HandMode.Home ? 0.08 : 0.15;
            var targets = Targets(n);
            var occ = n.Solver.Balls.Where(b => !b.Pocketed && b.Id != 0).Select(b => b.Pos).ToList();
            Vec2? best = null; double bq = -1;
            for (double x = minX; x <= maxX; x += step)
                for (double y = minY; y <= maxY; y += step)
                {
                    var p = new Vec2(x, y);
                    if (!Placement.IsValidHandSpot(md, n.Rest, n.Model.PocketCapture, p, n.St.Hand, occ)) continue;
                    double q = Geometric(n, p, targets, 0).Select(c => c.Quality).DefaultIfEmpty(0).Max();
                    if (q > bq) { bq = q; best = p; }
                }
            return best;
        }
    }
}
