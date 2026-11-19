using System;
using System.Collections.Generic;
using Boll.Core.Physics;
using Boll.Core.Rules;

namespace Boll.Core.Content
{
    /// <summary>Автоматическая проверка карты (CONCEPT §4): разметка не перекрыта движущимися, лузы достижимы, зазоры движущихся.</summary>
    public static class MapChecks
    {
        public static List<string> Run(MapData map, PhysicsModel model)
        {
            var errs = new List<string>();
            CheckLayoutVsMovers(map, errs);
            CheckMoverGaps(map, errs);
            CheckPocketsReachable(map, model, errs);
            return errs;
        }

        static double R(MapData m) => m.BallDiameter / 2;

        static void CheckLayoutVsMovers(MapData map, List<string> errs)
        {
            var rest = new Rest(map);
            if (!rest.HasMovers) return;
            var pts = new List<(string, Vec2)>();
            Layout l = map.Layout;
            foreach (var kv in l.Marks) pts.Add(("mark " + kv.Key, kv.Value));
            for (int i = 0; i < l.ReturnPoints.Length; i++) pts.Add(("returnPoint " + i, l.ReturnPoints[i]));
            foreach (Vec2 h in l.Home) pts.Add(("home", h));
            Vec2 c = Placement.HomeCenter(map); pts.Add(("home center", c));
            var rack = Placement.Rack(map.Kind, map, new Rng(1));
            foreach (var (id, p) in rack) pts.Add(("rack ball " + id, p));
            foreach (var (n, p) in pts) if (rest.InMoverZone(p)) errs.Add(map.Id + ": " + n + " in mover zone");
            // шарик и лузы: подвижный не должен закрывать лузу
            foreach (Pocket p in map.Pockets) if (rest.InMoverZone(p.Pos)) errs.Add(map.Id + ": pocket in mover zone");
        }

        static double SegSeg(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            if (Geo.SegmentsCross(a, b, c, d)) return 0;
            return Math.Min(Math.Min(Geo.DistToSegment(a, c, d), Geo.DistToSegment(b, c, d)),
                            Math.Min(Geo.DistToSegment(c, a, b), Geo.DistToSegment(d, a, b)));
        }

        /// <summary>Зазор между движущимся объектом и неподвижными частями ≥ 3 радиусов шара: шар не зажать.</summary>
        static void CheckMoverGaps(MapData map, List<string> errs)
        {
            double minGap = 3 * R(map);
            for (int oi = 0; oi < map.Obstacles.Length; oi++)
            {
                Obstacle o = map.Obstacles[oi];
                if (o.Type != ObstacleType.Mover) continue;
                var s = new Solver(map, new PhysicsModel { Id = "x", SlidingFriction = 0.1, RollingFriction = 0.01, CushionRestitution = 0.8, BallRestitution = 0.9, PocketCapture = 1, CueSpeed = 1, SpinDecay = 1, SpinInfluence = 1 });
                double period = o.Mode == MoverMode.Circle ? 2 * Math.PI * o.Radius / o.Speed : 0;
                if (o.Mode == MoverMode.PingPong)
                {
                    double len = 0; for (int i = 1; i < o.Path.Length; i++) len += (o.Path[i] - o.Path[i - 1]).Len;
                    period = 2 * len / o.Speed;
                }
                for (int k = 0; k < 240; k++)
                {
                    Vec2 off = s.MoverOffset(oi, period * k / 240.0);
                    Vec2 a = o.A + off, b = o.B + off;
                    for (int i = 0; i < map.Outline.Length; i++)
                        if (SegSeg(a, b, map.Outline[i], map.Outline[(i + 1) % map.Outline.Length]) < minGap)
                        { errs.Add(map.Id + ": mover " + oi + " too close to rail " + i); k = 240; break; }
                    foreach (Obstacle st in map.Obstacles)
                    {
                        if (st.Type == ObstacleType.Mover) continue;
                        double d = st.IsCircle ? Geo.DistToSegment(st.Center, a, b) - st.Radius : SegSeg(a, b, st.A, st.B);
                        if (d < minGap) { errs.Add(map.Id + ": mover " + oi + " too close to static obstacle"); k = 240; break; }
                    }
                    foreach (Pocket p in map.Pockets)
                        if (Geo.DistToSegment(p.Pos, a, b) < p.Throat) { errs.Add(map.Id + ": mover " + oi + " too close to pocket"); k = 240; break; }
                }
            }
        }

        /// <summary>У каждой лузы есть не меньше 10 точек стола, из которых путь центра шара по прямой до лузы свободен.</summary>
        static void CheckPocketsReachable(MapData map, PhysicsModel model, List<string> errs)
        {
            double r = R(map);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (Vec2 q in map.Outline) { minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X); minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
            for (int pi = 0; pi < map.Pockets.Length; pi++)
            {
                Pocket p = map.Pockets[pi];
                double pr = p.Throat * model.PocketCapture / 2;
                int ok = 0;
                for (double x = minX; x <= maxX && ok < 10; x += 0.1)
                    for (double y = minY; y <= maxY && ok < 10; y += 0.1)
                    {
                        var s = new Vec2(x, y);
                        if (!Free(map, s, r) || (s - p.Pos).Len < pr * 3) continue;
                        bool clear = true;
                        double dist = (p.Pos - s).Len;
                        for (double t = 0; t <= dist - pr; t += 0.01)
                        {
                            Vec2 q = s + (p.Pos - s) * (t / dist);
                            if (!Free(map, q, r)) { clear = false; break; }
                        }
                        if (clear) ok++;
                    }
                if (ok < 10) errs.Add(map.Id + ": pocket " + pi + " hardly reachable (" + ok + ")");
            }
        }

        static bool Free(MapData map, Vec2 p, double r)
        {
            if (!Geo.PointInPolygon(p, map.Outline) || Geo.DistToPolygonEdge(p, map.Outline) < r * 0.999) return false;
            foreach (Obstacle o in map.Obstacles)
            {
                if (o.Type == ObstacleType.Mover) continue;
                double d = o.IsCircle ? (p - o.Center).Len - o.Radius : Geo.DistToSegment(p, o.A, o.B);
                if (d < r) return false;
            }
            return true;
        }
    }
}
