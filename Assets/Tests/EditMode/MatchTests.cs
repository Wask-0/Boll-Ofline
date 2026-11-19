using System;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Physics;
using Boll.Core.Rules;
using NUnit.Framework;

namespace Boll.Tests
{
    public class MatchTests
    {
        static Match New(string kind, ulong seed = 1, string obs = "")
        {
            var s = SolverTests.Make(obs);
            string j = SolverTests.MapTpl.Replace("OBS", obs).Replace("\"eight\"", "\"" + kind + "\"")
                .Replace("\"returnPoints\"", "\"marks\":{\"back\":[0.6,0]},\"returnPoints\"");
            return new Match(MapLoader.Load(j), s.Model, seed);
        }

        [Test]
        public void StartSetsUpTable()
        {
            var m = New("eight");
            Assert.AreEqual(16, m.Solver.Balls.Count);
            Assert.AreEqual(Phase.Aiming, m.Phase);
            Assert.IsTrue(m.CanShoot);
            Assert.IsTrue(Boll.Core.Content.Geo.PointInPolygon(m.Cue.Pos, m.Map.Layout.Home));
            Assert.AreEqual(HandMode.Home, m.St.Hand);
        }

        [Test]
        public void ShotRunsToCompletionAndPassesTheTurnOrContinues()
        {
            var m = New("eight");
            Assert.IsTrue(m.Shoot(new ShotParams { Angle = 0, Power = 0.9 }));
            Assert.AreEqual(Phase.Shooting, m.Phase);
            Assert.IsFalse(m.Shoot(new ShotParams { Angle = 0, Power = 0.5 }), "нельзя бить во время удара");
            Assert.IsTrue(m.RunShot(480 * 200));
            Assert.IsNotNull(m.LastResult);
            Assert.IsFalse(m.SafetyTriggered);
            Assert.IsTrue(m.CanShoot || m.Phase == Phase.Over);
            Assert.IsTrue(m.Timeline == null);                       // рекордер сброшен
            Assert.IsTrue(Rest.AllAtRest(m.Solver));
        }

        [Test]
        public void IdenticalInputGivesIdenticalOutcome()
        {
            Match Go()
            {
                var m = New("nine", 42);
                m.TryPlaceCue(new Vec2(-0.8, 0.1));
                m.Shoot(new ShotParams { Angle = 0.03, Power = 0.8, SpinX = 0.2, SpinY = -0.3 });
                m.RunShot(480 * 200);
                return m;
            }
            Match a = Go(), b = Go();
            Assert.AreEqual(a.St.Current, b.St.Current);
            Assert.AreEqual(a.LastResult.Message, b.LastResult.Message);
            foreach (var ba in a.Solver.Balls)
            {
                var bb = b.BallById(ba.Id);
                Assert.AreEqual(ba.Pos.X, bb.Pos.X); Assert.AreEqual(ba.Pos.Y, bb.Pos.Y); Assert.AreEqual(ba.Pocketed, bb.Pocketed);
            }
        }

        [Test]
        public void HandPlacementRules()
        {
            var m = New("eight");
            Assert.IsTrue(m.TryPlaceCue(new Vec2(-0.9, 0.2)));        // внутри дома
            Assert.IsFalse(m.TryPlaceCue(new Vec2(0.3, 0)));          // вне дома, а разбой — из дома
            Assert.IsFalse(m.TryPlaceCue(new Vec2(-1.3, 0)));         // за бортом
            Assert.IsFalse(m.TryPlaceCue(m.Solver.Balls[0].Pos));     // на другой шар
        }

        [Test]
        public void EightRequiresCalledPocketOnEightBall()
        {
            var m = New("eight");
            m.St.Current = 0; m.St.BreakPending = false; m.St.TableOpen = false; m.St.Group[0] = 1; m.St.Group[1] = 2;
            foreach (var b in m.Solver.Balls.Where(b => b.Id >= 1 && b.Id <= 7 && b.Id != 0)) { b.Pocketed = true; m.St.OnTable.Remove(b.Id); }
            Assert.IsTrue(m.NeedsCalledPocket(0));
            Assert.IsFalse(m.Shoot(new ShotParams { Angle = 0, Power = 0.5 }));
            Assert.IsTrue(m.Shoot(new ShotParams { Angle = 0, Power = 0.5, CalledPocket = 2 }));
        }

        [Test]
        public void SafetyTimeEndsStuckShot()
        {
            var m = New("eight");
            m.SafetySteps = 100;
            m.Shoot(new ShotParams { Angle = 0.5, Power = 1.0 });
            Assert.IsTrue(m.RunShot(1000));
            Assert.IsTrue(m.SafetyTriggered);
            Assert.IsNotNull(m.LastResult);
            Assert.IsTrue(Rest.AllAtRest(m.Solver));
        }

        [Test]
        public void MapChoiceAfterWinOrLoss()
        {
            string j = SolverTests.MapTpl.Replace("OBS", "");
            var cat = new MapCatalog(new[] { (GameKind.Eight, j), (GameKind.Eight, j.Replace("\"id\":\"s\"", "\"id\":\"s2\"")) });
            var cur = cat.Maps(GameKind.Eight)[0];
            Assert.AreSame(cur, Match.NextMap(cat, cur, false, new Rng(1)));
            Assert.AreEqual("s2", Match.NextMap(cat, cur, true, new Rng(1)).Id);
        }

        [Test]
        public void SiberianAndSnookerStartWithBalls()
        {
            Assert.AreEqual(16, New("siberian").Solver.Balls.Count);
            Assert.AreEqual(10, New("nine").Solver.Balls.Count);
            Assert.AreEqual(Phase.Aiming, New("american").Phase);
        }
    }
}
