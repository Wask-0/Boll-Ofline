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

        public static string KindHint(GameKind k)
        {
            switch (k)
            {
                case GameKind.Eight: return "Пул, правила WPA";
                case GameKind.Nine: return "Пул, правила WPA";
                case GameKind.American: return "Русская пирамида";
                case GameKind.Siberian: return "Московская пирамида";
                default: return "Правила WPBSA";
            }
        }
    }

    public abstract class MenuPage : Page
    {
        /// <summary>Шапка: «Назад» и заголовок. Возвращает колонку для содержимого.</summary>
        protected RectTransform Frame(string title, bool back = true, float width = 0.7f)
        {
            var t = Ui.Title(R, title, 44, Ui.Txt);
            Ui.Place(t.rectTransform, 0, 1, 1, 1, 220, -110, 220, 14);
            if (back)
            {
                var b = Ui.Btn(R, "‹  Назад", Back, 64, 26, Ui.CardC);
                UnityEngine.Object.Destroy(b.GetComponent<LayoutElement>());
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(32, -26); rt.sizeDelta = new Vector2(180, 64);
            }
            var col = Ui.Column(R, 18);
            float m = (1 - width) / 2;
            Ui.Place(col, m, 0, 1 - m, 1, 0, 24, 0, 122);
            return col;
        }

        /// <summary>Строка списка: круглый значок, название, подпись; всё в карточке-кнопке.</summary>
        protected static Button ListItem(Transform p, string badge, string title, string sub, Action on, bool enabled, Color bg, Color? badgeColor = null)
        {
            var r = Ui.Round(p, bg, 26, "Item");
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = r.GetComponent<Image>();
            var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(0.82f, 0.82f, 0.82f); cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f); b.colors = cb;
            b.onClick.AddListener(() => { Sfx.Click(); on?.Invoke(); });
            r.gameObject.AddComponent<PressScale>();
            var s = r.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.3f); s.effectDistance = new Vector2(0, -4);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(22, 22, 14, 14); h.spacing = 22; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            r.gameObject.AddComponent<LayoutElement>().minHeight = 92;
            if (badge != null)
            {
                var bd = Ui.Round(r, badgeColor ?? Ui.Gold, 28, "Badge");
                var bl = bd.gameObject.AddComponent<LayoutElement>(); bl.preferredWidth = bl.minWidth = 56; bl.preferredHeight = bl.minHeight = 56;
                var t = Ui.Title(bd, badge, 28, new Color(0.08f, 0.12f, 0.09f)); Ui.Fill(t.rectTransform);
            }
            var col = Ui.Column(r, 2);
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.Label(col, title, 30, TextAnchor.MiddleLeft, Ui.Txt).fontStyle = FontStyle.Bold;
            if (!string.IsNullOrEmpty(sub)) Ui.Label(col, sub, 22, TextAnchor.MiddleLeft, Ui.Dim);
            b.interactable = enabled;
            return b;
        }

        protected static Text SectionTitle(Transform p, string s) => Ui.Title(p, s, 30, Ui.Gold, TextAnchor.MiddleLeft);
    }

    // ============================== главное меню ==============================
    public sealed class MainMenuPage : MenuPage
    {
        public override void Build()
        {
            // декоративные шары слева
            Ball(-150, 130, 440, new Color(0.07f, 0.07f, 0.08f), "8", 0.05f);
            Ball(90, -170, 300, new Color(0.93f, 0.75f, 0.1f), "1", 0.045f);
            Ball(260, 270, 200, new Color(0.8f, 0.15f, 0.1f), "3", 0.045f);

            var left = Ui.Column(R, 6); Ui.Place(left, 0.06f, 0.22f, 0.5f, 0.82f);
            left.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Title(left, "BOLL", 150, Ui.Gold, TextAnchor.MiddleLeft);
            Ui.Label(left, "OFFLINE БИЛЬЯРД", 36, TextAnchor.MiddleLeft, Ui.Txt).fontStyle = FontStyle.Bold;
            Ui.Label(left, "Играйте без интернета.", 26, TextAnchor.UpperLeft, Ui.Dim);

            // звёзды
            var pill = Ui.Round(R, Ui.CardC, 30, "Stars");
            pill.anchorMin = pill.anchorMax = new Vector2(0, 0); pill.pivot = new Vector2(0, 0); pill.anchoredPosition = new Vector2(48, 48); pill.sizeDelta = new Vector2(220, 60);
            var star = Ui.Box(pill, Ui.Gold, "Star"); star.GetComponent<Image>().sprite = UiArt.Star();
            star.anchorMin = star.anchorMax = new Vector2(0, 0.5f); star.pivot = new Vector2(0, 0.5f); star.anchoredPosition = new Vector2(18, 0); star.sizeDelta = new Vector2(36, 36);
            var sl = Ui.Title(pill, A.D.Stars + "  " + Plural(A.D.Stars), 26, Ui.Txt); Ui.Place(sl.rectTransform, 0, 0, 1, 1, 60, 0, 10, 0);

            var col = Ui.Column(R, 16); Ui.Place(col, 0.55f, 0.1f, 0.95f, 0.9f);
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            if (A.D.Active != null) Ui.Btn(col, "Продолжить партию", A.Continue, 92, 32, Ui.Gold);
            Ui.Btn(col, "Играть", () => A.Go(new KindPage(false)), 104, 38, Ui.Accent);
            var r1 = Ui.Row(col, 16, 84);
            Ui.Btn(r1, "Свободный режим", () => A.Go(new KindPage(true)), 84, 24, Ui.CardC);
            Ui.Btn(r1, "Магазин", () => A.Go(new ShopPage()), 84, 26, Ui.CardC);
            var r2 = Ui.Row(col, 16, 84);
            Ui.Btn(r2, "Статистика", () => A.Go(new StatsPage()), 84, 26, Ui.CardC);
            Ui.Btn(r2, "Настройки", () => A.Go(new SettingsPage()), 84, 26, Ui.CardC);
        }

        static string Plural(int n)
        {
            int a = n % 100, b = n % 10;
            return a >= 11 && a <= 14 ? "звёзд" : b == 1 ? "звезда" : b >= 2 && b <= 4 ? "звезды" : "звёзд";
        }

        void Ball(float x, float y, float size, Color c, string num, float alpha)
        {
            var b = Ui.Box(R, new Color(c.r, c.g, c.b, alpha), "Ball"); b.GetComponent<Image>().sprite = Sprites.Circle; b.GetComponent<Image>().raycastTarget = false;
            b.anchorMin = b.anchorMax = new Vector2(0, 0.5f); b.pivot = new Vector2(0.5f, 0.5f); b.anchoredPosition = new Vector2(x + size / 2, y); b.sizeDelta = new Vector2(size, size);
            var d = Ui.Box(b, new Color(1, 1, 1, alpha * 0.45f), "Disc"); d.GetComponent<Image>().sprite = Sprites.Circle; d.GetComponent<Image>().raycastTarget = false;
            d.anchorMin = d.anchorMax = new Vector2(0.5f, 0.5f); d.sizeDelta = new Vector2(size * 0.45f, size * 0.45f);
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
            var col = Frame(free ? "Свободный режим" : "Выберите игру", true, 0.62f);
            Ui.Label(col, free ? "Любая карта, любой соперник. Награды и статистики нет." : "Победа приносит звезду. Звёзды тратятся в магазине.", 24, TextAnchor.MiddleCenter, Ui.Dim);
            foreach (GameKind k in AppMain.Kinds)
            {
                GameKind kk = k;
                var row = Ui.Row(col, 14, 92);
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var main = ListItem(row, Names.Kind(k).Substring(0, 1), Names.Kind(k), Names.KindHint(k), () => A.Go(free ? (Page)new FreeSetupPage(kk) : new BotPage(kk)), true, Ui.CardHi);
                main.GetComponent<LayoutElement>().flexibleWidth = 1;
                var r = Ui.Btn(row, "Правила", () => A.Go(new RulesPage(kk)), 92, 24, Ui.CardC);
                var le = r.GetComponent<LayoutElement>(); le.preferredWidth = 190; le.minWidth = 190; le.flexibleWidth = 0;
            }
        }
    }

    public sealed class RulesPage : MenuPage
    {
        readonly GameKind kind;
        public RulesPage(GameKind k) { kind = k; }

        public override void Build()
        {
            var col = Frame("Правила: " + Names.Kind(kind), true, 0.82f);
            var list = Ui.Scroll(col);
            var card = Ui.Card(list, 34);
            Ui.Label(card, RulesText.For(kind), 26, TextAnchor.UpperLeft);
        }
    }

    // ============================== выбор бота ==============================
    public sealed class BotPage : MenuPage
    {
        readonly GameKind kind;
        public BotPage(GameKind k) { kind = k; }

        public override void Build()
        {
            var col = Frame(Names.Kind(kind) + " — соперник", true, 0.66f);
            var list = Ui.Scroll(col, 14);
            foreach (BotLevel b in A.Cat.Bots)
            {
                BotLevel bb = b;
                bool open = A.Prof.IsBotOpen(b.Level);
                Counter c = A.D.Stats.ByBot[b.Level - 1];
                string sub = !open ? "Закрыт: победите предыдущего соперника"
                    : c.Games == 0 ? "Ещё не играли" : "Партий: " + c.Games + ", побед: " + Profile.WinPercent(c);
                ListItem(list, b.Level.ToString(), b.Name, sub, () => A.NewGame(() => A.StartMatch(A.PickMap(kind), bb.Level, false)),
                    open, open ? Ui.CardHi : Ui.CardC, open ? Ui.Gold : new Color(0.4f, 0.48f, 0.44f));
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
            var col = Frame(Names.Kind(kind) + " — свободный режим", true, 0.86f);
            var cols = Ui.Row(col, 22, 400);
            cols.GetComponent<LayoutElement>().flexibleHeight = 1;
            var maps = A.Maps.Maps(kind);
            Side(cols, "Карта", maps.Select(m => m.Name).ToArray(), mapIdx, i => { mapIdx = i; A.Show(this); });
            Side(cols, "Соперник", A.Cat.Bots.Select(b => b.Name).ToArray(), level - 1, i => { level = i + 1; A.Show(this); });
            Ui.Btn(col, "Играть", () => A.NewGame(() => A.StartMatch(maps[mapIdx], level, true)), 92, 34, Ui.Gold);
        }

        static void Side(Transform p, string title, string[] items, int sel, Action<int> pick)
        {
            var card = Ui.Card(p, 20, 10);
            var le = card.gameObject.AddComponent<LayoutElement>(); le.flexibleHeight = 1; le.flexibleWidth = 1;
            Ui.Title(card, title, 30, Ui.Gold);
            var list = Ui.Scroll(card, 8);
            for (int i = 0; i < items.Length; i++)
            {
                int ii = i;
                Ui.Btn(list, (i + 1) + ".  " + items[i], () => pick(ii), 62, 24, i == sel ? Ui.Accent : Ui.Dark);
            }
        }
    }

    // ============================== настройки ==============================
    public sealed class SettingsPage : MenuPage
    {
        bool dirty;

        public override void Build()
        {
            var col = Frame("Настройки", true, 0.72f);
            var list = Ui.Scroll(col, 18);
            Settings s = A.D.Settings;

            // --- прицел ---
            var aim = Ui.Card(list);
            SectionTitle(aim, "Прицел");
            var rowAim = SettingRow(aim, "Линия удара", "Показывает, куда полетит биток");
            var swAim = new UiSwitch(rowAim, s.AimLine, null);
            var rowBounce = SettingRow(aim, "Линия отскока", "Требует включённой линии удара");
            var swBounce = new UiSwitch(rowBounce, s.BounceLine, v => { s.BounceLine = v; A.SaveNow(); });
            swBounce.SetEnabled(s.AimLine);
            swAim.Btn.onClick.AddListener(() =>
            {
                s.AimLine = swAim.On;
                if (!swAim.On) { s.BounceLine = false; swBounce.Set(false); }
                swBounce.SetEnabled(swAim.On);
                A.SaveNow();
            });

            // --- сила удара ---
            var pw = Ui.Card(list);
            SectionTitle(pw, "Сила удара");
            string[] pd = { "Сила — оттяжкой пальца от битка; отпустили палец — удар.", "Оттяжкой пальца или шкалой; со шкалой удар — кнопкой «Удар».", "Силу задаёт только шкала; удар — кнопкой «Удар»." };
            Text pdesc = null; UiSegment pseg = null;
            pseg = new UiSegment(pw, new[] { "Оттяжка", "Комбинир.", "Шкала" }, Mathf.Clamp(s.PowerInput, 0, 2), i => { s.PowerInput = i; A.SaveNow(); pseg.Select(i); pdesc.text = pd[i]; });
            pdesc = Ui.Label(pw, pd[Mathf.Clamp(s.PowerInput, 0, 2)], 22, TextAnchor.MiddleLeft, Ui.Dim);

            // --- физика ---
            var ph = Ui.Card(list);
            SectionTitle(ph, "Модель физики");
            var models = A.Cat.Models;
            int cur = Mathf.Max(0, Array.FindIndex(models, m => m.Id == s.Physics));
            UiSegment seg = null;
            seg = new UiSegment(ph, models.Select(m => m.Name).ToArray(), cur, i =>
            {
                if (models[i].Id == s.Physics) return;
                A.Ask("Новая модель физики будет использована со следующей партии. Незаконченная партия доигрывается на прежней модели.", "Сменить",
                    () => { s.Physics = models[i].Id; A.SaveNow(); seg.Select(i); }, "Отмена");
            });
            Ui.Label(ph, "Действует со следующей партии.", 22, TextAnchor.MiddleLeft, Ui.Dim);

            // --- звук ---
            var snd = Ui.Card(list, 26, 6);
            SectionTitle(snd, "Звук");
            Ui.SliderRow(snd, "Общая", s.Master, v => { s.Master = v; A.ApplyAudio(); dirty = true; });
            Ui.SliderRow(snd, "Музыка", s.Music, v => { s.Music = v; A.ApplyAudio(); dirty = true; });
            Ui.SliderRow(snd, "Эффекты", s.Effects, v => { s.Effects = v; A.ApplyAudio(); dirty = true; });

            Ui.Btn(list, "Поделиться журналом ошибок", A.ShareLog, 72, 26, Ui.CardC);
        }

        static RectTransform SettingRow(Transform p, string title, string sub)
        {
            var r = Ui.Rect(p, "Row");
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 20; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            r.gameObject.AddComponent<LayoutElement>().minHeight = 76;
            var col = Ui.Column(r, 2);
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.Label(col, title, 28, TextAnchor.MiddleLeft);
            Ui.Label(col, sub, 21, TextAnchor.MiddleLeft, Ui.Dim);
            return r;
        }

        public override void Closed() { if (dirty) A.SaveNow(); dirty = false; }
    }

    // ============================== статистика ==============================
    public sealed class StatsPage : MenuPage
    {
        public override void Build()
        {
            var col = Frame("Статистика", true, 0.72f);
            var list = Ui.Scroll(col, 16);

            var total = Ui.Card(list, 28, 10, true);
            var big = Ui.Row(total, 12, 110);
            Big(big, A.D.Stats.Total.Games.ToString(), "партий");
            Big(big, A.D.Stats.Total.Wins.ToString(), "побед");
            Big(big, Profile.WinPercent(A.D.Stats.Total), "побед в процентах");

            var bots = Ui.Card(list, 22, 8);
            SectionTitle(bots, "По соперникам");
            foreach (BotLevel b in A.Cat.Bots) Row(bots, b.Level + ".  " + b.Name, A.D.Stats.ByBot[b.Level - 1]);

            var kinds = Ui.Card(list, 22, 8);
            SectionTitle(kinds, "По видам игры");
            foreach (GameKind k in AppMain.Kinds) Row(kinds, Names.Kind(k), A.Prof.KindStats(k));
        }

        static void Big(Transform p, string value, string label)
        {
            var c = Ui.Column(p, 0);
            c.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Title(c, value, 56, Ui.Gold);
            Ui.Label(c, label, 22, TextAnchor.MiddleCenter, Ui.Dim);
        }

        static void Row(Transform p, string name, Counter c)
        {
            var r = Ui.Rect(p, "Row");
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12; h.padding = new RectOffset(6, 6, 6, 6); h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            r.gameObject.AddComponent<LayoutElement>().minHeight = 50;
            Ui.Label(r, name, 26, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var t = Ui.Label(r, c.Games + " / " + c.Wins, 24, TextAnchor.MiddleRight, Ui.Dim);
            var tl = t.gameObject.AddComponent<LayoutElement>(); tl.preferredWidth = 130;
            var pct = Ui.Title(r, Profile.WinPercent(c), 26, c.Games == 0 ? Ui.Dim : Ui.Gold, TextAnchor.MiddleRight);
            var pl = pct.gameObject.AddComponent<LayoutElement>(); pl.preferredWidth = 100;
        }
    }

    // ============================== магазин ==============================
    public sealed class ShopPage : MenuPage
    {
        public override void Build()
        {
            var col = Frame("Магазин", true, 0.74f);
            var list = Ui.Scroll(col, 16);

            var bal = Ui.Card(list, 22, 4, true);
            var row = Ui.Rect(bal, "Bal"); var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().minHeight = 56;
            var star = Ui.Box(row, Ui.Gold, "Star"); star.GetComponent<Image>().sprite = UiArt.Star();
            var sle = star.gameObject.AddComponent<LayoutElement>(); sle.preferredWidth = sle.preferredHeight = 44;
            Ui.Title(row, "У вас " + A.D.Stars, 36, Ui.Gold);

            var c1 = Ui.Card(list, 22, 12); SectionTitle(c1, "Сукно");
            foreach (Skin s in A.Cat.Cloths) Item(c1, s, true);
            var c2 = Ui.Card(list, 22, 12); SectionTitle(c2, "Кии");
            foreach (Skin s in A.Cat.Cues) Item(c2, s, false);
        }

        void Item(Transform p, Skin s, bool cloth)
        {
            var r = Ui.Round(p, Ui.Dark, 22, "Item");
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 12, 12); h.spacing = 18; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            r.gameObject.AddComponent<LayoutElement>().minHeight = 96;
            Color c = Ui.Hex(s.Color, Color.gray);
            var sw = Ui.Rect(r, "Swatch"); var sl = sw.gameObject.AddComponent<LayoutElement>(); sl.preferredWidth = sl.minWidth = 190; sl.preferredHeight = sl.minHeight = 72;
            if (cloth)
            {
                var felt = Ui.Round(sw, c, 14, "Felt"); Ui.Fill(felt);
                var ball = Ui.Box(felt, Color.white, "B"); ball.GetComponent<Image>().sprite = Sprites.Circle;
                ball.anchorMin = ball.anchorMax = new Vector2(0.5f, 0.5f); ball.sizeDelta = new Vector2(22, 22);
            }
            else
            {
                var tip = Ui.Round(sw, c, 6, "Tip"); tip.anchorMin = tip.anchorMax = new Vector2(0, 0.5f); tip.pivot = new Vector2(0, 0.5f); tip.sizeDelta = new Vector2(120, 12);
                var butt = Ui.Round(sw, c * 0.6f + new Color(0, 0, 0, 0.4f), 8, "Butt"); butt.anchorMin = butt.anchorMax = new Vector2(1, 0.5f); butt.pivot = new Vector2(1, 0.5f); butt.sizeDelta = new Vector2(76, 18);
            }
            var name = Ui.Title(r, s.Name, 28, Ui.Txt, TextAnchor.MiddleLeft); name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            bool sel = A.Prof.IsSelected(s.Id), own = A.Prof.IsOwned(s.Id);
            string st = sel ? "Выбрано" : own ? "Выбрать" : s.Price + " звёзд";
            var b = Ui.Btn(r, st, () => Tap(s), 70, 24, sel ? Ui.Gold : own ? Ui.Accent : Ui.CardC);
            var bl = b.GetComponent<LayoutElement>(); bl.preferredWidth = bl.minWidth = 190;
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
            var card = Ui.Card(R, 40, 18, true);
            Ui.Place(card, 0.5f, 0.5f, 0.5f, 0.5f); card.sizeDelta = new Vector2(760, 0);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Ui.Title(card, won ? "Победа!" : "Поражение", 76, won ? Ui.Gold : Ui.Txt);
            Ui.Label(card, Names.Kind(m.Kind) + ", карта «" + m.Map.Name + "»", 26, TextAnchor.MiddleCenter, Ui.Dim);
            Ui.Title(card, "Вы " + m.St.Score[0] + " : " + m.St.Score[1] + " Соперник", 44, Ui.Txt);
            if (!free && won) Ui.Label(card, "+1 звезда (всего " + A.D.Stars + ")", 28, TextAnchor.MiddleCenter, Ui.Gold);
            if (free) Ui.Label(card, "Свободный режим: наград и статистики нет.", 24, TextAnchor.MiddleCenter, Ui.Dim);
            Ui.Btn(card, free ? "Сыграть ещё" : won ? "Следующая игра" : "Переиграть", Next, 90, 32, Ui.Accent);
            Ui.Btn(card, "Главное меню", A.Home, 76, 28, Ui.Dark);
        }

        void Next()
        {
            MapData map = free ? m.Map : Match.NextMap(A.Maps, m.Map, won, new Rng((ulong)DateTime.UtcNow.Ticks));
            A.StartMatch(map, level, free);
        }

        public override void Back() { }   // «Назад» на экране итога ничего не делает
    }
}
