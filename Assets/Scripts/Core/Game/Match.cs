using System;
using System.Collections.Generic;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Physics;
using Boll.Core.Rules;

namespace Boll.Core.Game
{
    public enum Phase { Aiming, Shooting, Over }

    /// <summary>Параметры удара — всё, что нужно, чтобы переиграть удар с тем же результатом.</summary>
    public sealed class ShotParams
    {
        public int CueId;
        public double Angle, Power, SpinX, SpinY;   // Power 0..1; SpinX вправо, SpinY вверх (−1..1)
        public int CalledPocket = -1;               // «восьмёрка»: заказ лузы под «8»
        public int NominatedColor = -1;             // снукер: цветной после красного
        public int FreeBallId = -1;                 // снукер: свободный шар
    }

    public sealed class BallSnap
    {
        public int Id; public double X, Y, Vx, Vy, Wx, Wy, Wz; public bool Pocketed; public int PocketIndex;
    }

    /// <summary>Полное состояние партии для сохранения. Pending != null — удар, который нужно переиграть.</summary>
    public sealed class MatchSnapshot
    {
        public string MapId, PhysicsId;
        public ulong Rng; public long Step;
        public MatchState State;
        public List<BallSnap> Balls = new List<BallSnap>();
        public ShotParams Pending;
    }

    /// <summary>Автомат партии: прицел → удар → ожидание остановки → оценка → следующий ход / итог. Без Unity.</summary>
    public sealed class Match
    {
        public const double SafetySeconds = 180;

        public readonly MapData Map;
        public readonly PhysicsModel Model;
        public readonly GameKind Kind;
        public readonly Rng Rng;
        public readonly MatchState St;
        public readonly RulesEngine Rules;
        public readonly Solver Solver;
        public readonly Rest Rest;

        public Phase Phase { get; private set; } = Phase.Aiming;
        public ShotResult LastResult { get; private set; }
        public ShotParams LastShot { get; private set; }
        public long SafetySteps = (long)(SafetySeconds * 480);   // для тестов можно уменьшить
        public bool SafetyTriggered { get; private set; }
        public List<SolverEvent> Timeline => rec?.Timeline;

        public MatchSnapshot PreShot { get; private set; }   // состояние перед последним ударом (с параметрами) — сохранять сразу после Shoot
        ShotRecorder rec;
        ShotParams pending;
        long shotSteps;

        public Match(MapData map, PhysicsModel model, ulong seed)
        {
            Map = map; Model = model; Kind = map.Kind;
            Rng = new Rng(seed);
            St = MatchState.New(Kind, Rng);
            Rules = new RulesEngine(map, St, Rng);
            Solver = new Solver(map, model);
            Rest = new Rest(map);
            SetupTable();
        }

        Match(MapData map, PhysicsModel model, MatchSnapshot s)
        {
            Map = map; Model = model; Kind = map.Kind;
            Rng = new Rng(s.Rng); St = s.State;
            Rules = new RulesEngine(map, St, Rng);
            Solver = new Solver(map, model);
            Rest = new Rest(map);
            foreach (BallSnap b in s.Balls)
            {
                Ball x = Solver.AddBall(b.Id, new Vec2(b.X, b.Y));
                x.Vel = new Vec2(b.Vx, b.Vy); x.Wx = b.Wx; x.Wy = b.Wy; x.Wz = b.Wz; x.Pocketed = b.Pocketed; x.PocketIndex = b.PocketIndex;
            }
            Solver.StepIndex = s.Step;
            if (St.Over) Phase = Phase.Over;
        }

        public MatchSnapshot Snapshot(ShotParams pendingShot = null)
        {
            var s = new MatchSnapshot { MapId = Map.Id, PhysicsId = Model.Id, Rng = Rng.State, Step = Solver.StepIndex, State = St.Clone(), Pending = pendingShot };
            foreach (Ball b in Solver.Balls)
                s.Balls.Add(new BallSnap { Id = b.Id, X = b.Pos.X, Y = b.Pos.Y, Vx = b.Vel.X, Vy = b.Vel.Y, Wx = b.Wx, Wy = b.Wy, Wz = b.Wz, Pocketed = b.Pocketed, PocketIndex = b.PocketIndex });
            return s;
        }

        /// <summary>Восстановить партию. Если был удар в полёте — он переигрывается с теми же параметрами.</summary>
        public static Match Restore(MapData map, PhysicsModel model, MatchSnapshot s)
        {
            var m = new Match(map, model, s);
            if (s.Pending != null && !m.Shoot(s.Pending)) throw new InvalidOperationException("saved shot is not playable");
            return m;
        }

        public Ball Cue => Solver.Balls.FirstOrDefault(b => b.Id == 0);
        public Ball BallById(int id) => Solver.Balls.FirstOrDefault(b => b.Id == id);

        // ---------- расстановка ----------
        void SetupTable()
        {
            Solver.Balls.Clear();
            foreach (var (id, pos) in Placement.Rack(Kind, Map, Rng)) Solver.AddBall(id, pos);
            Vec2 c = Placement.FindSpot(Map, Rest, Model.PocketCapture, Placement.HomeCenter(Map), Occupied(-1));
            Solver.AddBall(0, c);
        }

        List<Vec2> Occupied(int exceptId)
        {
            var l = new List<Vec2>();
            foreach (Ball b in Solver.Balls) if (!b.Pocketed && b.Id != exceptId) l.Add(b.Pos);
            return l;
        }

        void Reinstate(Ball b, Vec2 preferred)
        {
            b.Vel = default; b.Wx = b.Wy = b.Wz = 0;
            b.Pos = Placement.FindSpot(Map, Rest, Model.PocketCapture, preferred, Occupied(b.Id));
            b.Pocketed = false; b.PocketIndex = -1;
        }

        // ---------- ввод игрока / бота ----------
        public bool CanShoot => Phase == Phase.Aiming && Rest.CanShoot(Solver);

        /// <summary>Шары, которыми можно бить (Американка — любой на столе).</summary>
        public List<int> CueCandidates() =>
            Kind == GameKind.American ? Solver.Balls.Where(b => !b.Pocketed).Select(b => b.Id).ToList() : new List<int> { 0 };

        /// <summary>«8» — цель: нужно заказать лузу.</summary>
        public bool NeedsCalledPocket(int cueId) =>
            Kind == GameKind.Eight && !St.BreakPending && !St.TableOpen && !Solver.Balls.Any(b => !b.Pocketed && b.Id != 8 && b.Id != 0 && GroupOf(b.Id) == St.Group[St.Current]);

        static int GroupOf(int id) => id >= 1 && id <= 7 ? 1 : id >= 9 && id <= 15 ? 2 : 0;

        public bool FreeBallAvailable => Kind == GameKind.Snooker && St.FreeBallEligible && Rules.IsSnookered(Cue.Pos, Solver.Balls);

        /// <summary>Поставить биток с руки. false — точка недопустима или руки нет.</summary>
        public bool TryPlaceCue(Vec2 pos)
        {
            if (Phase != Phase.Aiming || St.Hand == HandMode.None || !Rest.AllAtRest(Solver)) return false;
            Ball cue = Cue;
            if (!Placement.IsValidHandSpot(Map, Rest, Model.PocketCapture, pos, St.Hand, Occupied(0))) return false;
            cue.Pos = pos; cue.Vel = default;
            return true;
        }

        /// <summary>Сделать удар. false — удар сейчас недопустим (шары движутся, не заказана луза и т. п.).</summary>
        public bool Shoot(ShotParams p)
        {
            if (!CanShoot) return false;
            if (!CueCandidates().Contains(p.CueId)) return false;
            Ball cue = BallById(p.CueId);
            if (cue == null || cue.Pocketed) return false;
            if (NeedsCalledPocket(p.CueId) && (p.CalledPocket < 0 || p.CalledPocket >= Map.Pockets.Length)) return false;
            if (Kind == GameKind.Snooker)
            {
                if (St.ColourOn && !RulesEngine.IsColour(p.NominatedColor)) return false;
                if (p.FreeBallId >= 0 && !FreeBallAvailable) return false;
            }
            PreShot = Snapshot(p);
            pending = p; LastShot = p;
            Solver.Events.Clear();
            rec = new ShotRecorder(Solver);
            shotSteps = 0; SafetyTriggered = false;
            Solver.Strike(cue, p.Angle, p.Power, p.SpinX, p.SpinY);
            Phase = Phase.Shooting;
            return true;
        }

        // ---------- ход времени ----------
        /// <summary>Один шаг физики (1/480 с). Движущиеся объекты едут всегда, в том числе при прицеливании.</summary>
        public void Step()
        {
            if (Phase == Phase.Over) return;
            Solver.Step();
            if (Phase == Phase.Aiming) { HandleIdlePockets(); return; }
            rec.Update();
            shotSteps++;
            if (Rest.CanShoot(Solver)) { Finish(); return; }
            if (shotSteps >= SafetySteps) { RelocateStuck(); SafetyTriggered = true; Finish(); }
        }

        /// <summary>Шагает до конца удара не больше maxSteps шагов; true — удар завершён.</summary>
        public bool RunShot(long maxSteps)
        {
            for (long i = 0; i < maxSteps && Phase == Phase.Shooting; i++) Step();
            return Phase != Phase.Shooting;
        }

        void HandleIdlePockets()
        {
            if (Solver.Events.Count == 0) return;
            foreach (SolverEvent e in Solver.Events)
            {
                if (e.Type != EventType.Pocket) continue;
                Ball b = BallById(e.A);
                Reinstate(b, b.Id == 0 ? Placement.HomeCenter(Map) : Map.Layout.RackPoint);
            }
            Solver.Events.Clear();
        }

        /// <summary>Защитное время: застрявшие/ещё едущие шары останавливаются и переставляются на ближайшую свободную точку.</summary>
        void RelocateStuck()
        {
            foreach (Ball b in Solver.Balls.ToList())
            {
                if (b.Pocketed) continue;
                if (Rest.BallAtRest(b) && !(Rest.HasMovers && Rest.InMoverZone(b.Pos))) continue;
                b.Vel = default; b.Wx = b.Wy = b.Wz = 0;
                b.Pos = Placement.FindSpot(Map, Rest, Model.PocketCapture, b.Pos, Occupied(b.Id));
            }
        }

        void Finish()
        {
            var input = new ShotInput
            {
                CueId = pending.CueId, Events = rec.Timeline, CalledPocket = pending.CalledPocket,
                NominatedColor = pending.NominatedColor, FreeBallId = pending.FreeBallId,
                FromHome = St.Hand == HandMode.Home
            };
            ShotResult r = Rules.Evaluate(input);
            LastResult = r;
            if (r.Rerack) { SetupTable(); }
            else Apply(r);
            Phase = St.Over ? Phase.Over : Phase.Aiming;
            rec = null;
            Solver.Events.Clear();
        }

        void Apply(ShotResult r)
        {
            foreach (Respot rp in r.Respots)
            {
                Ball b = BallById(rp.BallId);
                if (b != null) Reinstate(b, rp.Point);
            }
            // шар числится на столе, но лежит в лузе (биток с руки, возвращённые правилами) — вернуть
            foreach (Ball b in Solver.Balls)
                if (b.Pocketed && St.OnTable.Contains(b.Id)) Reinstate(b, b.Id == 0 ? Placement.HomeCenter(Map) : Map.Layout.RackPoint);
            if (Kind == GameKind.Siberian && St.Hand == HandMode.Home)
            {
                int move = Rules.HomeShotBallToMove(Solver.Balls);
                if (move >= 0) Reinstate(BallById(move), Map.Layout.Marks["back"]);
            }
        }

        // ---------- выбор карты ----------
        /// <summary>После победы — случайная другая карта того же вида; после поражения — та же.</summary>
        public static MapData NextMap(MapCatalog catalog, MapData current, bool won, Rng rng) =>
            won ? catalog.Next(current.Kind, current.Id, rng.Next) : current;
    }
}
