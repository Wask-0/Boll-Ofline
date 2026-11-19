using System.Collections.Generic;

namespace Boll.Core.Content
{
    public enum GameKind { Eight, Nine, American, Siberian, Snooker }

    public enum ObstacleType { Wall, Bumper, Mover }

    public enum MoverMode { PingPong, Circle }

    /// <summary>Параметры вида: диаметр шара и горловина лузы по умолчанию (м).</summary>
    public static class KindDefaults
    {
        public static double BallDiameter(GameKind k) =>
            k == GameKind.American || k == GameKind.Siberian ? 0.068 : k == GameKind.Snooker ? 0.0525 : 0.05715;

        public static double Throat(GameKind k) =>
            k == GameKind.American || k == GameKind.Siberian ? 0.076 : k == GameKind.Snooker ? 0.086 : 0.115;

        public static string Key(GameKind k) => k.ToString().ToLowerInvariant();
    }

    public sealed class Pocket
    {
        public Vec2 Pos;
        public double Throat; // горловина, м
    }

    public sealed class Obstacle
    {
        public ObstacleType Type;
        public Vec2 A, B;            // отрезок (стенка, отрезок-бампер, движущаяся стенка)
        public Vec2 Center;          // круг-бампер; центр орбиты движущегося (Circle)
        public double Radius;        // круг-бампер; радиус орбиты
        public bool IsCircle;        // бампер-круг
        public double Bounce = 1.0;  // множитель бампера 1,05…1,40
        public Vec2[] Path;          // смещения от начала для PingPong (Path[0] = 0,0)
        public MoverMode Mode;
        public double Speed;         // м/с, постоянная
    }

    public sealed class Layout
    {
        public Vec2 RackPoint, RackAxis;     // место и ось расстановки (единичный вектор)
        public Vec2[] Home;                  // дом / зона D (многоугольник)
        public Vec2 HomeLineA, HomeLineB;    // линия дома
        public Dictionary<string, Vec2> Marks = new Dictionary<string, Vec2>(); // снукер: 6 цветных; пирамида: "back"
        public Vec2[] ReturnPoints;          // точки возврата шаров
    }

    public sealed class MapData
    {
        public string Id, Name;
        public GameKind Kind;
        public double BallDiameter;
        public Vec2[] Outline;               // CCW
        public Pocket[] Pockets;
        public Obstacle[] Obstacles;
        public Layout Layout;
    }

    public sealed class ContentException : System.Exception
    {
        public ContentException(string msg) : base(msg) { }
    }
}
