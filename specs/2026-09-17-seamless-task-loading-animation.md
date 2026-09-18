# Бесшовная анимация загрузки задач и переключения пространств

## 0. Метаданные

- Тип: `delivery-task`; профили `dotnet-desktop-client`, `ui-automation-testing`; contexts `testing-dotnet`, `visual-feedback`.
- Skills: `creator-vibe`, `record-app-screen`.
- Масштаб: medium — два UI-flow, общий control, lifecycle анимации и несколько уровней UI-тестов.
- Runtime: .NET 10, Avalonia 12.0.3, AppAutomation/Avalonia.Headless/FlaUI; checkout `d4d90de7` на detached HEAD.
- Референс: `C:\Users\Kibnet\Videos\unlimotion.mp4`, SHA-256 `E76ACD5384F6D48220FB78CDFCC2D4E73CA3080279196475996F2CF3798FEBD8`.
- Visual planning: `C:\Temp\unlimotion-animation-analysis\motion-storyboard.png`; полный contact sheet: `C:\Temp\unlimotion-animation-analysis\contact-sheet.png`.
- Ограничение: до фразы `Спеку подтверждаю` меняется только эта spec. Commit/push/PR/release не входят в scope.

## 1. Overview / Цель

Заменить два разных текущих индикатора — spinner загрузки задач и indeterminate progress bar переключения пространства — одной нативной анимацией в духе видео-драфта: тёмный открытый знак бесконечности и непрерывно проходящий по нему фиолетовый световой импульс.

Outcome contract:

- Success means: оба loading-flow выглядят единообразно; движение непрерывно внутри цикла и на шве; UI остаётся отзывчивым; тексты, блокировка действий и recovery semantics не меняются.
- Output: переиспользуемый Avalonia control, интеграция в оба overlay, regression/UI tests, автоматизированные `before`/`after` MP4 и screenshots.
- Stop rules: не маскировать seam повышением fps или растровым crossfade; не вводить production delay; падающий UI test, отсутствие full green или отсутствие visual evidence без объективного fallback блокируют завершение.

## 2. Текущее состояние (AS-IS)

- `MainScreen.axaml` содержит `TasksLoadingSpinner`; `MainScreen.axaml.cs` поворачивает его на 18° каждые 50 мс через `DispatcherTimer`. Timer работает всё время, пока view attached, даже при скрытом overlay.
- `MainControl.axaml` показывает отдельный `ProgressBar IsIndeterminate="True"` внутри `TaskSpaceSwitchOverlay`.
- State ownership уже корректен: `MainWindowViewModel.IsTasksLoading` отвечает за initial load, `SettingsViewModel.IsTaskSpaceSwitching` — за task-space operations.
- Стабильные ids `TasksLoadingOverlay`, `TasksLoadingSpinner`, `TaskSpaceSwitchOverlay`, `TaskSpaceSwitchProgress`, `TaskSpaceSelector` используются unit/headless/FlaUI tests.
- Есть checks на видимость/отзывчивость/disabled state и готовый `scripts/record-task-spaces-evidence.ps1` для A→B→A flow.
- Драфт: 640×360, H.264, 24 fps, 25 кадров, 1.041667 с, AAC stereo. Он содержит белый фон и watermark, не имеет alpha; first/last PSNR 33.88 dB, остаточное различие проходит по световому импульсу. Как production asset он непригоден.

## 3. Проблема

Два loading-flow визуально не связаны; spinner движется ступенчато, а прямое зацикливание MP4 сохранит фон/watermark и заметную смену светового поля на границе цикла.

## 4. Цели дизайна

- Один visual language для initial load и space switch.
- Сохранить характер драфта: открытая infinity-кривая, тёмная основа, фиолетовый halo/trail/ядро.
- Exact periodicity: `phase = elapsed modulo period`, поэтому `phase 1` визуально эквивалентна `phase 0`.
- Native vector rendering: прозрачность, DPI scaling, theme compatibility, без бинарного видео.
- Запрашивать кадры только когда control visible и attached.
- Сохранить automation ids и state contracts.

## 5. Non-Goals

- Не меняем storage/loading pipeline, скорость загрузки, task-space business logic или ViewModel API.
- Не меняем тексты `LoadingTasks` / `TaskSpaceSwitching`, локализацию, recovery flow и z-order.
- Не добавляем звук, MP4/GIF/WebP/Lottie/Rive/SkiaSharp или новую runtime dependency.
- Не копируем 3D-материал ролика pixel-perfect; сохраняем мотив и движение в языке приложения.
- Не добавляем production delay и не выполняем delivery side effects.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Ответственности

- Новый `src/Unlimotion/SeamlessLoadingIndicator.cs` — geometry, drawing layers, phase normalization и animation-frame lifecycle.
- `MainScreen.axaml` — тот же overlay/text, но общий indicator; `MainScreen.axaml.cs` — удалить старый timer/transform.
- `MainControl.axaml` — заменить progress bar тем же indicator.
- `Unlimotion.Test` — phase/lifecycle/render contract и существующие state-driven checks.
- FlaUI/test host — observable assertions и только automation-only задержка, достаточная для записи нескольких циклов; production timing неизменен.

### 6.2 Детальный дизайн

- Control рисует одну открытую infinity-geometry в нормализованном coordinate space.
- Слои: спокойная base stroke, широкий low-opacity purple halo, более узкий accent trail и короткое светлое ядро. Все слои используют одну geometry и phase.
- Стартовая длительность 1.6 с; допустимая настройка после video review — 1.4–1.8 с без изменения продуктового контракта.
- На открытых концах импульс затухает/возникает; перекрывающиеся фазы не допускают внезапного полного исчезновения.
- Кадры запрашиваются через `TopLevel.RequestAnimationFrame`. Следующий кадр планируется только при attached/effectively visible control с ненулевым size. Late callback после hide/detach не создаёт новый цикл.
- Geometry и постоянные primitives кешируются; `Render` не делает I/O и не создаёт bitmap.
- Background/text остаются контекстными; indicator имеет прозрачный canvas. Recovery overlay сохраняет более высокий `Panel.ZIndex`.
- Visual evidence: до изменения записать `C:\Temp\unlimotion-animation-evidence\before.mp4`, после — тем же flow/window/FPS/duration `after.mp4`; recorder без audio. Fallback только при объективной невозможности записи: одинаковые phase screenshots + tests + manifest/ffprobe с причиной.

### 6.3 User-Observable Scenarios

| Scenario | Trigger | Expected result | Evidence | AC |
| --- | --- | --- | --- | --- |
| Initial load | Незавершённая загрузка storage | Карточка `LoadingTasks`, новый indicator, responsive dispatcher; overlay исчезает по завершении | Headless + recorded startup/frames | AC1, AC2, AC4, AC6 |
| Space switch | A→B→A | Тот же indicator с `TaskSpaceSwitching`; selector/settings disabled; новые задачи доступны после overlay | FlaUI + before/after MP4 | AC1, AC3, AC4, AC6 |
| Fast operation | State короче периода | Indicator не удерживает UI искусственно | Headless state test | AC3, AC5 |
| Long/repeated operation | Несколько циклов, hide/show | Нет seam, зависания или накопления скорости | Boundary test + after video | AC2, AC5 |
| Recovery | Switch требует recovery | Recovery overlay остаётся сверху; semantics неизменны | UI check + z-order inspection | AC3, AC7 |

### 6.4 State / Interaction Matrix

| State | Trigger | Result | Edge case |
| --- | --- | --- | --- |
| Hidden | Loading flag=true | Overlay/indicator visible, frame loop active | Unattached/zero-size не планирует frame |
| Initial load active | Flag=false | Overlay hidden, frame loop stops | No minimum production duration |
| Idle | `IsTaskSpaceSwitching=true` | Switch overlay visible, actions disabled | Existing re-entry guard unchanged |
| Switch active | Existing finally=false | Overlay hidden; current error/recovery flow | Control не владеет async operation |
| Visible | Period boundary | Phase wraps to equivalent visual state | No cumulative drift |
| Hidden/detached | Late callback | No reschedule | No timer leak |

### 6.5 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Medium | agent | Native Avalonia vector, not cleaned MP4 | 0.95 | 3D texture differs | Нет |
| Reuse | agent | One control in both overlays | 0.98 | Low | Нет |
| Motion | agent | Time-based modulo phase, layered stroke | 0.92 | Needs visual tuning | Нет |
| Duration | agent | 1.6 s, tune within 1.4–1.8 s | 0.80 | Subjective pace | Нет |
| Theme | agent | Transparent control, brand purple, theme-aware base | 0.90 | Contrast must be checked | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Loading flags | Existing VMs | None | Full | State tests |
| Automation ids | Current XAML/tests | Preserve ids; concrete control type changes | Update over-specific helpers only | Headless/FlaUI |
| Assets/config/data | Project resources/settings | None | No migration | Diff/build |
| Rendering | Avalonia 12 core APIs | New small control | Desktop plus compile compatibility | Build/tests |

## 7. Алгоритмы и инварианты

- `period > 0`; phase находится в `[0,1)` и считается от elapsed time, не через накопительное `phase += step`.
- `t` и `t + N*period` дают одинаковые параметры rendering.
- Animated layers делят geometry/phase и различаются только trail constants.
- Indicator не меняет loading flags и не задерживает completion.
- Hidden/detached control не продолжает frame loop.
- Recovery z-order `91` остаётся выше switching `90`.

## 8. Интеграция

- `IsTasksLoading` остаётся единственным trigger initial overlay.
- `Settings.IsTaskSpaceSwitching` остаётся единственным trigger switch overlay.
- Attached/detached/visibility/layout и animation-frame callback управляют только rendering lifecycle.
- `TaskSpaceOperationRunner`/coordinator ownership не меняется.

## 9. Данные / состояние

- Persisted data, config, ViewModel и public API: без изменений.
- UI-only state: start timestamp, pending-frame flag, normalized phase; reset при новом visible attachment cycle.

## 10. Миграция / Rollback

- Миграция не нужна.
- Rollback: удалить control/tests и вернуть три view-файла/старый timer; пользовательские данные не затронуты.
- Generated MP4/PNG остаются local-only в `C:\Temp` или ignored artifacts; бинарные evidence не коммитятся.

## 11. Тестирование и критерии приёмки

- AC1. Оба overlay используют один `SeamlessLoadingIndicator`; bindings/text/automation ids сохранены.
- AC2. Tests доказывают exact wrap и continuity вокруг boundary; after-video показывает ≥2 цикла без perceptible jump.
- AC3. Loading/switch flags по-прежнему show/hide overlay и сохраняют disabled/recovery behavior.
- AC4. Dispatcher responsive во время blocking initial load; A→B→A доходит до actionable task content.
- AC5. Hidden/detached indicator не запрашивает кадры; show/hide не накапливает drift.
- AC6. Вид соответствует storyboard: open infinity, dark base, purple halo/trail/core, transparent canvas, light/dark contrast, без watermark/audio/video background.
- AC7. Нет data/config/API/storage change; recovery precedence сохранён.
- AC8. Targeted tests, affected build, full main TUnit, full Headless и targeted FlaUI/evidence green.

Planned tests:

- Новый test class для phase boundary/lifecycle/render contract.
- Обновить `MainScreenLoadingUiTests`, сохранив responsive-load check.
- Расширить `SettingsControlResponsiveUiTests`/task-space coverage для общего indicator.
- Сохранить readiness semantics `TaskLoadingPerformanceFlaUiTests`.
- Automation-only hold обеспечивает запись нескольких циклов; production delay запрещён.

Validation sequence:

1. `dotnet --info` и restore/toolchain preflight.
2. Baseline video через `scripts/record-task-spaces-evidence.ps1 -Phase Before` и recorder skill script.
3. Failing/characterization targeted tests.
4. Targeted main/headless/FlaUI tests с TUnit `--treenode-filter` и `--maximum-parallel-tests 1`.
5. `dotnet build src/Unlimotion.sln -c Debug -p:UseSharedCompilation=false`.
6. Full `src/Unlimotion.Test/Unlimotion.Test.csproj` и full Headless suite serially.
7. After video тем же recorder flow; ffprobe + representative frames.
8. `git diff --check`, `git status --short`, relevant diff.

Stop rules: timeout не повторять без новой гипотезы/evidence; TUnit не запускать с VSTest `--filter`; targeted green не заменяет full green; recorder failure требует documented objective fallback.

### Acceptance-to-Test Matrix

| AC | Automated | Visual/manual | Artifact |
| --- | --- | --- | --- |
| AC1 | MainScreen + switch UI tests | Inspect overlays | after screenshots/diff |
| AC2 | Phase boundary tests | ≥2 cycles normal/slow playback | `after.mp4`, contact sheet |
| AC3 | Existing/updated headless tests | Recovery screenshot if affected | logs/screenshots |
| AC4 | Blocking-load + FlaUI A→B→A | Actionable task after overlay | manifest/video |
| AC5 | Lifecycle/frame-request test | Optional profiler only on concern | test log |
| AC6 | Stable render/sizing checks | Light/dark screenshots, ffprobe no audio | PNG/MP4 metadata |
| AC7 | State tests + diff | Inspect z-index/no VM-data diff | diff/log |
| AC8 | Full/targeted runs | Exit codes/counts | TRX/log/manifest |

## 12. Риски и edge cases

- Backend differences in dash/glow rendering: use Avalonia core `DrawingContext`/Pen/Geometry; halo is visual, not correctness-critical.
- Fast operation shows partial cycle: expected, no artificial wait.
- Late callback races hide/detach: pending-frame guard checks lifecycle before reschedule.
- Existing tests may over-specify Grid/ProgressBar type: retain ids, relax only concrete type coupling.
- Excess glow/poor contrast: bounded opacity plus light/dark screenshot review.
- Reduced-motion is not an established app contract; do not invent a setting here. Separate follow-up if an authoritative platform signal exists.

### Expected User Review Objections

| Objection | Mitigation | Status |
| --- | --- | --- |
| «Не похоже на драфт» | Preserve silhouette/direction/purple layers and show recorded result | mitigated |
| «На seam всё ещё рывок» | Exact modulo invariant, boundary tests, ≥2-cycle review | mitigated |
| «При старте и switch снова разные виды» | Same control/sizing/motion; only context background/text differs | mitigated |
| «Loader тормозит UI» | Frames only visible/attached; responsiveness/lifecycle tests | mitigated |
| «В Git попадёт watermark/video» | No MP4 production asset; local-only evidence | mitigated |

Rework prevention: observable states, evidence, assumed decisions, objections, role review and EXEC proof path are all present.

## 13. План выполнения

1. После approval записать baseline before production change.
2. Добавить failing/characterization tests for phase/lifecycle/shared UI contract.
3. Реализовать control, интегрировать оба overlay, удалить old timer.
4. Targeted green и визуальная настройка внутри утверждённых границ.
5. Headless/FlaUI coverage и automation-only recording hold.
6. After video, light/dark screenshots, ≥2-cycle review.
7. Affected build, full suites, diff checks.
8. Full post-EXEC review, fixes, rerun affected checks, delivery of evidence.

## 14. Открытые вопросы

Нет блокирующих. Fine tuning скорости/яркости — агентское решение внутри диапазона и after-video gate. Переход к pixel-perfect video asset потребует обновления spec и нового approval.

## 15. Соответствие профилю

- UI flow покрывается unit/headless/FlaUI.
- Automation ids стабильны.
- UI thread не получает blocking work.
- Visual planning и before/after automated video обязательны.
- Affected/full build/test gate включён.

## 16. Таблица изменений файлов

| File | Change |
| --- | --- |
| `src/Unlimotion/SeamlessLoadingIndicator.cs` (new) | Geometry/render/lifecycle/phase |
| `src/Unlimotion/Views/MainScreen.axaml` | Shared indicator |
| `src/Unlimotion/Views/MainScreen.axaml.cs` | Remove timer |
| `src/Unlimotion/App.axaml.cs` | Automation-only evidence hold; zero delay without explicit env var |
| `src/Unlimotion/Views/MainControl.axaml` | Shared indicator |
| `src/Unlimotion.Test/MainScreenLoadingUiTests.cs` | Updated lookup/assertions |
| `src/Unlimotion.Test/SeamlessLoadingIndicatorTests.cs` (new) | Boundary/lifecycle/render tests |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | Switch indicator assertion |
| FlaUI/test host files, only if needed | Observable assertion and automation-only hold |
| `scripts/record-task-spaces-evidence.ps1` | Prefer no change; modify only if current manifest cannot prove state |

Final set may shrink when existing coverage already proves an AC; it may not expand outside indicator/UI/test/evidence scope without spec update.

## 17. Было → стало

| Area | Before | After |
| --- | --- | --- |
| Initial load | 20-fps rotating bar/dot | Native infinity/purple pulse |
| Space switch | Standard indeterminate bar | Same infinity indicator |
| Loop | Incremental steps/framework motion | Time-based exact periodic phase |
| Lifecycle | Always-running attached timer | Visible/attached frame callbacks only |
| Asset | Unshippable MP4 reference | Clean vector, no video asset |
| Evidence | State visibility checks | Boundary/lifecycle + before/after MP4 |

## 18. Альтернативы

- Cleaned MP4: closest texture, but alpha/codecs/platform/scaling/seam liabilities — rejected.
- GIF/WebP: no audio, but raster/theme limitations — rejected.
- Pure XAML keyframes: declarative, but trail/lifecycle/boundary contract less explicit — not chosen.
- Native custom control: more code, but exact phase, DPI/theme support and testable lifecycle — chosen.

## 19. Quality gate и review

### SPEC Linter Result

| # | Block | Status | Comment |
| ---: | --- | --- | --- |
| 1 | A | PASS | Goal and visible outcome cover both loading flows |
| 2 | A | PASS | AS-IS verified in XAML, code-behind, state owners and tests |
| 3 | A | PASS | Root problem is inconsistent indicators plus non-seamless raster reference |
| 4 | A | PASS | Motion, rendering, lifecycle and compatibility goals are explicit |
| 5 | A | PASS | Storage, API, copy, dependencies, production delay and delivery are excluded |
| 6 | B | PASS | Shared control, views, tests and harness responsibilities are separated |
| 7 | B | PASS | Both bindings, lifecycle hooks and automation touchpoints are named |
| 8 | B | PASS | Modulo phase, shared geometry and no-hidden-work invariants are explicit |
| 9 | B | PASS | Late callbacks, operation errors and recovery precedence are covered |
| 10 | B | PASS | Small cached vector render replaces an always-running hidden timer |
| 11 | C | PASS | Only UI-local timestamp/pending-frame/phase state is added |
| 12 | C | PASS | No persisted/config/API migration; automation ids stay compatible |
| 13 | C | PASS | Direct code/test rollback has no user-data impact |
| 14 | D | PASS | AC1-AC8 are observable and measurable |
| 15 | D | PASS | Every AC maps to automated plus visual/log evidence |
| 16 | D | PASS | Validation order, TUnit syntax and timeout/full-green stop rules are fixed |
| 17 | E | PASS | Baseline, TDD, implementation, tuning, evidence and full validation are ordered |
| 18 | E | PASS | Agent-owned decisions are bounded; no blocking user-owned question remains |
| 19 | E | PASS | Medium/expanded form is justified by two flows and visual/lifecycle evidence |
| 20 | F | PASS | Desktop/UI automation requirements, stable selectors and video evidence are included |

Итог: ГОТОВО.

### SPEC Rubric Result

| Criterion | Score | Rationale |
| --- | ---: | --- |
| Goal/boundaries | 5 | Two flows and non-goals fixed |
| AS-IS | 5 | XAML/timer/state/tests/recorder inspected |
| Design | 5 | Layers, lifecycle and phase contract concrete |
| Safety/rollback | 5 | No migration; direct code rollback |
| Testability | 5 | Boundary/state/e2e/full/video mapped |
| Autonomy | 5 | No blocking decision; tuning bounded |

Total: 30/30 — ready after exact approval.

### Role-Based Review Result

| Role | Applicability | Verdict | Result |
| --- | --- | --- | --- |
| Business analyst | not applicable | PASS | Business/state ownership unchanged |
| UX/designer | applicable | PASS | Bounded pace/layers/theme/storyboard/evidence |
| Tester | applicable | PASS | Boundary/lifecycle/state/e2e/full evidence mapped |
| Developer/architect | applicable | PASS | Rendering isolated; VM/data/API untouched |
| Delivery/security | not applicable | PASS | No external delivery; local-only safe fixtures |

### Post-SPEC Review

- Status/stop decision: PASS; exact approval may be requested.
- Scope/Evidence pass: this spec; central QUEST/testing/review/UI owners; local override; full 25-frame draft, metadata/hash/first-last comparison; MainScreen/MainControl/state refs; unit/headless/FlaUI tests; recorder harness; Avalonia 12 `RequestAnimationFrame`; planned files.
- Contract pass: only shared loading visual changes; text/state/data/recovery/selectors preserved; every AC has evidence; MP4 not shipped.
- Adversarial pass: fast/long/repeated load, hide/detach race, backend difference, test type coupling, contrast, recovery z-order and accidental production delay covered.
- Role pass: UX, tester and developer PASS; non-applicable roles justified.
- Fix/re-review: initial MP4 embedding concept replaced by native vector after format/frame inspection; exact phase, hidden-work and automation-only timing gates added; affected sections/matrix rechecked.
- Findings: Нет незакрытых находок. Remaining perceptual quality is not assumed; it is gated by after-video.
- Checks rerun: all 20 linter criteria, rubric total, scenarios/ledger/AC matrix, planned file scope, `git diff --check` and trailing-whitespace scan.
- No-findings justification: identified design and validation risks have an explicit invariant, evidence mapping and rollback; the only residual is future perceptual rendering quality, which cannot be claimed before EXEC and is a blocking video gate there.
- Manual-review challenge: likely findings would be a visually hidden seam, frames running while hidden, shipped binary/watermark, lost z-order or test delay in production. Each is an explicit AC/check.
- Unrelated changes: none before spec creation; only this spec is changed.

### Post-EXEC Review

- Status/stop decision: PASS; implementation and required local validation are complete. No commit, push, PR, release or deployment was performed.
- Scope/evidence pass: only the shared loading visual, its two call sites, lifecycle tests and evidence-only timing harness changed. Task/storage/config/API behavior and automation ids remain intact.
- Contract pass: initial loading and task-space switching use one native control; phase is timestamp/modulo based with a 1600 ms period; hidden controls reset to phase 0 and do not enqueue a successor frame; raster MP4 is not shipped.
- Adversarial pass: full serial testing exposed cross-dispatcher ownership of static Avalonia pens. All drawing resources were moved to per-control caches; the originally failing blocking-load test and the full main suite then passed.
- Visual pass: final after-video is 45 s H.264, 1002×540 at ~29.87 fps. Both A→B and B→A holds show more than two cycles; frames sampled 1600 ms apart yield 34.05 dB PSNR after H.264 capture. No watermark, video background or audio asset is present in the product.
- Previous visual refinement assessment superseded: the rounded open `u+n` approximation and five particle glints were rejected by the user as insufficiently similar. That iteration's passing UI checks did not establish reference fidelity. The following reference-traced refinement replaces it.
- Automation pass: final FlaUI manifest `20260917T200742325Z-18e2004600ce47d9bad264638b0ee45a` reports exit 0, scenario success and captured recording. One prior retry failed later in unrelated temporary-space removal persistence; the single evidence-based retry passed and the failure was retained in artifacts.
- Validation pass: targeted indicator/MainScreen/task-space tests green; full main TUnit 1060/1060; rebuilt full Headless 41/41; final `src/Unlimotion.sln` build succeeded with 0 errors; final Release evidence build/test green; `git diff --check` clean apart from line-ending notices.
- Findings: Нет незакрытых находок. Existing build warnings (Android native-binary version/16 KB page compatibility and pre-existing analyzers) remain outside this change.
- Manual-review challenge: seam, hidden work, cross-dispatcher media reuse, production delay, z-order and selector/state regressions were challenged by phase comparison, lifecycle assertions, full serial suites and recorded A→B→A flow.
- Unrelated changes: none observed; generated evidence remains under ignored `artifacts/` and `C:\Temp`, while the user-provided draft was read-only.

## Approval

### Уточнение визуальной точности по повторному замечанию пользователя

Предыдущая визуальная оценка отклонена пользователем: открытая S-образная линия и отдельные круглые блики не воспроизводят референс. Текущий EXEC сохраняет утверждённый native renderer, но заменяет геометрию на обведённые по исходному кадру круглые петли с задней диагональю, перекрытием, плоскими косыми срезами и объёмной кромкой. Свечение — несколько тонких непрерывных фиолетовых лент, мягкие ореолы и вытянутые белые вспышки разного размера. Плановый визуальный артефакт: `C:\Temp\unlimotion-animation-analysis\reference-full.png` (исходный кадр 640×360). Приёмка требует сравнения кадров оригинала и новой реализации в одинаковом масштабе; одних UI-тестов для заявления о сходстве недостаточно. Состояния загрузки, тайминг жизненного цикла и интеграции не меняются.

### Review реконструкции по исходному кадру

Следующее уточнение пользователя принято в рамках EXEC: сохранить одобренную геометрию, направить каждый блик от начала левой дуги до конца правой с плавным появлением/исчезновением; распределить световые нити по всей 44-unit ширине передней чёрной дорожки. Проверки этой ограниченной визуальной итерации: фазовые regression tests, UI loading tests, native storyboard/loop и recorded A-B-A. Исторический статус полного общего прогона ниже не заменять утверждением о новом полном green run.

Результат уточнения движения: три неравных блика движутся вдоль пути по возрастающей фазе, разнесённой на треть периода. `SmoothStep` на первых/последних 12% пути обеспечивает нулевую прозрачность и нулевую производную на возврате. Шесть нитей распределены по offsets -18…18 с отклонением до 2 units внутри полуширины 22. Review scope — renderer, regression test, новый light/dark storyboard и фактическая UI-запись. Контракт формы и жизненного цикла сохранён; проверены направление, fade, wrap и заполнение ширины. Indicator tests 4/4, MainScreen 2/2, Release build 0 errors. Первый FlaUI run упал на позднем удалении временного пространства; отдельный повтор на свежем fixture `20260918T120448919Z-3f3667110ee84c42b1a6a626c2ada8e8` прошёл полностью (exit 0, ScenarioSucceeded true). Финальная запись `after-left-to-right-final.mp4` и контактный лист осмотрены; крупный native preview — `left-to-right-preview.mp4` (8 s, 60 fps). Проверка фаз 0/1: PSNR 67.54 dB. Stop decision для этого визуального уточнения: PASS по целевым проверкам; общий suite не перезапускался, его предыдущий incomplete статус сохраняется.

- Scope/evidence: `SeamlessLoadingIndicator.cs`, тест периодичности свечения, исходный кадр и ролик, крупные native Avalonia/Skia renders в светлой/тёмной теме, `reference-comparison.png` и `reference-comparison.mp4` (local-only: `C:\Temp\unlimotion-animation-evidence`).
- Contract/UX: круглые петли, задние ветви, диагональные плоские срезы и переднее перекрытие восстановлены по координатам исходника. Четыре изогнутые световые нити имеют девять уровней мягкого ореола; три неравные вытянутые вспышки пульсируют возле изгибов. Фон прозрачный; интеграционные state/selector/lifecycle контракты сохранены.
- Adversarial/fix/re-review: посегментные широкие штрихи давали видимые узлы и были заменены целыми кривыми с градиентами; ограниченная маска обрезала ореол, её область расширена до всего viewport. Финальные крупные рендеры проверены после исправлений. Начальная и конечная фазы дают PSNR 68.6486 dB; runtime нормализует полный период точно в фазу 0. Небольшая разница растровой проверки связана с floating-point/color quantization, а не скачком контура.
- Developer: геометрия и brushes принадлежат экземпляру; 36 целых световых контуров вместо тысяч коротких draw calls. Новый видеоплеер, bitmap assets и зависимости в продукт не добавлены.
- Validation: финальные Release builds прошли с 0 errors; MainScreen loading tests 2/2 (10 секунд). Первый конкурентный FlaUI run остановился на ошибке exclusive lease возврата в A; отдельный финальный run `20260918T084834333Z-3043975ca0f246f4b62526fc593913ad` прошёл: exit 0, `ScenarioSucceeded=true`, 45 s captured recording `after-reference-traced-final.mp4`, контактный лист осмотрен. Первый full main run встретил timeout cache hydration и был остановлен; медленный повторный serial run остановлен для целевой диагностики (гипотеза зависания renderer отвергнута проходящими MainScreen тестами). Последующий parallel=4 run остановлен после 691 finished test-lifecycle events, без финального success report. Полный green main suite в этой итерации НЕ подтверждён; проверка всей сборки остаётся incomplete, исторические 1060/1060 выше относятся к первоначальной реализации.
- Stop decision: визуальная реализация и native/UI evidence подготовлены; aggregate full-suite validation incomplete. Это не release/merge-ready claim. Role review: UX — близкая реконструкция основных контуров и света с явно указанной границей точности; developer — state/lifecycle untouched и per-instance resources; tester — targeted/UI evidence подтверждено, full-main completion не подтверждено; delivery — локальные артефакты, публикации не было.
- Final targeted evidence: rebuilt Headless UI suite 41/41 (1m 53s); final indicator phase/lifecycle suite 3/3; MainScreen 2/2; recorded FlaUI scenario 1/1. `git diff --check` без ошибок (только существующие notices о CRLF). Эти успешные проверки не подменяют незавершённый full main run.
- Residual visual limitation: native vector воссоздаёт основные контуры и световой рисунок; мелкие 3D-reflections и шум исходного видео не идентичны. Пиксельное тождество с оригиналом не заявляется.

### Кинематографичные исчезающие росчерки

Уточнение пользователя принято в существующий EXEC: вместо шести постоянных нитей оставить три компактных росчерка разной длины и толщины. Удалить постоянные декоративные дуги и пульсирующий фоновый ореол. Каждый росчерк появляется на левой стороне, проходит путь и полностью гаснет справа; вспышка привязана к максимуму его профиля, на той же смещённой дорожке и с общей прозрачностью.

Реализация: lengths 0.32/0.21/0.12 пути, widths 2.6/1.6/0.8 units, offsets -16/1/16. Девять вложенных непрерывных сужающихся контуров дают мягкий свет без узлов от круглых концов отдельных сегментов. Профиль равен нулю за границами росчерка и достигает максимума на 72% его длины; эта координата является якорем блика. Общий SmoothStep fade остаётся нулевым при переносе с правого среза на левый. Одобренная чёрная геометрия и integration/lifecycle не меняются.

Проверены native Avalonia/Skia light/dark renders и восемь фаз storyboard. Начальная/конечная фазы пиксельно совпадают (PSNR infinity). Целевые regression tests 5/5 и MainScreen UI tests 2/2 прошли. Полный suite не перезапускался; прежнее ограничение aggregate validation сохраняется. Артефакты: `C:\Temp\unlimotion-animation-evidence\cinematic-preview.mp4`, `cinematic-sheet.png`. Release build: 0 errors; recorded FlaUI A→B→A run `20260918T183745059Z-402d4afd7ef540c6b10420b842983095` завершился с exit 0, Success true, Captured. Запись `after-cinematic.mp4` и контактный лист `cinematic-ui-sheet.png` проверены. Scope review: PASS; состояние загрузки, selectors, контур и период сохранены, свет полностью исчезает при wrap, flare использует тот же peak/offset/opacity. Ресурсы остаются per-instance. Новые зависимости, публикация и изменения storage отсутствуют.

### Холодный фиолетовый и двенадцать скоростей

Следующее уточнение пользователя продолжает утверждённый EXEC: 12 росчерков вместо 3, каждый со своей длиной, толщиной и скоростью; палитра смещена от розового к сине-фиолетовому. Оттенки ореола 70/35/255…110/75/255, яркое ядро 205/215/255, вспышки холодно-белые. По creator-vibe немного снижена интенсивность ореола, чтобы дополнительные линии не скрывали чёрную форму.

Длины 0.065…0.32 пути, толщины 0.45…2.6, offsets -17…17. Скорости — 9…20 проходов за общий 24-секундный цикл (1.2…2.67 секунды на проход); целое число проходов гарантирует непрерывность общего wrap. Отдельные фазы, fade и привязка блика к пику сохранены. Период увеличен только для общего повторения композиции, не для длительности отдельного прохода. Добавлен тест 12 разных размеров и измеряемых скоростей, проверки periodicity/wrap расширены на все 12 линий. Indicator 6/6, MainScreen UI 2/2; полный suite не перезапускался. Native preview: `C:\Temp\unlimotion-animation-evidence\blue-twelve-preview.mp4` (24 s, 30 fps), светлый storyboard и тёмный render осмотрены. Форма, интеграционные состояния и selectors не менялись.

Review холодной палитры и 12 линий: PASS по ограниченному scope. Native seam PSNR infinity. Release build 0 errors; FlaUI A→B→A `20260918T193059858Z-1d375eb18f1f4fdfad5ec374d4d8dcc9`: exit 0, Success true, Captured. Запись `after-blue-twelve.mp4` (45 s, 30 fps) и её контактный лист осмотрены. Предыдущее сравнимое evidence — `after-cinematic.mp4`. Тесты проверяют реальные различия производной позиции всех 12 линий, размеры, fade/wrap, общий цикл и UI loading lifecycle. Scope не включает полный regression suite, публикацию или release.

### Нерегулярные запуски без синхронного ряда

Уточнение пользователя: линии периодически выстраиваются в ряд. В рамках EXEC равномерные независимые часы заменены детерминированным seeded расписанием для каждой из 12 линий. Интервалы варьируются до нормализации (0.65…1.35), проход занимает 82…94% интервала, остаток — полностью тёмная пауза. Начальное смещение каждой линии также псевдослучайное. Расписание рассчитывается один раз, повторяется через 24 секунды; во время прохода скорость постоянна, границы остаются плавными и невидимыми. Случайные краткие сближения возможны; регулярная синхронизация запусков убрана. Палитра, геометрия и размерные параметры не изменены.

Проверки ограничены изменённым расписанием: 7/7 indicator tests, включая различные интервалы и тёмные паузы всех проходов, скорость, fade/wrap и periodicity; MainScreen UI 2/2. Полный suite и повторная FlaUI запись в этой итерации не запускались. Native 24-second preview строится тем же renderer; итоговый артефакт `C:\Temp\unlimotion-animation-evidence\random-launches-preview.mp4`.

Подтверждено пользователем точной фразой `Спеку подтверждаю` 2026-09-17.

## 20. Журнал действий агента

| Phase | Block | Confidence | Missing | Next | Human handoff | Actual human contact | Rationale | Artifacts |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Instructions/memory preflight | 0.98 | None | Inspect draft/repo | No | No | Applied central QUEST/UI stack and historical loading caution | Read-only docs |
| SPEC | Draft analysis | 0.95 | Actual in-app render | Inspect UI | No | No | Draft is reference, not shippable asset | MP4/temp PNGs |
| SPEC | AS-IS/test/harness inspection | 0.96 | After-render | Select design | No | No | Found two indicators and reusable recorder flow | Read-only source/tests |
| SPEC | Design | 0.92 | Fine tuning | Review spec | No | No | Native vector dominates raster alternatives | This spec |
| SPEC | Full post-SPEC review | 0.94 | Linter granularity | Fix audit | No | No | Review found grouped instead of per-criterion linter evidence | This spec |
| SPEC | Fix and re-review | 0.96 | Exact approval | Request approval | Yes | Yes, in final response | Expanded linter to 20 checks and repeated scope/whitespace gates | This spec |
| EXEC | Approval received | 1.00 | None | Capture baseline and run toolchain preflight | No | Yes: user wrote `Спеку подтверждаю` | Exact QUEST transition granted; implementation remains inside approved scope | This spec |
| EXEC | Baseline evidence | 0.98 | New render not implemented | Add expected-red UI contract | No | No | Baseline A→B→A passed and recorded 45 s at 1002×540; current progress bar is visible in sampled frames | Before MP4, manifest, screenshots |
| EXEC | Expected red | 0.99 | None | Implement shared indicator | No | No | Targeted UI test failed for the intended reason: expected `SeamlessLoadingIndicator`, received `Grid` | MainScreen loading UI test log |
| EXEC | Shared indicator implementation | 0.96 | Perceptual video gate | Extend evidence hold and record after-state | No | No | Both loading surfaces now share a cached native vector renderer with a 1600 ms modulo phase and visible/attached frame scheduling | Control, XAML, code-behind |
| EXEC | Targeted green | 0.98 | Full suites | Capture after-video | No | No | Indicator phase/lifecycle tests (3), MainScreen loading UI tests (2), and task-space overlay test (1) passed | TUnit output |
| EXEC | First after-evidence audit | 0.99 | Two-cycle visible hold | Correct desktop injection point | No | No | Video exposed that the delay had been added to the headless setup rather than the production desktop switch path; no false visual claim retained | Rejected after MP4/contact sheet |
| EXEC | Full-suite defect discovery | 0.99 | Revalidation | Move drawing resources to instance cache | No | No | Serial main suite exposed Avalonia thread ownership of static pens across Headless sessions; run stopped and the cache boundary was corrected | Full TUnit failure trace |
| EXEC | Full validation | 0.99 | Final visual exact-build check | Rebuild evidence and audit diff | No | No | Main 1060/1060, rebuilt Headless 41/41 and full solution build completed with zero errors | Test reports, build output |
| EXEC | Final evidence | 0.99 | None | Post-EXEC review | No | No | Final exact-code A→B→A scenario exited 0 and recorded 45 s; two-cycle contact sheet and 1600 ms frame comparison inspected | After MP4, manifest, screenshots, PSNR |
| EXEC | Post-EXEC review | 0.99 | None | Hand off local result | Yes | No | Scope, contracts, adversarial failures, role concerns, warnings and delivery boundary were rechecked; no open finding remains | This spec, final diff/status |
| EXEC | User visual refinement | 0.99 | None | Hand off refreshed visual evidence | Yes | Yes: requested explicit `u+n` silhouette and multiple differently sized glints | Rebuilt the black mark as two open letter paths and spread five graded glints across the moving trail; targeted tests, Release evidence flow and enlarged-frame inspection passed | `after-un-letters.mp4`, contact sheet, close-up, FlaUI manifest |
| EXEC | Reference-traced reconstruction | 0.95 | Full main suite completion | Hand off visual comparison and bounded validation status | Yes | Yes: user rejected previous similarity | Traced circular bowls and rear crossing from source coordinates; replaced particles by curved filaments and unequal flares; inspected same-scale comparison, dark render and exact-code passing A-B-A recording. MainScreen 2/2; full-main runs not completed and not claimed green | `reference-comparison.mp4`, `after-reference-traced-final.mp4`, final manifest, phase seam PSNR |
| EXEC | Left-to-right flares and full-width light | 0.99 | No targeted gap; historical full-suite limitation remains | Hand off loop | Yes | Yes: user approved shape and requested two motion refinements | Added directional wrap/fade regression; six distributed filaments; 6 targeted tests, Release build and final recorded A-B-A passed after one retained late-removal failure | `left-to-right-preview.mp4`, `after-left-to-right-final.mp4`, manifest |
