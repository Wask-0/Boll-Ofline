using System;

namespace Boll.Core.Content
{
    public readonly struct Vec2
    {
        public readonly double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double k) => new Vec2(a.X * k, a.Y * k);
        public double Dot(Vec2 o) => X * o.X + Y * o.Y;
        public double Cross(Vec2 o) => X * o.Y - Y * o.X;
        public double Len => Math.Sqrt(X * X + Y * Y);
        public bool IsFinite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);
    }

    public static class Geo
    {
        public static double Area2(Vec2[] p)
        {
            double s = 0;
            for (int i = 0; i < p.Length; i++) s += p[i].Cross(p[(i + 1) % p.Length]);
            return s;
        }

        public static bool PointInPolygon(Vec2 pt, Vec2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vec2 a = poly[i], b = poly[j];
                if ((a.Y > pt.Y) != (b.Y > pt.Y) && pt.X < (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        public static double DistToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            double l2 = ab.Dot(ab);
            double t = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, (p - a).Dot(ab) / l2));
            return (p - (a + ab * t)).Len;
        }

        public static double DistToPolygonEdge(Vec2 p, Vec2[] poly)
        {
            double d = double.MaxValue;
            for (int i = 0; i < poly.Length; i++)
                d = Math.Min(d, DistToSegment(p, poly[i], poly[(i + 1) % poly.Length]));
            return d;
        }

        /// <summary>Собственное пересечение отрезков (общие концы соседних рёбер не считаются).</summary>
        public static bool SegmentsCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a);
            double d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        public static bool IsSimplePolygon(Vec2[] p)
        {
            int n = p.Length;
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegmentsCross(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n])) return false;
                }
            return Math.Abs(Area2(p)) > 1e-9;
        }
    }
}
