# Восстановление и Git-история независимых карточек

## 0. Метаданные

- Expanded SPEC, medium / multi-module; фаза SPEC, код не изменяется.
- Владелец: автор workspace; ветка `feat/daily-feed`.
- База: `cd29e734` плюс пять ранее показанных файлов; актуальный `origin/main`: `dce4e196` (проверен 07.10.2026).
- Профили: dotnet-desktop-client, ui-automation-testing, ui-feature-parity; context testing-dotnet.
- Central stack: routing-matrix, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance/mode, testing-baseline, spec-linter/rubric, review-loops. Canonical expanded template: центральный `templates/specs/_template.md`.
- Поверхность: Codex / Windows PowerShell; effective model ID не доступен через используемые инструменты. Модельная миграция/eval не применимы: изменяется Avalonia UI.
- Связь: `2026-10-02-workspace-independent-documents.md`, `2026-10-02-task-card-status-recovery.md`, `2026-09-25-task-git-history.md`.

## 1. Overview / Цель

Поручение: «Перенести» восстановление через Reload и Git-историю из уже существующей карточки main в независимые карточки workspace. Результат: эти возможности доступны в каждой карточке и работают с её задачей, даже когда активна соседняя панель.

Success means: пользователь открывает A и B рядом, восстанавливает A и читает её Git-историю без изменения B; сохранение поздних правок и защита от воскрешения удалённого файла не нарушены. Output: реализация, актуальные UI/VM проверки, просмотренные PNG и демонстрация. Stop: только после обязательных AC и post-EXEC review; неизвестная причина падения save guard не объявляется исправленной по одному повтору.

## 2. AS-IS

`MainControl.axaml` содержит `CurrentTaskReloadButton` и `TaskGitHistoryPanel`; `TaskCardView.axaml` содержит StatusHistory, но не эти возможности. `TaskCardDocumentViewModel` владеет Task и relation roots, не Git-панелью. `TaskItemViewModel` уже имеет ReloadTaskCommand, CanReloadTask, IsMissingFromStorage и persistence guards. `TaskPresentationControl.Dispose` освобождает CardContext. Legacy/global selection нельзя использовать как владельца действий A.

Пять ранее принятых локальных emoji/workspace файлов сохраняются отдельно от нового diff. Старые полные прогоны до ребейза не доказывают готовность нового кандидата.

## 3. Проблема

Новая компоновка выделила карточку, но не перенесла все возможности main и их lifecycle-контракты.

## 4. Цели дизайна

Переиспользовать существующие storage/recovery/history API и локализацию; менять только владельца UI-состояния. Не выполнять файловые/Git запросы синхронно на UI-потоке. Сохранить стабильные AutomationId; различать одинаковые ID через scope карточки.

## 5. Non-Goals

Новый UX detached-черновика при external Missing, редизайн меню/докинга, изменение формата задач, CLI observation/preview, AreaIds null/clone cleanup, установка и работа с боевой базой не входят. Git history, StatusHistory и история переходов не объединяются. Уже данное условное разрешение merge сохраняется, но не заменяет проверки готовности.

## 6. TO-BE

### 6.1 Ответственности

TaskCardDocumentViewModel: свой task scope. TaskCardView: меню, error state, собственный TaskHistoryPaneViewModel/lifetime и history UI, как у существующей MainControl. History VM/provider находятся в UI assembly: не вводить обратную зависимость ViewModel → UI. TaskItemViewModel: существующие Reload и сохранение, только необходимые исправления совместимости guards. Workspace navigation: отсутствие преждевременного успешного перехода при late edits. Legacy MainControl сохраняет свои возможности.

### 6.2 Дизайн и визуальный план

```
Панель A                              Панель B
Заголовок / статус / меню ⋯            Заголовок / статус / меню ⋯
  меню: Перечитать задачу [первый]      собственные команды B
Ошибка операции A [при наличии]        ошибка B [при наличии]
Прокручиваемое тело A                  прокручиваемое тело B
Git-история A [раскрываемая секция]     Git-история B [своя секция]
```

Git-секция вне body-scroll, ограничена доступной высотой: её содержимое прокручивается, тело задачи остаётся доступным. На узком окне нет горизонтального наложения или исчезновения меню/error. Внешний вид, подписи, пустое/error/non-Git состояние соответствуют существующей реализации main, без новой дизайн-системы.

Reload command, доступность и ошибка берутся из CardContext.Task. Git lookup использует SourceId именно этой задачи. Каждая карточка создаёт собственные TaskHistoryPaneViewModel и GitTaskHistoryProvider: provider stateful, его session/cursor/reset нельзя разделять с соседней карточкой. Переиспользуется реализация, не экземпляр. Закрытие/замена документа и смена пространства отменяют запрос и делают поздний результат неприменимым по owner/version. Несохранённые поля не заменяются ответом history provider.

Video evidence: новый автоматизированный native A/B flow до/после при доступном интерактивном desktop. При недоступности desktop фиксируются точная причина и fallback: rendered Headless PNG до/после плюс assertions реального меню/переходов. PNG инспектируется; он не объявляется native/video доказательством. Генерируемые артефакты не коммитятся по умолчанию.

### 6.3 User-Observable Scenarios

| Сценарий | Действие | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Recovery A/B | Открыть A/B, изменить B, выбрать первый пункт меню Reload A | Данные/ошибка A обновлены, B и её черновик/scroll/history прежние | Scoped pointer UI test + PNG | 1,2 |
| Git A/B | Раскрыть Git A, выбрать revision, переключиться на B | История относится к A, B не переключается и не теряет правки | UI test + isolated Git fixture | 3 |
| Поздний запрос | Закрыть A или сменить scope до завершения Git запроса | Ответ не попадает в B/новое пространство | Controlled async UI test | 4 |
| Concurrent recovery | Reload/flush в обоих порядках, edit во время ожидания | Нет зависания и потери late edit; ошибка удерживает переход | VM + actual UI fault fixture | 5,6 |
| Missing/read-back | Удалить fixture файл извне / неизвестный результат записи | Нет записи/воскрешения; failure сообщается, переход не успешен | Storage spy + VM/UI tests | 7 |
| Journal | Перечитать A с существующим журналом | UI восстанавливает A, строгая CLI проверка ничего не записывает | Actual Reload pointer + bytes/mtime/посторонний receipt | 8 |

### 6.4 State / Interaction Matrix

| Состояние | Trigger | Результат |
| --- | --- | --- |
| Loaded/Failed | Reload | Существующий recovery main, собственная ошибка A; late edits сохранены |
| Active flush | Reload | Новая операция недоступна; уже принятый Reload не образует цикл ожидания |
| Reload принят | flush/Seal | Завершение обеих операций без deadlock, pending producers учтены |
| Missing/disposed | Любая ожидающая запись | Запись запрещена, flush явно отклонён, нет dirty spin |
| OutcomeUnknown | Flush | Явный failure без Update; последующий явный Reload разрешает guarded writes лишь после успешного authoritative read-back, без нового автоматического recovery pipeline |
| Git Loading | Close/scope change | Cancel/invalidate; поздний ответ игнорируется |
| Non-Git/empty/error | Открыть историю | Существующее явное состояние main, не чужая история |

### 6.5 Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Перенос возможностей main | user | Прямое «Перенести»; exact SPEC gate ещё нужен | 1.0 | Нельзя считать это разрешением нового Missing UX | Нет, кроме approval |
| Владелец действий | agent | CardContext.Task и отдельный history lifetime | 1.0 | Глобальная selection повредит B | Нет |
| External Missing UX | agent | Не вводить detached UI; сохранить нынешнее поведение workspace | 0.9 | Не смешивать защиту storage и новый UX | Нет |
| Guards | agent | Ошибка/отклонённый переход, без spin/записи; без автоповтора failed write | 0.9 | Потеря late edits | Нет |

### 6.6 Runtime / Config / Data Contract

JSON/schema и provider API неизменны, миграции нет. Source of truth: TaskItemViewModel, текущий Git provider, task storage и journal main. UI Reload сохраняет его recovery-контракт; CLI strict observation остаётся read-only. Fixtures изолированы от пользовательских Tasks и настроек.

## 7. Инварианты

Перед каждой записью после acquisition gate повторно проверяются Missing/disposed/read-back. Failed flush не меняет route/history и не сбрасывает draft. Проверяется весь guarded set до стабильного окончания, включая A, изменённую пока сохранялась B. Не создавать параллельный persistence pipeline.

## 8. Точки интеграции

Конструктор/Dispose TaskCardView и CardContext, собственный SourceId, bindings menu/error/history, navigation save guard, flush/Reload/Seal completion. Закрытие визуальной карточки освобождает только её history/subscriptions и не должно неожиданно закрывать жизнь общей TaskItemViewModel соседних представлений.

## 9. Данные / состояние

Новые поля только неперсистентного history/lifecycle состояния карточки при необходимости. Сохраняются все dirty-поля задачи, включая IsGoal/AreaIds. Формат и трактовка этих полей не меняются.

## 10. Миграция / Rollback

Не требуется миграция. Прежде EXEC зафиксировать/сохранить принятые пять файлов отдельно; изменения этой спеки отдельным commit. Откат только собственного change set, без reset чужих файлов; пользовательские данные не меняются тестами. Merge после свежей проверки кандидата/PR CI, не старого remote head.

## 11. AC и тестирование

| AC | Проверяемый результат | Test / evidence |
| --- | --- | --- |
| 1 | Reload первым пунктом меню каждой реальной карточки; command/error принадлежат ей | Новые WorkspaceCardRecoveryHistoryUiTests: actual scoped pointer click |
| 2 | Данные, dirty revision, route/history/index, selection и ненулевой scroll B неизменны после A Reload Loaded/Failed | A/B UI tests с delayed/fault storage |
| 3 | Git revision/details соответствуют A/SourceId; StatusHistory/navigation history независимы; после A→B→A LoadMore продолжает курсор A | UI tests + GitTaskHistoryProviderTests с двумя собственными provider instances |
| 4 | Closed/replaced/scope-changed owner не принимает поздний Git ответ; B не меняется; закрытие B не сбрасывает pending/page/cursor A | Controlled provider tests + UI |
| 5 | Reload→flush, flush→Reload, Reload/flush→Seal завершаются без взаимного ожидания | TaskItemViewModelStatusCommandTests deterministic barriers |
| 6 | Late edits A пока сохраняется B не пропущены; failure сохраняет draft/route | WorkspaceTaskSaveGuardUiTests; исходный ScopeCommit case с выясненной причиной прежнего failure |
| 7 | Missing/disposal/read-back после gate исключают Update и resurrection; flush не крутится с dirty | Storage-spy barrier tests + UI failure state |
| 8 | Strict observation не меняет bytes/mtime; UI journal Reload работает в own-card scope, посторонний receipt неизменен; обработка собственного journal соответствует main (включая его удаление после восстановления) | Изолированная journal fixture и реальный UI click |
| 9 | Header/menu/error/history читаемы в wide/narrow; тело и Git доступны прокруткой | Просмотренные rendered PNG до/после, native video или описанный fallback |
| 10 | Объединённый кандидат собирается обычным способом; текущие mandatory suites/CI успешны | Debug solution / Release Desktop; Main, rendered Headless, native matrix исходной workspace SPEC; точный PR head CI |

Команды (новые имена тестов плановые, не выдаются за выполненные):

```powershell
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --treenode-filter '/*/*/WorkspaceCardRecoveryHistoryUiTests/*' --maximum-parallel-tests 1 --report-trx
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --treenode-filter '/*/*/WorkspaceTaskSaveGuardUiTests/*' --maximum-parallel-tests 1 --report-trx
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --treenode-filter '/*/*/TaskItemViewModelStatusCommandTests/*' --maximum-parallel-tests 1 --report-trx
dotnet build src/Unlimotion.sln -c Debug
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release
```

Перед полным/native прогоном резервировать общий слот и проверять процессы; не вмешиваться в установленное приложение пользователя. Полные команды/проекты и evidence фиксируются при EXEC по фактической структуре. Нет PASS при отсутствующем обязательном runtime; недоступный native desktop — явный blocker соответствующего gate, а не замена Headless. Успешный run повторять лишь после изменения, failure или конкретного незакрытого риска.

## 12. Риски / Expected User Review Objections

| Возражение | Mitigation | Статус |
| --- | --- | --- |
| «Reload всё равно меняет активную B» | Две реальные карточки, own task binding и проверки всех состояний B | mitigated design |
| «Git пропал или недоступен снизу» | Секция вне body-scroll, ограниченная высота/внутренняя прокрутка, narrow PNG | mitigated design |
| «Восстановление потеряло несохранённый текст» | Late-edit барьеры, error/retry, no auto-retry failed write | mitigated design |
| «Ради переноса ввели новый UX удаления» | Detached draft не входит; Missing storage guard отдельно от UI | mitigated scope |

Rework prevention: сценарии, решения, AC/evidence и роли заполнены. Риск общих VM/subscriptions проверяется на close и замене route. Возможный starvation от бесконечного ввода не маскируется таймером успешного сохранения.

## 13. План

Approval → сохранение принятого base → перенести scoped menu/error и history lifetime → adversarial guards/UI tests → адресные проверки/новый показ → review → текущая полная матрица и PR CI → merge по сохранённому прямому разрешению при готовности.

## 14. Открытые вопросы

Нет блокирующих продуктовых решений в ограниченном переносе. Новое external Missing/detached решение исключено, не считается принятым.

## 15. Соответствие профилю

UI parity P0: отсутствующие menu/history; P0 safety: own scope/guards. Avalonia UI tests обязательны, AppAutomation 1.9 и существующий runner сохраняются; повышение версии не требуется этой задачей. UI IO async, AutomationId scoped. Central review/gate не заменены skill.

## 16. Планируемые файлы

TaskCardView.axaml/.cs, TaskCardDocumentViewModel.cs, TaskPresentationControl.cs (при необходимости lifecycle); TaskItemViewModel.cs / WorkspaceNavigationViewModel.cs только необходимые guards; существующий history VM/provider по результатам discovery; новые scoped UI tests и существующие status/save-guard tests; AppAutomation Authoring/Headless/FlaUI сценарий. Чужие CLI и emoji изменения не перерабатывать без причины.

## 17. Было → стало

Legacy-only Reload/history → возможности каждой карточки; global selection owner → CardContext.Task; старые pre-rebase результаты → свежий совместимый кандидат с own-card evidence.

## 18. Альтернативы

Оставить legacy карточку — сохраняет функции, но нарушает согласованную компоновку. Скопировать глобальные bindings — проще, но ломает A/B. Выбран перенос существующих механизмов с локальным владельцем, не новая реализация storage/Git.

## 19. Quality gate / Review

### SPEC Linter

| № | Статус | Основание |
| --- | --- | --- |
| 1 | PASS | §1 наблюдаемый перенос A/B |
| 2 | PASS | §2 исходные Views/VM просмотрены |
| 3 | PASS | §3 потеря parity после выделения карточки |
| 4 | PASS | §4 scope, reuse, async |
| 5 | PASS | §5 detached/CLI/schema исключены |
| 6 | PASS | §6.1 own UI provider без обратной assembly зависимости |
| 7 | PASS | §8 lifecycle/save/navigation |
| 8 | PASS | §7 guarded set/late edits |
| 9 | PASS | §6.4 explicit Reload/failure/no resurrection |
| 10 | PASS | Async IO, bounded UI/history scroll, paging; performance benchmark не нужен без заявления ускорения |
| 11 | PASS | §9 ephemeral state, schema unchanged |
| 12 | PASS | §10 migration не нужна |
| 13 | PASS | §10 own change set rollback |
| 14 | PASS | §11 десять измеримых AC |
| 15 | PASS | UI/VM barriers, adverse lifecycle и AC→evidence |
| 16 | PASS | §11 конкретные commands, serial slot и stop |
| 17 | PASS | §13 approval→targeted→full/CI |
| 18 | PASS | §6.5/14 нет нерешённого нового Missing UX |
| 19 | PASS | §0 multi-module Expanded |
| 20 | PASS | §15 UI parity/testing и central phase gate |

Итог: ГОТОВО к запросу approval, не к merge.

### SPEC Rubric

Цель/границы 5 (§1/5); AS-IS 5 (§2 и просмотренный source); дизайн 5 (§6/8); безопасность 5 (§7/9/10); проверяемость 5 (AC1–10, runtime план не выдаётся за PASS); автономность 5 (§13/14). Итого 30/30 для SPEC. Баллы не заменяют approval/EXEC evidence.

### Post-SPEC Review / Role-Based Result

Scope/Evidence: эта SPEC; canonical expanded template, quest-mode/governance, linter/rubric, review-loops, desktop/UI-parity/UI-automation profiles; MainControl.axaml/.cs, TaskCardView.axaml/.cs, TaskCardDocumentViewModel.cs, TaskPresentationControl.cs, TaskItemViewModel guards, TaskHistoryModels.cs и GitTaskHistoryProvider session/reset; существующий CliStatusJournalReloadUiTests fixture проверен reviewer. Source/tree commands read-only; runtime не запускался.

Contract: §5/6.5 исключают новый detached Missing UX/CLI/schema; AC1–10 покрывают поручение и необходимую совместимость. Сохранённое merge разрешение не объявлено одобрением ещё не показанной реализации.

Adversarial: рассмотрены A→B→A paging, dispose B при pending A, close/scope late result, shared VM lifetime, editor A меняется при сохранении B, OutcomeUnknown не превращается в автоматический recovery. Fixed and re-review: исправлены UI assembly dependency, solution path и три находки ниже; повторный source/spec review без тестов.

| Role | Verdict | Основание |
| --- | --- | --- |
| Business analyst | PASS | Recovery собственной задачи, сохранение черновика и переходов |
| UX / designer | PASS | Wireframe/menu/error, bounded history, wide/narrow acceptance и исключённый новый Missing UX |
| Tester | PASS | AC→flow/negative evidence; actual clicks, nonzero scroll, barriers, full gates |
| Developer / architect | PASS | Own provider/session; ViewModel не зависит от UI; reuse guards |
| Delivery / operations | PASS | Isolated fixtures, сохранение base, CI exact head и conditional merge; нет установки |

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Shared Git session | Свой history VM не исключал общий stateful provider | Отдельный provider и paging/dispose tests | fixed/re-reviewed |
| MEDIUM | OutcomeUnknown | Формулировка могла означать автоматический read-back в Flush | Explicit failure + явный Reload | fixed/re-reviewed |
| LOW | Journal evidence | Непонятно, какой receipt не меняется | Unrelated receipt; собственный journal обрабатывается как main | fixed/re-reviewed |

Depth checklist: unrelated пять файлов сохранены; AC проверяемы; runtime evidence ещё отсутствует и не заявлен; ссылки/solution path сверены; regression scenarios §6.3/6.4; docs only на SPEC; скрытые UI/storage изменения ограничены §5. Manual-review challenge: не потерялись ли LoadMore/cursor после B dispose и late edits после Reload — явно проверяются AC3–7. No-findings justification после исправлений: перечисленные контрпримеры имеют AC/evidence и однозначный ограниченный контракт, открытых продуктовых вопросов нет.

Reviewer `card_recovery_spec_review` выполнял только чтение. Фактический child sandbox danger-full-access, поэтому это процедурный внешний review, не технически изолированный read-only review. Fallback: отдельный root adversarial/source pass с указанными контрпримерами; риск отсутствия sandbox isolation явно сохранён. Stop decision: PASS для запроса approval после final re-review, не разрешение EXEC. Post-EXEC: не выполнен; tests/videos/PNG/CI пока только план.

## Approval

Ожидается «Спеку подтверждаю». «Перенести» фиксирует выбранный scope, но не подменяет exact gate.

## 20. Журнал действий агента

| Фаза | Решение | Evidence / остаток | Следующее действие | Решение пользователя | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC 07.10.2026 | Подготовлен ограниченный перенос main возможностей, detached UX исключён | Source просмотрен, код не менялся; review pending | Post-SPEC review, approval | «Перенести» | Эта SPEC |
| Post-SPEC review | Own provider, explicit Reload и journal evidence уточнены, audit заполнен | Процедурный reviewer + отдельный adversarial fallback, без runtime | Exact approval | Ещё не получено | §19 |
| EXEC 07.10.2026 | Exact approval получен; начинаем ограниченный перенос | Сохраняем принятый base отдельно от переноса; runtime нового scope ещё не запускался | Реализация и scoped tests | «Спеку подтверждаю» | Эта SPEC |
| EXEC перенос UI | Принятый пятифайловый base сохранён commit `5db0b05a`; Reload/error/history перенесены, provider и subscriptions собственные; common styles сохранены для legacy | Journal UI 4/4 (legacy/standalone), первый scoped 2/3: неверное ожидание неизменности pending flag при штатном autosave; вместо него проверяются текст и actual editable revision. Следующий scoped snapshot 6/6, latest10 ещё идёт | Late-details/guard tests, source review, затем актуальная полная матрица | Существующее exact approval | `card-recovery-journal.log`, `card-recovery-scoped-fixed.log`, `card-recovery-scoped-current.log` |
| EXEC lifecycle review | Reviewer MEDIUM common styles/lifecycle/details coverage исправлены. ScopeRevision ранее не уведомлял подписчиков: added notification + cancellation; actual details/paging clicks и route/scope/detach variants добавлены | Viewed rendered A/B recovery, wide details и narrow PNG: Skia контент, не native. Post-EXEC PASS/merge ещё не заявлены; прежний ScopeCommit cause открыт | Дождаться текущих tests, закрыть остаток AC | Новых решений не требуется | `TaskHistoryStyles.axaml`, `WorkspaceCardRecoveryHistoryUiTests.cs` |
