using System;
using System.Collections.Generic;
using Boll.Core.Content;

namespace Boll.Core.Physics
{
    public enum EventType { BallBall, Cushion, Pocket, CenterCross }
    public enum Surface { Rail, Wall, Bumper, Mover }

    /// <summary>Событие шага. A — id шара; B — id второго шара (BallBall) или -1; Index — ребро контура / препятствие / луза.</summary>
    public struct SolverEvent
    {
        public EventType Type;
        public Surface Surface;
        public long Step;
        public int A, B, Index;
        public Vec2 Pos, Pos2; // позиции шара A (и B для BallBall) в момент события
        public double Speed; // нормальная скорость сближения до удара, м/с
    }

    public sealed class Ball
    {
        public int Id;
        public Vec2 Pos, Vel;
        public double Wx, Wy, Wz; // угловая скорость, рад/с
        public bool Pocketed;
        public int PocketIndex = -1;
    }

    /// <summary>Детерминированный 2D-решатель: шаг 1/480 с, double, непрерывная проверка столкновений.</summary>
    public sealed class Solver
    {
        public const double Dt = 1.0 / 480.0;
        public const double G = 9.81;
        public const int MaxHitsPerStep = 64;
        const double BallFriction = 0.05;

        struct Seg { public Vec2 A, B; public double E; public Surface Kind; public int Index; }
        struct Circ { public Vec2 C; public double Rad, E; public int Index; }
        struct Mover { public Obstacle O; public int Index; public Vec2 Off0, Vel; }

        public readonly MapData Map;
        public readonly PhysicsModel Model;
        public readonly double R;
        public readonly List<Ball> Balls = new List<Ball>();
        public readonly List<SolverEvent> Events = new List<SolverEvent>();
        public long StepIndex;
        public double Time => StepIndex * Dt;

        readonly Seg[] segs;
        readonly Circ[] circs;
        readonly Mover[] movers;
        readonly double[] pocketR2;
        readonly double spinDecayK;
        double moverElapsed;

        public Solver(MapData map, PhysicsModel model)
        {
            Map = map; Model = model; R = map.BallDiameter / 2;
            var sl = new List<Seg>(); var cl = new List<Circ>(); var ml = new List<Mover>();
            double e = model.CushionRestitution;
            for (int i = 0; i < map.Outline.Length; i++)
                sl.Add(new Seg { A = map.Outline[i], B = map.Outline[(i + 1) % map.Outline.Length], E = e, Kind = Surface.Rail, Index = i });
            for (int i = 0; i < map.Obstacles.Length; i++)
            {
                Obstacle o = map.Obstacles[i];
                if (o.Type == ObstacleType.Wall) sl.Add(new Seg { A = o.A, B = o.B, E = e, Kind = Surface.Wall, Index = i });
                else if (o.Type == ObstacleType.Mover) ml.Add(new Mover { O = o, Index = i });
                else if (o.IsCircle) cl.Add(new Circ { C = o.Center, Rad = o.Radius, E = e * o.Bounce, Index = i });
                else sl.Add(new Seg { A = o.A, B = o.B, E = e * o.Bounce, Kind = Surface.Bumper, Index = i });
            }
            segs = sl.ToArray(); circs = cl.ToArray(); movers = ml.ToArray();
            pocketR2 = new double[map.Pockets.Length];
            for (int i = 0; i < pocketR2.Length; i++)
            {
                double r = map.Pockets[i].Throat * model.PocketCapture / 2;
                pocketR2[i] = r * r;
            }
            spinDecayK = Math.Exp(-model.SpinDecay * Dt);
        }

        public Ball AddBall(int id, Vec2 pos)
        {
            var b = new Ball { Id = id, Pos = pos };
            Balls.Add(b);
            return b;
        }

        /// <summary>Удар кием. power 0..1; a — смещение вправо, b — вверх (−1..1, внутри единичного круга).</summary>
        public void Strike(Ball ball, double angle, double power, double a, double b)
        {
            double dx = Math.Cos(angle), dy = Math.Sin(angle);
            double v = Math.Max(0, Math.Min(1, power)) * Model.CueSpeed;
            ball.Vel = new Vec2(dx * v, dy * v);
            double k = 2.5 * v * 0.5 * Model.SpinInfluence / R;
            double top = k * b;
            ball.Wx = -dy * top; ball.Wy = dx * top;
            ball.Wz = -k * a;
        }

        /// <summary>Смещение движущегося препятствия (индекс в Map.Obstacles) в момент t.</summary>
        public Vec2 MoverOffset(int obstacleIndex, double t)
        {
            Obstacle o = Map.Obstacles[obstacleIndex];
            double dist = o.Speed * t;
            if (o.Mode == MoverMode.Circle)
            {
                Vec2 mid = (o.A + o.B) * 0.5;
                double th = Math.Atan2(mid.Y - o.Center.Y, mid.X - o.Center.X) + dist / o.Radius;
                Vec2 np = new Vec2(o.Center.X + o.Radius * Math.Cos(th), o.Center.Y + o.Radius * Math.Sin(th));
                return np - mid;
            }
            double total = 0;
            for (int i = 1; i < o.Path.Length; i++) total += (o.Path[i] - o.Path[i - 1]).Len;
            double s = dist % (2 * total);
            if (s > total) s = 2 * total - s;
            for (int i = 1; i < o.Path.Length; i++)
            {
                Vec2 d = o.Path[i] - o.Path[i - 1];
                double l = d.Len;
                if (s <= l) return o.Path[i - 1] + d * (l > 0 ? s / l : 0);
                s -= l;
            }
            return o.Path[o.Path.Length - 1];
        }

        public double MaxSpeed()
        {
            double m = 0;
            foreach (Ball b in Balls) if (!b.Pocketed) m = Math.Max(m, b.Vel.Len);
            return m;
        }

        /// <summary>Остановить все шары (для быстрых оценочных прогонов бота).</summary>
        public void StopAll() { foreach (Ball b in Balls) { b.Vel = default; b.Wx = b.Wy = b.Wz = 0; } }

        public Vec2 MoverSegmentOffset(int obstacleIndex) => MoverOffset(obstacleIndex, Time);

        public void Step()
        {
            double t0 = StepIndex * Dt;
            for (int i = 0; i < movers.Length; i++)
            {
                Vec2 o0 = MoverOffset(movers[i].Index, t0), o1 = MoverOffset(movers[i].Index, t0 + Dt);
                movers[i].Off0 = o0; movers[i].Vel = (o1 - o0) * (1.0 / Dt);
            }
            moverElapsed = 0;

            for (int i = 0; i < Balls.Count; i++) if (!Balls[i].Pocketed) Friction(Balls[i]);

            double tau = Dt;
            int hits = 0;
            while (tau > 0)
            {
                double best = double.MaxValue; int kind = 0, i1 = 0, i2 = 0; Vec2 bn = default; bool bn_set = false;
                if (hits < MaxHitsPerStep) FindEarliest(tau, ref best, ref kind, ref i1, ref i2, ref bn, ref bn_set);
                if (kind == 0) { Advance(tau); break; }
                Advance(best);
                Resolve(kind, i1, i2, bn);
                hits++;
                tau -= best;
            }
            PushOut();
            StepIndex++;
            CheckPockets();
        }

        void Advance(double t)
        {
            for (int i = 0; i < Balls.Count; i++)
            {
                Ball b = Balls[i];
                if (b.Pocketed) continue;
                b.Pos = b.Pos + b.Vel * t;
            }
            moverElapsed += t;
        }

        void Friction(Ball b)
        {
            double vx = b.Vel.X, vy = b.Vel.Y;
            double ux = vx - R * b.Wy, uy = vy + R * b.Wx;
            double um = Math.Sqrt(ux * ux + uy * uy);
            if (um > 1e-4)
            {
                double a = Model.SlidingFriction * G;
                double te = Math.Min(Dt, um / (3.5 * a));
                double ex = ux / um, ey = uy / um;
                b.Vel = new Vec2(vx - a * ex * te, vy - a * ey * te);
                b.Wx += -2.5 * a * ey / R * te;
                b.Wy += 2.5 * a * ex / R * te;
            }
            else
            {
                double sp = Math.Sqrt(vx * vx + vy * vy);
                if (sp > 0)
                {
                    double ds = Model.RollingFriction * G * Dt;
                    b.Vel = sp <= ds ? new Vec2(0, 0) : b.Vel * ((sp - ds) / sp);
                }
                b.Wy = b.Vel.X / R; b.Wx = -b.Vel.Y / R;
            }
            b.Wz *= spinDecayK;
            if (Math.Abs(b.Wz) < 1e-3) b.Wz = 0;
        }

        // kind: 1 ball-ball, 2 seg, 3 circle, 4 mover
        void FindEarliest(double tau, ref double best, ref int kind, ref int i1, ref int i2, ref Vec2 bn, ref bool bnSet)
        {
            int n = Balls.Count;
            for (int i = 0; i < n; i++)
            {
                Ball a = Balls[i];
                if (a.Pocketed) continue;
                bool aMoving = a.Vel.X != 0 || a.Vel.Y != 0;
                for (int j = i + 1; j < n; j++)
                {
                    Ball b = Balls[j];
                    if (b.Pocketed || (!aMoving && b.Vel.X == 0 && b.Vel.Y == 0)) continue;
                    if (SweepCircle(b.Pos - a.Pos, b.Vel - a.Vel, default, 2 * R, tau, out double t, out Vec2 nn) && t < best)
                    { best = t; kind = 1; i1 = i; i2 = j; bn = nn; }
                }
                if (!aMoving) continue;
                for (int s = 0; s < segs.Length; s++)
                    if (SweepSegment(a.Pos, a.Vel, segs[s].A, segs[s].B, R, tau, out double t, out Vec2 nn) && t < best)
                    { best = t; kind = 2; i1 = i; i2 = s; bn = nn; }
                for (int c = 0; c < circs.Length; c++)
                    if (SweepCircle(a.Pos, a.Vel, circs[c].C, circs[c].Rad + R, tau, out double t, out Vec2 nn) && t < best)
                    { best = t; kind = 3; i1 = i; i2 = c; bn = nn; }
            }
            for (int i = 0; i < n; i++)
            {
                Ball a = Balls[i];
                if (a.Pocketed) continue;
                for (int m = 0; m < movers.Length; m++)
                {
                    GetMoverSeg(m, out Vec2 ma, out Vec2 mb);
                    if (SweepSegment(a.Pos, a.Vel - movers[m].Vel, ma, mb, R, tau, out double t, out Vec2 nn) && t < best)
                    { best = t; kind = 4; i1 = i; i2 = m; bn = nn; }
                }
            }
        }

        void GetMoverSeg(int m, out Vec2 a, out Vec2 b)
        {
            Vec2 off = movers[m].Off0 + movers[m].Vel * moverElapsed;
            a = movers[m].O.A + off; b = movers[m].O.B + off;
        }

        static bool SweepCircle(Vec2 p, Vec2 w, Vec2 c, double rad, double tmax, out double t, out Vec2 n)
        {
            t = 0; n = default;
            Vec2 d = p - c;
            double A = w.Dot(w), B = d.Dot(w);
            if (A < 1e-18 || B >= 0) return false;
            double C = d.Dot(d) - rad * rad;
            if (C <= 0) { t = 0; double l = d.Len; n = l > 0 ? d * (1 / l) : new Vec2(1, 0); return true; }
            double disc = B * B - A * C;
            if (disc < 0) return false;
            t = (-B - Math.Sqrt(disc)) / A;
            if (t > tmax) return false;
            Vec2 q = d + w * t; double ql = q.Len;
            n = ql > 0 ? q * (1 / ql) : new Vec2(1, 0);
            return true;
        }

        static bool SweepSegment(Vec2 p, Vec2 w, Vec2 a, Vec2 b, double r, double tmax, out double t, out Vec2 n)
        {
            t = double.MaxValue; n = default;
            bool hit = false;
            Vec2 ab = b - a; double len = ab.Len;
            Vec2 e = ab * (1 / len), nn = new Vec2(-e.Y, e.X);
            double d0 = (p - a).Dot(nn), wn = w.Dot(nn);
            double sgn = d0 >= 0 ? 1 : -1, dist = Math.Abs(d0), wnn = wn * sgn;
            if (wnn < -1e-12)
            {
                double tt = Math.Max(0, (dist - r) / -wnn);
                if (tt <= tmax)
                {
                    double s = (p + w * tt - a).Dot(e);
                    if (s >= 0 && s <= len) { t = tt; n = nn * sgn; hit = true; }
                }
            }
            for (int k = 0; k < 2; k++)
                if (SweepCircle(p, w, k == 0 ? a : b, r, tmax, out double te, out Vec2 ne) && te < t)
                { t = te; n = ne; hit = true; }
            return hit;
        }

        void Resolve(int kind, int i1, int i2, Vec2 n)
        {
            Ball a = Balls[i1];
            if (kind == 1)
            {
                Ball b = Balls[i2];
                Vec2 nn = b.Pos - a.Pos; double l = nn.Len;
                if (l > 0) n = nn * (1 / l);
                double vn = (b.Vel - a.Vel).Dot(n);
                if (vn >= 0) return;
                double e = Model.BallRestitution, j = -(1 + e) * vn / 2;
                a.Vel = a.Vel - n * j; b.Vel = b.Vel + n * j;
                Vec2 t = new Vec2(-n.Y, n.X);
                double s = (b.Vel - a.Vel).Dot(t) - R * (a.Wz + b.Wz);
                double jt = Math.Max(-BallFriction * j, Math.Min(BallFriction * j, -s / 7));
                b.Vel = b.Vel + t * jt; a.Vel = a.Vel - t * jt;
                b.Wz += -2.5 * jt / R; a.Wz += -2.5 * jt / R;
                Events.Add(new SolverEvent { Type = EventType.BallBall, Step = StepIndex, A = a.Id, B = b.Id, Index = -1, Speed = -vn, Pos = a.Pos, Pos2 = b.Pos });
                return;
            }
            Vec2 vw = default; double er; Surface sf; int idx;
            if (kind == 2) { er = segs[i2].E; sf = segs[i2].Kind; idx = segs[i2].Index; }
            else if (kind == 3) { er = circs[i2].E; sf = Surface.Bumper; idx = circs[i2].Index; }
            else { er = Model.CushionRestitution; sf = Surface.Mover; idx = movers[i2].Index; vw = movers[i2].Vel; }
            Vec2 rel = a.Vel - vw;
            double vnn = rel.Dot(n);
            if (vnn >= 0) return;
            double jn = -(1 + er) * vnn;
            a.Vel = a.Vel + n * jn;
            Vec2 tt = new Vec2(-n.Y, n.X);
            double sl = rel.Dot(tt) - a.Wz * R;
            double jtt = Math.Max(-Model.SlidingFriction * jn, Math.Min(Model.SlidingFriction * jn, -sl / 3.5));
            a.Vel = a.Vel + tt * jtt;
            a.Wz += -2.5 * jtt / R;
            Events.Add(new SolverEvent { Type = EventType.Cushion, Surface = sf, Step = StepIndex, A = a.Id, B = -1, Index = idx, Speed = -vnn, Pos = a.Pos });
        }

        void PushOut()
        {
            for (int i = 0; i < Balls.Count; i++)
            {
                Ball b = Balls[i];
                if (b.Pocketed || (b.Vel.X == 0 && b.Vel.Y == 0)) continue;
                for (int s = 0; s < segs.Length; s++)
                {
                    Vec2 ab = segs[s].B - segs[s].A;
                    double l2 = ab.Dot(ab), u = Math.Max(0, Math.Min(1, (b.Pos - segs[s].A).Dot(ab) / l2));
                    Vec2 d = b.Pos - (segs[s].A + ab * u);
                    double dl = d.Len;
                    if (dl < R - 1e-9 && dl > 1e-12) b.Pos = b.Pos + d * ((R - dl) / dl);
                }
                for (int c = 0; c < circs.Length; c++)
                {
                    Vec2 d = b.Pos - circs[c].C; double dl = d.Len, need = circs[c].Rad + R;
                    if (dl < need - 1e-9 && dl > 1e-12) b.Pos = b.Pos + d * ((need - dl) / dl);
                }
            }
        }

        void CheckPockets()
        {
            for (int i = 0; i < Balls.Count; i++)
            {
                Ball b = Balls[i];
                if (b.Pocketed) continue;
                for (int p = 0; p < pocketR2.Length; p++)
                {
                    Vec2 d = b.Pos - Map.Pockets[p].Pos;
                    if (d.Dot(d) > pocketR2[p]) continue;
                    b.Pocketed = true; b.PocketIndex = p;
                    b.Vel = default; b.Wx = b.Wy = b.Wz = 0;
                    Events.Add(new SolverEvent { Type = EventType.Pocket, Step = StepIndex - 1, A = b.Id, B = -1, Index = p, Pos = b.Pos });
                    break;
                }
            }
        }
    }
}
