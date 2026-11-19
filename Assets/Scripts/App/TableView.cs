using System;
using System.Collections.Generic;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Physics;
using UnityEngine;
using UnityEngine.UI;

namespace Boll.App
{
    /// <summary>Спрайты, нарисованные из кода (никаких внешних картинок и шейдеров).</summary>
    public static class Sprites
    {
        static Sprite white, circle, ring; static readonly Dictionary<string, Sprite> balls = new Dictionary<string, Sprite>();

        static Sprite Make(int n, Func<float, float, Color> f, float ppu)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n * 2 - 1, (y + 0.5f) / n * 2 - 1);
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), ppu);
        }

        public static Sprite White => white != null ? white : (white = Make(2, (u, v) => Color.white, 2));
        public static Sprite Circle => circle != null ? circle : (circle = Make(128, (u, v) => new Color(1, 1, 1, Mathf.Clamp01((1 - Mathf.Sqrt(u * u + v * v)) * 64)), 128));
        public static Sprite Ring => ring != null ? ring : (ring = Make(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return new Color(1, 1, 1, Mathf.Clamp01((1 - r) * 64) * Mathf.Clamp01((r - 0.86f) * 64));
        }, 128));

        public static Sprite Ball(Color body, bool stripe, bool disc)
        {
            string key = ColorUtility.ToHtmlStringRGB(body) + stripe + disc;
            if (balls.TryGetValue(key, out Sprite s)) return s;
            Color white = new Color(0.96f, 0.95f, 0.9f);
            s = Make(64, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((1 - r) * 32);
                Color c = body;
                if (stripe && Mathf.Abs(v) >= 0.5f) c = white;
                if (disc && r < 0.46f) c = white;
                float hl = Mathf.Exp(-((u + 0.35f) * (u + 0.35f) + (v - 0.35f) * (v - 0.35f)) * 12) * 0.35f;
                float sh = 1 - 0.28f * r * r;
                return new Color(Mathf.Clamp01(c.r * sh + hl), Mathf.Clamp01(c.g * sh + hl), Mathf.Clamp01(c.b * sh + hl), a);
            }, 64);
            balls[key] = s;
            return s;
        }
    }

    /// <summary>Рисует стол и шары в мировых координатах (1 ед. = 1 м) и переводит экран ↔ мир.</summary>
    public sealed class TableView
    {
        readonly Match m; readonly GameObject root; readonly double R; readonly RectTransform numberLayer;
        readonly Dictionary<int, (Transform t, Text num)> balls = new Dictionary<int, (Transform, Text)>();
        readonly List<(int idx, Transform t, Obstacle o)> movers = new List<(int, Transform, Obstacle)>();
        readonly Dictionary<int, Transform> shadows = new Dictionary<int, Transform>();
        readonly List<Transform> pocketRings = new List<Transform>(); readonly List<Transform> homeSegs = new List<Transform>();
        readonly Transform cueT, cueButt, aim1, aim2, aim3, ghost, selRing, calledRing;
        readonly SpriteRenderer cueSr;
        double minX, maxX, minY, maxY;
        float scale = 100, camX, camY; int sw, sh; Rect region;
        public Camera Cam;

        public TableView(Match match, Color cloth, Color cue, RectTransform numbers, Camera cam)
        {
            m = match; R = m.Map.BallDiameter / 2; numberLayer = numbers; Cam = cam;
            root = new GameObject("Table");
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            foreach (Vec2 p in m.Map.Outline) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            BuildCloth(cloth);
            foreach (Pocket p in m.Map.Pockets) Circle("pocket", p.Pos, p.Throat * 1.05, new Color(0.03f, 0.03f, 0.03f), 1);
            Obstacle[] obs = m.Map.Obstacles;
            for (int i = 0; i < obs.Length; i++)
            {
                Obstacle o = obs[i];
                if (o.IsCircle) { Circle("bumper", o.Center, o.Radius * 2, new Color(0.95f, 0.55f, 0.15f), 2); continue; }
                Color c = o.Type == ObstacleType.Mover ? new Color(0.35f, 0.65f, 0.95f) : o.Type == ObstacleType.Bumper ? new Color(0.95f, 0.55f, 0.15f) : new Color(0.55f, 0.38f, 0.22f);
                Transform t = NewSprite("obstacle", Sprites.White, c, 2);
                Seg(t, o.A, o.B, o.Type == ObstacleType.Wall ? 0.018f : 0.026f);
                if (o.Type == ObstacleType.Mover) movers.Add((i, t, o));
            }
            if (m.Map.Layout.Home != null)
                for (int i = 0; i < m.Map.Layout.Home.Length; i++)
                {
                    Transform t = NewSprite("home", Sprites.White, new Color(1, 1, 1, 0.35f), 1);
                    Seg(t, m.Map.Layout.Home[i], m.Map.Layout.Home[(i + 1) % m.Map.Layout.Home.Length], 0.006f);
                    homeSegs.Add(t); t.gameObject.SetActive(false);
                }
            foreach (Ball b in m.Solver.Balls)
            {
                Transform sh = NewSprite("shadow" + b.Id, Sprites.Circle, new Color(0, 0, 0, 0.3f), 4);
                sh.localScale = Vector3.one * (float)(R * 2.1);
                shadows[b.Id] = sh;
                Transform t = NewSprite("ball" + b.Id, BallSprite(b.Id, out bool numbered), Color.white, 5);
                t.localScale = Vector3.one * (float)(R * 2);
                Text num = null;
                if (numbered && numbers != null)
                {
                    num = Ui.Label(numbers, b.Id.ToString(), 20, TextAnchor.MiddleCenter, new Color(0.08f, 0.08f, 0.08f));
                    num.fontStyle = FontStyle.Bold;
                }
                balls[b.Id] = (t, num);
            }
            cueT = NewSprite("cue", Sprites.White, cue, 7); cueSr = cueT.GetComponent<SpriteRenderer>(); cueT.gameObject.SetActive(false);
            cueButt = NewSprite("cueButt", Sprites.White, cue * 0.55f + new Color(0, 0, 0, 0.45f), 7); cueButt.gameObject.SetActive(false);
            aim1 = NewSprite("aim1", Sprites.White, new Color(1, 1, 1, 0.55f), 3); aim2 = NewSprite("aim2", Sprites.White, new Color(1, 1, 0.6f, 0.45f), 3); aim3 = NewSprite("aim3", Sprites.White, new Color(1, 1, 1, 0.35f), 3);
            ghost = NewSprite("ghost", Sprites.Ring, new Color(1, 1, 1, 0.7f), 3); ghost.localScale = Vector3.one * (float)(R * 2);
            selRing = NewSprite("sel", Sprites.Ring, new Color(0.95f, 0.8f, 0.3f), 6); selRing.localScale = Vector3.one * (float)(R * 2.8); selRing.gameObject.SetActive(false);
            calledRing = NewSprite("called", Sprites.Ring, new Color(0.95f, 0.8f, 0.3f), 4); calledRing.gameObject.SetActive(false);
            HideAim();
        }

        // ---------- построение ----------
        Color BallColor(int id, out bool stripe, out bool disc)
        {
            stripe = false; disc = false;
            GameKind k = m.Kind;
            Color[] pool = { new Color(0.93f, 0.75f, 0.1f), new Color(0.1f, 0.3f, 0.75f), new Color(0.8f, 0.15f, 0.1f), new Color(0.42f, 0.17f, 0.57f),
                             new Color(0.92f, 0.46f, 0.1f), new Color(0.1f, 0.5f, 0.22f), new Color(0.48f, 0.12f, 0.12f), new Color(0.07f, 0.07f, 0.08f) };
            Color ivory = new Color(0.95f, 0.93f, 0.85f);
            if (id == 0) return k == GameKind.American || k == GameKind.Siberian ? new Color(0.85f, 0.25f, 0.18f) : ivory;
            if (k == GameKind.Snooker)
            {
                Color[] sn = { new Color(0.95f, 0.85f, 0.1f), new Color(0.1f, 0.6f, 0.25f), new Color(0.5f, 0.3f, 0.12f), new Color(0.1f, 0.3f, 0.8f), new Color(0.95f, 0.55f, 0.7f), new Color(0.07f, 0.07f, 0.08f) };
                return id <= 15 ? new Color(0.78f, 0.1f, 0.1f) : sn[Mathf.Clamp(id - 16, 0, 5)];
            }
            if (k == GameKind.American || k == GameKind.Siberian) { disc = true; return ivory; }
            disc = true;
            if (id >= 9) { stripe = true; return pool[Mathf.Clamp(id - 9, 0, 6)]; }
            return pool[Mathf.Clamp(id - 1, 0, 7)];
        }

        Sprite BallSprite(int id, out bool numbered)
        {
            Color c = BallColor(id, out bool stripe, out bool disc);
            numbered = disc && id > 0;
            return Sprites.Ball(c, stripe, disc && id > 0);
        }

        Transform NewSprite(string name, Sprite s, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = s; sr.color = c; sr.sortingOrder = order;
            return go.transform;
        }

        void Circle(string name, Vec2 p, double diameter, Color c, int order)
        {
            Transform t = NewSprite(name, Sprites.Circle, c, order);
            t.position = V(p); t.localScale = Vector3.one * (float)diameter;
        }

        static Vector3 V(Vec2 p) => new Vector3((float)p.X, (float)p.Y, 0);

        static void Seg(Transform t, Vec2 a, Vec2 b, float thick)
        {
            Vec2 d = b - a; double len = d.Len;
            t.position = V((a + b) * 0.5);
            t.rotation = Quaternion.Euler(0, 0, (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI));
            t.localScale = new Vector3((float)len, thick, 1);
        }

        void BuildCloth(Color cloth)
        {
            const double margin = 0.09;
            double w = maxX - minX + 2 * margin, h = maxY - minY + 2 * margin;
            double ppm = Math.Min(260, 2000 / Math.Max(w, h));
            int tw = (int)Math.Ceiling(w * ppm), th = (int)Math.Ceiling(h * ppm);
            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[tw * th];
            Color32 wood = new Color32(92, 58, 32, 255), edge = (Color32)(cloth * 0.72f), clear = new Color32(0, 0, 0, 0), main = (Color32)cloth;
            Vec2[] o = m.Map.Outline;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    var p = new Vec2(minX - margin + (x + 0.5) / ppm, minY - margin + (y + 0.5) / ppm);
                    bool inside = Geo.PointInPolygon(p, o);
                    double d = Geo.DistToPolygonEdge(p, o);
                    px[y * tw + x] = inside ? (d < 0.014 ? edge : main) : (d < margin * 0.8 ? wood : clear);
                }
            tex.SetPixels32(px); tex.Apply();
            var go = new GameObject("cloth"); go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tw, th), Vector2.zero, (float)ppm);
            sr.sortingOrder = 0;
            go.transform.position = new Vector3((float)(minX - margin), (float)(minY - margin), 0);
        }

        // ---------- камера ----------
        /// <summary>region — область экрана (в пикселях, начало внизу слева), в которую вписывается стол.</summary>
        public void SetRegion(Rect r)
        {
            region = r; sw = Screen.width; sh = Screen.height;
            double tw = maxX - minX + 0.25, th = maxY - minY + 0.25;
            scale = (float)Math.Min(r.width / tw, r.height / th);
            Cam.orthographicSize = sh / (2f * scale);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            camX = (float)(cx - (r.center.x - sw / 2f) / scale); camY = (float)(cy - (r.center.y - sh / 2f) / scale);
            Cam.transform.position = new Vector3(camX, camY, -10);
        }

        public bool NeedsRefit => Screen.width != sw || Screen.height != sh;
        public Rect Region => region;
        public Vector2 ToScreen(Vec2 w) => new Vector2(sw / 2f + ((float)w.X - camX) * scale, sh / 2f + ((float)w.Y - camY) * scale);
        public Vec2 ToWorld(Vector2 s) => new Vec2(camX + (s.x - sw / 2f) / scale, camY + (s.y - sh / 2f) / scale);
        public float PixelsPerMeter => scale;

        // ---------- кадр ----------
        public void Sync(float canvasScale)
        {
            foreach (Ball b in m.Solver.Balls)
            {
                var (t, num) = balls[b.Id];
                bool vis = !b.Pocketed;
                t.gameObject.SetActive(vis);
                shadows[b.Id].gameObject.SetActive(vis);
                shadows[b.Id].position = V(b.Pos + new Vec2(R * 0.18, -R * 0.22));
                if (num != null) num.gameObject.SetActive(vis);
                if (!vis) continue;
                t.position = V(b.Pos);
                if (num != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(numberLayer, ToScreen(b.Pos), null, out Vector2 lp);
                    num.rectTransform.anchoredPosition = lp;
                    num.fontSize = Mathf.Max(8, Mathf.RoundToInt((float)(R * 2) * scale / canvasScale * 0.42f));
                }
            }
            foreach (var (idx, t, o) in movers)
            {
                Vec2 off = m.Solver.MoverSegmentOffset(idx);
                Seg(t, o.A + off, o.B + off, 0.026f);
            }
        }

        public void ShowHome(bool on) { foreach (Transform t in homeSegs) t.gameObject.SetActive(on); }

        public void ShowSelection(Vec2? p) { selRing.gameObject.SetActive(p.HasValue); if (p.HasValue) selRing.position = V(p.Value); }

        public void ShowCalled(int pocket)
        {
            calledRing.gameObject.SetActive(pocket >= 0);
            if (pocket < 0) return;
            Pocket p = m.Map.Pockets[pocket];
            calledRing.position = V(p.Pos); calledRing.localScale = Vector3.one * (float)(p.Throat * 1.7);
        }

        public void ShowCue(bool on, Vec2 ball, double angle, double pull)
        {
            cueT.gameObject.SetActive(on); cueButt.gameObject.SetActive(on);
            if (!on) return;
            Vec2 dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
            double gap = R + 0.01 + pull;
            Vec2 tip = ball - dir * gap, mid = ball - dir * (gap + 0.9), butt = ball - dir * (gap + 1.45);
            Seg(cueT, tip, mid, 0.011f);          // шафт тоньше, рукоять толще и темнее
            Seg(cueButt, mid, butt, 0.021f);
        }

        public void HideAim() { aim1.gameObject.SetActive(false); aim2.gameObject.SetActive(false); aim3.gameObject.SetActive(false); ghost.gameObject.SetActive(false); }

        public void ShowAim(Vec2 origin, int cueId, double angle, bool bounce)
        {
            Vec2 dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
            Hit h = Cast(origin, dir, cueId);
            Vec2 end = origin + dir * Math.Min(h.T, 6);
            aim1.gameObject.SetActive(true); Seg(aim1, origin + dir * R, end, 0.008f);
            ghost.gameObject.SetActive(h.Kind != 0); ghost.position = V(end);
            aim2.gameObject.SetActive(false); aim3.gameObject.SetActive(false);
            if (!bounce || h.Kind == 0) return;
            if (h.Kind == 2)
            {
                Vec2 n = h.Normal; double dn = dir.Dot(n);
                Vec2 d2 = new Vec2(dir.X - 2 * dn * n.X, dir.Y - 2 * dn * n.Y);
                Hit h2 = Cast(end, d2, cueId);
                aim2.gameObject.SetActive(true); Seg(aim2, end, end + d2 * Math.Min(h2.T, 1.2), 0.008f);
            }
            else
            {
                Vec2 c = new Vec2(h.Ball.X - end.X, h.Ball.Y - end.Y); double l = c.Len; if (l < 1e-9) return;
                Vec2 n = c * (1 / l);
                aim2.gameObject.SetActive(true); Seg(aim2, h.Ball, h.Ball + n * 0.45, 0.008f);
                double along = dir.Dot(n); Vec2 tg = new Vec2(dir.X - along * n.X, dir.Y - along * n.Y); double tl = tg.Len;
                if (tl > 1e-6) { aim3.gameObject.SetActive(true); Seg(aim3, end, end + tg * (0.3 / tl), 0.008f); }
            }
        }

        // ---------- луч по столу (круг радиуса R, движущийся по прямой) ----------
        struct Hit { public double T; public int Kind; public Vec2 Normal, Ball; }

        Hit Cast(Vec2 o, Vec2 d, int cueId)
        {
            var best = new Hit { T = 1e9 };
            foreach (Ball b in m.Solver.Balls)
            {
                if (b.Pocketed || b.Id == cueId) continue;
                double t = RayCircle(o, d, b.Pos, 2 * R);
                if (t >= 0 && t < best.T) best = new Hit { T = t, Kind = 1, Ball = b.Pos };
            }
            foreach (Vec2[] seg in Segments(true)) Seg2(ref best, o, d, seg[0], seg[1]);
            foreach (Obstacle ob in m.Map.Obstacles)
            {
                if (ob.IsCircle)
                {
                    double t = RayCircle(o, d, ob.Center, ob.Radius + R);
                    if (t >= 0 && t < best.T) { Vec2 p = o + d * t; Vec2 n = p - ob.Center; double l = n.Len; best = new Hit { T = t, Kind = 2, Normal = l > 0 ? n * (1 / l) : n }; }
                }
            }
            return best;
        }

        IEnumerable<Vec2[]> Segments(bool all)
        {
            Vec2[] ol = m.Map.Outline;
            for (int i = 0; i < ol.Length; i++) yield return new[] { ol[i], ol[(i + 1) % ol.Length] };
            for (int i = 0; i < m.Map.Obstacles.Length; i++)
            {
                Obstacle ob = m.Map.Obstacles[i];
                if (ob.IsCircle) continue;
                Vec2 off = ob.Type == ObstacleType.Mover ? m.Solver.MoverSegmentOffset(i) : default;
                yield return new[] { ob.A + off, ob.B + off };
            }
        }

        static double RayCircle(Vec2 o, Vec2 d, Vec2 c, double rad)
        {
            Vec2 f = o - c; double b = f.Dot(d), cc = f.Dot(f) - rad * rad;
            double disc = b * b - cc; if (disc < 0 || cc < 0) return -1;
            double t = -b - Math.Sqrt(disc);
            return t >= 0 ? t : -1;
        }

        void Seg2(ref Hit best, Vec2 o, Vec2 d, Vec2 a, Vec2 b)
        {
            Vec2 e = b - a; double len = e.Len; if (len < 1e-9) return;
            Vec2 u = e * (1 / len);
            for (int s = -1; s <= 1; s += 2)
            {
                Vec2 n = new Vec2(-u.Y * s, u.X * s);
                Vec2 a2 = a + n * R;
                double den = d.Cross(u);
                if (Math.Abs(den) < 1e-9) continue;
                Vec2 w = a2 - o;
                double t = w.Cross(u) / den, q = w.Cross(d) / den;
                if (t >= 0 && t < best.T && q >= 0 && q <= len && d.Dot(n) < 0) best = new Hit { T = t, Kind = 2, Normal = n };
            }
            foreach (Vec2 p in new[] { a, b })
            {
                double t = RayCircle(o, d, p, R);
                if (t >= 0 && t < best.T) { Vec2 q = o + d * t - p; double l = q.Len; best = new Hit { T = t, Kind = 2, Normal = l > 0 ? q * (1 / l) : q }; }
            }
        }

        public void Destroy()
        {
            UnityEngine.Object.Destroy(root);
            foreach (var kv in balls) if (kv.Value.num != null) UnityEngine.Object.Destroy(kv.Value.num.gameObject);
        }
    }
}
