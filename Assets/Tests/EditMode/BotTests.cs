using System.Diagnostics;
using System.IO;
using Boll.Core.Bot;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Rules;
using NUnit.Framework;

namespace Boll.Tests
{
    public class BotTests
    {
        static Catalogs Cat()
        {
            string d = Path.Combine(UnityEngine.Application.dataPath, "Resources/catalog");
            return CatalogLoader.LoadAll(File.ReadAllText(d + "/physics.json"), File.ReadAllText(d + "/bots.json"), File.ReadAllText(d + "/skins.json"));
        }

        static Match New(string kind, ulong seed)
        {
            string j = SolverTests.MapTpl.Replace("OBS", "").Replace("\"eight\"", "\"" + kind + "\"")
                .Replace("\"returnPoints\"", "\"marks\":{\"back\":[0.6,0]},\"returnPoints\"");
            return new Match(MapLoader.Load(j), Cat().Model("medium"), seed);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(5)] [TestCase(8)]
        public void EveryLevelPlaysAValidBreakAndTurn(int level)
        {
            var bot = Cat().Bots[level - 1];
            foreach (string kind in new[] { "eight", "nine", "american", "siberian" })
            {
                var m = New(kind, 3);
                for (int i = 0; i < 3 && m.Phase != Phase.Over; i++)
                {
                    var pl = new BotPlanner(m, bot);
                    pl.Finish();
                    Assert.IsNotNull(pl.Result, kind);
                    Assert.IsTrue(m.Shoot(pl.Result), kind + " shot " + i);
                    Assert.IsTrue(m.RunShot(480 * 200));
                }
            }
        }

        [Test]
        public void ChunkedWorkEqualsFinish()
        {
            var bot = Cat().Bots[3];
            var a = New("nine", 9); var b = New("nine", 9);
            var pa = new BotPlanner(a, bot); pa.Finish();
            var pb = new BotPlanner(b, bot); int guard = 0;
            while (!pb.Work(1) && guard++ < 1000) { }
            Assert.IsTrue(pb.Done);
            Assert.AreEqual(pa.Result.Angle, pb.Result.Angle);
            Assert.AreEqual(pa.Result.Power, pb.Result.Power);
        }

        [Test, Explicit("подстройка уровней: долго")]
        public void LevelLadder()
        {
            var cat = Cat();
            var sw = Stopwatch.StartNew();
            for (int hi = 2; hi <= 8; hi++)
            {
                int wins = 0, draws = 0, sh = 0, games = 6;
                for (int g = 0; g < games; g++)
                {
                    var m = New("nine", (ulong)(100 + g));
                    var lv = g % 2 == 0 ? new[] { cat.Bots[hi - 1], cat.Bots[hi - 2] } : new[] { cat.Bots[hi - 2], cat.Bots[hi - 1] };
                    int w = BotArena.Play(m, lv, out int n); sh += n; if (w < 0) draws++;
                    if (w >= 0 && lv[w].Level == hi) wins++;
                }
                UnityEngine.Debug.Log("LADDER " + hi + " vs " + (hi - 1) + ": " + wins + "/" + games + " draws=" + draws + " avgShots=" + (sh / games) + " t=" + sw.Elapsed.TotalSeconds);
            }
        }
    
        [Test, Explicit("диагностика")]
        public void Diagnose()
        {
            var cat = Cat();
            var m = New("nine", 7);
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < 14 && m.Phase != Phase.Over; i++)
            {
                int who = m.St.Current;
                var pl = new BotPlanner(m, cat.Bots[7]); pl.Finish();
                int onBefore = m.St.OnTable.Count;
                m.Shoot(pl.Result); m.RunShot(480 * 200);
                log.Append("P" + who + " pow=" + pl.Result.Power.ToString("0.00") + " ang=" + pl.Result.Angle.ToString("0.00") + " foul=" + m.LastResult.Foul + " pots=" + (onBefore - m.St.OnTable.Count) + " next=" + m.St.Current + " | ");
            }
            UnityEngine.Debug.Log("DIAG " + log);
        }
    
        /// <summary>Параллельная лесенка: уровень hi против hi−1, games партий (половина — «восьмёрка», половина — «девятка»).</summary>
        [TestCase(2, 60), TestCase(3, 60), TestCase(4, 60), TestCase(5, 60), TestCase(6, 60), TestCase(7, 60), TestCase(8, 60)]
        [Explicit("подстройка уровней: долго")]
        public void Ladder(int hi, int games)
        {
            var cat = Cat(); var model = cat.Model("medium");
            int wins = 0, draws = 0; long shots = 0;
            var sw = Stopwatch.StartNew();
            System.Threading.Tasks.Parallel.For(0, games, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 10 }, g =>
            {
                string kind = g % 2 == 0 ? "eight" : "nine";
                string j = SolverTests.MapTpl.Replace("OBS", "").Replace("\"eight\"", "\"" + kind + "\"");
                var m = new Match(MapLoader.Load(j), model, (ulong)(1000 + g));
                bool hiFirst = (g / 2) % 2 == 0;
                var lv = hiFirst ? new[] { cat.Bots[hi - 1], cat.Bots[hi - 2] } : new[] { cat.Bots[hi - 2], cat.Bots[hi - 1] };
                int w = BotArena.Play(m, lv, out int n);
                lock (cat)
                {
                    shots += n;
                    if (w < 0) draws++; else if (lv[w].Level == hi) wins++;
                }
            });
            UnityEngine.Debug.Log("LADDER " + hi + " vs " + (hi - 1) + ": " + wins + "/" + games + " (" + (100 * wins / games) + "%) draws=" + draws + " avgShots=" + (shots / games) + " t=" + sw.Elapsed.TotalSeconds.ToString("0"));
        }
    
        /// <summary>Доля удалённых «чистых забитых» за один ход после общего разбоя: прямая мера силы уровня.</summary>
        [TestCase("eight"), TestCase("nine")]
        [Explicit("замер силы уровней")]
        public void PotRate(string kind)
        {
            var cat = Cat(); var model = cat.Model("medium");
            int N = 60; var pots = new int[9]; var fouls = new int[9]; var cont = new int[9];
            var sw = Stopwatch.StartNew();
            System.Threading.Tasks.Parallel.For(0, 8 * N, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 10 }, idx =>
            {
                int level = idx / N + 1, g = idx % N;
                string j = SolverTests.MapTpl.Replace("OBS", "").Replace("\"eight\"", "\"" + kind + "\"");
                var m = new Match(MapLoader.Load(j), model, (ulong)(500 + g));
                var br = new BotPlanner(m, cat.Bots[7]); br.Finish(); m.Shoot(br.Result); m.RunShot(480 * 200);
                if (m.Phase == Phase.Over) return;
                int before = m.St.OnTable.Count, who = m.St.Current;
                var pl = new BotPlanner(m, cat.Bots[level - 1]); pl.Finish();
                if (!m.Shoot(pl.Result)) return;
                m.RunShot(480 * 200);
                lock (pots)
                {
                    pots[level] += before - m.St.OnTable.Count > 0 ? 1 : 0;
                    fouls[level] += m.LastResult.Foul ? 1 : 0;
                    cont[level] += (!m.LastResult.Foul && m.St.Current == who) ? 1 : 0;
                }
            });
            var sb = new System.Text.StringBuilder("POTRATE " + kind + ": ");
            for (int l = 1; l <= 8; l++) sb.Append("L" + l + " pot=" + pots[l] + " foul=" + fouls[l] + " cont=" + cont[l] + " | ");
            UnityEngine.Debug.Log(sb + "t=" + sw.Elapsed.TotalSeconds.ToString("0"));
        }
    }
}
