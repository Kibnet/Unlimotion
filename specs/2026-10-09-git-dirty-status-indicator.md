# Компактный индикатор Git с видимыми незакоммиченными изменениями

## 0. Метаданные
- Задача Unlimotion: cdcddb1a-dc3c-45af-8999-885056a237b4, Prepared.
- Полный контекст: задача → 232a9bf4-fee5-434c-b93c-ac0e53a2a557 «Фичи» → a5ef0164-8def-417b-9b7d-d235516dd8c7 «♾️Unlimotion». Прочитан CLI snapshot 09.10; пропущенных родителей нет.
- Профили: dotnet-desktop-client + ui-automation-testing; локальный AGENTS.override.md.
- Форма Expanded, масштаб medium: асинхронное состояние, Git lock, смена task-space, публичное UI-поведение; short неприменима.
- Фаза EXEC после exact approval 09.10.2026. Историческая база SPEC: main e987023dc93daba6753440eec813edcbaa468da9, рабочее дерево до этой SPEC чистое.
- Поверхность Codex; фактический model ID/reasoning из доверенных runtime metadata не доступен, не угадывается. Модельный eval неприменим: меняется обычная .NET/Avalonia функция.
- Источник template: C:/Users/Kibnet/.codex/agents/templates/specs/_template.md.
- Evidence: C:/Obsidian/kibnet/Unlimotion/Ночной агент/Материалы/cdcddb1a-dc3c-45af-8999-885056a237b4/20261009T040028-unlimotion2/git-fixture-results.json.
- Никаких push/merge/install/release и записей пользовательских задач этим scope.

## 1. Overview / Цель
Исходное поручение: «Я хочу чтобы ещё было видно что есть незакоммиченные изменения». Постоянная маленькая пиктограмма в правом верхнем углу должна отражать Git и текущую синхронизацию без открытия настроек.
Success: после сохранения задачи пользователь видит наличие незакоммиченных файлов; после обычной разрешённой синхронизации видит новый измеренный статус, а при ошибке не теряет Dirty.
Output: локальная реализация, unit/integration/UI evidence, русский tooltip/details. Само наблюдение не запускает синхронизацию.
Stop: до exact approval менять только эту SPEC; при source drift/непредусмотренной мутации остановить зависимое действие и сохранить evidence.

## 2. AS-IS
Прочитаны IRemoteBackupService.cs и SettingsUiState.cs. BackupStatusState отражает подключение/операцию, но не рабочее дерево. MainWindow.axaml содержит MainScreen; интеграция индикатора относится к MainScreen, не к самостоятельной панели Settings.
BackupViaGitService.GetConflictStatus(): missing repo и exception возвращают None. Этот метод не является oracle чистоты.
Push() использует RetrieveStatus.IsDirty, Stage и Commit под LockObject — запрещён как путь refresh.
GetSettings()/BeginTaskSpaceOperation() закрепляют task-space sourceId/repositoryPath через AsyncLocal. Наблюдение нельзя переносить между пространствами.
10 изолированных native Git fixtures PASS: clean; modified; untracked; deleted; staged; MM; no upstream; ahead; divergence; conflict. Они не являются тестами будущего LibGit2Sharp adapter или Avalonia UI.

## 3. Проблема
Состояние подключения ошибочно воспринимается как отсутствие незакоммиченных данных; текущий сервис не выдаёт достоверное отдельное наблюдение рабочего дерева.

## 4. Цели дизайна
Наблюдение отдельно от операций и отображения; отсутствие network side effects; source ownership; неизменность файлов; честное unknown/stale; доступность без зависимости только от цвета.

## 5. Non-Goals
Не менять алгоритмы commit/pull/push, конфликторазрешение, credentials, Task JSON, автосинхронизацию, Git history, граф задач или settings migration. Не добавлять кнопку скрытого push при клике по индикатору. Не считать accepted/merged признаком текущей чистоты.

## 6. TO-BE
### 6.1 Ответственности
- Новый read-only observer рядом с BackupViaGitService: один immutable result, LibGit2Sharp, тот же кооперативный Git lock; без stage/commit/fetch.
- Отдельный coordinator во ViewModel: подписки refresh, single-flight/debounce, source generation, cancellation/dispose.
- Отдельное derived presentation state в VM; существующий BackupStatusState остаётся совместимым.
- MainControl header (Row0, existing ColumnDefinitions="*,Auto", right StackPanel): focusable indicator + details flyout; локализация и automation IDs. Не overlay поверх MainScreen/toasts.

### 6.2 Контракт и visual planning
GitWorktreeObservation: sourceId, repositoryPath, generation, observedAt, observationKind (Known/Unknown/NotApplicable), dirtyFileCount?, hasConflicts?, ahead?, behind?, errorCode?, safeErrorText?.
Unknown не имеет выдуманных нулей. NotApplicable для server/non-file space, а не ошибка Git.
Состояние операции хранится отдельно: operationBusy, lastOperationError; изменение этой функции не меняет существующие commands.
Idle разрешён лишь при **fresh** Known, dirty=0, no conflict, no operation error, no active operation, no read-error и no stale. При failed read последний Known сохраняется только как исторический sample: прежний clean отображается Unknown/stale, не Idle; прежний Dirty сохраняет видимый Dirty-признак плюс stale/error overlay и время последнего достоверного измерения. Ahead/behind nullable; commit-only divergence видимо отдельным символом/tooltip, не Dirty.
Presentation priority: NotApplicable → confirmed conflict/terminal operation error Attention → active operation Syncing → Unknown (без valid sample) → Dirty → Idle. Ошибка/timeout read во время Syncing — overlay «данные устарели/обновление недоступно», не завершение sync и не новая operation failure. Последний Known sample, включая Dirty, остаётся в details. Confirmed конфликт не исчезает при retry. При начале нового operation прежняя terminal error переносится в историю details, active error очищается; новый failure снова Attention. Успешное terminal outcome очищает active operation error, но не read error/stale, которые очищает только fresh successful read. Source switch очищает sample/error/history текущего отображения. Cancellation не называется success.
Пиктограмма 28–32 DIP, hit target минимум 32 DIP, счётчик файлов допускается compact badge. Никаких пульсирующих warning toast от таймера.
Wireframe (план, не rendered proof):
```text
[Основное содержимое / tabs .....................] [✎ 4]
                                                    └ tooltip / Enter:
  Git: есть незакоммиченные изменения
  Файлов: 4          Commits впереди: 1 / позади: —
  Конфликтов: нет    Проверено: 04:02
  [Открыть настройки Git]   (существующий безопасный переход)
```
В Attention details всё ещё показывают «Файлов: 4». Unknown: «Состояние Git неизвестно»; — означает неизвестно, не ноль. Server space: «Git не применяется».
В narrow layout цифра может перейти в flyout, но наличие Dirty остаётся отдельным видимым признаком. Keyboard Enter/Space открывает details, Escape закрывает; фокус возвращается.
Video: после EXEC passing isolated UI flow; baseline — meaningful Settings connected/dirty state, отдельного индикатора до изменения нет. Если безопасный window recorder невозможен, точная причина + Headless PNG/trace; отсутствие возможности не выдаётся за native acceptance.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| S1 | Сохранить задачу | Постоянный Dirty с числом файлов, без настроек | UI run + PNG/video | 1 |
| S2 | Обычный sync | Syncing, затем fresh observed состояние | controlled repo flow | 2 |
| S3 | Sync error/conflict при Dirty | Attention; число файлов не исчезает | injected error/conflict UI | 3 |
| S4 | Missing repo / unknown / устаревание | Не Idle и не фиктивные 0 | UI negative matrix | 4 |
| S5 | Перейти A→B, поздний ответ A | В B не показывается статус A | deterministic coordinator + UI | 5 |
| S6 | Узкое окно и клавиатура | Indicator/details доступны, ничего не перекрыто | 800×600 + keyboard | 6 |

### 6.4 State / Interaction Matrix
| Before | Trigger | After | Negative/concurrent |
| --- | --- | --- | --- |
| Unknown | successful read | Known | Error remains Unknown, no toast loop |
| Known | task saved / external edit | coalesced refresh | During refresh old state marked pending |
| Dirty | sync starts | Syncing + retained dirty data | no extra operation |
| Syncing | read timeout while operation active | Syncing + stale overlay + retained Dirty | timeout не заменяет primary state на Unknown |
| Attention(error) | retry begins | Syncing + previous error in details | confirmed conflict остаётся Attention |
| Syncing | structured success/failure/cancel | refresh + operation result | normal return не доказывает success |
| Any A | switch to B | clear display→Unknown B | reject A generation |
| Known | >30 seconds since observation | stale label | delayed worker never freezes UI |
| Any | non-file space | NotApplicable | do not open Git repository |

### 6.5 Decision Ledger
| Decision | Owner | Chosen | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Show Dirty / separate files and commits | user, 22.09 | preserve exact request | 1 | none | Нет |
| No hidden fetch | user boundary | local refs only | 1 | remote freshness unknown | Нет |
| Read shared lock off UI | agent | async observer/coordinator | .95 | contention | Нет |
| Debounce 300ms; fallback refresh 15s visible only; stale 30s | agent | initial reversible constants | .85 | resource/latency | Нет |
| Click details, no sync | agent | safest diagnostic interaction | .95 | none | Нет |
| Implement this specific SPEC | user phase gate | not approved | 1 | unauthorized changes | Нет отдельной неоднозначности; exact approval gate в Approval |

### 6.6 Runtime / Config / Data Matrix
| Area | Source | Change | Compatibility | Check |
| --- | --- | --- | --- | --- |
| Git bytes/index/refs | active repo | read only | no migration | pre/post hash/index refs |
| Task-space | immutable current source/path capture | carry source/path/generation, НЕ live lease | no expired context reuse | delayed A→B |
| Settings state | SettingsViewModel | consume, not replace enum | existing commands preserved | regression tests |
| Ahead/behind | local upstream refs | nullable | no fetch | missing upstream/divergence |
| UI lifetime | MainScreen/VM | own subscriptions | dispose on replacement | leak/cancellation tests |

## 7. Правила / алгоритмы
DirtyFileCount — distinct changed paths from status, включая untracked/deleted/staged; MM — один файл. Ignored files не count. Rename — одна status entry, а не сумма old/new path. Conflict path входит один раз в dirty count.
Ahead/behind — commits, отдельные nullable поля. Local ref observation не обещает актуальность удалённого сервера; detached/unborn/missing tracking → unknown counts.
Semaphore/lock access выполняется background; timeout 2s сообщает Unknown/stale без удаления lock или blocking UI. Не внедрять синхронный GetAwaiter().GetResult() в UI.
Публиковать observation лишь для совпавшего sourceId, canonical repositoryPath и generation. Apply результата на UI dispatcher. Error text без token/SSH private path/raw command secrets.

## 8. Интеграция и триггеры
Refresh: изменение байтов task storage, terminal structured Git operation, смена активного пространства, visible-screen activation и 15s fallback timer.
Конкретные hooks:
- TaskSourceManager.ActiveSourceChanged: отсоединить старые subscriptions, generation++, capture Descriptor.Id + active FileStorage.Path + Git configuration snapshot, немедленно Unknown/NotApplicable.
- FileTaskStorage.SaveCore/Remove: добавить отдельный optional IStorageMutationObservation.StorageBytesMayHaveChanged. Сигнал after commit/rollback decision либо finally с flag outcomeUnknown; он сообщает только необходимость измерить, НЕ «save успешен». OnAfterWritePersisted сейчас protected test hook и находится до guardedWrite.Commit, OnWriteFinished вызывается даже после failure — ни один нельзя объявлять готовым success event. Существующий Updating не используется как универсальный success oracle. Один coordinator subscriber на current storage; server implementations не обязаны поддерживать новый optional interface.
- BackupViaGitService: optional IBackupOperationDiagnostics event Started/Terminal(sourceId, repositoryPath, operationId, result Success/Failed/Cancelled/SkippedUnknown, safeErrorCode). Телеметрический scope вокруг Push/Pull/PullExistingRepository/ConnectRepository/CommitResolvedConflicts/ResolveConflict(-Fields), НЕ вокруг read observer. Все ShowUiError внутри такого scope маркируют Failed, включая swallowed PushRemote catch и early returns missing remote/branch; invalid repository silent return маркирует SkippedUnknown, не Success. Unhandled exception тоже Failed, сохраняется исходное throw поведение. Существующие command algorithms, retries/toasts не меняются. Event subscribers exception-isolated; telemetry не должна влиять на Git outcome. Сигнал source-bound, для active runner берёт его OperationId, иначе отдельный ID и immutable captured scope.
- App.RunBackupOperationAsync и Quartz GitPushJob/GitPullJob уже получают runner context/backupScope. Не менять их command success text в этом scope: индикатор доверяет structured service outcome, а не normal return/Connected. Subscribe в App lifetime; UI VM получает coordinator. Telemetry core same-source для scheduled/manual операций, old-source late events игнорируются.
Телеметрия агрегирует outermost logical operation: вложенные public calls (ConnectRepository→Pull, CloneOrUpdateRepo→ConnectRepository) наследуют source/path/operationId и единый outcome accumulator через scoped AsyncLocal. Ровно1Started и1Terminal на внешний scope; внутренний scope не очищает busy и не публикует terminal. Failed sticky до внешнего terminal и не понижается последующим inner/outer success; SkippedUnknown остаётся, если внешний путь не сделал явный успешный эквивалент этой пропущенной операции. Helper ShowUiError добавляет error в текущий accumulator. Возврат/throw каждого вложенного scope корректно освобождает только свой refcount; source mismatch запрещён. Tests: Connect→Pull swallowed error→outer normal return, вложенный busy без flicker, Clone→Connect без double terminal, exception/cancel cleanup, late different-source event. Это диагностическое наблюдение, не изменение retry/merge/command алгоритмов.
Observer принимает immutable sourceId/path/generation/config snapshot, НЕ TaskSpaceOperationContext. Он не вызывает App.RunBackupOperationAsync (тот пишет PersistActiveProjectionCore), не acquires runner lease и не вызывает BeginTaskSpaceOperation на истёкшем context. Только background TryEnter общего Git lock, максимум2s; затем pure read core. Смена source во время ожидания/чтения ведёт к discard. Не обещается глобальная atomicity относительно внешних Git writers.
После terminal событие только ставит refresh в очередь; read core запускается после освобождения lock. Не делать nested RunExclusiveAsync. Coordinator отписывается/cancel при dispose; новые taskspaces не наследуют старый cache.

## 9. Модель состояния
Только ephemeral calculated observation/presentation, без persisted settings/Task JSON/schema изменений. Операционный busy/error независим от последнего Git sample; stale не очищает dirty evidence.

## 10. Rollout / rollback
Первый запуск Unknown до первого read; без автоинициализации репозитория/remote.
Локальная сборка после approval. Установка и публикация не входят.
Rollback — удалить observer/coordinator/UI wiring из собственной будущей ветки, сохранив пользовательские Git/task bytes. Не делать reset пользовательского checkout. Schema rollback не нужен.

## 11. Проверки / AC
Все следующие AC обязательны для EXEC. Это таблица утверждённых verifiers; текущие результаты приведены в §19.1 и docs/testing/git-dirty-status-indicator/README.md.
| AC | Automated | Manual/visual | Artifact |
| --- | --- | --- | --- |
| 1 Сохранение создаёт видимый Dirty | integration real temp repo + Headless flow | rendered after save | dirty-save PNG/video |
| 2 Sync не скрывает Dirty и обновляет status | fake controlled operation + repo | before/during/after | sync trace |
| 3 Error/conflict не выдаёт Clean | real conflict repo + настоящий swallowed PushRemote error/early missing branch; normal return+Connected; retry/error reset; sync-read-timeout | Attention/Syncing retained Dirty details | error PNG+structured outcome trace |
| 4 Unknown/stale/NotApplicable честны | missing/tracking/unborn/detached/timeout; last clean→repeated read errors никогда Idle, retained Dirty→stale overlay | RU/EN tooltip | negative matrix |
| 5 Source/lifetime/thread safety | late A, path change, cancellation, dispose | taskspace UI switch | cross-source trace |
| 6 Layout/accessibility | Headless keyboard + bounds 800×600 | light/dark RU/EN inspected PNG | compact matrix |
| 7 Read-only observation | byte/index/ref pre/post and command spy | verify no stage/fetch/commit | preservation manifest |
| 8 Compatibility/regression | main + relevant full headless suites/build | existing Settings flows | TRX/build logs |

Команды после EXEC: dotnet build Unlimotion.sln -m:1; dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter (точный discovered класс нового coordinator); dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -- --maximum-parallel-tests 1; полная main suite dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj. До запуска проверить SDK/runner; placeholder filter не выдавать за выполненную команду.
Изолированные 10 Git fixtures — preparatory evidence для AC7 semantics, не принятие AC1–8.
Stop: воспроизвести blocker на baseline, сохранить TRX/logs; не бесконечно повторять unchanged pass. Full baseline failure отдельно от новой regression.

## 12. Риски / objections
| Likely objection | Почему | Mitigation | Status |
| --- | --- | --- | --- |
| «Опять не видно незакоммиченные» | исходный запрос | S1, сохраняемый Dirty признак при Attention | mitigated |
| «Число смешало commits и файлы» | ранее уточнено | отдельные counters и MM fixture | mitigated |
| «Индикатор неожиданно отправил данные» | push мутирует | observer read-only, click details | mitigated |
| «Зелёное означает неизвестно» | None при error | explicit Unknown/stale | mitigated |
| «В маленьком окне всё перекрыто» | постоянный toolbar | compact bounds + keyboard | mitigated |
Rework checklist: named UI output yes; scenarios→AC yes; decisions explicit; objections covered; relevant separate review/re-review PASS; AC are verifiers; implementation proof path specified.

## 13. План
После approval: read-only observer + preservation/negative tests → coordinator source/lifetime tests → UI/localization → save/sync integration and rendered tests → build/full suites → post-EXEC review. Не менять existing sync algorithms ради индикатора.

## 14. Открытые вопросы
User-owned неоднозначных вопросов о функции нет. Exact approval — фазовый gate, не предложение повторить старые требования.
Блокирующих вопросов дизайна нет. Конкретизированные hooks/telemetry и negative scenarios прошли отдельный adversarial re-review. Approval этой редакции получено; см. Approval. Нового user-owned вопроса нет.

## 15. Профиль
dotnet-desktop-client: background work, UI regression, build/test. ui-automation-testing: stable automation IDs GitStatusIndicator/GitStatusDetails, negative flows, visual planning, safe video/fallback.
appautomation skill нужен при создании/запуске UI tests в EXEC, не для этой SPEC-only подготовки.

## 16. Планируемые файлы
| Файл | Роль |
| --- | --- |
| src/Unlimotion/Services/BackupViaGitService.cs + новый observer | read-only observation и общий lock |
| src/Unlimotion.ViewModel/новые observation/coordinator типы | ephemeral source-bound state |
| src/Unlimotion.ViewModel/MainWindowViewModel.cs | lifecycle wiring |
| src/Unlimotion/Views/MainControl.axaml | header indicator/flyout |
| src/Unlimotion/App.axaml.cs | coordinator/service lifetime wiring, без изменения sync commands |
| src/Unlimotion.FileStorage/FileTaskStorage.cs + optional interface | mutation-observation signal, без нового save outcome |
| src/Unlimotion.ViewModel/Resources/Strings.resx и Strings.ru.resx | EN/RU strings |
| src/Unlimotion.Test + tests/Unlimotion.UiTests.Headless | integration, lifecycle и UI |
| README/CHANGELOG применимые секции | только EXEC, описание нового поведения |
Точные resource/test файлы подтвердить перед EXEC; контракт не требует менять весь SettingsViewModel.

## 17. Было → станет
Connected ≠ clean → connection отдельно и измеренный dirty отдельно.
None при error → новый explicit Unknown без изменения legacy conflict API.
Настройки для знания статуса → постоянный small indicator с details.
Неограниченный ambient scope → captured generation-bound result.

## 18. Альтернативы
GetConflictStatus reuse отклонён: смешивает unknown и none.
Push для refresh отклонён: побочный commit.
FileWatcher как единственный oracle отклонён: overflow/ref/index events и внешние changes; watcher может быть будущей оптимизацией, fallback read обязателен.
Суммарное число commits+files отклонено пользователем.
Отдельная кнопка ручного refresh не нужна для исходного outcome, не заменяет refresh после save.

## 19. Quality / review
### SPEC Linter Result
| № | Статус | Проверяемое основание |
| --- | --- | --- |
| 1 | PASS | §§1,6.3 исходный Dirty outcome |
| 2 | PASS | §2 actual service/VM/UI files |
| 3 | PASS | §3 connection≠worktree observation |
| 4 | PASS | §4 source ownership/read-only/accessibility |
| 5 | PASS | §5 no Git algorithm/provider/task mutations |
| 6 | PASS | §6.1 service/coordinator/UI responsibilities |
| 7 | PASS | §8 concrete source/storage/service/App hooks |
| 8 | PASS | §7 distinct paths/counts/generation |
| 9 | PASS | §§6.2,8 swallowed errors, retry/reset, stale |
| 10 | PASS | §7 off-UI/2s wait/debounce; constants §6.5 |
| 11 | PASS | §9 ephemeral observation only |
| 12 | PASS | §§6.6,10 optional interfaces/no migration |
| 13 | PASS | §10 preserve owner bytes, own diff rollback |
| 14 | PASS | §11 eight measurable mandatory AC |
| 15 | PASS | §11 test/evidence including negatives |
| 16 | PASS | §11 actual csproj commands/stop/baseline |
| 17 | PASS | §13 real dependencies |
| 18 | PASS | §§6.5,14 no unresolved design choice |
| 19 | PASS | §0 medium expanded reason |
| 20 | PASS | §15 desktop/UI owner requirements |
Итог ГОТОВО для approval, не implementation acceptance.

### SPEC Rubric Result
Цель5 (§1); AS-IS5 (§2); design5 (§§6–8); safety5 (§§5,10); tests5 (§11); autonomy5 (§§13–14):30/30. Это self-assessment SPEC, не эмпирическая оценка приложения и не замена approval.

### Role-Based Review Result
| Role | Applicability | Question / evidence | Verdict |
| --- | --- | --- | --- |
| Workflow analyst | applicable | Исходный Dirty сохранён в S1/S3, нет sourceState→task completion | PASS design |
| UX/designer | applicable | Wireframe, persistent glyph, narrow/keyboard, Unknown/stale и retained Dirty | PASS design; rendered acceptance только EXEC |
| Tester | applicable | Все8AC, swallowed errors, nested telemetry, late-source, MM/no-upstream | PASS plan |
| Architect | applicable | Immutable sample, lifecycle, pure read lock, optional diagnostic APIs | PASS design |
| Operations/security | applicable | Zero observation writes/network; source/op identity; safe errors; rollback | PASS design |

### Post-SPEC Review
Status PASS. Scope/Evidence: current SPEC; central QUEST/template/linter/rubric/review, selected profiles/local override; IRemoteBackupService, SettingsUiState, MainWindow/MainScreen/MainControl header, BackupViaGitService status/Push/Pull/context/error paths; TaskSpaceOperationRunner fully; App manual/wrapper; Quartz GitPushJob/GitPullJob fully; FileTaskStorage SaveCore/hook/publication; UnifiedTaskStorage.Update/ITaskStorage; TaskSourceManager event; actual csproj/resource inventory; CLI all parents and10native Git fixtures.
Contract pass: outcome/non-goals,8AC, ephemeral model, zero observe mutations, approval boundary сверены. Nested wrappers не меняют service algorithms.
Adversarial pass: separate reviewer git_indicator_spec_review нашёл5находок; автор исправил source/wiring facts и контракты. Reviewer имеет danger-full-access: **техническая read-only изоляция отсутствует**, все действия были чтением. Это отдельный adversarial review в writable session, НЕ технически read-only enforced review.
| Severity | Area | Finding | Action / status |
| --- | --- | --- | --- |
| HIGH | operation outcome | normal return скрывает swallowed failure | structured sticky diagnostic signal+real path tests; fixed design |
| MEDIUM | UI priority/reset | lock timeout мог скрыть Syncing; old error forever | primary operation + overlays/reset; fixed design |
| MEDIUM | lease lifetime | nested/expired operation context | immutable observer, no runner/App wrapper; fixed design |
| MEDIUM | nested telemetry | inner terminal мог обнулить busy/Failed | outermost shared accumulator/1Started1Terminal; fixed design |
| MEDIUM | stale clean | failed read мог оставить Idle | fresh-only Idle + AC4 negatives; fixed design |
Additional author fixes: actual main suite src/Unlimotion.Test, actual MainControl header, storage test hooks не success event, optional observation signal.
Fix and re-review: reviewer повторно сверил §§6–8/11/14/16; final targeted verdict PASS, remaining findings none. Role-based check выше. Структура20linter/6rubric заполнена автором после вердикта; substantive contracts не изменены этим audit update.
Depth checklist: source/owner diff clean доSPEC; outcome/8AC mapped; no unsupported UI/runtime claims; regression/cancellation/conflicts specified; README/CHANGELOG только plannedEXEC; public optional diagnostics explicit; operational result distinct from read-error; manual challenge «Dirty после реального save» обязателен AC1.
No-findings justification: final counterexamples old clean+error, long sync+timeout, nested Connect→Pull failed, late A→B теперь имеют однозначное expected state и negative test; remaining implementation uncertainty не выдаётся за evidence PASS.
Stop decision: показывать эту SPEC для exact approval. Не реализовывать автоматически ночью. Остаточный риск: writable review sandbox, external Git writer nonatomic snapshot; EXEC требует всех8AC и отдельного post-EXEC.
Это исторический post-SPEC audit. Текущее post-EXEC состояние приведено ниже; installed acceptance не заявляется.

### 19.1. Post-EXEC review — итоговый audit

**Статус NEEDS-FIX / AC8 incomplete.** Локальный feature diff выполнен в отдельном worktree feat/git-dirty-status-indicator на e987023dc93daba6753440eec813edcbaa468da9. Исходный approved SHA256 в основном checkout повторно совпал; primary source не менялся. Подробные scope/contract/adversarial/finding/fix/re-review/roles/depth/no-findings/stop и AC-to-test matrix: docs/testing/git-dirty-status-indicator/README.md.

Current evidence: production solution PASS0 errors/123 warnings; latest Headless build PASS0 errors/1 warning; полный latest Headless PASS59/59,0skips,3:05.862; все24новых main casesPASS в полном main;8byte/index/refs manifest pairs equal, configured service/remote spy0events/connections. Latest12renderedPNG и2actual-selectorPNG просмотрены; lifetime/ancestor/window/owner trace сохранён. AC1–7 подтверждены по утверждённым verifiers; native/FlaUI/installed/physical minimize/remote rollout не подтверждены.

Полный main:1423executed1422passed1failed0skipped,2:13:56.215. Единственный failure — existing phone CalendarDatePicker containment; candidate isolated430 и чистый baseline e987023d repeat430 имеют точно одинаковые left=-2/right334/viewport380. Первыйbaseline3/3 и6candidatefreshrepeats18/18 не делают failed fullmain green; причина intermittentfailure/влияниеfeature не установлены. Согласно central testing-baseline/review-loops AC8 остаётся blocker. Согласно stop §11 baseline evidence сохранён, unchanged repeats прекращены; product planning layout/assertion допуски не менялись. Productfix за пределамиfeature scope автоматически не добавлен.

Исправленные findings: UTF-8glyphs,actualselector/Space,configuredspy,after-window initialsource/dispatcherfixture,ownerreplacement/ancestorvisibility,quietdefaulttimer. Дополнительный test-only fixturefix после matchingcandidate/baselineCLIhang: persistent owned dispatcher на целый CLI asyncflow, ordinaryAvaloniaSynchronizationContext вместоblockingcustomPost; unchanged4/4 иfull59PASS подтверждают в том числе последующее восстановлениеdefaultfixtures. Первоначальнаяworkerexception не изолирована, dumps/logs сохранены.

Separate adversarialreview git_indicator_review выполнял только чтение,безedits/build/tests; danger-full-access/never не даёт техническойread-onlyизоляции. Sourcefixes иfixturecleanup повторно проверены; общийPASSневыдан. Linter20/20,rubric30/30 изpost-SPEC — историческаяdesignпроверка, неimplementationacceptance. Rolesworkflow/UX/tester/architect/operations применены, evidenceвaudit. Depthoutcome/owner/negative/lifetime/read-only/rollback/gatesmapped; schemas/legacy status не заменены; generatedevidenceignored; noCHANGELOG/versionbumpдлялокальногоdiff.

No-findings justificationдляsource/visual: concretehistoricalclean/readerror,dirty/sync/error,nestedswallowedoutcome,lateA→B/dispose,replacement/hide/show иordinarysave assertions/pixels. Общегоno-findings/PASS нет из-заmainAC8. **Stop decision NEEDS-FIX:** concrete reviewable feature diff иvalidation evidence сохранены, обязательныйgreen gateнеполучен; новаяproductscopeextension неавторизована. Residualbounds: cooperativeGitLock,externalwriter nonatomic snapshot,nativeread cancellation тольконапримененииreply,localrefsмогутбытьстарееremote. Rollback толькособственныйdiff; persistedstate/migration отсутствуют.

## 20. Журнал действий агента
| Фаза | Решение | Evidence/остаток | Далее | Человек | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC 09.10 | Исходный запрос сохранён; 10 Git fixtures PASS | Самостоятельная подготовка, не app acceptance | wiring/review | Нет нового решения | эта SPEC |
| Post-SPEC findings | HIGH swallowed errors; MEDIUM priority/expired lease | Конкретизирован optional diagnostic signal, overlay/reset policy и pure immutable read scope; actual source paths исправлены | targeted re-review | Approval не запрошен | эта SPEC |
| Re-review findings | MEDIUM nested telemetry и stale-clean | Outermost sticky outcome aggregation и fresh-only Idle добавлены;5findings исправлены в design, не runtime | final targeted re-review | Не требовалось | эта SPEC |
| Final post-SPEC | Separate targeted re-review PASS, нет remaining findings | Audit20linter/6rubric/roles заполнен; writable sandbox boundary сохранён | Exact approval этой SPEC | Не получено | эта SPEC |

| EXEC 09.10 | Hash подтверждён; main e987023d; изолированный worktree git-dirty-indicator, feat/git-dirty-status-indicator | Чужие ветки не менялись, source task read/write не выполнялся | observer/coordinator/telemetry/UI + AC1–8 | Exact approval получено через поручение | собственный worktree |

| EXEC validation | Solution PASS; new main24/24; latest Headless59/59; main1422/1423 | NEEDS-FIX/AC8: matching baseline phone failure сохранён; CLI fixture correction4/4/full59PASS | Gate остаётся open, planning product fix автоматически не добавлен | Scope contradiction за пределамиfeature требует явного разрешения | docs/testing/git-dirty-status-indicator/README.md |
