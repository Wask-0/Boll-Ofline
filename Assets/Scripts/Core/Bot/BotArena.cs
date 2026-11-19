using Boll.Core.Content;
using Boll.Core.Game;

namespace Boll.Core.Bot
{
    /// <summary>Партия бот против бота (для подстройки уровней и проверки карт: предел 150 ударов).</summary>
    public static class BotArena
    {
        public const int MaxShots = 150;

        /// <summary>Победитель 0/1 или −1, если за MaxShots ударов партия не закончилась. levels[0] играет за игрока 0.</summary>
        public static int Play(Match m, BotLevel[] levels, out int shots)
        {
            shots = 0;
            while (m.Phase != Phase.Over && shots < MaxShots)
            {
                var planner = new BotPlanner(m, levels[m.St.Current]);
                planner.Finish();
                if (!m.Shoot(planner.Result)) break;
                m.RunShot(480 * 200);
                shots++;
            }
            return m.St.Over ? m.St.Winner : -1;
        }
    }
}
