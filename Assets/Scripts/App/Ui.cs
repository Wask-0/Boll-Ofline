using System;
using UnityEngine;
using UnityEngine.UI;

namespace Boll.App
{
    /// <summary>Мелкие помощники для построения uGUI из кода (русский шрифт — встроенный LegacyRuntime).</summary>
    public static class Ui
    {
        public static readonly Color Bg = new Color(0.06f, 0.14f, 0.10f), PanelC = new Color(0.10f, 0.24f, 0.17f, 0.98f),
            Gold = new Color(0.93f, 0.78f, 0.35f), Red = new Color(0.62f, 0.14f, 0.14f), Dark = new Color(0.14f, 0.20f, 0.17f),
            Txt = new Color(0.97f, 0.95f, 0.88f), Dim = new Color(0.65f, 0.72f, 0.66f);
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

        public static Text Label(Transform p, string s, int size = 24, TextAnchor a = TextAnchor.MiddleCenter, Color? c = null)
        {
            var r = Rect(p, "Text");
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font; t.text = s; t.fontSize = size; t.alignment = a; t.color = c ?? Txt;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Btn(Transform p, string s, Action on, float h = 72, int size = 26, Color? bg = null)
        {
            var r = Box(p, bg ?? Red, "Btn");
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = r.GetComponent<Image>();
            var cb = b.colors; cb.pressedColor = new Color(0.7f, 0.7f, 0.7f); cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f); b.colors = cb;
            if (on != null) b.onClick.AddListener(() => on());
            Fill(Label(r, s, size).rectTransform);
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
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = h; le.minHeight = h;
            return r;
        }

        /// <summary>Прокручиваемый вертикальный список: возвращает контейнер для элементов.</summary>
        public static RectTransform Scroll(Transform p, float spacing = 12)
        {
            var view = Rect(p, "Scroll");
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = new RectOffset(0, 0, 0, 24);
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            var le = view.gameObject.AddComponent<LayoutElement>(); le.flexibleHeight = 1; le.flexibleWidth = 1;
            return content;
        }

        public static Slider MakeSlider(Transform p, int value, Action<int> on)
        {
            var r = Rect(p, "Slider");
            var bg = Box(r, Dark); Place(bg, 0, 0.35f, 1, 0.65f);
            var fillArea = Rect(r, "Fill Area"); Place(fillArea, 0, 0.35f, 1, 0.65f);
            var fill = Box(fillArea, Gold, "Fill");
            var hArea = Rect(r, "Handle Area"); Fill(hArea); hArea.offsetMin = new Vector2(16, 0); hArea.offsetMax = new Vector2(-16, 0);
            var h = Box(hArea, Txt, "Handle"); h.sizeDelta = new Vector2(32, 0);
            h.anchorMin = new Vector2(0, 0); h.anchorMax = new Vector2(0, 1);
            var s = r.gameObject.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = h; s.targetGraphic = h.GetComponent<Image>();
            s.minValue = 0; s.maxValue = 100; s.wholeNumbers = true; s.value = value;
            s.onValueChanged.AddListener(v => on((int)v));
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 56; le.minHeight = 56;
            return s;
        }

        public static Color Hex(string s, Color fallback) => ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
    }
}
