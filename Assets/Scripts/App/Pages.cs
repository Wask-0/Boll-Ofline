using System;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Rules;
using Boll.Core.Save;
using UnityEngine;
using UnityEngine.UI;

namespace Boll.App
{
    public static class Names
    {
        public static string Kind(GameKind k)
        {
            switch (k)
            {
                case GameKind.Eight: return "Восьмёрка";
                case GameKind.Nine: return "Девятка";
                case GameKind.American: return "Американка";
                case GameKind.Siberian: return "Сибирка";
                default: return "Снукер";
            }
        }
    }

    public abstract class MenuPage : Page
    {
        /// <summary>Заголовок + кнопка «Назад»; возвращает колонку для содержимого.</summary>
        protected RectTransform Frame(string title, bool back = true, float width = 0.7f)
        {
            var head = Ui.Box(R, Ui.PanelC, "Head"); Ui.Place(head, 0, 1, 1, 1, 0, -96, 0, 0);
            Ui.Label(head, title, 40, TextAnchor.MiddleCenter, Ui.Gold).rectTransform.anchorMax = Vector2.one;
            Ui.Fill(head.GetComponentInChildren<Text>().rectTransform);
            if (back)
            {
                var b = Ui.Btn(head, "Назад", Back, 64, 26, Ui.Dark);
                Ui.Place((RectTransform)b.transform, 0, 0.5f, 0, 0.5f); var rt = (RectTransform)b.transform;
                rt.pivot = new Vector2(0, 0.5f); rt.anchoredPosition = new Vector2(24, 0); rt.sizeDelta = new Vector2(160, 64);
                Destroy(b.GetComponent<LayoutElement>());
            }
            var col = Ui.Column(R, 14);
            float m = (1 - width) / 2;
            Ui.Place(col, m, 0, 1 - m, 1, 0, 24, 0, 120);
            return col;
        }

        static void Destroy(UnityEngine.Object o) => UnityEngine.Object.Destroy(o);
    }

    // ============================== главное меню ==============================
    public sealed class MainMenuPage : MenuPage
    {
        public override void Build()
        {
            var col = Frame("Boll offline", false, 0.45f);
            col.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 30, 0);
            if (A.D.Active != null) Ui.Btn(col, "Продолжить", A.Continue, 84, 32, Ui.Gold).GetComponentInChildren<Text>().color = Color.black;
            Ui.Btn(col, "Играть", () => A.Go(new KindPage(false)), 84, 32);
            Ui.Btn(col, "Свободный режим", () => A.Go(new KindPage(true)), 84, 30);
            Ui.Btn(col, "Магазин  (звёзд: " + A.D.Stars + ")", () => A.Go(new ShopPage()), 84, 30);
            Ui.Btn(col, "Статистика", () => A.Go(new StatsPage()), 84, 30);
            Ui.Btn(col, "Настройки", () => A.Go(new SettingsPage()), 84, 30);
        }

        public override void Back() => A.Ask("Выйти из игры?", "Выйти", Application.Quit, "Остаться");
    }

    // ============================== выбор вида ==============================
    public sealed class KindPage : MenuPage
    {
        readonly bool free;
        public KindPage(bool free) { this.free = free; }

        public override void Build()
        {
            var col = Frame(free ? "Свободный режим: вид игры" : "Выберите игру");
            foreach (GameKind k in AppMain.Kinds)
            {
                GameKind kk = k;
                var row = Ui.Row(col, 12, 84);
                Ui.Btn(row, Names.Kind(k), () => A.Go(free ? (Page)new FreeSetupPage(kk) : new BotPage(kk)), 84, 32);
                var r = Ui.Btn(row, "Правила", () => A.Go(new RulesPage(kk)), 84, 24, Ui.Dark);
                r.GetComponent<LayoutElement>().preferredWidth = 200; r.GetComponent<LayoutElement>().flexibleWidth = 0;
            }
        }
    }

    public sealed class RulesPage : MenuPage
    {
        readonly GameKind kind;
        public RulesPage(GameKind k) { kind = k; }

        public override void Build()
        {
            var col = Frame("Правила: " + Names.Kind(kind), true, 0.84f);
            var list = Ui.Scroll(col);
            var t = Ui.Label(list, RulesText.For(kind), 26, TextAnchor.UpperLeft);
        }
    }

    // ============================== выбор бота ==============================
    public sealed class BotPage : MenuPage
    {
        readonly GameKind kind;
        public BotPage(GameKind k) { kind = k; }

        public override void Build()
        {
            var col = Frame(Names.Kind(kind) + ": выберите соперника");
            var list = Ui.Scroll(col);
            foreach (BotLevel b in A.Cat.Bots)
            {
                BotLevel bb = b;
                bool open = A.Prof.IsBotOpen(b.Level);
                Counter c = A.D.Stats.ByBot[b.Level - 1];
                string text = b.Level + ". " + b.Name + (open ? "   (партий: " + c.Games + ", побед: " + Profile.WinPercent(c) + ")" : "   — закрыт");
                var btn = Ui.Btn(list, text, () => A.NewGame(() => A.StartMatch(A.PickMap(kind), bb.Level, false)), 76, 26, open ? Ui.Red : Ui.Dark);
                btn.interactable = open;
            }
            Ui.Label(list, "Победа над сильнейшим из открытых соперников открывает следующего.", 22, TextAnchor.MiddleCenter, Ui.Dim);
        }
    }

    // ============================== свободный режим ==============================
    public sealed class FreeSetupPage : MenuPage
    {
        readonly GameKind kind; int mapIdx, level = 1;
        public FreeSetupPage(GameKind k) { kind = k; }

        public override void Build()
        {
            var col = Frame("Свободный режим: " + Names.Kind(kind), true, 0.84f);
            var cols = Ui.Row(col, 24, 520);
            var le = cols.GetComponent<LayoutElement>(); le.flexibleHeight = 1;
            var left = Ui.Column(cols, 10); var right = Ui.Column(cols, 10);
            Ui.Label(left, "Карта", 28, TextAnchor.MiddleCenter, Ui.Gold);
            var maps = A.Maps.Maps(kind);
            var lm = Ui.Scroll(left, 8);
            for (int i = 0; i < maps.Count; i++)
            {
                int ii = i;
                Ui.Btn(lm, (i + 1) + ". " + maps[i].Name, () => { mapIdx = ii; A.Show(this); }, 64, 26, i == mapIdx ? Ui.Red : Ui.Dark);
            }
            Ui.Label(right, "Соперник", 28, TextAnchor.MiddleCenter, Ui.Gold);
            var lb = Ui.Scroll(right, 8);
            foreach (BotLevel b in A.Cat.Bots)
            {
                int l = b.Level;
                Ui.Btn(lb, l + ". " + b.Name, () => { level = l; A.Show(this); }, 64, 26, l == level ? Ui.Red : Ui.Dark);
            }
            Ui.Btn(col, "Играть", () => A.NewGame(() => A.StartMatch(maps[mapIdx], level, true)), 84, 32, Ui.Gold).GetComponentInChildren<Text>().color = Color.black;
        }
    }

    // ============================== настройки ==============================
    public sealed class SettingsPage : MenuPage
    {
        public override void Build()
        {
            var col = Frame("Настройки", true, 0.8f);
            var list = Ui.Scroll(col, 14);
            Settings s = A.D.Settings;
            Toggle(list, "Линия удара", s.AimLine, v => { s.AimLine = v; if (!v) s.BounceLine = false; A.SaveNow(); A.Show(this); });
            var bounce = Toggle(list, s.AimLine ? "Линия отскока" : "Линия отскока (нужна линия удара)", s.BounceLine, v => { s.BounceLine = v; A.SaveNow(); A.Show(this); });
            bounce.interactable = s.AimLine;

            Ui.Label(list, "Модель физики", 28, TextAnchor.MiddleCenter, Ui.Gold);
            foreach (PhysicsModel m in A.Cat.Models)
            {
                PhysicsModel mm = m;
                Ui.Btn(list, m.Name, () =>
                {
                    if (mm.Id == s.Physics) return;
                    A.Ask("Новая модель физики будет использована со следующей партии. Незаконченная партия доигрывается на прежней модели.", "Сменить",
                        () => { s.Physics = mm.Id; A.SaveNow(); A.Show(this); }, "Отмена");
                }, 64, 26, m.Id == s.Physics ? Ui.Red : Ui.Dark);
            }

            Ui.Label(list, "Громкость", 28, TextAnchor.MiddleCenter, Ui.Gold);
            Slide(list, "Общая", s.Master, v => { s.Master = v; A.ApplyAudio(); });
            Slide(list, "Музыка", s.Music, v => { s.Music = v; A.ApplyAudio(); });
            Slide(list, "Эффекты", s.Effects, v => { s.Effects = v; A.ApplyAudio(); });
            Ui.Btn(list, "Поделиться журналом ошибок", A.ShareLog, 68, 24, Ui.Dark);
        }

        Button Toggle(Transform p, string label, bool on, Action<bool> set) =>
            Ui.Btn(p, label + ":  " + (on ? "вкл" : "выкл"), () => set(!on), 68, 26, on ? Ui.Red : Ui.Dark);

        void Slide(Transform p, string label, int value, Action<int> set)
        {
            var row = Ui.Row(p, 16, 56);
            var t = Ui.Label(row, label + ": " + value, 26, TextAnchor.MiddleLeft);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 260;
            Ui.MakeSlider(row, value, v => { t.text = label + ": " + v; set(v); });
            row.GetComponent<LayoutElement>().preferredHeight = 56;
            // сохраняем при уходе со страницы
            pendingSave = true;
        }

        bool pendingSave;
        public override void Closed() { if (pendingSave) A.SaveNow(); }
    }

    // ============================== статистика ==============================
    public sealed class StatsPage : MenuPage
    {
        public override void Build()
        {
            var col = Frame("Статистика", true, 0.8f);
            var list = Ui.Scroll(col, 10);
            Row(list, "Всего", A.D.Stats.Total, true);
            Ui.Label(list, "По соперникам", 28, TextAnchor.MiddleCenter, Ui.Gold);
            foreach (BotLevel b in A.Cat.Bots) Row(list, b.Level + ". " + b.Name, A.D.Stats.ByBot[b.Level - 1], false);
            Ui.Label(list, "По видам игры", 28, TextAnchor.MiddleCenter, Ui.Gold);
            foreach (GameKind k in AppMain.Kinds) Row(list, Names.Kind(k), A.Prof.KindStats(k), false);
        }

        static void Row(Transform p, string name, Counter c, bool big)
        {
            var r = Ui.Box(p, Ui.Dark, "Row");
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(20, 20, 8, 8); h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = true;
            Ui.Label(r, name, big ? 30 : 26, TextAnchor.MiddleLeft);
            var t = Ui.Label(r, "партий " + c.Games + "   побед " + c.Wins + "   " + Profile.WinPercent(c), big ? 30 : 26, TextAnchor.MiddleRight);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 520;
            r.gameObject.AddComponent<LayoutElement>().minHeight = 56;
        }
    }

    // ============================== магазин ==============================
    public sealed class ShopPage : MenuPage
    {
        public override void Build()
        {
            var col = Frame("Магазин", true, 0.8f);
            Ui.Label(col, "Звёзд: " + A.D.Stars, 32, TextAnchor.MiddleCenter, Ui.Gold);
            var list = Ui.Scroll(col, 10);
            Ui.Label(list, "Сукно", 28, TextAnchor.MiddleCenter, Ui.Gold);
            foreach (Skin s in A.Cat.Cloths) Item(list, s, true);
            Ui.Label(list, "Кии", 28, TextAnchor.MiddleCenter, Ui.Gold);
            foreach (Skin s in A.Cat.Cues) Item(list, s, false);
        }

        void Item(Transform p, Skin s, bool cloth)
        {
            var r = Ui.Box(p, Ui.Dark, "Item");
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 10, 10); h.spacing = 16; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            r.gameObject.AddComponent<LayoutElement>().minHeight = 96;
            var sw = Ui.Box(r, Ui.Hex(s.Color, Color.gray), "Swatch");
            var le = sw.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = cloth ? 120 : 220; le.preferredHeight = cloth ? 76 : 30;
            if (!cloth) { le.preferredHeight = 30; }
            var name = Ui.Label(r, s.Name, 28, TextAnchor.MiddleLeft); name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            string st = A.Prof.IsSelected(s.Id) ? "Выбрано" : A.Prof.IsOwned(s.Id) ? "Куплено" : s.Price + " звёзд";
            var b = Ui.Btn(r, st, () => Tap(s), 76, 24, A.Prof.IsSelected(s.Id) ? Ui.Gold : A.Prof.IsOwned(s.Id) ? Ui.Red : Ui.Dark);
            if (A.Prof.IsSelected(s.Id)) b.GetComponentInChildren<Text>().color = Color.black;
            var bl = b.GetComponent<LayoutElement>(); bl.preferredWidth = 220; bl.minWidth = 220;
        }

        void Tap(Skin s)
        {
            if (A.Prof.IsOwned(s.Id)) { A.Prof.Select(s.Id); A.SaveNow(); A.Show(this); return; }
            if (A.D.Stars < s.Price) { A.Info("Не хватает звёзд."); return; }
            A.Ask("Купить «" + s.Name + "» за " + s.Price + " звёзд?", "Купить", () =>
            {
                if (A.Prof.Buy(s.Id) == BuyResult.Ok) { A.Prof.Select(s.Id); A.SaveNow(); }
                A.Show(this);
            });
        }
    }

    // ============================== итог партии ==============================
    public sealed class ResultPage : Page
    {
        readonly Match m; readonly int level; readonly bool free, won;
        public ResultPage(Match m, int level, bool free, bool won) { this.m = m; this.level = level; this.free = free; this.won = won; }

        public override void Build()
        {
            var col = Ui.Column(R, 22, 0);
            Ui.Place(col, 0.28f, 0.1f, 0.72f, 0.9f);
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Label(col, won ? "Победа!" : "Поражение", 64, TextAnchor.MiddleCenter, won ? Ui.Gold : Ui.Txt);
            Ui.Label(col, Names.Kind(m.Kind) + ", карта «" + m.Map.Name + "»", 28, TextAnchor.MiddleCenter, Ui.Dim);
            Ui.Label(col, "Счёт: вы " + m.St.Score[0] + " : " + m.St.Score[1] + " соперник", 36);
            if (!free && won) Ui.Label(col, "+1 звезда (всего " + A.D.Stars + ")", 28, TextAnchor.MiddleCenter, Ui.Gold);
            if (free) Ui.Label(col, "Свободный режим: награды и статистики нет.", 24, TextAnchor.MiddleCenter, Ui.Dim);
            Ui.Btn(col, free ? "Сыграть ещё" : won ? "Следующая игра" : "Переиграть", Next, 84, 32, Ui.Red);
            Ui.Btn(col, "Главное меню", A.Home, 84, 30, Ui.Dark);
        }

        void Next()
        {
            MapData map = free ? m.Map : Match.NextMap(A.Maps, m.Map, won, new Rng((ulong)DateTime.UtcNow.Ticks));
            A.StartMatch(map, level, free);
        }

        public override void Back() { }   // «Назад» на экране итога ничего не делает
    }
}
