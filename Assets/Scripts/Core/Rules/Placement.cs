using System;
using System.Collections.Generic;
using Boll.Core.Content;
using Boll.Core.Physics;

namespace Boll.Core.Rules
{
    public enum HandMode { None, Anywhere, Home }

    /// <summary>Расстановка шаров, возврат шаров на стол, проверка точки «с руки».</summary>
    public static class Placement
    {
        const double Gap = 1.0001;

        public static Vec2 HomeCenter(MapData map)
        {
            double x = 0, y = 0;
            foreach (Vec2 p in map.Layout.Home) { x += p.X; y += p.Y; }
            return new Vec2(x / map.Layout.Home.Length, y / map.Layout.Home.Length);
        }

        /// <summary>Начальная расстановка (без битка). Пул: вершина в rackPoint; пирамида: в отметке "back".</summary>
        public static List<(int id, Vec2 pos)> Rack(GameKind kind, MapData map, Rng rng)
        {
            double d = map.BallDiameter * Gap, rowD = d * Math.Sqrt(3) / 2;
            Vec2 u = map.Layout.RackAxis, v = new Vec2(-u.Y, u.X);
            bool pyr = kind == GameKind.American || kind == GameKind.Siberian;
            Vec2 apex = pyr ? map.Layout.Marks["back"] : map.Layout.RackPoint;
            int[] rows = kind == GameKind.Nine ? new[] { 1, 2, 3, 2, 1 } : new[] { 1, 2, 3, 4, 5 };
            var pts = new List<Vec2>();
            for (int r = 0; r < rows.Length; r++)
                for (int j = 0; j < rows[r]; j++)
                    pts.Add(apex + u * (r * rowD) + v * ((j - (rows[r] - 1) / 2.0) * d));

            var ids = new int[pts.Count];
            var rest = new List<int>();
            if (kind == GameKind.Eight)
            {
                var solids = new List<int> { 1, 2, 3, 4, 5, 6, 7 }; var stripes = new List<int> { 9, 10, 11, 12, 13, 14, 15 };
                int s = solids[rng.Next(7)], t = stripes[rng.Next(7)];
                bool flip = rng.Toss();
                ids[10] = flip ? s : t; ids[14] = flip ? t : s; ids[4] = 8;
                for (int i = 1; i <= 15; i++) if (i != 8 && i != s && i != t) rest.Add(i);
            }
            else if (kind == GameKind.Nine)
            {
                ids[0] = 1; ids[4] = 9;
                for (int i = 2; i <= 8; i++) rest.Add(i);
            }
            else for (int i = 1; i <= 15; i++) rest.Add(i);
            if (kind != GameKind.Snooker) rng.Shuffle(rest);
            var res = new List<(int, Vec2)>();
            int k = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (ids[i] == 0) ids[i] = rest[k++];
                res.Add((ids[i], pts[i]));
            }
            if (kind == GameKind.Snooker)
                for (int c = 0; c < 6; c++) res.Add((16 + c, map.Layout.Marks[RulesEngine.SnookerColors[c]]));
            return res;
        }

        public static int BallCount(GameKind kind) => kind == GameKind.Nine ? 10 : kind == GameKind.Snooker ? 22 : 16; // с битком

        // ---- свободные точки ----
        public static bool IsFree(MapData map, Rest rest, double capture, Vec2 p, IList<Vec2> occupied)
        {
            double r = map.BallDiameter / 2;
            if (!Geo.PointInPolygon(p, map.Outline) || Geo.DistToPolygonEdge(p, map.Outline) < r) return false;
            foreach (Obstacle o in map.Obstacles)
            {
                if (o.Type == ObstacleType.Mover) continue;
                double dd = o.IsCircle ? (p - o.Center).Len - o.Radius : Geo.DistToSegment(p, o.A, o.B);
                if (dd < r) return false;
            }
            foreach (Pocket k in map.Pockets) if ((p - k.Pos).Len < k.Throat * capture / 2 + r * 0.25) return false;
            if (rest != null && rest.InMoverZone(p)) return false;
            foreach (Vec2 q in occupied) if ((p - q).Len < map.BallDiameter + 1e-6) return false;
            return true;
        }

        /// <summary>Куда поставить возвращаемый шар (CONCEPT §3, общие поправки).</summary>
        public static Vec2 FindSpot(MapData map, Rest rest, double capture, Vec2 own, IList<Vec2> occupied)
        {
            if (IsFree(map, rest, capture, own, occupied)) return own;
            foreach (Vec2 p in map.Layout.ReturnPoints) if (IsFree(map, rest, capture, p, occupied)) return p;
            Vec2 ax = map.Layout.RackAxis;
            for (int dir = 1; dir >= -1; dir -= 2)
                for (int i = 1; i <= 3000; i++)
                {
                    Vec2 p = own + ax * (dir * i * 0.001);
                    if (IsFree(map, rest, capture, p, occupied)) return p;
                }
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (Vec2 q in map.Outline) { minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X); minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
            double best = double.MaxValue; Vec2 bp = own;
            for (double x = Math.Ceiling(minX / 0.005) * 0.005; x <= maxX; x += 0.005)
                for (double y = Math.Ceiling(minY / 0.005) * 0.005; y <= maxY; y += 0.005)
                {
                    var p = new Vec2(x, y);
                    double d = (p - own).Len;
                    if (d < best && IsFree(map, rest, capture, p, occupied)) { best = d; bp = p; }
                }
            return bp;
        }

        public static bool IsValidHandSpot(MapData map, Rest rest, double capture, Vec2 p, HandMode mode, IList<Vec2> occupied)
        {
            if (mode == HandMode.None) return false;
            if (mode == HandMode.Home && !Geo.PointInPolygon(p, map.Layout.Home)) return false;
            return IsFree(map, rest, capture, p, occupied);
        }
    }
}
