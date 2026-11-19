using Boll.Core.Content;
using Boll.Core.Physics;
using NUnit.Framework;

namespace Boll.Tests
{
    public class RestTests
    {
        const string Ping = @"{""type"":""mover"",""a"":[0.5,-0.2],""b"":[0.5,0.2],""speed"":0.3,""mode"":""pingpong"",""path"":[[0,0],[-0.5,0]]}";
        const string Circ = @"{""type"":""mover"",""a"":[0.5,-0.1],""b"":[0.5,0.1],""speed"":0.3,""mode"":""circle"",""center"":[0,0],""radius"":0.5}";

        [TestCase(0.25, 0.0, true)]
        [TestCase(0.25, 0.22, true)]
        [TestCase(0.25, 0.24, false)]
        [TestCase(0.25, 0.5, false)]
        [TestCase(-0.2, 0.0, false)]
        public void PingPongZone(double x, double y, bool inside)
        {
            var s = SolverTests.Make(Ping);
            Assert.AreEqual(inside, new Rest(s.Map).InMoverZone(new Vec2(x, y)));
        }

        [TestCase(0.0, 0.0, false)]
        [TestCase(0.5, 0.0, true)]
        [TestCase(-0.5, 0.0, true)]
        [TestCase(0.0, 0.5, true)]
        [TestCase(0.0, 0.3, false)]
        public void CircleZone(double x, double y, bool inside)
        {
            var s = SolverTests.Make(Circ);
            Assert.AreEqual(inside, new Rest(s.Map).InMoverZone(new Vec2(x, y)));
        }

        [Test]
        public void CanShootRules()
        {
            var s = SolverTests.Make(Ping);
            var rest = new Rest(s.Map);
            var b = s.AddBall(0, new Vec2(-0.5, 0));
            Assert.IsTrue(rest.CanShoot(s));
            b.Vel = new Vec2(0.01, 0);
            Assert.IsFalse(rest.CanShoot(s));
            b.Vel = new Vec2(0.001, 0);
            Assert.IsTrue(rest.CanShoot(s));
            b.Wz = 0.2;
            Assert.IsFalse(rest.CanShoot(s));
            b.Wz = 0;
            s.AddBall(1, new Vec2(0.25, 0));
            Assert.IsFalse(rest.CanShoot(s));
            Assert.AreEqual(1, rest.StuckBalls(s).Count);
        }
    }
}
