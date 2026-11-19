using System;
using System.Collections.Generic;
using Boll.Core.Content;

namespace Boll.Core.Physics
{
    /// <summary>Признаки покоя шаров и зона хода движущихся объектов (CONCEPT §4).</summary>
    public sealed class Rest
    {
        public const double MaxSpeed = 0.002;   // м/с
        public const double MaxSpin = 0.1;      // рад/с

        readonly double r;
        readonly List<(Vec2 a, Vec2 b)> sweepSegs = new List<(Vec2, Vec2)>(); // отрезки-границы зоны
        readonly List<Vec2[]> sweepQuads = new List<Vec2[]>();                 // заливка зоны (PingPong)
        double extra;                                                          // запас на дискретизацию орбиты

        public Rest(MapData map)
        {
            r = map.BallDiameter / 2;
            foreach (Obstacle o in map.Obstacles)
            {
                if (o.Type != ObstacleType.Mover) continue;
                if (o.Mode == MoverMode.PingPong) AddPingPong(o); else AddCircle(o);
            }
        }

        void AddPingPong(Obstacle o)
        {
            sweepSegs.Add((o.A + o.Path[0], o.B + o.Path[0]));
            for (int i = 1; i < o.Path.Length; i++)
            {
                Vec2 p = o.Path[i - 1], q = o.Path[i];
                sweepSegs.Add((o.A + q, o.B + q));
                sweepSegs.Add((o.A + p, o.A + q));
                sweepSegs.Add((o.B + p, o.B + q));
                sweepQuads.Add(new[] { o.A + p, o.B + p, o.B + q, o.A + q });
            }
        }

        void AddCircle(Obstacle o)
        {
            Vec2 mid = (o.A + o.B) * 0.5;
            double th0 = Math.Atan2(mid.Y - o.Center.Y, mid.X - o.Center.X);
            int n = Math.Max(16, (int)Math.Ceiling(2 * Math.PI * o.Radius / (r / 2)));
            for (int i = 0; i < n; i++)
            {
                double th = th0 + 2 * Math.PI * i / n;
                Vec2 np = new Vec2(o.Center.X + o.Radius * Math.Cos(th), o.Center.Y + o.Radius * Math.Sin(th));
                sweepSegs.Add((o.A + (np - mid), o.B + (np - mid)));
            }
            extra = Math.Max(extra, Math.PI * o.Radius / n); // половина шага выборки
        }

        public bool HasMovers => sweepSegs.Count > 0;

        /// <summary>Шар (центр pos) касается зоны хода какого-либо движущегося объекта.</summary>
        public bool InMoverZone(Vec2 pos)
        {
            foreach (Vec2[] q in sweepQuads) if (Geo.PointInPolygon(pos, q)) return true;
            double lim = r + extra;
            foreach (var (a, b) in sweepSegs) if (Geo.DistToSegment(pos, a, b) < lim) return true;
            return false;
        }

        public static bool BallAtRest(Ball b) =>
            b.Pocketed || (b.Vel.Len < MaxSpeed && Math.Sqrt(b.Wx * b.Wx + b.Wy * b.Wy + b.Wz * b.Wz) < MaxSpin);

        public static bool AllAtRest(Solver s)
        {
            foreach (Ball b in s.Balls) if (!BallAtRest(b)) return false;
            return true;
        }

        /// <summary>Удар можно делать (и удар завершён): все шары стоят и ни один не лежит в зоне хода.</summary>
        public bool CanShoot(Solver s)
        {
            if (!AllAtRest(s)) return false;
            return StuckBalls(s).Count == 0;
        }

        /// <summary>Неподвижные шары, лежащие в зоне хода движущегося объекта.</summary>
        public List<Ball> StuckBalls(Solver s)
        {
            var l = new List<Ball>();
            if (!HasMovers) return l;
            foreach (Ball b in s.Balls) if (!b.Pocketed && InMoverZone(b.Pos)) l.Add(b);
            return l;
        }
    }
}
