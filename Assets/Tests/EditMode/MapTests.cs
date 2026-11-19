using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Boll.Core.Bot;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Physics;
using NUnit.Framework;

namespace Boll.Tests
{
    public class MapTests
    {
        static readonly GameKind[] Kinds = { GameKind.Eight, GameKind.Nine, GameKind.American, GameKind.Siberian, GameKind.Snooker };

        static string Dir => Path.Combine(UnityEngine.Application.dataPath, "Resources", "maps");

        static List<(GameKind, string)> Sources() =>
            Kinds.SelectMany(k => Directory.GetFiles(Path.Combine(Dir, KindDefaults.Key(k)), "*.json").OrderBy(f => f).Select(f => (k, File.ReadAllText(f)))).ToList();

        static Catalogs Cat()
        {
            string d = Path.Combine(UnityEngine.Application.dataPath, "Resources/catalog");
            return CatalogLoader.LoadAll(File.ReadAllText(d + "/physics.json"), File.ReadAllText(d + "/bots.json"), File.ReadAllText(d + "/skins.json"));
        }

        [Test]
        public void SevenMapsPerKindAndAllLoad()
        {
            var cat = new MapCatalog(Sources());
            Assert.AreEqual(35, cat.Count);
            foreach (var k in Kinds) Assert.AreEqual(7, cat.Maps(k).Count, k.ToString());
        }

        [Test]
        public void AllMapsPassAutomaticChecks()
        {
            var cat = new MapCatalog(Sources());
            var phys = Cat().Models;
            var errs = new List<string>();
            foreach (var k in Kinds)
                foreach (MapData m in cat.Maps(k))
                    foreach (var model in phys) errs.AddRange(MapChecks.Run(m, model).Distinct());
            Assert.IsEmpty(errs.Distinct().ToList(), string.Join("\n", errs.Distinct().Take(30)));
        }

        [Test]
        public void MoversPushBallsOutOfTheirZone()
        {
            var cat = new MapCatalog(Sources());
            var model = Cat().Model("medium");
            foreach (var k in Kinds)
                foreach (MapData m in cat.Maps(k))
                {
                    var rest = new Rest(m);
                    if (!rest.HasMovers) continue;
                    var s = new Solver(m, model);
                    int oi = Array.FindIndex(m.Obstacles, o => o.Type == ObstacleType.Mover);
                    Obstacle mo = m.Obstacles[oi];
                    var mid = (mo.A + mo.B) * 0.5;
                    var b = s.AddBall(1, mid + new Vec2(0.05, 0));    // шар прямо в зоне хода
                    if (!rest.InMoverZone(b.Pos)) b.Pos = mid;
                    for (int i = 0; i < 480 * 90 && !(Rest.BallAtRest(b) && !rest.InMoverZone(b.Pos)); i++) s.Step();
                    Assert.IsTrue(b.Pocketed || (Rest.BallAtRest(b) && !rest.InMoverZone(b.Pos)), m.Id + ": ball stuck in mover zone");
                }
        }

        [Test, Explicit("партия бот 8 против бота 8 на каждой карте: ≤150 ударов")]
        public void BotVsBotFitsIn150Shots()
        {
            var cat = new MapCatalog(Sources());
            var c = Cat(); var model = c.Model("medium");
            var all = Kinds.SelectMany(k => cat.Maps(k)).ToList();
            var bad = new List<string>(); int done = 0;
            System.Threading.Tasks.Parallel.ForEach(all, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 10 }, m =>
            {
                var match = new Match(m, model, 77);
                int w = BotArena.Play(match, new[] { c.Bots[7], c.Bots[7] }, out int shots);
                lock (bad) { done++; if (w < 0) bad.Add(m.Id + " (" + shots + " shots)"); }
            });
            UnityEngine.Debug.Log("BOTBOT maps=" + done + " over150=" + bad.Count + " " + string.Join(", ", bad));
            Assert.IsEmpty(bad);
        }
    
        [Test, Explicit("диагностика одной карты")]
        public void DiagnoseMap([Values("eight_4")] string id)
        {
            var cat = new MapCatalog(Sources()); var c = Cat();
            MapData map = Kinds.SelectMany(k => cat.Maps(k)).First(m => m.Id == id);
            var match = new Match(map, c.Model("medium"), 77);
            var hist = new Dictionary<string, int>(); int pots = 0;
            for (int i = 0; i < 150 && match.Phase != Phase.Over; i++)
            {
                var pl = new BotPlanner(match, c.Bots[7]); pl.Finish();
                int before = match.St.OnTable.Count;
                if (!match.Shoot(pl.Result)) { hist["SHOOT REJECTED"] = 1; break; }
                match.RunShot(480 * 200);
                string key = match.LastResult.Foul ? match.LastResult.Reason.ToString() : match.LastResult.Rerack ? "Rerack" : "ok";
                hist[key] = hist.TryGetValue(key, out int v) ? v + 1 : 1;
                pots += before - match.St.OnTable.Count;
                if (match.SafetyTriggered) hist["safety"] = hist.TryGetValue("safety", out v) ? v + 1 : 1;
            }
            UnityEngine.Debug.Log("DIAGMAP " + id + " pots=" + pots + " " + string.Join(", ", hist.Select(kv => kv.Key + "=" + kv.Value)) + " onTable=" + match.St.OnTable.Count + " groups=" + match.St.Group[0] + "/" + match.St.Group[1]);
        }
    }
}
