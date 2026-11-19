using System;
using System.Diagnostics;
using System.Linq;
using Boll.Core.Bot;
using Boll.Core.Content;
using Boll.Core.Game;
using Boll.Core.Physics;
using Boll.Core.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Boll.App
{
    /// <summary>Ловит касания прямо на элементе (стол, шкала силы, окно точки удара).</summary>
    public sealed class Catcher : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<Vector2> Down, Drag, Up;
        public void OnPointerDown(PointerEventData e) => Down?.Invoke(e.position);
        public void OnDrag(PointerEventData e) => Drag?.Invoke(e.position);
        public void OnPointerUp(PointerEventData e) => Up?.Invoke(e.position);
    }

    public sealed class PlayPage : Page
    {
        readonly Match M; readonly int level; readonly bool free;
        BotLevel bot;
        TableView tv; float sf = 1;

        // ввод игрока
        enum Drag { None, Aim, Place }
        Drag drag; double aimAngle; double power, spinX, spinY; bool scaleTouched, placed, freeSelect, freeAvail;
        int cueId, called = -1, nominated = -1, freeBall = -1;

        // состояние
        bool paused, shooting; double acc; float overTimer, toastT; bool finished;
        BotPlanner planner; float botT; double botAngle;

        // HUD
        Text info, score, toast, hint, powerText; RectTransform powerFillRt, powerHandle; Button shootBtn, freeBtn; RectTransform spinDot, colourBox, pauseRoot;
        Button[] colourBtns;

        public PlayPage(Match m, int level, bool free)
        {
            M = m; this.level = level; this.free = free;
            shooting = M.Phase == Phase.Shooting;     // восстановленный удар в полёте
            BeginTurn(true);
        }

        bool sliderMode => PMode == 2 || (PMode == 1 && scaleTouched);   // сила задаётся шкалой, а не оттяжкой
        int PMode => AppMain.I.D.Settings.PowerInput;

        bool PlayerTurn => M.St.Current == 0;

        // ================= построение =================
        public override void Build()
        {
            bot = A.Cat.Bots[Mathf.Clamp(level, 1, A.Cat.Bots.Length) - 1];
            R.GetComponent<Image>().color = Color.clear;
            sf = R.GetComponentInParent<Canvas>().scaleFactor;

            var catcher = Ui.Box(R, new Color(0, 0, 0, 0), "Catcher"); Ui.Fill(catcher);
            var c = catcher.gameObject.AddComponent<Catcher>(); c.Down = OnDown; c.Drag = OnDrag; c.Up = OnUp;
            var numbers = Ui.Fill(Ui.Rect(R, "Numbers"));

            Skin cloth = A.Cat.Cloths.FirstOrDefault(s => s.Id == A.D.Cloth) ?? A.Cat.Cloths[0];
            Skin cue = A.Cat.Cues.FirstOrDefault(s => s.Id == A.D.Cue) ?? A.Cat.Cues[0];
            tv = new TableView(M, Ui.Hex(cloth.Color, new Color(0.12f, 0.42f, 0.23f)), Ui.Hex(cue.Color, new Color(0.85f, 0.7f, 0.42f)), numbers, A.TableCam);
            Fit();

            BuildHud();
            if (paused) BuildPause();
        }

        void Fit()
        {
            sf = R.GetComponentInParent<Canvas>().scaleFactor;
            float x0 = 20 * sf, y0 = 20 * sf, x1 = Screen.width - 280 * sf, y1 = Screen.height - 100 * sf;
            tv.SetRegion(new Rect(x0, y0, Mathf.Max(100, x1 - x0), Mathf.Max(100, y1 - y0)));
        }

        static RectTransform Anchor(RectTransform r, Vector2 a, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = a; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        void BuildHud()
        {
            var top = Ui.Box(R, new Color(0.04f, 0.1f, 0.07f, 0.85f), "Top"); Ui.Place(top, 0, 1, 1, 1, 0, -80, 0, 0);
            info = Ui.Label(top, "", 26, TextAnchor.MiddleLeft); Ui.Place(info.rectTransform, 0, 0, 0.42f, 1, 24, 0, 0, 0);
            score = Ui.Label(top, "", 30, TextAnchor.MiddleCenter, Ui.Gold); Ui.Place(score.rectTransform, 0.42f, 0, 0.78f, 1);
            var pb = Ui.Btn(top, "Пауза", Pause, 60, 26, Ui.Dark); Destroy(pb.GetComponent<LayoutElement>());
            Anchor((RectTransform)pb.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(170, 60));

            toast = Ui.Label(R, "", 34, TextAnchor.MiddleCenter, Ui.Gold);
            Anchor(toast.rectTransform, new Vector2(0.4f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(900, 60)); toast.gameObject.SetActive(false);
            hint = Ui.Label(R, "", 26, TextAnchor.MiddleCenter, Ui.Txt);
            Anchor(hint.rectTransform, new Vector2(0.4f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(900, 50));

            // шкала силы
            var bar = Ui.Box(R, new Color(0, 0, 0, 0.001f), "Power");
            Ui.Place(bar, 1, 0, 1, 1, -115, 345, 25, 130);      // колонка шириной 90, центр на 70 от правого края
            var track = Ui.Round(bar, new Color(0.03f, 0.09f, 0.07f), 10, "Track");
            track.anchorMin = new Vector2(0.5f, 0); track.anchorMax = new Vector2(0.5f, 1); track.sizeDelta = new Vector2(22, -60); track.anchoredPosition = Vector2.zero;
            var fill = Ui.Round(track, Ui.Gold, 10, "Fill"); powerFillRt = fill;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(1, 0); fill.offsetMin = fill.offsetMax = Vector2.zero;
            powerHandle = Ui.Rect(track, "Handle");
            var himg = powerHandle.gameObject.AddComponent<Image>(); himg.sprite = Sprites.Circle; himg.raycastTarget = false;
            powerHandle.anchorMin = powerHandle.anchorMax = new Vector2(0.5f, 0); powerHandle.sizeDelta = new Vector2(58, 58);
            powerHandle.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -3);
            var dot = Ui.Rect(powerHandle, "Dot"); var dimg = dot.gameObject.AddComponent<Image>(); dimg.sprite = Sprites.Circle; dimg.color = Ui.Gold; dimg.raycastTarget = false;
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f); dot.sizeDelta = new Vector2(24, 24);
            powerText = Ui.Title(R, "Сила 0 %", 24);
            Anchor(powerText.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-70, -96), new Vector2(200, 32));
            var pc = bar.gameObject.AddComponent<Catcher>();
            pc.Down = pc.Drag = p =>
            {
                if (!CanInput || PMode == 0) return;
                scaleTouched = true;
                float h = bar.rect.height;
                power = Math.Max(0, Math.Min(1, (LocalNorm(bar, p).y * h - 30) / Math.Max(1, h - 60)));
            };

            // окно точки удара
            var spin = Ui.Box(R, new Color(0.95f, 0.93f, 0.85f), "Spin"); spin.GetComponent<Image>().sprite = Sprites.Circle;
            Anchor(spin, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-52, 140), new Vector2(160, 160));
            var h = Ui.Box(spin, new Color(0.7f, 0.7f, 0.65f), "H"); Anchor(h, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 2));
            var v = Ui.Box(spin, new Color(0.7f, 0.7f, 0.65f), "V"); Anchor(v, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 160));
            spinDot = Ui.Box(spin, Ui.Red, "Dot"); spinDot.GetComponent<Image>().sprite = Sprites.Circle;
            Anchor(spinDot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            var sc = spin.gameObject.AddComponent<Catcher>();
            sc.Down = sc.Drag = p =>
            {
                if (!CanInput) return;
                Vector2 n = LocalNorm(spin, p) * 2 - Vector2.one;
                if (n.magnitude > 1) n.Normalize();
                spinX = n.x; spinY = n.y;
            };
            var spinLbl = Ui.Label(R, "Точка удара", 20, TextAnchor.MiddleCenter, Ui.Dim);
            Anchor(spinLbl.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-52, 304), new Vector2(160, 28));

            shootBtn = Ui.Btn(R, "Удар", OnShootButton, 90, 34, Ui.Red); Destroy(shootBtn.GetComponent<LayoutElement>());
            Anchor((RectTransform)shootBtn.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(230, 100));

            if (M.Kind == GameKind.Snooker)
            {
                colourBox = Ui.Rect(R, "Colours"); Anchor(colourBox, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, -20), new Vector2(110, 460));
                var vl = colourBox.gameObject.AddComponent<VerticalLayoutGroup>(); vl.spacing = 8; vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = vl.childForceExpandHeight = true;
                string[] nm = { "Жёлтый 2", "Зелёный 3", "Коричн. 4", "Синий 5", "Розовый 6", "Чёрный 7" };
                Color[] cc = { new Color(0.9f, 0.8f, 0.1f), new Color(0.1f, 0.55f, 0.25f), new Color(0.5f, 0.3f, 0.12f), new Color(0.1f, 0.3f, 0.8f), new Color(0.9f, 0.45f, 0.65f), new Color(0.1f, 0.1f, 0.12f) };
                colourBtns = new Button[6];
                for (int i = 0; i < 6; i++)
                {
                    int id = 16 + i;
                    colourBtns[i] = Ui.Btn(colourBox, nm[i], () => nominated = id, 70, 18, cc[i]);
                    if (i == 0 || i == 5 || i == 4) colourBtns[i].GetComponentInChildren<Text>().color = i == 5 ? Color.white : Color.black;
                }
                freeBtn = Ui.Btn(R, "Свободный шар", () => { freeSelect = !freeSelect; if (!freeSelect) freeBall = -1; }, 70, 22, Ui.Dark);
                Destroy(freeBtn.GetComponent<LayoutElement>());
                Anchor((RectTransform)freeBtn.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -100), new Vector2(240, 70));
            }
        }

        static Vector2 LocalNorm(RectTransform rt, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out Vector2 lp);
            Rect r = rt.rect;
            return new Vector2((lp.x - r.xMin) / r.width, (lp.y - r.yMin) / r.height);
        }

        static void Destroy(UnityEngine.Object o) => UnityEngine.Object.Destroy(o);

        // ================= ход партии =================
        void BeginTurn(bool first = false)
        {
            power = 0; scaleTouched = false; called = -1; nominated = -1; freeBall = -1; freeSelect = false; drag = Drag.None;
            placed = M.St.Hand == HandMode.None; planner = null; botT = 0;
            cueId = M.CueCandidates().Contains(cueId) && M.BallById(cueId) != null && !M.BallById(cueId).Pocketed ? cueId : M.CueCandidates()[0];
            if (M.Kind != GameKind.American) cueId = 0;
            freeAvail = M.Kind == GameKind.Snooker && PlayerTurn && M.Phase == Phase.Aiming && M.St.FreeBallEligible && M.FreeBallAvailable;
            if (first)
            {
                Vec2 cp = M.Cue.Pos; Vec2 mid = new Vec2((M.Map.Outline.Min(p => p.X) + M.Map.Outline.Max(p => p.X)) / 2, (M.Map.Outline.Min(p => p.Y) + M.Map.Outline.Max(p => p.Y)) / 2);
                aimAngle = Math.Atan2(mid.Y - cp.Y, mid.X - cp.X); botAngle = aimAngle;
            }
        }

        bool CanInput => !paused && PlayerTurn && M.Phase == Phase.Aiming && !shooting && overTimer <= 0 && !A.HasDialog;

        void Fire()
        {
            if (!CanInput) return;
            if (M.Kind == GameKind.Snooker && M.St.ColourOn && !RulesEngine.IsColour(nominated)) { Toast("Выберите цветной шар слева", 2); return; }
            if (M.NeedsCalledPocket(cueId) && called < 0) { Toast("Коснитесь лузы для «8»", 2); return; }
            if (freeSelect && freeBall < 0) { Toast("Коснитесь шара, который заявляете", 2); return; }
            var p = new ShotParams { CueId = cueId, Angle = aimAngle, Power = Math.Max(0.02, power), SpinX = spinX, SpinY = spinY, CalledPocket = called, NominatedColor = nominated, FreeBallId = freeBall };
            if (!M.Shoot(p)) { Toast("Сейчас удар недоступен", 2); return; }
            Launched();
        }

        void Launched()
        {
            shooting = true; power = 0; drag = Drag.None; tv.ShowHome(false);
            if (A.D.Active != null) { A.D.Active.Snapshot = M.PreShot; A.SaveNow(); }
        }

        void OnShotEnd()
        {
            shooting = false;
            if (A.D.Active != null) { A.D.Active.Snapshot = M.Snapshot(); A.SaveNow(); }
            ShotResult r = M.LastResult;
            if (r != null && (r.Foul || r.Rerack || r.Warning) && !string.IsNullOrEmpty(r.Message)) Toast(r.Message, 2.2f);
            if (M.Phase == Phase.Over) overTimer = 2.2f;
            BeginTurn();
        }

        void FinishMatch()
        {
            if (finished) return; finished = true;
            bool won = M.St.Winner == 0;
            if (!free) A.Prof.RecordResult(M.Kind, level, won);
            A.D.Active = null; A.SaveNow();
            A.Show(new ResultPage(M, level, free, won));
        }

        void OnShootButton()
        {
            if (!CanInput) return;
            if (!placed) { placed = true; tv.ShowHome(false); return; }
            if (placed && PMode == 0) return;
            if (power <= 0) { Toast(PMode == 2 ? "Задайте силу шкалой" : "Задайте силу: оттяните палец или двиньте шкалу", 2); return; }
            Fire();
        }

        // ================= ввод =================
        void OnDown(Vector2 pos)
        {
            if (!CanInput) return;
            Vec2 w = tv.ToWorld(pos);
            if (!placed) { drag = Drag.Place; M.TryPlaceCue(w); return; }
            // шар для заявки свободного шара / выбор битка в Американке
            if (freeSelect || M.Kind == GameKind.American)
            {
                Ball hit = M.Solver.Balls.Where(b => !b.Pocketed && (b.Pos - w).Len < M.Map.BallDiameter * 0.75).OrderBy(b => (b.Pos - w).Len).FirstOrDefault();
                if (hit != null)
                {
                    if (freeSelect && hit.Id != 0) freeBall = hit.Id;
                    else if (M.Kind == GameKind.American && M.CueCandidates().Contains(hit.Id)) cueId = hit.Id;
                    return;
                }
            }
            if (M.NeedsCalledPocket(cueId))
            {
                int pi = -1; double best = double.MaxValue;
                for (int i = 0; i < M.Map.Pockets.Length; i++)
                {
                    double d = (M.Map.Pockets[i].Pos - w).Len;
                    if (d < Math.Max(M.Map.Pockets[i].Throat, 0.1) * 1.6 && d < best) { best = d; pi = i; }
                }
                if (pi >= 0) { called = pi; return; }
            }
            drag = Drag.Aim; UpdateAim(pos);
        }

        void OnDrag(Vector2 pos)
        {
            if (!CanInput) return;
            if (drag == Drag.Place) M.TryPlaceCue(tv.ToWorld(pos));
            else if (drag == Drag.Aim) UpdateAim(pos);
        }

        void OnUp(Vector2 pos)
        {
            if (drag == Drag.Aim && !sliderMode && CanInput)
            {
                if (power > 0.02) { drag = Drag.None; Fire(); }
                else power = 0;
            }
            drag = Drag.None;
        }

        void UpdateAim(Vector2 pos)
        {
            Ball cue = M.BallById(cueId); if (cue == null) return;
            Vector2 v = tv.ToScreen(cue.Pos) - pos;
            float dp = v.magnitude / Ui.Dp;
            if (dp < 48f) { if (!sliderMode) power = 0; return; }
            aimAngle = Math.Atan2(v.y, v.x);
            if (!sliderMode) power = Math.Max(0, Math.Min(1, (dp - 48f) / (150f - 48f)));
        }

        // ================= пауза =================
        void Pause() { if (finished) return; paused = true; BuildPause(); }

        void Resume() { paused = false; if (pauseRoot != null) Destroy(pauseRoot.gameObject); pauseRoot = null; }

        void BuildPause()
        {
            pauseRoot = Ui.Box(R, new Color(0, 0, 0, 0.8f), "Pause"); Ui.Fill(pauseRoot);
            var col = Ui.Column(pauseRoot, 14); Ui.Place(col, 0.32f, 0.05f, 0.68f, 0.95f);
            col.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ui.Label(col, "Пауза", 52, TextAnchor.MiddleCenter, Ui.Gold);
            Ui.Btn(col, "Продолжить", Resume, 76, 30, Ui.Gold).GetComponentInChildren<Text>().color = Color.black;
            Ui.Btn(col, "Переиграть", () => A.Ask("Партия будет засчитана как поражение. Переиграть?", "Переиграть", () => { A.Abandon(); A.StartMatch(M.Map, level, free); }), 76, 28);
            Ui.Btn(col, "Правила", () => A.Go(new RulesPage(M.Kind)), 76, 28, Ui.Dark);
            Ui.Btn(col, "Настройки", () => A.Go(new SettingsPage()), 76, 28, Ui.Dark);
            Ui.Btn(col, "Главное меню", () => A.Ask("Партия будет засчитана как поражение. Выйти в меню?", "Выйти", () => { A.Abandon(); A.Home(); }), 76, 28, Ui.Dark);
        }

        public override void Back() { if (finished || overTimer > 0) return; if (paused) Resume(); else Pause(); }

        public override void OnSystemPause() { if (!finished && !paused) { paused = true; if (tv != null && R != null) BuildPause(); } }

        public override void Closed() { if (tv != null) tv.Destroy(); tv = null; }

        void Toast(string s, float t) { toast.text = s; toast.gameObject.SetActive(true); toastT = t; }

        // ================= кадр =================
        public override void Tick()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (tv == null) return;
            if (tv.NeedsRefit) Fit();
            if (!paused && !finished)
            {
                if (M.Phase != Phase.Over) StepPhysics(dt);
                if (M.Phase == Phase.Aiming && !PlayerTurn && overTimer <= 0 && !A.HasDialog) BotTurn(dt);
                if (overTimer > 0) { overTimer -= dt; if (overTimer <= 0) FinishMatch(); }
                if (toastT > 0) { toastT -= dt; if (toastT <= 0) toast.gameObject.SetActive(false); }
            }
            if (finished || tv == null) return;
            UpdateHud();
            tv.Sync(sf);
            DrawAim(dt);
        }

        void StepPhysics(float dt)
        {
            acc += dt;
            int n = (int)(acc * 480); acc -= n / 480.0; n = Math.Min(n, 160);
            for (int i = 0; i < n; i++)
            {
                M.Step();
                foreach (SolverEvent e in M.Solver.Events)
                {
                    if (e.Type == Boll.Core.Physics.EventType.BallBall) Sfx.Hit(e.Speed);
                    else if (e.Type == Boll.Core.Physics.EventType.Cushion)
                    {
                        if (e.Surface == Surface.Bumper) Sfx.Bumper(e.Speed); else Sfx.Rail(e.Speed);
                    }
                }
                if (shooting && M.Phase != Phase.Shooting) { OnShotEnd(); break; }
            }
        }

        void BotTurn(float dt)
        {
            if (planner == null) { planner = new BotPlanner(M, bot); botT = 0; }
            botT += dt;
            var sw = Stopwatch.StartNew();
            while (!planner.Done && sw.ElapsedMilliseconds < 8 && botT < BotPlanner.MaxTurnSeconds - 0.8) planner.Work(1);
            if (!planner.Done && botT >= BotPlanner.MaxTurnSeconds - 0.8) planner.Finish();
            if (planner.Done && botT >= BotPlanner.MinAimSeconds)
            {
                planner.Finish();
                ShotParams r = planner.Result;
                if (!M.Shoot(r)) M.Shoot(new ShotParams { CueId = M.CueCandidates()[0], Angle = r.Angle, Power = 0.3, NominatedColor = r.NominatedColor });
                if (M.Phase == Phase.Shooting) Launched();
                planner = null;
            }
        }

        void UpdateHud()
        {
            bool mine = PlayerTurn;
            string who = mine ? "Ваш ход" : "Ход: " + bot.Name;
            if (M.Kind == GameKind.Eight) who += M.St.TableOpen ? "\nСтол открыт" : "\nВаши шары: " + (M.St.Group[0] == 1 ? "сплошные" : "полосатые");
            else if (M.Kind == GameKind.Snooker) who += M.St.ColourOn ? "\nНа цветной" : "\nНа красный";
            info.text = who;
            score.text = "Вы " + M.St.Score[0] + " : " + M.St.Score[1] + " Соперник";

            bool can = CanInput;
            shootBtn.interactable = can;
            Ui.SetText(shootBtn, !placed ? "Готово" : "Удар");
            powerFillRt.anchorMax = new Vector2(1, (float)power);
            powerHandle.anchorMin = powerHandle.anchorMax = new Vector2(0.5f, (float)power); powerHandle.anchoredPosition = Vector2.zero;
            powerText.text = "Сила " + Mathf.RoundToInt((float)power * 100) + " %";
            spinDot.anchoredPosition = new Vector2((float)spinX, (float)spinY) * 65f;
            shootBtn.gameObject.SetActive(!placed || PMode != 0);

            string h = "";
            if (can && !placed) h = "Перетащите биток, затем нажмите «Готово»";
            else if (can && M.NeedsCalledPocket(cueId) && called < 0) h = "Коснитесь лузы для «8»";
            else if (can && PMode == 2) h = "Прицельтесь пальцем, силу задайте шкалой и нажмите «Удар»";
            else if (can && M.Kind == GameKind.American) h = "Коснитесь шара, которым будете бить";
            else if (can && freeSelect) h = freeBall < 0 ? "Коснитесь шара, который заявляете" : "Заявлен шар " + freeBall;
            hint.text = h;

            if (colourBox != null)
            {
                bool show = can && M.St.ColourOn;
                colourBox.gameObject.SetActive(show);
                for (int i = 0; i < 6; i++) colourBtns[i].transform.localScale = nominated == 16 + i ? Vector3.one * 1.12f : Vector3.one;
                freeBtn.gameObject.SetActive(can && freeAvail);
                Ui.SetText(freeBtn, freeSelect ? "Свободный: вкл" : "Свободный шар");
            }
        }

        void DrawAim(float dt)
        {
            bool mine = PlayerTurn && M.Phase == Phase.Aiming && !shooting && !paused;
            tv.ShowHome(mine && !placed && M.St.Hand == HandMode.Home);
            tv.ShowCalled(called);
            Ball cue = M.BallById(cueId);
            tv.ShowSelection(M.Kind == GameKind.American && mine && cue != null ? cue.Pos : (Vec2?)null);
            if (cue == null || cue.Pocketed || shooting || M.Phase != Phase.Aiming) { tv.ShowCue(false, default, 0, 0); tv.HideAim(); return; }
            if (mine)
            {
                if (!placed) { tv.ShowCue(false, default, 0, 0); tv.HideAim(); return; }
                tv.ShowCue(true, cue.Pos, aimAngle, 0.01 + power * 0.22);
                if (A.D.Settings.AimLine) tv.ShowAim(cue.Pos, cueId, aimAngle, A.D.Settings.BounceLine); else tv.HideAim();
            }
            else
            {
                Ball bc = M.Cue; if (bc == null) return;
                double target = planner != null && planner.Done && planner.Result != null ? planner.Result.Angle : botAngle + dt * 0.9;
                double diff = Math.Atan2(Math.Sin(target - botAngle), Math.Cos(target - botAngle));
                botAngle += diff * Math.Min(1, dt * 6) + (planner != null && planner.Done ? 0 : dt * 0.9);
                tv.ShowCue(true, bc.Pos, botAngle, 0.03 + 0.03 * Math.Sin(Time.time * 6));
                tv.HideAim();
            }
        }
    }
}
