using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Boll.Core.Content
{
    /// <summary>Разбор и проверка формата/геометрии карты. Любая ошибка — ContentException с путём.</summary>
    public static class MapLoader
    {
        static readonly string[] SnookerMarks = { "yellow", "green", "brown", "blue", "pink", "black" };

        public static MapData Load(string json)
        {
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception e) { throw new ContentException("map: bad JSON: " + e.Message); }

            var m = new MapData();
            m.Id = Str(o, "id");
            m.Name = Str(o, "name");
            string ks = Str(o, "kind");
            if (!Enum.TryParse(ks, true, out GameKind kind) || !Enum.IsDefined(typeof(GameKind), kind))
                throw Err(m, "kind '" + ks + "' unknown");
            m.Kind = kind;
            m.BallDiameter = Opt(o, "ballDiameter", KindDefaults.BallDiameter(kind));
            if (m.BallDiameter < 0.02 || m.BallDiameter > 0.12) throw Err(m, "ballDiameter out of range");

            Vec2[] outline = Poly(m, o, "outline", 3);
            if (!Geo.IsSimplePolygon(outline)) throw Err(m, "outline self-intersects or degenerate");
            if (Geo.Area2(outline) < 0) Array.Reverse(outline);
            m.Outline = outline;

            double defThroat = KindDefaults.Throat(kind);
            var pockets = new List<Pocket>();
            foreach (JToken t in Arr(m, o, "pockets", 4, 12))
            {
                var po = t as JObject ?? throw Err(m, "pockets: object expected");
                var p = new Pocket { Pos = P(m, po, "pos"), Throat = Opt(po, "throat", defThroat) };
                if (p.Throat < m.BallDiameter * 1.05 || p.Throat > 0.3) throw Err(m, "pocket throat out of range");
                if (!Geo.PointInPolygon(p.Pos, outline) && Geo.DistToPolygonEdge(p.Pos, outline) > p.Throat)
                    throw Err(m, "pocket far from table");
                pockets.Add(p);
            }
            m.Pockets = pockets.ToArray();

            var obs = new List<Obstacle>();
            if (o["obstacles"] is JArray oa)
                foreach (JToken t in oa) obs.Add(ParseObstacle(m, t as JObject ?? throw Err(m, "obstacles: object expected")));
            m.Obstacles = obs.ToArray();

            m.Layout = ParseLayout(m, o["layout"] as JObject ?? throw Err(m, "layout missing"));
            Validate(m);
            return m;
        }

        static Obstacle ParseObstacle(MapData m, JObject t)
        {
            string ty = Str(t, "type");
            var ob = new Obstacle();
            switch (ty)
            {
                case "wall":
                    ob.Type = ObstacleType.Wall; ob.A = P(m, t, "a"); ob.B = P(m, t, "b"); break;
                case "bumper":
                    ob.Type = ObstacleType.Bumper;
                    ob.Bounce = Req(t, "k");
                    if (ob.Bounce < 1.05 || ob.Bounce > 1.40) throw Err(m, "bumper k must be 1.05..1.40");
                    if (t["c"] != null)
                    {
                        ob.IsCircle = true; ob.Center = P(m, t, "c"); ob.Radius = Req(t, "r");
                        if (ob.Radius <= 0) throw Err(m, "bumper r must be > 0");
                    }
                    else { ob.A = P(m, t, "a"); ob.B = P(m, t, "b"); }
                    break;
                case "mover":
                    ob.Type = ObstacleType.Mover; ob.A = P(m, t, "a"); ob.B = P(m, t, "b");
                    ob.Speed = Req(t, "speed");
                    if (ob.Speed <= 0 || ob.Speed > 3) throw Err(m, "mover speed must be 0..3 m/s");
                    string mode = Str(t, "mode");
                    if (mode == "pingpong")
                    {
                        ob.Mode = MoverMode.PingPong;
                        var path = new List<Vec2>();
                        foreach (JToken pt in Arr(m, t, "path", 2, 64)) path.Add(PV(m, pt));
                        if (path[0].Len > 1e-9) throw Err(m, "mover path[0] must be [0,0]");
                        ob.Path = path.ToArray();
                    }
                    else if (mode == "circle")
                    {
                        ob.Mode = MoverMode.Circle; ob.Center = P(m, t, "center"); ob.Radius = Req(t, "radius");
                        if (ob.Radius <= 0) throw Err(m, "mover radius must be > 0");
                        Vec2 mid = (ob.A + ob.B) * 0.5;
                        if (Math.Abs((mid - ob.Center).Len - ob.Radius) > 1e-6) throw Err(m, "mover midpoint must lie on orbit");
                    }
                    else throw Err(m, "mover mode '" + mode + "' unknown");
                    break;
                default: throw Err(m, "obstacle type '" + ty + "' unknown");
            }
            if (ob.Type != ObstacleType.Bumper || !ob.IsCircle)
                if ((ob.B - ob.A).Len < 1e-6) throw Err(m, "zero-length obstacle");
            return ob;
        }

        static Layout ParseLayout(MapData m, JObject l)
        {
            var r = new Layout();
            r.RackPoint = P(m, l, "rackPoint");
            r.RackAxis = P(m, l, "rackAxis");
            double len = r.RackAxis.Len;
            if (Math.Abs(len - 1) > 1e-6) throw Err(m, "rackAxis must be unit vector");
            r.Home = Poly(m, l, "home", 3);
            r.HomeLineA = P(m, l, "homeLineA"); r.HomeLineB = P(m, l, "homeLineB");
            if (l["marks"] is JObject mk)
                foreach (var kv in mk) r.Marks[kv.Key] = PV(m, kv.Value);
            var rp = new List<Vec2>();
            foreach (JToken t in Arr(m, l, "returnPoints", 1, 64)) rp.Add(PV(m, t));
            r.ReturnPoints = rp.ToArray();
            return r;
        }

        static void Validate(MapData m)
        {
            double br = m.BallDiameter / 2;
            Layout l = m.Layout;
            if (m.Kind == GameKind.Snooker)
            {
                foreach (string k in SnookerMarks) if (!l.Marks.ContainsKey(k)) throw Err(m, "mark '" + k + "' missing");
            }
            else if (m.Kind == GameKind.American || m.Kind == GameKind.Siberian)
            {
                if (!l.Marks.ContainsKey("back")) throw Err(m, "mark 'back' missing");
            }

            var pts = new List<(string, Vec2)> { ("rackPoint", l.RackPoint) };
            foreach (var kv in l.Marks) pts.Add(("mark " + kv.Key, kv.Value));
            for (int i = 0; i < l.ReturnPoints.Length; i++) pts.Add(("returnPoint " + i, l.ReturnPoints[i]));
            foreach (Vec2 h in l.Home) pts.Add(("home vertex", h));

            foreach (var (name, p) in pts)
            {
                if (!Geo.PointInPolygon(p, m.Outline) || Geo.DistToPolygonEdge(p, m.Outline) < br)
                    throw Err(m, name + " outside table");
                foreach (Obstacle ob in m.Obstacles)
                {
                    if (ob.Type == ObstacleType.Mover) continue; // зона хода проверяется в Rest / проверке карт
                    double d = ob.IsCircle ? (p - ob.Center).Len - ob.Radius : Geo.DistToSegment(p, ob.A, ob.B);
                    if (d < br) throw Err(m, name + " blocked by obstacle");
                }
            }
            foreach (Obstacle ob in m.Obstacles)
            {
                if (ob.Type == ObstacleType.Mover) continue;
                if (ob.IsCircle)
                {
                    if (!Geo.PointInPolygon(ob.Center, m.Outline)) throw Err(m, "bumper outside table");
                }
                else if (!Geo.PointInPolygon(ob.A, m.Outline) && Geo.DistToPolygonEdge(ob.A, m.Outline) > 1e-9
                      || !Geo.PointInPolygon(ob.B, m.Outline) && Geo.DistToPolygonEdge(ob.B, m.Outline) > 1e-9)
                    throw Err(m, "obstacle outside table");
            }
        }

        // ---- JSON helpers ----
        static ContentException Err(MapData m, string msg) => new ContentException("map " + (m.Id ?? "?") + ": " + msg);

        static string Str(JObject o, string k)
        {
            if (!(o[k] is JValue v) || v.Type != JTokenType.String || string.IsNullOrEmpty((string)v))
                throw new ContentException("map: field '" + k + "' missing or not a string");
            return (string)v;
        }

        static double Req(JObject o, string k)
        {
            if (!(o[k] is JValue v) || (v.Type != JTokenType.Float && v.Type != JTokenType.Integer))
                throw new ContentException("map: field '" + k + "' missing or not a number");
            return (double)v;
        }

        static double Opt(JObject o, string k, double def) => o[k] == null ? def : Req(o, k);

        static JArray Arr(MapData m, JObject o, string k, int min, int max)
        {
            if (!(o[k] is JArray a) || a.Count < min || a.Count > max) throw Err(m, k + ": array of " + min + ".." + max + " expected");
            return a;
        }

        static Vec2 PV(MapData m, JToken t)
        {
            if (!(t is JArray a) || a.Count != 2 || !IsNum(a[0]) || !IsNum(a[1])) throw Err(m, "point [x,y] expected");
            var v = new Vec2((double)a[0], (double)a[1]);
            if (!v.IsFinite || Math.Abs(v.X) > 10 || Math.Abs(v.Y) > 10) throw Err(m, "point out of range");
            return v;
        }

        static bool IsNum(JToken t) => t.Type == JTokenType.Float || t.Type == JTokenType.Integer;

        static Vec2 P(MapData m, JObject o, string k) => PV(m, o[k] ?? throw Err(m, k + " missing"));

        static Vec2[] Poly(MapData m, JObject o, string k, int min)
        {
            var a = Arr(m, o, k, min, 256);
            var r = new Vec2[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = PV(m, a[i]);
            return r;
        }
    }
}
