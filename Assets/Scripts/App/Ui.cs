using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Boll.App
{
    /// <summary>Рисованные из кода картинки интерфейса: скруглённые прямоугольники, градиент, звезда.</summary>
    public static class UiArt
    {
        static readonly Dictionary<int, Sprite> round = new Dictionary<int, Sprite>();
        static Sprite grad, star;

        /// <summary>Скруглённый прямоугольник с радиусом r (9-slice): тянется до любого размера.</summary>
        public static Sprite Round(int r)
        {
            if (round.TryGetValue(r, out Sprite s)) return s;
            int n = 2 * r + 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float half = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - r), 0), dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - r), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
                }
            tex.SetPixels(px); tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            round[r] = s;
            return s;
        }

        public static Sprite Gradient()
        {
            if (grad != null) return grad;
            const int h = 256;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Color top = new Color(0.075f, 0.22f, 0.165f), bottom = new Color(0.025f, 0.075f, 0.055f);
            for (int y = 0; y < h; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (h - 1f));
                tex.SetPixel(0, y, c); tex.SetPixel(1, y, c);
            }
            tex.Apply();
            return grad = Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100);
        }

        public static Sprite Star()
        {
            if (star != null) return star;
            const int n = 96;
            var v = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / 5, r = i % 2 == 0 ? 0.95f : 0.4f;
                v[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(((x + (sx + 0.5f) / 3) / n) * 2 - 1, ((y + (sy + 0.5f) / 3) / n) * 2 - 1);
                            bool inside = false;
                            for (int i = 0, j = 9; i < 10; j = i++)
                                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
                            if (inside) hit++;
                        }
                    tex.SetPixel(x, y, new Color(1, 1, 1, hit / 9f));
                }
            tex.Apply();
            return star = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }
    }

    /// <summary>Лёгкое «вдавливание» кнопки при нажатии.</summary>
    public sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public void OnPointerDown(PointerEventData e) => transform.localScale = Vector3.one * 0.96f;
        public void OnPointerUp(PointerEventData e) => transform.localScale = Vector3.one;
        public void OnPointerExit(PointerEventData e) => transform.localScale = Vector3.one;
    }

    public sealed class UiSwitch
    {
        public Button Btn; public bool On; Image track; RectTransform knob;
        public UiSwitch(Transform p, bool on, Action<bool> changed)
        {
            var r = Ui.Round(p, Ui.Off, 26, "Switch");
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = le.minWidth = 100; le.preferredHeight = le.minHeight = 52;
            track = r.GetComponent<Image>();
            knob = Ui.Round(r, Color.white, 22, "Knob");
            knob.anchorMin = knob.anchorMax = new Vector2(0.5f, 0.5f); knob.sizeDelta = new Vector2(44, 44);
            Btn = r.gameObject.AddComponent<Button>(); Btn.targetGraphic = track; Btn.transition = Selectable.Transition.None;
            Btn.onClick.AddListener(() => { if (!Btn.interactable) return; Sfx.Click(); Set(!On); changed?.Invoke(On); });
            Set(on);
        }

        public void Set(bool v) { On = v; track.color = v ? Ui.Accent : Ui.Off; knob.anchoredPosition = new Vector2(v ? 24 : -24, 0); }
        public void SetEnabled(bool e) { Btn.interactable = e; track.color = new Color(track.color.r, track.color.g, track.color.b, e ? 1 : 0.4f); knob.GetComponent<Image>().color = new Color(1, 1, 1, e ? 1 : 0.5f); }
    }

    public sealed class UiSegment
    {
        readonly Image[] imgs; readonly Text[] txts;
        public UiSegment(Transform p, string[] labels, int sel, Action<int> pick)
        {
            var track = Ui.Round(p, Ui.Off * 0.55f + new Color(0, 0, 0, 0.45f), 26, "Segment");
            var h = track.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 6, 6, 6); h.spacing = 6; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = true;
            var le = track.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = le.minHeight = 72;
            imgs = new Image[labels.Length]; txts = new Text[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int ii = i;
                var seg = Ui.Round(track, Color.clear, 20, "Seg");
                imgs[i] = seg.GetComponent<Image>();
                var b = seg.gameObject.AddComponent<Button>(); b.targetGraphic = imgs[i]; b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { Sfx.Click(); pick(ii); });
                txts[i] = Ui.Label(seg, labels[i], 26); Ui.Fill(txts[i].rectTransform);
            }
            Select(sel);
        }

        public void Select(int sel)
        {
            for (int i = 0; i < imgs.Length; i++)
            {
                imgs[i].color = i == sel ? Ui.Gold : Color.clear;
                txts[i].color = i == sel ? new Color(0.08f, 0.12f, 0.1f) : Ui.Txt;
                txts[i].fontStyle = i == sel ? FontStyle.Bold : FontStyle.Normal;
            }
        }
    }

    /// <summary>Мелкие помощники для построения uGUI из кода (русский шрифт — встроенный LegacyRuntime).</summary>
    public static class Ui
    {
        public static readonly Color Bg = new Color(0.04f, 0.11f, 0.08f), PanelC = new Color(0.10f, 0.24f, 0.18f, 0.98f),
            Gold = new Color(0.96f, 0.77f, 0.30f), Red = new Color(0.76f, 0.22f, 0.20f), Dark = new Color(0.12f, 0.28f, 0.22f),
            CardC = new Color(0.09f, 0.22f, 0.17f, 0.94f), CardHi = new Color(0.14f, 0.32f, 0.25f, 0.96f), Accent = new Color(0.13f, 0.56f, 0.41f),
            Off = new Color(0.20f, 0.30f, 0.26f), Txt = new Color(0.96f, 0.95f, 0.90f), Dim = new Color(0.62f, 0.73f, 0.67f);
        static Font font;
        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static float Dp => Screen.dpi > 0 ? Screen.dpi / 160f : 1f;

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Fill(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        public static RectTransform Place(RectTransform r, float x0, float y0, float x1, float y1, float l = 0, float b = 0, float rr = 0, float t = 0)
        {
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rr, -t);
            return r;
        }

        public static RectTransform Box(Transform p, Color c, string name = "Box")
        {
            var r = Rect(p, name);
            r.gameObject.AddComponent<Image>().color = c;
            return r;
        }

        /// <summary>Скруглённый блок.</summary>
        public static RectTransform Round(Transform p, Color c, int radius = 24, string name = "Round")
        {
            var r = Rect(p, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiArt.Round(radius); img.type = Image.Type.Sliced; img.color = c;
            return r;
        }

        static void Shade(RectTransform r, float dist = 5, float alpha = 0.35f)
        {
            var s = r.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, alpha); s.effectDistance = new Vector2(0, -dist);
        }

        /// <summary>Карточка-раздел: скруглённая панель с вертикальной раскладкой.</summary>
        public static RectTransform Card(Transform p, float pad = 26, float spacing = 14, bool hi = false)
        {
            var r = Round(p, hi ? CardHi : CardC, 28, "Card");
            Shade(r, 6, 0.3f);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad); v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return r;
        }

        public static Text Label(Transform p, string s, int size = 24, TextAnchor a = TextAnchor.MiddleCenter, Color? c = null)
        {
            var r = Rect(p, "Text");
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font; t.text = s; t.fontSize = size; t.alignment = a; t.color = c ?? Txt;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Text Title(Transform p, string s, int size, Color? c = null, TextAnchor a = TextAnchor.MiddleCenter)
        {
            var t = Label(p, s, size, a, c); t.fontStyle = FontStyle.Bold; return t;
        }

        static bool IsLight(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b > 0.5f;

        public static Button Btn(Transform p, string s, Action on, float h = 72, int size = 26, Color? bg = null)
        {
            Color col = bg ?? Red;
            var r = Round(p, col, 22, "Btn");
            Shade(r, 4, 0.35f);
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = r.GetComponent<Image>();
            var cb = b.colors; cb.highlightedColor = new Color(1.06f, 1.06f, 1.06f); cb.pressedColor = new Color(0.82f, 0.82f, 0.82f); cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f); b.colors = cb;
            b.onClick.AddListener(() => { Sfx.Click(); on?.Invoke(); });
            r.gameObject.AddComponent<PressScale>();
            var t = Title(r, s, size, IsLight(col) ? new Color(0.07f, 0.12f, 0.09f) : Txt);
            Fill(t.rectTransform);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = h; le.minHeight = h;
            return b;
        }

        public static void SetText(Button b, string s) => b.GetComponentInChildren<Text>().text = s;

        public static RectTransform Column(Transform p, float spacing = 14, int pad = 0)
        {
            var r = Rect(p, "Col");
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return r;
        }

        public static RectTransform Row(Transform p, float spacing = 14, float h = 72)
        {
            var r = Rect(p, "Row");
            var v = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            v.spacing = spacing; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = v.childForceExpandHeight = true;
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = h; le.minHeight = h; le.flexibleHeight = 0;
            return r;
        }

        /// <summary>Прокручиваемый вертикальный список: возвращает контейнер для элементов.</summary>
        public static RectTransform Scroll(Transform p, float spacing = 16)
        {
            var view = Rect(p, "Scroll");
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = new RectOffset(0, 0, 8, 40);
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Elastic; sr.scrollSensitivity = 40; sr.decelerationRate = 0.12f;
            var le = view.gameObject.AddComponent<LayoutElement>(); le.flexibleHeight = 1; le.flexibleWidth = 1;
            return content;
        }

        /// <summary>Красивый ползунок: тёмная дорожка, золотая заливка, крупная круглая ручка.</summary>
        public static Slider MakeSlider(Transform p, int value, Action<int> on)
        {
            var r = Rect(p, "Slider");
            r.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);   // зона касания
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = le.minHeight = 64; le.flexibleWidth = 1;

            var track = Round(r, new Color(0.03f, 0.09f, 0.07f), 9, "Track");
            track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f); track.sizeDelta = new Vector2(0, 18);

            var fillArea = Rect(r, "Fill Area");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.offsetMin = new Vector2(24, -9); fillArea.offsetMax = new Vector2(-24, 9);
            var fill = Round(fillArea, Gold, 9, "Fill");
            fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1);
            fill.offsetMin = new Vector2(-24, 0); fill.offsetMax = Vector2.zero;

            var hArea = Rect(r, "Handle Area");
            hArea.anchorMin = Vector2.zero; hArea.anchorMax = Vector2.one; hArea.offsetMin = new Vector2(24, 0); hArea.offsetMax = new Vector2(-24, 0);
            var h = Rect(hArea, "Handle");
            var himg = h.gameObject.AddComponent<Image>(); himg.sprite = Sprites.Circle; himg.color = Color.white;
            h.anchorMin = new Vector2(0, 0); h.anchorMax = new Vector2(0, 1); h.sizeDelta = new Vector2(48, -16);
            Shade(h, 3, 0.45f);
            var dot = Rect(h, "Dot"); var dimg = dot.gameObject.AddComponent<Image>(); dimg.sprite = Sprites.Circle; dimg.color = Gold;
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f); dot.sizeDelta = new Vector2(20, 20); dimg.raycastTarget = false;

            var s = r.gameObject.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = h; s.targetGraphic = himg; s.transition = Selectable.Transition.None;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0; s.maxValue = 100; s.wholeNumbers = true; s.value = value;
            s.onValueChanged.AddListener(v => on((int)v));
            return s;
        }

        /// <summary>Строка «название — ползунок — значение».</summary>
        public static void SliderRow(Transform p, string label, int value, Action<int> on)
        {
            var row = Rect(p, "SliderRow");
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 20; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false; hl.childAlignment = TextAnchor.MiddleCenter;
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = le.minHeight = 72;
            var name = Label(row, label, 28, TextAnchor.MiddleLeft);
            var nl = name.gameObject.AddComponent<LayoutElement>(); nl.preferredWidth = nl.minWidth = 190;
            var pill = Round(row, new Color(0.03f, 0.09f, 0.07f), 22, "Value");
            var pl = pill.gameObject.AddComponent<LayoutElement>(); pl.preferredWidth = pl.minWidth = 96; pl.preferredHeight = pl.minHeight = 48;
            var val = Title(pill, value.ToString(), 26, Gold); Fill(val.rectTransform);
            var slider = MakeSlider(row, value, v => { val.text = v.ToString(); on(v); });
            slider.transform.SetSiblingIndex(1);
        }

        public static Color Hex(string s, Color fallback) => ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
    }
}
