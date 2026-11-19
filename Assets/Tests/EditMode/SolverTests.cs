using System;
using Boll.Core.Content;
using Boll.Core.Physics;
using NUnit.Framework;

namespace Boll.Tests
{
    public class SolverTests
    {
        internal const string MapTpl = @"{
 ""id"":""s"",""name"":""S"",""kind"":""eight"",
 ""outline"":[[-1.27,-0.635],[1.27,-0.635],[1.27,0.635],[-1.27,0.635]],
 ""pockets"":[{""pos"":[-1.27,-0.635]},{""pos"":[0,-0.66]},{""pos"":[1.27,-0.635]},{""pos"":[-1.27,0.635]},{""pos"":[0,0.66]},{""pos"":[1.27,0.635]}],
 ""obstacles"":[OBS],
 ""layout"":{""rackPoint"":[0.6,0],""rackAxis"":[1,0],""home"":[[-1.2,-0.58],[-0.6,-0.58],[-0.6,0.58],[-1.2,0.58]],
   ""homeLineA"":[-0.6,-0.6],""homeLineB"":[-0.6,0.6],""returnPoints"":[[-0.9,0.5]]}}";

        internal static Solver Make(string obs = "", string model = "medium")
        {
            var m = MapLoader.Load(MapTpl.Replace("OBS", obs));
            var phys = CatalogLoader.LoadPhysics(System.IO.File.ReadAllText(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "Resources/catalog/physics.json")), out _);
            return new Solver(m, Array.Find(phys, p => p.Id == model));
        }

        static void Run(Solver s, double seconds) { long n = (long)(seconds / Solver.Dt); for (long i = 0; i < n; i++) s.Step(); }

        static SolverEvent Until(Solver s, EventType t)
        {
            for (int i = 0; i < 480 * 20; i++) { s.Step(); foreach (var e in s.Events) if (e.Type == t) return e; }
            Assert.Fail("no " + t); return default;
        }

        static SolverEvent First(Solver s, EventType t) { foreach (var e in s.Events) if (e.Type == t) return e; Assert.Fail("no " + t); return default; }

        [Test]
        public void SlideThenRollDistance()
        {
            var s = Make();
            var b = s.AddBall(0, new Vec2(-1, 0));
            s.Strike(b, 0, 0.5 / 6.0, 0, 0); // v0 = 0.5
            Run(s, 20);
            double mu = s.Model.SlidingFriction, g = Solver.G;
            double ts = 2 * 0.5 / (7 * mu * g), x1 = 0.5 * ts - 0.5 * mu * g * ts * ts, v1 = 5.0 / 7 * 0.5;
            double expect = -1 + x1 + v1 * v1 / (2 * s.Model.RollingFriction * g);
            Assert.AreEqual(expect, b.Pos.X, 0.005);
            Assert.AreEqual(0, b.Vel.Len, 1e-12);
        }

        [Test]
        public void CushionRestitution()
        {
            var s = Make();
            var b = s.AddBall(0, new Vec2(0, 0));
            s.Strike(b, 0, 0.3, 0, 0);
            var e = Until(s, EventType.Cushion);
            Assert.AreEqual(Surface.Rail, e.Surface);
            Assert.AreEqual(-s.Model.CushionRestitution * e.Speed, b.Vel.X, 0.01);
        }

        [Test]
        public void BumperIsStrongerThanRail()
        {
            var s = Make(@"{""type"":""bumper"",""c"":[0.5,0],""r"":0.05,""k"":1.2}");
            var b = s.AddBall(0, new Vec2(0, 0));
            s.Strike(b, 0, 0.3, 0, 0);
            var e = Until(s, EventType.Cushion);
            Assert.AreEqual(Surface.Bumper, e.Surface);
            Assert.AreEqual(-s.Model.CushionRestitution * 1.2 * e.Speed, b.Vel.X, 0.01);
        }

        [Test]
        public void HeadOnBallCollisionTransfersMomentum()
        {
            var s = Make();
            var c = s.AddBall(0, new Vec2(-0.5, 0));
            var t = s.AddBall(1, new Vec2(0, 0));
            s.Strike(c, 0, 1.0 / 6, 0, 0);
            var e = Until(s, EventType.BallBall);
            double ex = (1 + s.Model.BallRestitution) / 2 * e.Speed;
            Assert.AreEqual(ex, t.Vel.X, 0.01);
        }

        [Test]
        public void FastBallNeverLeavesTable()
        {
            var s = Make();
            var b = s.AddBall(0, new Vec2(-0.3, 0.1));
            s.Strike(b, 0.7, 1.0, 0.3, 0.5);
            for (int i = 0; i < 480 * 8 && !b.Pocketed; i++)
            {
                s.Step();
                Assert.IsTrue(Math.Abs(b.Pos.X) <= 1.27 && Math.Abs(b.Pos.Y) <= 0.635, "left table at step " + i);
            }
        }

        [Test]
        public void BallIsPocketed()
        {
            var s = Make();
            var b = s.AddBall(0, new Vec2(0.7, 0.2));
            s.Strike(b, Math.Atan2(0.435, 0.57), 0.3, 0, 0);
            Run(s, 3);
            Assert.IsTrue(b.Pocketed);
            Assert.AreEqual(5, b.PocketIndex);
            Assert.AreEqual(EventType.Pocket, First(s, EventType.Pocket).Type);
        }

        [Test]
        public void MoverPushesRestingBall()
        {
            var s = Make(@"{""type"":""mover"",""a"":[0.5,-0.2],""b"":[0.5,0.2],""speed"":0.3,""mode"":""pingpong"",""path"":[[0,0],[-0.5,0]]}");
            var b = s.AddBall(0, new Vec2(0.2, 0));
            Run(s, 1.2);
            Assert.Less(b.Vel.X, -0.3, "ball should be kicked away from mover");
            Assert.AreEqual(Surface.Mover, First(s, EventType.Cushion).Surface);
            Run(s, 1.0);
            double moverX = 0.5 + s.MoverSegmentOffset(0).X;
            Assert.LessOrEqual(b.Pos.X + s.R, moverX + 1e-6);
        }

        [Test]
        public void Deterministic()
        {
            Solver Go()
            {
                var s = Make(@"{""type"":""bumper"",""c"":[0.3,0.1],""r"":0.05,""k"":1.3}");
                for (int i = 0; i < 5; i++) s.AddBall(i, new Vec2(0.4 + 0.06 * i, 0.02 * i));
                var c = s.AddBall(9, new Vec2(-0.8, 0.05));
                s.Strike(c, 0.05, 0.9, 0.4, -0.3);
                Run(s, 10);
                return s;
            }
            Solver a = Go(), b = Go();
            for (int i = 0; i < a.Balls.Count; i++)
            {
                Assert.AreEqual(a.Balls[i].Pos.X, b.Balls[i].Pos.X);
                Assert.AreEqual(a.Balls[i].Pos.Y, b.Balls[i].Pos.Y);
            }
            Assert.AreEqual(a.Events.Count, b.Events.Count);
        }
    }
}
