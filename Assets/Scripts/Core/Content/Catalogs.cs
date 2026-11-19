using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Boll.Core.Content
{
    public sealed class PhysicsModel
    {
        public string Id, Name;
        public double SlidingFriction, RollingFriction, SpinDecay, SpinInfluence,
                      CushionRestitution, BallRestitution, PocketCapture, CueSpeed;
    }

    public enum SpinSkill { None, TopBack, Full }
    public enum Fallback { None, IfNoShot, Tactical }

    public sealed class BotLevel
    {
        public int Level;
        public string Name;
        public double AngleSpreadDeg;
        public SpinSkill Spin;
        public int Lookahead;        // 0 = не смотрит вперёд
        public Fallback Fallback;
        public bool Clusters;
    }

    public sealed class Skin
    {
        public string Id, Name, Image;
        public int Price;
        public string Color;         // сукно: #RRGGBB
    }

    public sealed class Catalogs
    {
        public PhysicsModel[] Models;
        public string DefaultModel;
        public BotLevel[] Bots;
        public Skin[] Cloths, Cues;
        public PhysicsModel Model(string id) => Models.FirstOrDefault(m => m.Id == id);
    }

    public static class CatalogLoader
    {
        public static PhysicsModel[] LoadPhysics(string json, out string defaultId)
        {
            JObject o = Parse(json, "physics");
            defaultId = S(o, "default", "physics");
            var list = new List<PhysicsModel>();
            foreach (JToken t in A(o, "models", "physics"))
            {
                var j = (JObject)t;
                var m = new PhysicsModel
                {
                    Id = S(j, "id", "physics"), Name = S(j, "name", "physics"),
                    SlidingFriction = N(j, "slidingFriction", "physics"), RollingFriction = N(j, "rollingFriction", "physics"),
                    SpinDecay = N(j, "spinDecay", "physics"), SpinInfluence = N(j, "spinInfluence", "physics"),
                    CushionRestitution = N(j, "cushionRestitution", "physics"), BallRestitution = N(j, "ballRestitution", "physics"),
                    PocketCapture = N(j, "pocketCapture", "physics"), CueSpeed = N(j, "cueSpeed", "physics"),
                };
                if (m.SlidingFriction <= 0 || m.RollingFriction <= 0 || m.SpinDecay < 0 || m.SpinInfluence < 0 || m.CueSpeed <= 0
                    || m.CushionRestitution <= 0 || m.CushionRestitution > 1 || m.BallRestitution <= 0 || m.BallRestitution > 1
                    || m.PocketCapture < 0.5 || m.PocketCapture > 2)
                    throw new ContentException("physics " + m.Id + ": value out of range");
                list.Add(m);
            }
            if (list.Count != 3 || list.Select(m => m.Id).Distinct().Count() != 3) throw new ContentException("physics: need 3 unique models");
            string def = defaultId;
            if (list.All(m => m.Id != def)) throw new ContentException("physics: default model missing");
            return list.ToArray();
        }

        public static BotLevel[] LoadBots(string json)
        {
            JObject o = Parse(json, "bots");
            var list = new List<BotLevel>();
            foreach (JToken t in A(o, "levels", "bots"))
            {
                var j = (JObject)t;
                var b = new BotLevel
                {
                    Level = (int)N(j, "level", "bots"), Name = S(j, "name", "bots"),
                    AngleSpreadDeg = N(j, "angleSpreadDeg", "bots"),
                    Spin = Enum<SpinSkill>(S(j, "spin", "bots")),
                    Lookahead = (int)N(j, "lookahead", "bots"),
                    Fallback = Enum<Fallback>(S(j, "fallback", "bots")),
                    Clusters = (bool)(j["clusters"] ?? throw new ContentException("bots: clusters missing")),
                };
                if (b.AngleSpreadDeg <= 0 || b.Lookahead < 0 || b.Lookahead > 8) throw new ContentException("bots: level " + b.Level + " out of range");
                list.Add(b);
            }
            if (list.Count != 8) throw new ContentException("bots: need 8 levels");
            for (int i = 0; i < 8; i++) if (list[i].Level != i + 1) throw new ContentException("bots: levels must be 1..8 in order");
            return list.ToArray();
        }

        public static void LoadSkins(string json, out Skin[] cloths, out Skin[] cues)
        {
            JObject o = Parse(json, "skins");
            cloths = Skins(o, "cloths");
            cues = Skins(o, "cues");
        }

        public static Catalogs LoadAll(string physicsJson, string botsJson, string skinsJson)
        {
            var c = new Catalogs { Models = LoadPhysics(physicsJson, out string def), DefaultModel = def, Bots = LoadBots(botsJson) };
            LoadSkins(skinsJson, out c.Cloths, out c.Cues);
            return c;
        }

        static Skin[] Skins(JObject o, string key)
        {
            var list = new List<Skin>();
            foreach (JToken t in A(o, key, "skins"))
            {
                var j = (JObject)t;
                var s = new Skin { Id = S(j, "id", "skins"), Name = S(j, "name", "skins"), Image = S(j, "image", "skins"), Price = (int)N(j, "price", "skins") };
                s.Color = j["color"] != null ? S(j, "color", "skins") : null;
                if (s.Price < 0) throw new ContentException("skins: negative price");
                list.Add(s);
            }
            if (list.Count == 0 || list[0].Price != 0) throw new ContentException("skins " + key + ": first must be free default");
            if (list.Select(s => s.Id).Distinct().Count() != list.Count) throw new ContentException("skins " + key + ": duplicate id");
            return list.ToArray();
        }

        static T Enum<T>(string s) where T : struct
        {
            if (!System.Enum.TryParse(s, true, out T v)) throw new ContentException("bad enum value '" + s + "'");
            return v;
        }

        static JObject Parse(string json, string what)
        {
            try { return JObject.Parse(json); }
            catch (Exception e) { throw new ContentException(what + ": bad JSON: " + e.Message); }
        }

        static string S(JObject o, string k, string w)
        {
            if (!(o[k] is JValue v) || v.Type != JTokenType.String) throw new ContentException(w + ": '" + k + "' missing");
            return (string)v;
        }

        static double N(JObject o, string k, string w)
        {
            if (!(o[k] is JValue v) || (v.Type != JTokenType.Float && v.Type != JTokenType.Integer)) throw new ContentException(w + ": '" + k + "' missing");
            return (double)v;
        }

        static JArray A(JObject o, string k, string w) => o[k] as JArray ?? throw new ContentException(w + ": '" + k + "' missing");
    }

    /// <summary>Набор загруженных карт. Источник текстов даёт App (Resources.LoadAll).</summary>
    public sealed class MapCatalog
    {
        readonly Dictionary<GameKind, List<MapData>> byKind = new Dictionary<GameKind, List<MapData>>();

        public MapCatalog(IEnumerable<(GameKind kind, string json)> sources)
        {
            foreach (var (kind, json) in sources)
            {
                MapData m = MapLoader.Load(json);
                if (m.Kind != kind) throw new ContentException("map " + m.Id + ": kind differs from folder");
                if (!byKind.TryGetValue(kind, out var l)) byKind[kind] = l = new List<MapData>();
                if (l.Any(x => x.Id == m.Id)) throw new ContentException("map " + m.Id + ": duplicate id");
                l.Add(m);
            }
        }

        public IReadOnlyList<MapData> Maps(GameKind k) => byKind.TryGetValue(k, out var l) ? l : (IReadOnlyList<MapData>)new MapData[0];
        public int Count => byKind.Values.Sum(l => l.Count);

        /// <summary>Случайная другая карта того же вида (после победы). pick — индекс из диапазона [0,n).</summary>
        public MapData Next(GameKind k, string currentId, Func<int, int> pick)
        {
            var l = Maps(k).Where(m => m.Id != currentId).ToList();
            if (l.Count == 0) return Maps(k).FirstOrDefault(m => m.Id == currentId);
            return l[pick(l.Count)];
        }
    }
}
