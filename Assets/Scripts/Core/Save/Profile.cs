using System;
using System.Linq;
using Boll.Core.Content;

namespace Boll.Core.Save
{
    public enum BuyResult { Ok, NotEnoughStars, AlreadyOwned, Unknown }

    /// <summary>Прогрессия: звёзды, открытие ботов, статистика, скины. Работает поверх SaveData.</summary>
    public sealed class Profile
    {
        public const int MaxBotLevel = 8;
        public readonly SaveData D;
        readonly Catalogs cat;

        public Profile(SaveData data, Catalogs catalogs) { D = data; cat = catalogs; Reconcile(); }

        // ---------- итоги партий ----------
        /// <summary>Итог партии в обычном режиме. Свободный режим сюда не передаётся. Прерванная партия — поражение.</summary>
        public void RecordResult(GameKind kind, int botLevel, bool won)
        {
            string key = KindDefaults.Key(kind);
            if (!D.Stats.ByKind.TryGetValue(key, out Counter ck)) D.Stats.ByKind[key] = ck = new Counter();
            Counter cb = D.Stats.ByBot[botLevel - 1];
            foreach (Counter c in new[] { D.Stats.Total, cb, ck }) { c.Games++; if (won) c.Wins++; }
            if (!won) return;
            D.Stars++;
            if (botLevel == D.BotsUnlocked && D.BotsUnlocked < MaxBotLevel) D.BotsUnlocked++;
        }

        public bool IsBotOpen(int level) => level >= 1 && level <= D.BotsUnlocked;

        /// <summary>Процент побед, округление до целого; «—», если партий нет.</summary>
        public static string WinPercent(Counter c) => c.Games == 0 ? "—" : ((int)Math.Round(100.0 * c.Wins / c.Games, MidpointRounding.AwayFromZero)) + " %";

        public Counter KindStats(GameKind k) => D.Stats.ByKind.TryGetValue(KindDefaults.Key(k), out var c) ? c : new Counter();

        // ---------- скины ----------
        Skin Find(string id) => cat.Cloths.Concat(cat.Cues).FirstOrDefault(s => s.Id == id);
        bool IsCloth(string id) => cat.Cloths.Any(s => s.Id == id);

        public bool IsOwned(string id) { Skin s = Find(id); return s != null && (s.Price == 0 || D.Owned.ContainsKey(id)); }
        public bool IsSelected(string id) => D.Cloth == id || D.Cue == id;

        public BuyResult Buy(string id)
        {
            Skin s = Find(id);
            if (s == null) return BuyResult.Unknown;
            if (IsOwned(id)) return BuyResult.AlreadyOwned;
            if (D.Stars < s.Price) return BuyResult.NotEnoughStars;
            D.Stars -= s.Price; D.Owned[id] = s.Price;
            return BuyResult.Ok;
        }

        public bool Select(string id)
        {
            if (!IsOwned(id)) return false;
            if (IsCloth(id)) D.Cloth = id; else D.Cue = id;
            return true;
        }

        /// <summary>Пропавший из каталога купленный скин — звёзды возвращаются без сообщения; недоступный выбор — стандартный.</summary>
        void Reconcile()
        {
            foreach (string id in D.Owned.Keys.ToList())
                if (Find(id) == null) { D.Stars += D.Owned[id]; D.Owned.Remove(id); }
            if (!IsOwned(D.Cloth)) D.Cloth = cat.Cloths[0].Id;
            if (!IsOwned(D.Cue)) D.Cue = cat.Cues[0].Id;
        }
    }
}
