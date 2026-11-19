using System.IO;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Save;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Boll.Tests
{
    public class SaveTests
    {
        static Catalogs Cat()
        {
            string d = Path.Combine(UnityEngine.Application.dataPath, "Resources/catalog");
            return CatalogLoader.LoadAll(File.ReadAllText(d + "/physics.json"), File.ReadAllText(d + "/bots.json"), File.ReadAllText(d + "/skins.json"));
        }

        // ---------- хранилище ----------
        [Test]
        public void RoundTripAndAtomicBackup()
        {
            var fs = new MemoryFs(); var st = new SaveStore(fs);
            var d = st.Load(out var s0); Assert.AreEqual(LoadStatus.Fresh, s0);
            d.Stars = 3; st.Save(d);
            d.Stars = 5; st.Save(d);
            Assert.IsFalse(fs.Exists(SaveStore.Tmp));
            Assert.AreEqual(3, JsonConvert.DeserializeObject<SaveData>(fs.Read(SaveStore.Bak)).Stars);
            Assert.AreEqual(5, st.Load(out var s1).Stars); Assert.AreEqual(LoadStatus.Ok, s1);
        }

        [Test]
        public void CorruptMainFallsBackToBackup()
        {
            var fs = new MemoryFs(); var st = new SaveStore(fs);
            var d = st.Load(out _); d.Stars = 3; st.Save(d); d.Stars = 4; st.Save(d);
            fs.Write(SaveStore.Main, "{not json");
            var r = st.Load(out var s); Assert.AreEqual(LoadStatus.Recovered, s); Assert.AreEqual(3, r.Stars);
            fs.Write(SaveStore.Main, "garbage"); fs.Write(SaveStore.Bak, "garbage");
            st.Load(out s); Assert.AreEqual(LoadStatus.Corrupt, s);
        }

        [Test]
        public void UnknownVersionIsTreatedAsCorrupt()
        {
            var fs = new MemoryFs(); var st = new SaveStore(fs);
            fs.Write(SaveStore.Main, JsonConvert.SerializeObject(new SaveData { Version = 99 }));
            st.Load(out var s); Assert.AreEqual(LoadStatus.Corrupt, s);
        }

        // ---------- восстановление партии ----------
        [Test]
        public void SnapshotRestoreReplaysShotIdentically()
        {
            var s = SolverTests.Make();
            string j = SolverTests.MapTpl.Replace("OBS", "");
            var map = MapLoader.Load(j);
            var m = new Match(map, s.Model, 5);
            m.TryPlaceCue(new Vec2(-0.8, 0.1));
            var p = new ShotParams { Angle = 0.02, Power = 0.85, SpinX = 0.3, SpinY = 0.2 };
            Assert.IsTrue(m.Shoot(p));
            string saved = JsonConvert.SerializeObject(m.PreShot);          // «закрыли во время удара»
            m.RunShot(480 * 200);

            var restored = Match.Restore(map, s.Model, JsonConvert.DeserializeObject<MatchSnapshot>(saved));
            Assert.AreEqual(Phase.Shooting, restored.Phase);
            restored.RunShot(480 * 200);

            Assert.AreEqual(m.St.Current, restored.St.Current);
            Assert.AreEqual(m.LastResult.Message, restored.LastResult.Message);
            foreach (var a in m.Solver.Balls)
            {
                var b = restored.BallById(a.Id);
                Assert.AreEqual(a.Pos.X, b.Pos.X); Assert.AreEqual(a.Pos.Y, b.Pos.Y); Assert.AreEqual(a.Pocketed, b.Pocketed);
            }
            // состояние после остановки тоже восстанавливается
            var idle = Match.Restore(map, s.Model, JsonConvert.DeserializeObject<MatchSnapshot>(JsonConvert.SerializeObject(m.Snapshot())));
            Assert.AreEqual(Phase.Aiming, idle.Phase); Assert.AreEqual(m.St.Current, idle.St.Current);
        }

        // ---------- прогрессия ----------
        [Test]
        public void WinGivesStarAndOpensNextBotOnlyFromStrongestOpen()
        {
            var p = new Profile(new SaveData(), Cat());
            Assert.IsTrue(p.IsBotOpen(4)); Assert.IsFalse(p.IsBotOpen(5));
            p.RecordResult(GameKind.Eight, 2, true);
            Assert.AreEqual(1, p.D.Stars); Assert.IsFalse(p.IsBotOpen(5));
            p.RecordResult(GameKind.Eight, 4, true);
            Assert.IsTrue(p.IsBotOpen(5)); Assert.AreEqual(2, p.D.Stars);
            p.RecordResult(GameKind.Nine, 4, false);
            Assert.IsFalse(p.IsBotOpen(6)); Assert.AreEqual(2, p.D.Stars);
            for (int l = 5; l <= 8; l++) p.RecordResult(GameKind.Nine, l, true);
            Assert.AreEqual(8, p.D.BotsUnlocked);
        }

        [Test]
        public void StatsAndPercent()
        {
            var p = new Profile(new SaveData(), Cat());
            Assert.AreEqual("—", Profile.WinPercent(p.D.Stats.Total));
            p.RecordResult(GameKind.Eight, 1, true); p.RecordResult(GameKind.Eight, 1, false); p.RecordResult(GameKind.Nine, 3, false);
            Assert.AreEqual(3, p.D.Stats.Total.Games); Assert.AreEqual("33 %", Profile.WinPercent(p.D.Stats.Total));
            Assert.AreEqual(2, p.D.Stats.ByBot[0].Games); Assert.AreEqual("50 %", Profile.WinPercent(p.D.Stats.ByBot[0]));
            Assert.AreEqual(2, p.KindStats(GameKind.Eight).Games); Assert.AreEqual(0, p.KindStats(GameKind.Snooker).Games);
        }

        [Test]
        public void ShopFlow()
        {
            var p = new Profile(new SaveData { Stars = 10 }, Cat());
            Assert.IsTrue(p.IsOwned("cloth_green")); Assert.IsTrue(p.IsSelected("cloth_green"));
            Assert.AreEqual(BuyResult.AlreadyOwned, p.Buy("cloth_green"));
            Assert.AreEqual(BuyResult.Unknown, p.Buy("nope"));
            Assert.IsFalse(p.Select("cloth_blue"));
            Assert.AreEqual(BuyResult.Ok, p.Buy("cloth_blue")); Assert.AreEqual(0, p.D.Stars);
            Assert.AreEqual(BuyResult.NotEnoughStars, p.Buy("cue_dark"));
            Assert.IsTrue(p.Select("cloth_blue")); Assert.AreEqual("cloth_blue", p.D.Cloth);
            Assert.AreEqual("cue_classic", p.D.Cue);
        }

        [Test]
        public void MissingPurchasedSkinRefundsStarsSilently()
        {
            var d = new SaveData { Stars = 1, Cloth = "gone" };
            d.Owned["gone"] = 10;
            var p = new Profile(d, Cat());
            Assert.AreEqual(11, d.Stars); Assert.IsFalse(d.Owned.ContainsKey("gone")); Assert.AreEqual("cloth_green", d.Cloth);
        }

        // ---------- журнал ----------
        [Test]
        public void ErrorLogCapAndCrashDetection()
        {
            var fs = new MemoryFs(); int n = 0;
            var log = new ErrorLog(fs, () => (++n).ToString());
            Assert.IsFalse(log.Start("dev", "1.0", "6000"));
            log.Close();
            Assert.IsFalse(new ErrorLog(fs, () => "t").Start("dev", "1.0", "6000"));     // прошлая сессия закрыта штатно
            Assert.IsTrue(new ErrorLog(fs, () => "t").Start("dev", "1.0", "6000"));      // прошлая START без CLOSE
            var big = new ErrorLog(new MemoryFs(), () => "t");
            for (int i = 0; i < 400; i++) big.Exception("e" + i, new string('x', 2000));
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(big.Text), ErrorLog.MaxBytes);
            StringAssert.Contains("e399", big.Text);
        }
    }
}
