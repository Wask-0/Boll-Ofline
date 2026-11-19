# Boll offline — CLAUDE.md
Бильярд 2D, Android, Unity 6000.5.2f1. Полная концепция: CONCEPT.md (читать ТОЛЬКО нужный раздел через Grep/offset, не целиком).

## Правила работы
- Экономить токены: не читать Library/Temp/.meta/сцены; править через Edit; ответы краткие.
- Логика — в `Assets/Scripts/Core` (asmdef Boll.Core, без UnityEngine, double, шаг 1/480 с, детерминизм). Unity-часть — `Assets/Scripts/App`.
- Тесты: `Assets/Tests/EditMode`; писать тест вместе с модулем.
- Контент: `Assets/Resources/catalog|maps` (JSON). Ресурсы — CC0, учёт в ASSETS.md.
- Язык UI — русский.

## Структура
Core/Content (карты, каталоги, JSON) · Core/Physics (Solver, Rest) · Core/Rules (RulesEngine, расстановка) · App (UI, ввод, сохранение).

## Прогресс (обновлять; шаги = CONCEPT §14)
- [x] 1 загрузчик карт  [x] 2 каталоги  [x] 3 решатель  [x] 4 покой/зоны  [x] 5 правила 4 видов
- [x] 6 снукер  [x] 7 автомат партии  [x] 8 сохранение/журнал/звёзды  [~] 9 бот (лесенка 51–73%, цель 65% не достигнута; перемерить на 35 картах)  [x] 10 35 карт
- [~] 11 UI (экраны, ввод, бот в игре написаны и запущены в редакторе; не проверено на телефоне и касаниях)  [ ] 12 тексты  [~] 13 звук (синтез в Sfx.cs, тени, кий) — проверить на слух и телефоне  [ ] 14 FPS/APK  [ ] 15 релиз

## Решения
- Решено: снукер в 1.0 (35 карт); порядок Core→тесты→UI; Unity MCP доступен (refresh_unity, run_tests).
- Карта JSON: outline, pockets, obstacles(wall|bumper|mover pingpong/circle), layout(rackPoint,rackAxis,home,homeLineA/B,marks,returnPoints). Newtonsoft в Core. Проверка зон движущихся — шаг 10/4.
- Solver (Core/Physics/Solver.cs): Ball{Id,Pos,Vel,Wx,Wy,Wz,Pocketed}; Strike(ball,angle,power,a,b); Step(); Events (BallBall/Cushion{Surface,Index}/Pocket). Движущиеся едут всегда (Step двигает их сам). Трение шар-шар 0.05, шар-борт = SlidingFriction модели.
- Rest(map): InMoverZone, CanShoot(solver), StuckBalls, AllAtRest (v<0.002, w<0.1).
- Rules: Rng(SplitMix64), Placement(Rack,FindSpot,IsValidHandSpot), MatchState(+ShotInput/ShotResult/Respot), ShotRecorder (Timeline: события решателя + CenterCross; звать Update() после каждого Step), RulesEngine.Evaluate(ShotInput). Пирамида: вершина в marks.back, ось rackAxis; биток 0 (Американка: любой шар). Центральная линия = середина контура вдоль rackAxis. Сибирка: HomeShotBallToMove. Снукер: RulesEngine.Snooker.cs, ид: биток 0, красные 1–15, цветные 16–21; ShotInput.NominatedColor/FreeBallId; OnBalls(), IsSnookered(cue,balls); конец кадра/переигровка чёрного в ShotResult.Tiebreak.
- Game/Match.cs: Match(map,model,seed); Shoot(ShotParams), Step()/RunShot(n), TryPlaceCue, CanShoot, Phase{Aiming,Shooting,Over}, LastResult, SafetySteps (180 с → переставляет застрявшие), NextMap. Step() звать всегда (движущиеся едут и при прицеливании); при прицеливании упавшие шары возвращаются.
- Save/: IFileSystem(DiskFs,MemoryFs), SaveStore(save.json+tmp+bak; LoadStatus), SaveData(Stars,Owned,BotsUnlocked,Stats,Settings,LastMap,Active), Profile(RecordResult,Buy,Select), ErrorLog(256КБ,Start→crash?). Match.PreShot сохранять сразу после Shoot, Match.Snapshot() после остановки; Match.Restore переигрывает Pending.
- Bot/: BotPlanner(match, BotLevel).Work(sims)/Finish() → Result(ShotParams)+HandSpot; поиск = оценочные прогоны копии Match + RulesEngine; шум через match.Rng; BotArena.Play; BotTuning.For(level) (MaxSims, Width). Тест LevelLadder (Explicit) — замер уровней; на 6 партиях 8>1 только 4/6 (nine) — нужна большая выборка и подстройка. Бот не заявляет свободный шар; битком всегда 0 (в Американке).
- Бот: замеры (60 партий/пара, eight+nine, стол-прямоугольник): 2v1 51%, 3v2 73, 4v3 55, 5v4 61, 6v5 53, 7v6 70, 8v7 60. PotRate по восьмёрке L1..L8: 8,5,17,22,20,32,38,58 % за ход. Тесты Ladder/PotRate/Diagnose — Explicit (параллельно, ~5 мин). Добавлены: SuccessProb (учёт разброса), бонус позиции на листе поиска.
- Карты: Assets/Resources/maps/<вид>/<вид>_1..7.json, генератор tools/gen_maps.py (потом перегенерировать и refresh assets). MapChecks.Run (разметка/зазоры/лузы) в тесте AllMapsPassAutomaticChecks; BotVsBotFitsIn150Shots (Explicit, ~2 мин) — 35/35 ≤150 ударов. Бот в Американке берёт любой шар как биток (CueOf). Тесты: 87 обычных + 13 Explicit.
- App (шаг 11): AppMain (создаётся сам, uGUI из кода, Input System; навигация Page: Go/Pop/Show, диалоги Modal/Ask/Info), Pages.cs (меню, вид, бот, свободный режим, настройки, статистика, магазин, итог), PlayPage.cs (ввод: палец вокруг битка/оттяжка 48–150 dp, шкала силы, окно точки удара, пауза, бот по кадрам), TableView.cs (спрайты из кода, стол растром), RulesText.cs. Sfx.cs: звуки и музыка синтезируются кодом, громкости из настроек.
