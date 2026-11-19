using System;
using System.Collections.Generic;
using System.Linq;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Rules;
using Boll.Core.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Boll.App
{
    public abstract class Page
    {
        protected AppMain A; public RectTransform R;
        public void Init(AppMain a, RectTransform r) { A = a; R = r; }
        public abstract void Build();
        public virtual void Back() => A.Pop();
        public virtual void Tick() { }
        public virtual void OnSystemPause() { }
        public virtual void Closed() { }
    }

    /// <summary>Точка входа Unity-части: загрузка данных, навигация, диалоги, сохранение. Создаётся сама при старте.</summary>
    public sealed class AppMain : MonoBehaviour
    {
        public static AppMain I;
        public Catalogs Cat; public MapCatalog Maps; public SaveStore Store; public SaveData D; public Profile Prof; public ErrorLog Log;
        public bool DataCorrupt;
        public static readonly GameKind[] Kinds = { GameKind.Eight, GameKind.Nine, GameKind.American, GameKind.Siberian, GameKind.Snooker };

        RectTransform pageRoot, dialogLayer;
        Page current; readonly Stack<Page> history = new Stack<Page>();
        readonly List<(GameObject go, Action cancel)> dialogs = new List<(GameObject, Action)>();
        public Camera TableCam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("App");
            DontDestroyOnLoad(go);
            go.AddComponent<AppMain>();
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            BuildCamerasAndCanvas();
            LoadData();
            if (DataCorrupt) { Modal("Данные игры повреждены. Переустановите игру.", null, ("Выйти", Application.Quit)); return; }
            Show(new MainMenuPage());
        }

        void BuildCamerasAndCanvas()
        {
            foreach (Camera c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            var cg = new GameObject("TableCam");
            cg.transform.SetParent(transform);
            TableCam = cg.AddComponent<Camera>();
            TableCam.orthographic = true; TableCam.clearFlags = CameraClearFlags.SolidColor; TableCam.backgroundColor = Ui.Bg;
            TableCam.transform.position = new Vector3(0, 0, -10); TableCam.nearClipPlane = 0.1f; TableCam.farClipPlane = 50;

            var cv = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cv.transform.SetParent(transform);
            var canvas = cv.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = cv.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1280, 720); sc.matchWidthOrHeight = 0.5f;
            pageRoot = Ui.Fill(Ui.Rect(cv.transform, "Page"));
            dialogLayer = Ui.Fill(Ui.Rect(cv.transform, "Dialogs"));

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.transform.SetParent(transform);
                var m = es.AddComponent<InputSystemUIInputModule>();
                m.AssignDefaultActions();
            }
        }

        // ---------- данные ----------
        static string Txt(string path) => Resources.Load<TextAsset>(path).text;

        void LoadData()
        {
            Cat = CatalogLoader.LoadAll(Txt("catalog/physics"), Txt("catalog/bots"), Txt("catalog/skins"));
            var src = new List<(GameKind, string)>();
            foreach (GameKind k in Kinds)
                foreach (TextAsset t in Resources.LoadAll<TextAsset>("maps/" + KindDefaults.Key(k)).OrderBy(a => a.name, StringComparer.Ordinal)) src.Add((k, t.text));
            Maps = new MapCatalog(src);

            var fs = new DiskFs(Application.persistentDataPath);
            Store = new SaveStore(fs);
            Log = new ErrorLog(fs, () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            Log.Start(SystemInfo.deviceModel, Application.version, Application.unityVersion);
            Application.logMessageReceived += (cond, stack, type) => { if (type == LogType.Exception) Log.Exception(cond, stack); };
            Application.quitting += () => Log.Close();

            D = Store.Load(out LoadStatus st);
            DataCorrupt = st == LoadStatus.Corrupt;
            if (DataCorrupt) D = new SaveData();
            Prof = new Profile(D, Cat);
            if (Cat.Model(D.Settings.Physics) == null) D.Settings.Physics = Cat.DefaultModel;
        }

        public void SaveNow() { if (!DataCorrupt) { try { Store.Save(D); } catch (Exception e) { Log.Exception(e.Message, e.StackTrace); } } }

        public PhysicsModel CurrentModel => Cat.Model(D.Settings.Physics) ?? Cat.Model(Cat.DefaultModel);

        public MapData FindMap(string id) => Kinds.SelectMany(k => Maps.Maps(k)).FirstOrDefault(m => m.Id == id);

        public MapData PickMap(GameKind k)
        {
            if (D.LastMap.TryGetValue(KindDefaults.Key(k), out string id)) { MapData m = Maps.Maps(k).FirstOrDefault(x => x.Id == id); if (m != null) return m; }
            return Maps.Maps(k)[0];
        }

        // ---------- партии ----------
        public void NewGame(Action start)
        {
            if (D.Active == null) { start(); return; }
            Ask("Есть незаконченная партия. Если начать новую, прежняя будет засчитана как поражение.", "Начать новую", () => { Abandon(); start(); });
        }

        /// <summary>Незаконченная партия — поражение (в свободном режиме без счёта).</summary>
        public void Abandon()
        {
            if (D.Active == null) return;
            MapData m = FindMap(D.Active.Snapshot.MapId);
            if (!D.Active.Free && m != null) Prof.RecordResult(m.Kind, D.Active.BotLevel, false);
            D.Active = null; SaveNow();
        }

        public void StartMatch(MapData map, int level, bool free)
        {
            if (!free) D.LastMap[KindDefaults.Key(map.Kind)] = map.Id;
            var m = new Match(map, CurrentModel, (ulong)DateTime.UtcNow.Ticks ^ (ulong)(uint)Environment.TickCount);
            D.Active = new ActiveMatch { Free = free, BotLevel = level, Snapshot = m.Snapshot() };
            SaveNow();
            history.Clear();
            Show(new PlayPage(m, level, free));
        }

        public void Continue()
        {
            try
            {
                MapData map = FindMap(D.Active.Snapshot.MapId);
                PhysicsModel model = Cat.Model(D.Active.Snapshot.PhysicsId);
                if (map == null || model == null) throw new InvalidOperationException("map/model missing");
                Match m = Match.Restore(map, model, D.Active.Snapshot);
                history.Clear();
                Show(new PlayPage(m, D.Active.BotLevel, D.Active.Free));
            }
            catch (Exception e)
            {
                Log.Exception("restore failed: " + e.Message, e.StackTrace);
                D.Active = null; SaveNow();
                Info("Незаконченную партию восстановить не удалось.");
                Show(new MainMenuPage());
            }
        }

        // ---------- навигация ----------
        public void Show(Page p)
        {
            current?.Closed();
            for (int i = pageRoot.childCount - 1; i >= 0; i--) Destroy(pageRoot.GetChild(i).gameObject);
            var root = Ui.Box(pageRoot, Ui.Bg, "Screen"); Ui.Fill(root);
            current = p; p.Init(this, root);
            p.Build();
        }

        public void Go(Page p) { history.Push(current); Show(p); }

        public void Pop() { if (history.Count > 0) Show(history.Pop()); else Show(new MainMenuPage()); }

        public void Home() { history.Clear(); Show(new MainMenuPage()); }

        void Update()
        {
            if (current == null) return;
            if (dialogs.Count == 0) current.Tick();
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) OnBack();
        }

        void OnBack()
        {
            if (dialogs.Count > 0) { var d = dialogs[dialogs.Count - 1]; if (d.cancel != null) { CloseDialog(d.go); d.cancel(); } return; }
            current?.Back();
        }

        void OnApplicationPause(bool paused) { if (paused) Suspend(); }
        void OnApplicationFocus(bool focus) { if (!focus) Suspend(); }
        void Suspend() { if (current != null && D != null) { current.OnSystemPause(); SaveNow(); } }

        // ---------- диалоги ----------
        void CloseDialog(GameObject go) { dialogs.RemoveAll(d => d.go == go); Destroy(go); }

        public void Modal(string text, Action cancel, params (string label, Action act)[] buttons)
        {
            var dim = Ui.Box(dialogLayer, new Color(0, 0, 0, 0.72f), "Dialog"); Ui.Fill(dim);
            var panel = Ui.Box(dim, Ui.PanelC, "Panel");
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(820, 0);
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(40, 40, 36, 36); v.spacing = 28;
            v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Ui.Label(panel, text, 28);
            var row = Ui.Row(panel, 20, 76);
            GameObject go = dim.gameObject;
            foreach (var (label, act) in buttons)
            {
                Action a = act;
                Ui.Btn(row, label, () => { CloseDialog(go); a?.Invoke(); }, 76, 26, label == buttons[0].label ? Ui.Red : Ui.Dark);
            }
            dialogs.Add((go, cancel));
        }

        public void Ask(string text, string okLabel, Action ok, string cancelLabel = "Отмена") =>
            Modal(text, () => { }, (okLabel, ok), (cancelLabel, null));

        public void Info(string text) => Modal(text, () => { }, ("Понятно", null));

        public bool HasDialog => dialogs.Count > 0;

        public void ShareLog()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND"))
                {
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", Log.Text.Length > 200000 ? Log.Text.Substring(Log.Text.Length - 200000) : Log.Text);
                    using (var cls = new AndroidJavaClass("android.content.Intent"))
                    using (var chooser = cls.CallStatic<AndroidJavaObject>("createChooser", intent, "Поделиться журналом"))
                    using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var act = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                        act.Call("startActivity", chooser);
                }
            }
            catch (Exception e) { Info("Не удалось открыть «Поделиться»: " + e.Message); }
#else
            GUIUtility.systemCopyBuffer = Log.Text;
            Info("Журнал скопирован в буфер обмена.");
#endif
        }
    }
}
