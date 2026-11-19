using System;
using System.Collections.Generic;
using Boll.Core.Content;
using Boll.Core.Physics;

namespace Boll.Core.Rules
{
    public enum FoulReason { None, NoContact, WrongFirstBall, NoRailAfterContact, CueBallPocketed, IllegalBreak, HomeShot, ThreeFouls }

    public sealed class Respot { public int BallId; public Vec2 Point; }

    public sealed class ShotInput
    {
        public int CueId;                       // какой шар бьют (пул: 0)
        public IReadOnlyList<SolverEvent> Events;
        public int CalledPocket = -1;           // «восьмёрка»: заказанная луза для «8»
        public bool FromHome;                   // удар с руки из дома (Сибирка)
        public int NominatedColor = -1;         // снукер: заявленный цветной (16–21), когда «на» цветной после красного
        public int FreeBallId = -1;             // снукер: заявленный «свободный шар»
    }

    public sealed class ShotResult
    {
        public bool Foul, Rerack, Warning, Tiebreak;
        public FoulReason Reason;
        public string Message = "";             // для показа 2 с
        public int Winner = -1;
        public int NextPlayer;
        public HandMode Hand;
        public List<Respot> Respots = new List<Respot>();
    }

    /// <summary>Состояние партии, нужное правилам. Публичные поля — для сохранения в JSON.</summary>
    public sealed class MatchState
    {
        public GameKind Kind;
        public int Current;                     // 0 — игрок, 1 — бот
        public int[] Score = new int[2];
        public bool BreakPending = true;
        public HandMode Hand = HandMode.Home;
        public bool Over; public int Winner = -1;
        public HashSet<int> OnTable = new HashSet<int>();
        // «восьмёрка»
        public bool TableOpen = true;
        public int[] Group = new int[2];        // 0 нет, 1 сплошные 1–7, 2 полосатые 9–15
        // «девятка»
        public int[] FoulStreak = new int[2];
        // снукер
        public bool ColourOn;                   // после красного — «на» цветной
        public bool Tiebreak;                   // переигровка чёрного
        public bool FreeBallEligible;           // после фола можно заявить свободный шар (если снукер)
        // пирамида
        public List<int>[] Scored = { new List<int>(), new List<int>() };

        public static MatchState New(GameKind kind, Rng rng)
        {
            var s = new MatchState { Kind = kind, Current = rng.Toss() ? 1 : 0 };
            s.ResetBalls();
            return s;
        }

        public MatchState Clone()
        {
            var c = (MatchState)MemberwiseClone();
            c.Score = (int[])Score.Clone(); c.Group = (int[])Group.Clone(); c.FoulStreak = (int[])FoulStreak.Clone();
            c.OnTable = new HashSet<int>(OnTable);
            c.Scored = new[] { new List<int>(Scored[0]), new List<int>(Scored[1]) };
            return c;
        }

        public void ResetBalls()
        {
            OnTable.Clear();
            int n = Placement.BallCount(Kind);
            for (int i = 0; i < n; i++) OnTable.Add(i);
        }
    }

    /// <summary>Собирает события шага из решателя и добавляет пересечения центральной линии.</summary>
    public sealed class ShotRecorder
    {
        readonly Solver solver;
        readonly Vec2 axis;
        readonly double center;
        readonly Dictionary<int, double> side = new Dictionary<int, double>();
        int read;
        public readonly List<SolverEvent> Timeline = new List<SolverEvent>();

        public ShotRecorder(Solver s)
        {
            solver = s; axis = s.Map.Layout.RackAxis;
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (Vec2 p in s.Map.Outline) { double d = p.Dot(axis); mn = Math.Min(mn, d); mx = Math.Max(mx, d); }
            center = (mn + mx) / 2;
            read = s.Events.Count;
            foreach (Ball b in s.Balls) side[b.Id] = b.Pos.Dot(axis) - center;
        }

        /// <summary>Вызывать после каждого Solver.Step().</summary>
        public void Update()
        {
            foreach (Ball b in solver.Balls)
            {
                if (b.Pocketed) continue;
                double d = b.Pos.Dot(axis) - center;
                if (side.TryGetValue(b.Id, out double old) && old != 0 && (old < 0) != (d < 0))
                    Timeline.Add(new SolverEvent { Type = EventType.CenterCross, Step = solver.StepIndex, A = b.Id, B = -1, Index = -1, Pos = b.Pos });
                side[b.Id] = d;
            }
            for (; read < solver.Events.Count; read++) Timeline.Add(solver.Events[read]);
        }
    }
}
