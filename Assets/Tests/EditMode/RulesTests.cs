using System.Collections.Generic;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Physics;
using Boll.Core.Rules;
using NUnit.Framework;

namespace Boll.Tests
{
    public class RulesTests
    {
        const string SnookerMarks = "{\"yellow\":[-0.9,0.2],\"green\":[-0.9,-0.2],\"brown\":[-0.9,0],\"blue\":[0,0],\"pink\":[0.6,0],\"black\":[1.0,0]}";

        static MapData Map(string kind)
        {
            string j = SolverTests.MapTpl.Replace("OBS", "").Replace("\"eight\"", "\"" + kind + "\"")
                .Replace("\"returnPoints\"", "\"marks\":" + (kind == "snooker" ? SnookerMarks : "{\"back\":[0.6,0]}") + ",\"returnPoints\"");
            return MapLoader.Load(j);
        }

        static RulesEngine Eng(string kind, bool brk = false, int cur = 0)
        {
            var map = Map(kind);
            var st = MatchState.New(map.Kind, new Rng(1));
            st.Current = cur; st.BreakPending = brk;
            return new RulesEngine(map, st);
        }

        static SolverEvent BB(int a, int b, Vec2 posB = default) => new SolverEvent { Type = EventType.BallBall, A = a, B = b, Pos2 = posB };
        static SolverEvent Cu(int ball, int edge = 0, double x = 0.5, Surface s = Surface.Rail) =>
            new SolverEvent { Type = EventType.Cushion, A = ball, Surface = s, Index = edge, Pos = new Vec2(x, -0.6) };
        static SolverEvent Pk(int ball, int pocket = 0) => new SolverEvent { Type = EventType.Pocket, A = ball, Index = pocket };
        static SolverEvent Cross(int ball) => new SolverEvent { Type = EventType.CenterCross, A = ball };
        static ShotInput Shot(int cue, params SolverEvent[] ev) => new ShotInput { CueId = cue, Events = ev };

        // ---------- «восьмёрка» ----------
        [Test]
        public void Eight_LegalBreakWithPocketKeepsTurnAndOpenTable()
        {
            var e = Eng("eight", true);
            var r = e.Evaluate(Shot(0, BB(0, 1), Pk(3)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(0, r.NextPlayer);
            Assert.IsTrue(e.St.TableOpen); Assert.IsFalse(e.St.BreakPending);
            Assert.IsFalse(e.St.OnTable.Contains(3));
        }

        [Test]
        public void Eight_IllegalBreakReracksAndOpponentBreaks()
        {
            var e = Eng("eight", true);
            var r = e.Evaluate(Shot(0, BB(0, 1), Cu(1), Cu(2), Cu(3)));
            Assert.IsTrue(r.Rerack); Assert.AreEqual(1, r.NextPlayer); Assert.AreEqual(HandMode.Home, r.Hand);
            Assert.IsTrue(e.St.BreakPending); Assert.AreEqual(16, e.St.OnTable.Count);
        }

        [Test]
        public void Eight_FourBallsToRailIsLegalBreak()
        {
            var e = Eng("eight", true);
            var r = e.Evaluate(Shot(0, BB(0, 1), Cu(1), Cu(2), Cu(3), Cu(4)));
            Assert.IsFalse(r.Rerack); Assert.AreEqual(1, r.NextPlayer);
        }

        [Test]
        public void Eight_EightOnBreakWithoutFoulReracksSameBreaker()
        {
            var e = Eng("eight", true);
            var r = e.Evaluate(Shot(0, BB(0, 1), Pk(8)));
            Assert.IsTrue(r.Rerack); Assert.AreEqual(0, r.NextPlayer);
            r = Eng("eight", true).Evaluate(Shot(0, BB(0, 1), Pk(8), Pk(0)));
            Assert.AreEqual(1, r.NextPlayer);
        }

        [Test]
        public void Eight_GroupAssignedFromFirstPocketedBall()
        {
            var e = Eng("eight");
            var r = e.Evaluate(Shot(0, BB(0, 9), Pk(9), Pk(2)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(0, r.NextPlayer);
            Assert.AreEqual(2, e.St.Group[0]); Assert.AreEqual(1, e.St.Group[1]); Assert.IsFalse(e.St.TableOpen);
            Assert.AreEqual(1, e.St.Score[0]); Assert.AreEqual(1, e.St.Score[1]);
        }

        [Test]
        public void Eight_OpponentBallOnlyPassesTurn()
        {
            var e = Eng("eight");
            e.St.TableOpen = false; e.St.Group[0] = 1; e.St.Group[1] = 2;
            var r = e.Evaluate(Shot(0, BB(0, 3), Pk(10)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(1, r.NextPlayer);
        }

        [Test]
        public void Eight_Fouls()
        {
            var e = Eng("eight");
            var r = e.Evaluate(Shot(0, Cu(0)));
            Assert.AreEqual(FoulReason.NoContact, r.Reason); Assert.AreEqual(HandMode.Anywhere, r.Hand); Assert.AreEqual(1, r.NextPlayer);

            e = Eng("eight"); e.St.TableOpen = false; e.St.Group[0] = 1; e.St.Group[1] = 2;
            Assert.AreEqual(FoulReason.WrongFirstBall, e.Evaluate(Shot(0, BB(0, 9), Pk(3))).Reason);

            e = Eng("eight");
            Assert.AreEqual(FoulReason.NoRailAfterContact, e.Evaluate(Shot(0, Cu(0), BB(0, 1))).Reason);

            e = Eng("eight");
            Assert.AreEqual(FoulReason.CueBallPocketed, e.Evaluate(Shot(0, BB(0, 1), Pk(0))).Reason);

            e = Eng("eight"); // 8 первой на открытом столе — фол
            Assert.AreEqual(FoulReason.WrongFirstBall, e.Evaluate(Shot(0, BB(0, 8), Cu(8))).Reason);
        }

        [Test]
        public void Eight_WinOnCalledPocketAfterClearing()
        {
            var e = Eng("eight");
            e.St.TableOpen = false; e.St.Group[0] = 1; e.St.Group[1] = 2;
            for (int i = 1; i <= 7; i++) e.St.OnTable.Remove(i);
            var r = e.Evaluate(new ShotInput { CueId = 0, CalledPocket = 4, Events = new[] { BB(0, 8), Pk(8, 4) } });
            Assert.AreEqual(0, r.Winner); Assert.IsTrue(e.St.Over);
        }

        [Test]
        public void Eight_LossCases()
        {
            Eight_Loss(c => new ShotInput { CueId = 0, CalledPocket = 1, Events = new[] { BB(0, 8), Pk(8, 4) } }, 7); // не та луза
            Eight_Loss(c => new ShotInput { CueId = 0, CalledPocket = 4, Events = new[] { BB(0, 8), Pk(8, 4), Pk(0) } }, 7); // фол
            Eight_Loss(c => new ShotInput { CueId = 0, CalledPocket = 4, Events = new[] { BB(0, 7), Pk(7), Pk(8, 4) } }, 6); // вместе с последним шаром
        }

        static void Eight_Loss(System.Func<int, ShotInput> shot, int removeUpTo)
        {
            var e = Eng("eight");
            e.St.TableOpen = false; e.St.Group[0] = 1; e.St.Group[1] = 2;
            for (int i = 1; i <= removeUpTo; i++) e.St.OnTable.Remove(i);
            var r = e.Evaluate(shot(0));
            Assert.AreEqual(1, r.Winner);
        }

        // ---------- «девятка» ----------
        [Test]
        public void Nine_FirstBallMustBeLowest()
        {
            var e = Eng("nine");
            Assert.AreEqual(FoulReason.WrongFirstBall, e.Evaluate(Shot(0, BB(0, 2), Cu(2))).Reason);
            e = Eng("nine");
            var r = e.Evaluate(Shot(0, BB(0, 1), Pk(1)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(0, r.NextPlayer); Assert.AreEqual(1, e.St.Score[0]);
            Assert.AreEqual(FoulReason.WrongFirstBall, e.Evaluate(Shot(0, BB(0, 3), Cu(3))).Reason); // теперь «2»
        }

        [Test]
        public void Nine_WinAndRespot()
        {
            var e = Eng("nine");
            Assert.AreEqual(0, e.Evaluate(Shot(0, BB(0, 1), Pk(9))).Winner);
            e = Eng("nine");
            var r = e.Evaluate(Shot(0, BB(0, 1), Pk(9), Pk(0)));
            Assert.AreEqual(-1, r.Winner); Assert.AreEqual(9, r.Respots[0].BallId);
            Assert.IsTrue(e.St.OnTable.Contains(9));
        }

        [Test]
        public void Nine_ThreeFoulsLose()
        {
            var e = Eng("nine");
            var r = e.Evaluate(Shot(0, Cu(0))); r = e.Evaluate(Shot(1, Cu(0))); // игрок 0 -> 1
            e.St.Current = 0;
            r = e.Evaluate(Shot(0, Cu(0)));
            Assert.IsTrue(r.Warning);
            e.St.Current = 0;
            r = e.Evaluate(Shot(0, Cu(0)));
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void Nine_BreakNeedsPocketOrFourRails()
        {
            Assert.AreEqual(FoulReason.IllegalBreak, Eng("nine", true).Evaluate(Shot(0, BB(0, 1), Cu(2))).Reason);
            Assert.IsFalse(Eng("nine", true).Evaluate(Shot(0, BB(0, 1), Cu(2), Cu(3), Cu(4), Cu(5))).Foul);
        }

        // ---------- пирамида ----------
        [Test]
        public void American_ScoringAndContinuing()
        {
            var e = Eng("american");
            var r = e.Evaluate(Shot(0, BB(0, 5), Pk(5), Pk(7)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(2, e.St.Score[0]); Assert.AreEqual(0, r.NextPlayer);
            Assert.IsFalse(e.St.OnTable.Contains(5));
        }

        [Test]
        public void American_BallPocketedBeforeContactReturnsWithoutPoints()
        {
            var e = Eng("american");
            var r = e.Evaluate(Shot(0, Pk(5), BB(0, 6), Cu(6, 0, 0.2), Cu(6, 1, 0.2)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(0, e.St.Score[0]);
            Assert.AreEqual(5, r.Respots[0].BallId); Assert.IsTrue(e.St.OnTable.Contains(5));
            Assert.AreEqual(1, r.NextPlayer);
        }

        [Test]
        public void American_NoScoreNeedsTwoCushionsOrCenterCross()
        {
            Assert.AreEqual(FoulReason.NoRailAfterContact, Eng("american").Evaluate(Shot(0, BB(0, 5), Cu(5, 0, 0.2), Cu(5, 0, 0.3))).Reason);
            Assert.IsFalse(Eng("american").Evaluate(Shot(0, BB(0, 5), Cu(5, 0, 0.2), Cu(5, 1, 0.2))).Foul);
            Assert.IsFalse(Eng("american").Evaluate(Shot(0, BB(0, 5), Cross(5), Cu(5))).Foul);
            Assert.AreEqual(FoulReason.NoRailAfterContact, Eng("american").Evaluate(Shot(0, BB(0, 5))).Reason);
        }

        [Test]
        public void American_FoulReturnsBallsAndPenalty()
        {
            var e = Eng("american");
            e.St.Score[0] = 3; e.St.Scored[0].AddRange(new[] { 9, 4, 12 });
            var r = e.Evaluate(Shot(0, BB(0, 5), Pk(7), Pk(0)));
            Assert.IsFalse(r.Foul); // забито после контакта — очки
            e = Eng("american");
            e.St.Score[0] = 3; e.St.Scored[0].AddRange(new[] { 9, 4, 12 });
            r = e.Evaluate(Shot(0, Pk(0))); // без контакта, биток забит
            Assert.IsTrue(r.Foul); Assert.AreEqual(1, r.NextPlayer);
            Assert.AreEqual(2, e.St.Score[0]);
            CollectionAssert.AreEquivalent(new[] { 0, 4 }, r.Respots.Select(x => x.BallId).ToArray());
        }

        [Test]
        public void American_EightPointsWin()
        {
            var e = Eng("american"); e.St.Score[0] = 7;
            Assert.AreEqual(0, e.Evaluate(Shot(0, BB(0, 5), Pk(5))).Winner);
        }

        [Test]
        public void Siberian_CuePocketedAfterContactGivesPointAndHandFromHome()
        {
            var e = Eng("siberian");
            var r = e.Evaluate(Shot(0, BB(0, 5), Pk(0)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(1, e.St.Score[0]); Assert.AreEqual(HandMode.Home, r.Hand); Assert.AreEqual(0, r.NextPlayer);
            Assert.IsTrue(e.St.OnTable.Contains(0));
        }

        [Test]
        public void Siberian_ShotFromHomeAtBallInHomeIsFoul()
        {
            var e = Eng("siberian");
            var inHome = new Vec2(-0.9, 0);
            var r = e.Evaluate(new ShotInput { CueId = 0, FromHome = true, Events = new[] { BB(0, 5, inHome), Pk(5) } });
            Assert.AreEqual(FoulReason.HomeShot, r.Reason); Assert.AreEqual(0, e.St.Score[0]);
            Assert.AreEqual(5, r.Respots[0].BallId);
            e = Eng("siberian");
            r = e.Evaluate(new ShotInput { CueId = 0, FromHome = true, Events = new[] { BB(0, 5, new Vec2(0, 0)), Pk(5) } });
            Assert.IsFalse(r.Foul);
        }

        [Test]
        public void Siberian_HomeShotMovesNearestWhiteWhenNoneBeyondHomeLine()
        {
            var e = Eng("siberian");
            var balls = new List<Ball> { new Ball { Id = 1, Pos = new Vec2(-1.0, 0) }, new Ball { Id = 2, Pos = new Vec2(-0.7, 0.1) } };
            Assert.AreEqual(2, e.HomeShotBallToMove(balls));
            balls.Add(new Ball { Id = 3, Pos = new Vec2(0.2, 0) });
            Assert.AreEqual(-1, e.HomeShotBallToMove(balls));
        }

        // ---------- расстановка и возврат ----------
        [Test]
        public void EightRackLayout()
        {
            var map = Map("eight");
            var rack = Placement.Rack(GameKind.Eight, map, new Rng(7));
            Assert.AreEqual(15, rack.Count);
            Assert.AreEqual(8, rack[4].id);
            Assert.AreNotEqual(rack[10].id < 8, rack[14].id < 8); // один сплошной, другой полосатый
            Assert.AreEqual(15, rack.Select(x => x.id).Distinct().Count());
            for (int i = 0; i < rack.Count; i++)
                for (int j = i + 1; j < rack.Count; j++)
                    Assert.Greater((rack[i].pos - rack[j].pos).Len, map.BallDiameter - 1e-9);
        }

        [Test]
        public void NineRackAndPyramidRack()
        {
            var r9 = Placement.Rack(GameKind.Nine, Map("nine"), new Rng(3));
            Assert.AreEqual(9, r9.Count); Assert.AreEqual(1, r9[0].id); Assert.AreEqual(9, r9[4].id);
            var rp = Placement.Rack(GameKind.American, Map("american"), new Rng(3));
            Assert.AreEqual(15, rp.Count); Assert.AreEqual(0.6, rp[0].pos.X, 1e-9);
        }

        [Test]
        public void FindSpotChain()
        {
            var map = Map("eight");
            var rest = new Rest(map);
            var occ = new List<Vec2>();
            Assert.AreEqual(0.6, Placement.FindSpot(map, rest, 1.075, new Vec2(0.6, 0), occ).X, 1e-12);
            occ.Add(new Vec2(0.6, 0));                                   // занято -> точка возврата
            Assert.AreEqual(-0.9, Placement.FindSpot(map, rest, 1.075, new Vec2(0.6, 0), occ).X, 1e-12);
            occ.Add(new Vec2(-0.9, 0.5));                                // занято и она -> линия по оси
            var p = Placement.FindSpot(map, rest, 1.075, new Vec2(0.6, 0), occ);
            Assert.AreEqual(0, p.Y, 1e-12); Assert.Greater(p.X, 0.6 + map.BallDiameter);
        }
    
        // ---------- снукер ----------
        static RulesEngine Snk(int reds = 15, bool colourOn = false)
        {
            var e = Eng("snooker");
            for (int i = 15; i > reds; i--) e.St.OnTable.Remove(i);
            e.St.ColourOn = colourOn;
            return e;
        }

        [Test]
        public void Snooker_RedThenColour()
        {
            var e = Snk();
            var r = e.Evaluate(new ShotInput { CueId = 0, Events = new[] { BB(0, 3), Pk(3) } });
            Assert.IsFalse(r.Foul); Assert.AreEqual(1, e.St.Score[0]); Assert.IsTrue(e.St.ColourOn); Assert.AreEqual(0, r.NextPlayer);
            Assert.IsFalse(e.St.OnTable.Contains(3));
            r = e.Evaluate(new ShotInput { CueId = 0, NominatedColor = 19, Events = new[] { BB(0, 19), Pk(19) } });
            Assert.AreEqual(6, e.St.Score[0]); Assert.IsFalse(e.St.ColourOn);
            Assert.AreEqual(19, r.Respots[0].BallId); Assert.AreEqual(0, r.Respots[0].Point.X, 1e-9); Assert.IsTrue(e.St.OnTable.Contains(19));
        }

        [Test]
        public void Snooker_MissIsNotFoul()
        {
            var e = Snk();
            var r = e.Evaluate(Shot(0, BB(0, 3), Cu(3)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(1, r.NextPlayer); Assert.AreEqual(0, e.St.Score[1]);
            e = Snk();
            r = e.Evaluate(Shot(0, Cu(0), Pk(0)));
            Assert.IsFalse(r.Foul); Assert.AreEqual(HandMode.Home, r.Hand); Assert.AreEqual(1, r.NextPlayer); Assert.AreEqual(0, e.St.Score[1]);
        }

        [Test]
        public void Snooker_FoulValues()
        {
            var e = Snk();                                                   // красный «на», тронут жёлтый (2) → минимум 4
            var r = e.Evaluate(Shot(0, BB(0, 16), Cu(16)));
            Assert.IsTrue(r.Foul); Assert.AreEqual(4, e.St.Score[1]); Assert.IsTrue(e.St.FreeBallEligible);
            e = Snk();                                                       // забит чёрный не «на» → 7, цветной возвращается, красный остаётся забитым
            r = e.Evaluate(Shot(0, BB(0, 2), Pk(2), Pk(21)));
            Assert.AreEqual(7, e.St.Score[1]); Assert.AreEqual(0, e.St.Score[0]);
            Assert.IsFalse(e.St.OnTable.Contains(2)); Assert.IsTrue(e.St.OnTable.Contains(21));
            Assert.AreEqual(21, r.Respots[0].BallId);
            e = Snk();                                                       // биток после контакта в лузе
            r = e.Evaluate(Shot(0, BB(0, 2), Pk(0)));
            Assert.IsTrue(r.Foul); Assert.AreEqual(4, e.St.Score[1]); Assert.AreEqual(HandMode.Home, r.Hand);
        }

        [Test]
        public void Snooker_ColourSequence()
        {
            var e = Snk(0);
            var r = e.Evaluate(Shot(0, BB(0, 16), Pk(16)));
            Assert.AreEqual(2, e.St.Score[0]); Assert.IsFalse(e.St.OnTable.Contains(16));
            r = e.Evaluate(Shot(0, BB(0, 4), Cu(4)));                        // «на» зелёный (3), тронут коричневый (4)
            Assert.IsTrue(r.Foul); Assert.AreEqual(4, e.St.Score[1]);
        }

        [Test]
        public void Snooker_LastBlackWinsOrTiebreak()
        {
            var e = Snk(0);
            for (int i = 16; i <= 20; i++) e.St.OnTable.Remove(i);
            e.St.Score[0] = 10; e.St.Score[1] = 12;
            var r = e.Evaluate(Shot(0, BB(0, 21), Pk(21)));
            Assert.AreEqual(17, e.St.Score[0]); Assert.AreEqual(0, r.Winner);

            e = Snk(0);
            for (int i = 16; i <= 20; i++) e.St.OnTable.Remove(i);
            e.St.Score[0] = 0; e.St.Score[1] = 7;
            r = e.Evaluate(Shot(0, BB(0, 21), Pk(21)));
            Assert.IsTrue(r.Tiebreak); Assert.AreEqual(-1, r.Winner); Assert.IsTrue(e.St.OnTable.Contains(21)); Assert.AreEqual(HandMode.Home, r.Hand);
            e.St.Current = 1;
            r = e.Evaluate(Shot(0, BB(0, 21), Cu(21)));                      // промах, ход переходит
            Assert.IsFalse(r.Foul); Assert.AreEqual(0, r.NextPlayer);
            e.St.Current = 0;
            r = e.Evaluate(Shot(0, BB(0, 21), Pk(21)));
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void Snooker_FreeBall()
        {
            var e = Snk();
            e.Evaluate(Shot(0, BB(0, 16), Cu(16)));                          // фол игрока 0, ход игрока 1
            Assert.IsTrue(e.St.FreeBallEligible);
            var r = e.Evaluate(new ShotInput { CueId = 0, FreeBallId = 19, Events = new[] { BB(0, 19), Pk(19) } });
            Assert.IsFalse(r.Foul); Assert.AreEqual(1, e.St.Score[1] - 4); Assert.IsTrue(e.St.ColourOn);
            Assert.AreEqual(19, r.Respots[0].BallId); Assert.AreEqual(1, r.NextPlayer);
        }

        [Test]
        public void Snooker_IsSnookered()
        {
            var e = Snk(1);
            for (int i = 16; i <= 21; i++) e.St.OnTable.Remove(i); // на столе только один красный
            var cue = new Vec2(-0.5, 0);
            var balls = new List<Ball> { new Ball { Id = 1, Pos = new Vec2(0, 0) }, new Ball { Id = 19, Pos = new Vec2(-0.25, 0) } };
            Assert.IsTrue(e.IsSnookered(cue, balls));
            balls[1].Pos = new Vec2(-0.25, 0.5);
            Assert.IsFalse(e.IsSnookered(cue, balls));
        }

        [Test]
        public void SnookerRack()
        {
            var map = Map("snooker");
            var rack = Placement.Rack(GameKind.Snooker, map, new Rng(1));
            Assert.AreEqual(21, rack.Count);
            Assert.AreEqual(1, rack[0].id); Assert.AreEqual(0.6, rack[0].pos.X, 1e-9);
            Assert.AreEqual(21, rack.Last().id); Assert.AreEqual(1.0, rack.Last().pos.X, 1e-9);
        }
    }
}
