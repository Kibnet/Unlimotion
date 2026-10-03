# Восстановление смены статуса из карточки задачи

## 0. Метаданные

- Тип: delivery-task; основной профиль `dotnet-desktop-client`, overlay `ui-automation-testing`, context `testing-dotnet`.
- Владелец: Павел; реализация и интеграция — основной агент.
- Масштаб: medium; Expanded SPEC: recovery пересекает UI, ViewModel и storage/cache; допустимость short не выполнена.
- Canonical template: `C:\Users\Kibnet\.codex\agents\templates\specs\_template.md`.
- Instruction stack: central `AGENTS.md`, `routing-matrix`, `creator-vibe-lens`, `model-behavior-baseline`, `tool-execution-baseline`, `collaboration-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `spec-linter`, `spec-rubric`, `review-loops`, указанные профиль/context и локальный `AGENTS.override.md`.
- Поверхность: Codex desktop, Windows/PowerShell; фактические model/tier/effort не меняются и не используются как доказательство качества продукта.
- Репозиторий: `main`, исходный HEAD `46711e60d6ef453e794104ee7d9f81ad6f37c68c`; до SPEC рабочая копия чистая.
- Фаза: EXEC. Пользователь дал exact approval «Спеку подтверждаю» 2026-10-02. Реализация ведётся в изолированном worktree `C:\Users\Kibnet\.codex\worktrees\task-card-status-recovery\Unlimotion`, ветка `fix/task-card-status-recovery`.

## 1. Overview / Цель

Исходный симптом: «Смена статуса из карточки задачи не срабатывает, просит обновить задачу, а кнопки обновления нет».

Success means: в обычной карточке можно сменить допустимый статус; после сбоя пользователь видит причину и доступное действие восстановления, обновляет карточку из хранилища и повторяет выбор статуса без перезапуска и потери своих правок. Невозможный по правилам переход остаётся запрещённым и объясняется соответствующей причиной.

Output: локальное исправление, regression/UI coverage, результаты проверок, просмотренные визуальные артефакты. Публикация, установка, релиз и изменения пользовательских задач требуют отдельного поручения.

Stop rules: SPEC заканчивается проверенным планом и exact approval gate; EXEC заканчивается только после обязательных проверок исходного сценария на fixture. Если воспроизведение реального отказа выявит другой storage-дефект, не скрывать его добавлением кнопки: установить причину и оценить изменение scope до зависимого исправления.

## 2. Текущее состояние (AS-IS)

Подтверждено чтением актуального кода:

- `TaskStatusPicker` открывает меню и вызывает `TaskItemViewModel.TrySelectStatusOptionAsync`; карточка использует `CurrentTaskStatusButton`.
- ViewModel ждёт текущие записи, сохраняет ожидающие правки редактора, выполняет status command, применяет authoritative snapshot и сохраняет правки, появившиеся во время операции.
- `UnifiedTaskStorage.ExecuteStatusCommandAsync` сериализует команды и сверяет результат с cache. При `OutcomeUnknown` уже выполняется попытка перечитать задачу; её ошибка не должна подменять подтверждённый snapshot выдуманным откатом.
- `GetStatusOperationFailureMessage` распознаёт доменные transition reasons, `OutcomeUnknown` и `StatusPreconditionFailed`; прочие причины попадают в общий `TaskStatusSaveFailed`.
- Русские `TaskStatusSaveFailed` и `TaskStatusOutcomeUnknown` предлагают «Обновите задачу перед повторной попыткой».
- В `MainControl.axaml` у карточки есть перенос, архивирование и удаление; команды обновления нет. `ITaskStorage` не предлагает явного reload-контракта.
- Граф может отказать до записи: отсутствующая задача, нарушение графа, активный agent lease, ошибка чтения. Одинаковое сообщение о сохранении скрывает различие этих случаев.
- `IStorage.Load` не гарантирует обход cache; `FileTaskStorage.Load(id, forced: true)` предоставляет принудительное чтение, но возвращает `null` также при parse/read error и пустом файле. Ошибка отмечается в live graph diagnostics. Сам `null` не доказывает удаление.
- Bare forced `Load` читает файл асинхронно и затем публикует новый domain snapshot/revision без captured-generation guard. Поэтому facade revision check сам по себе не защищает от watcher update/delete между чтением bytes и публикацией старого reload snapshot.
- Existing directory lock запускает штатный recoverable journal recovery до операции; refresh не должен обходить этот инвариант ради формального отсутствия filesystem writes.
- `ServerStorage.LoadCoreAsync` также перехватывает backend/auth/transport/mapping exception и возвращает null. Ordinary `Load` не позволяет подтвердить server deletion; нужен отдельный typed recovery read.
- Server hub Saved/Removed события приходят без monotonic `StorageRevision`; default `0` допускается существующими facade/VM guards. Для delayed server recovery response требуется отдельная source-lifetime event epoch, не выдуманная backend revision.
- Применение storage snapshot умеет сохранять локально изменённые поля; generation/tombstone guards защищают от запоздалых результатов.

Граница evidence:

- Запущен установленный `Unlimotion.Desktop` 1.32.0 (`d1e6702...`), отдельно от текущего HEAD исходников.
- Из settings прочитаны только сведения об активном локальном источнике. Выполнена read-only структурная проверка всех 3027 task-файлов: JSON errors, duplicate IDs, missing/reverse links, duplicate relations/criteria и containment/dependency cycles не обнаружены; изменение файла во время его чтения не обнаружено. Это последовательный, не атомарный снимок и не запуск production status command.
- Проверка не воспроизводит конкретное нажатие пользователя. Вопрос о задаче и целевом статусе направлен; ответ необязателен для подтверждённого UI/recovery дефекта, но нужен для точного воспроизведения индивидуального случая.
- Тесты, запись статусов и переустановка приложения на фазе SPEC не выполнялись.

## 3. Проблема

Контракт восстановления карточки неполон: ошибка предлагает недоступное действие, а некоторые структурированные причины отказа теряются. Пользователь остаётся с неработающей сменой статуса без понятного дальнейшего шага. Причина конкретного storage-отказа пользователя пока не установлена.

## 4. Цели дизайна

- Доступное восстановление из той же карточки.
- Подтверждение статуса чтением хранилища; никаких оптимистических успешных статусов.
- Сохранение pending editor fields, подтверждённой истории и generation ordering.
- Одинаковый recovery-контракт для локального и серверного источника.
- Regression test отличает сбой/отказ от успешной смены статуса и повторного восстановления.

## 5. Non-Goals

- Автоматическая повторная status-запись после неизвестного результата.
- Ослабление графовой валидации, правил перехода, agent lease или архивной каскадности.
- Ремонт/массовая перезапись пользовательского графа, удаление журналов или файлов.
- Перепроектирование watcher, storage transactions, task spaces и autosave.
- Публикация, commit/push/PR, установка, перезапуск пользовательского приложения.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- Storage facade: явное асинхронное чтение актуальной задачи и безопасное применение snapshot в cache.
- ViewModel: reload command, busy/error/recovery state, сохранение локальных правок и согласование с status/autosave lifecycle.
- View: доступная кнопка и объяснение результата. Локализованные сообщения выбираются по structured reason.
- UI Authoring/TestHost: детерминированная fixture отказа и recovery; Headless/FlaUI адаптеры проверяют реальную карточку.

### 6.2 Детальный дизайн

1. Добавить reload capability на уровне `ITaskStorage`/`UnifiedTaskStorage`. Результат различает успешный snapshot, физическое отсутствие и ошибку чтения. Метод не создаёт новую `Update`/`Save`/status operation и не запускает миграцию. Existing journal recovery под storage lock сохраняется: он может завершить ранее начатую транзакцию, поэтому read-only означает отсутствие новой бизнес-записи, а не обещание неизменных filesystem bytes при pending journal. Default для неподдерживающего storage возвращает контролируемую ошибку, а не успешный no-op. Конкретное имя result-типа — внутренняя деталь; wire JSON/task model не меняются.
2. Локальное чтение выполнить через узкий typed reload primitive в `FileStorage`/`FileTaskStorage`, а не bare `Load(forced:true)`. Под existing directory/command boundary прочитать detached snapshot и диагностику mapped source-файла без преждевременной публикации. До чтения захватить source/watcher generation и mapping; перед публикацией проверить их и актуальность source hash в согласованном cache/domain boundary. Изменение/удаление во время read не может назначить старым bytes новую revision: stale snapshot отклонить и перечитать/применить newest watcher state. Retries ограничить; непрерывные внешние изменения завершаются ReadFailed с доступной повторной попыткой и снятым busy. Не менять ordinary `Load` для других consumers без доказанной необходимости. При null/empty проверить diagnostics и физическое отсутствие mapped file: пустой/повреждённый/недоступный файл — ReadFailed, не Missing. Не удалять UI snapshot на основании одного null. В `ServerStorage` добавить узкий typed recovery read напрямую через существующий ServiceStack client и mapper: только подтверждённый backend not-found даёт Missing; network/auth/mapping error и неполный/невалидный response дают ReadFailed. Existing swallowing `Load` нельзя использовать как доказательство удаления. Связи и доступность синхронизируются существующим путём; ограничиться необходимым read-back, не выполнять холодный full-directory init.
3. Reload и status согласуются существующим command/cache boundary; повторное нажатие во время операции не запускает конкурентное обновление. UI не блокируется. Edit во время чтения сохраняется как pending local field; результат применяется только к исходным task/source/context и с актуальной revision. Для server recovery захватить source-lifetime cache/event epoch до запроса; server hub Saved/Removed, status/autosave cache updates и dispose/source switch изменяют этот epoch до применения соответствующих событий. Проверку epoch и применение read response выполнить в одном cache synchronization boundary. Изменившийся epoch запрещает позднему response перезаписать более новое hub/cache update/delete; discard/re-read ограничены тем же retry budget. Default server StorageRevision=0 не является protection и не сравнивается как локальная backend revision. Не менять semantics существующих hub/CRUD consumers ради новой глобальной versioning схемы.
4. Reload вызывает existing snapshot merge, сохраняя все pending editable fields: title, description, planning, importance, wanted, repeater, completion criteria. Сам reload не должен пытаться предварительно сохранить dirty revision: иначе временная ошибка записи сделает recovery недоступным. Уже допущенный autosave согласовать с чтением: дождаться его завершения, но обработать fault отдельно и продолжить reload; failed editable revision остаётся dirty. На время reload новые autosave producers не должны конкурировать с read/merge. После него штатный autosave, вызванный пользовательским edit, может продолжиться с актуальным authoritative status; reload не создаёт отдельный save/retry для dirty полей. Последующий осознанный status command выполняет штатный editor drain и запись. Проверить delayed autosave success и failure до reload: нет повторной записи, pending поля и свежий статус сохранены.
5. В command bar добавить отдельную кнопку `Обновить` с accessible name `Обновить задачу`, tooltip и стабильным `CurrentTaskReloadButton`. Кнопка доступна и без ошибки, чтобы действие не исчезало вместе с toast. На reload/status она disabled; закрытая/сменённая карточка не принимает старый результат.
6. Сохранить ошибку status/reload в состоянии карточки до следующего успешного восстановления/операции. В коротком сообщении обозначить причину; для диагностики дать детали structured error через раскрываемые `Подробности` без сырых JSON, настроек, токенов или stack trace. Отказ в доменном правиле не называется ошибкой записи. Не добавлять общую подсистему логирования.
7. `OutcomeUnknown` сообщает, что статус ещё не подтверждён; после успешного reload показать фактический статус. Не предлагать автоматическое повторение и не утверждать, что запись отменена. `StorageFailed` предлагает доступное обновление и повторный выбор. `ValidationFailed` сообщает о некорректном графе; `ExecutionStateDenied` — об активном агенте; `TaskNotFound` — об отсутствии задачи. Обновление не обещает снять постоянные ограничения.
8. После успешного refresh убрать прежнюю storage/reload ошибку, пересчитать status options и показать подтверждённый статус. При ошибке чтения сохранить последнюю карточку и правки, оставить кнопку для новой попытки. При подтверждённом удалении показать «Задача удалена из хранилища», запретить status, autosave и lifecycle final-save для этого объекта, не воскресить его. Dirty текст оставить доступным для копирования; встроенная навигация остаётся доступной. Не обещать сохранение удалённой задачи или persistence несохранённых правок после закрытия приложения.

Visual planning artifact — текстовый wireframe в этой SPEC:

```text
┌ Карточка задачи ─────────────────────────────────────┐
│ [значок статуса ▼] Название                          │
│ …существующие метаданные…       [Обновить] [⚙]       │
│ Ошибка: не удалось подтвердить статус.              │
│ Нажмите «Обновить», затем проверьте текущий статус.  │
│ [Подробности ▸]                                    │
│ …существующие описание, критерии, планирование…      │
└─────────────────────────────────────────────────────┘
```

Состояния: обычное — error area скрыта; busy — кнопки refresh/status disabled, данные остаются читаемыми; error — краткая причина и доступное восстановление; successful reload — фактический статус и сохранённые local edits; missing — объяснение и disabled status. Проверить узкую карточку, light/dark и RU/EN: кнопка/сообщение не перекрывают меню или редактор.

UI video evidence: repository имеет `scripts/record-status-contract-evidence.ps1`, FlaUI и ffmpeg/ffprobe. После approval добавить recovery-сценарий и обеспечить recording hook на принятом harness. Сохранить failing/repro `before` и passing `after` на synthetic tasks, в `artifacts/task-card-status-recovery/`, local-only. Existing terminal/unarchive video не доказывает этот дефект. Fallback допускается только при конкретной технической причине; next-best — отрисованные Headless PNG и test/log report. Без реально записанного/просмотренного артефакта не заявлять визуальную проверку.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result | Evidence required | AC |
| --- | --- | --- | --- | --- |
| S1 | В карточке выбрать допустимый статус | Значок/выбранный статус, storage и история согласованы | UI assertion и file read-back | AC1 |
| S2 | Status command получает временный storage failure | Понятная ошибка и видимая кнопка refresh; успешного статуса нет | RED/GREEN regression и before/after | AC2, AC3 |
| S3 | После устранения временного сбоя нажать refresh и повторно выбрать статус | Фактический статус перечитан, новая запись подтверждена | End-to-end fixture recovery | AC3 |
| S4 | Исправить поля до/во время reload | Правки остались в редакторе и последующей успешной записи | UI + unit race test | AC4 |
| S5 | Запись могла пройти, но результат неизвестен | Refresh показывает записанный статус без второй history entry | Fault injection + read-back | AC5 |
| S6 | Граф/agent lease/удаление запрещают операцию | Конкретная причина и честные доступные действия | Negative tests, screenshot | AC6, AC7 |
| S7 | Закрыть карточку/сменить source во время reload | Старый результат не изменяет новую карточку и cache | Deterministic lifecycle test | AC7 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected result | Error/concurrent case |
| --- | --- | --- | --- |
| Idle | Status selection | Штатная запись и read-back | Structured failure, статус не выдумывается |
| Error/Idle | Refresh | Read-only authoritative reload | Last snapshot/pending edits сохранены при read failure |
| Busy | Повторное refresh/status | Нет второй операции | Редактор остаётся работоспособным |
| Dirty editor | Refresh | Merge только соответствующих pending fields | Новая dirty revision не теряется |
| Unknown status | Refresh succeeded | Фактический подтверждённый статус | Автоматической status-записи нет |
| Source disposed/switched | Delayed result | Результат отклонён | Новый task/source не гидратируется |
| Physically deleted | Refresh | Missing state, status disabled | Tombstone запрещает воскрешение |

### 6.5 Decision Ledger

| Decision | Owner | Chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Recovery action | agent | Отдельная кнопка в command bar | 0.95 | Доступность при узкой ширине проверяется UI | Нет |
| Local edits | agent | Сохранять dirty поля, read-only reload | 0.95 | Требуется race coverage | Нет |
| Unknown outcome | agent | Read-back без автоповтора | 1.0 | Повтор может дублировать историю/side effects | Нет |
| Причина конкретного отказа | agent | Не считать установленной без exact repro | 1.0 | Кнопка сама по себе не подтверждает root cause | Нет; fixture доказывает recovery, индивидуальный случай отдельно |
| Scope | user | Локальный bugfix; обязательный фазовый approval отдельно в §Approval | 1.0 | Код до exact approval запрещён | Нет design choice; approval gate сохранён |

### 6.6 Runtime / Config / Data Contract Matrix

| Area | Current source of truth | Expected change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Task data | Активный storage, не UI cache | Чтение по явному действию, existing journal recovery | Task JSON/schema unchanged | Нет новой записи; no-journal fixture bytes unchanged; pending-journal recovery test |
| Status | TaskGraphCommandService result/read-back | Точное отображение причин и результата | Policy/lease guards unchanged | Structured denial tests |
| Cache/source | Revision/tombstone и active context | Reload следует тем же guards | Нет переносов между spaces | Ordering/source-switch tests |
| UI localization | Strings.resx / Strings.ru.resx | Кнопка, summary, details label | RU/EN сохраняются | UI/text resource coverage |
| Installed app | 1.32.0 отдельная установка | Не меняется этой реализацией | Нет автоматической установки | Final честно отличает код/проверку/установку |

## 7. Бизнес-правила / Алгоритмы

Reload читает и отображает, status command пишет после проверки правил. Existing journal recovery завершается под штатным lock до authoritative read-back; refresh не открывает новый business write scope. Наличие кнопки не даёт права обойти блокировку графа, критерии, плановый старт или agent execution. Status history создаётся только существующей подтверждённой записью. Отсутствующий, пустой и повреждённый файлы не эквивалентны.

## 8. Точки интеграции и триггеры

`TaskStatusPicker → TrySelectStatusOptionAsync → ExecuteStatusOperationAsync`; `CurrentTaskReloadButton → ReloadCommand → storage read → existing snapshot merge/cache synchronization`. Ошибка обновляет presentation state; следующая успешная операция снимает устаревшую ошибку. Existing watcher обработка остаётся независимым путём изменения cache.

## 9. Изменения модели данных / состояния

Только transient ViewModel presentation state: busy, last operation summary/details, missing/recovery. Reload result/capability — внутренний .NET facade contract, не task JSON и не persisted settings. Pending editor revision, status history и source revisions сохраняют значение.

## 10. Миграция / Rollout / Rollback

Миграция отсутствует. Локальный код можно откатить целиком; refresh не создаёт новую бизнес-запись, settings не меняет. Штатное завершение ранее начатой транзакции не откатывается произвольным возвратом cache. Внешний rollout не входит в поручение. Existing desktop продолжает работать до отдельно согласованной установки.

## 11. Тестирование и критерии приёмки

- **AC1:** допустимый переход из реальной карточки подтверждается storage, обновляет icon/options и сохраняет одну корректную history entry.
- **AC2:** ошибка не предлагает недоступного действия; кнопка обновления видима, reason соответствует фактическому failure, busy не оставляет карточку навсегда disabled.
- **AC3:** deterministic temporary failure → successful reload → explicit retry → confirmed status проходит из той же карточки без app restart; reload не создаёт новой status/business записи, сохраняет штатный journal recovery.
- **AC4:** все pending editable поля сохраняются при reload, включая edit во время чтения; последующая штатная запись сохраняет их. Deterministic already-running autosave → delayed success/failure → reload подтверждает согласованное чтение, отсутствие нового save от reload, сохранение failed dirty revision и newest authoritative status. Autosave failure не блокирует само обновление.
- **AC5:** после unknown outcome read-back показывает фактический статус и не создаёт вторую status/history запись; read failure сохраняет последнюю карточку.
- **AC6:** validation, active agent и missing task получают отдельную причину; recovery не ослабляет policy и не утверждает, что постоянный отказ исправлен обновлением.
- **AC7:** двойной click, delayed older revision, удаление, закрытие/смена source не теряют правки, не гидратируют чужую карточку и не воскрешают удалённую задачу. Отдельные deterministic pauses между read bytes и publish с watcher update/delete проверяют both domain live graph и facade/UI cache: старый snapshot не получает newer accepted revision. Server read → hub update/delete → delayed response при StorageRevision=0 проверяет source-lifetime epoch guard и отсутствие overwrite/resurrection. ReadFailed после исчерпания retries снимает busy.
- **AC8:** UI evidence показывает error, доступный refresh и восстановленный статус; RU/EN, light/dark и узкая карточка читаемы.

Обязательные проверки: новый failing regression до fix; targeted ViewModel/storage/picker UI tests; обычная desktop build; полный `Unlimotion.Test` из-за общего facade/cache контракта; полный Headless suite; targeted FlaUI recovery и before/after video. Использовать TUnit/MTP `--treenode-filter`, UI serial execution. Существующая UI coverage обязательна по локальному override.

Подтверждённый preflight: SDK `10.0.401` при `global.json 10.0.400/latestPatch`, Avalonia `12.0.4`, AppAutomation `1.6.0`, TUnit `1.44.0`; Python, ffmpeg, ffprobe доступны. Перед long runs проверить restore и интерактивный desktop. Историческая длительность Main около 20 минут и Headless около 2 минут — ориентир из прошлой сессии, не текущий замер. Progress получать через runner output/TRX в local-only artifacts; не прерывать без проверки прогресса.

План команд, последовательно для общего output/shared UI state:

```powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/TaskItemViewModelStatusCommandTests/*' --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/UnifiedTaskStorageStatusCommandTests/*' --maximum-parallel-tests 1
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/MainControlTaskStatusIconUiTests/*' --maximum-parallel-tests 1
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -- --maximum-parallel-tests 1
dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug -- --treenode-filter '/*/*/MainWindowFlaUiTests/TaskCardStatusRecovery*' --maximum-parallel-tests 1
git diff --check
```

Новые тестовые имена/recording hook уточняются в том же scope при EXEC. Video driver существующего сценария нельзя запустить как доказательство нового flow без адаптации: он hardcodes terminal/unarchive test. UI screenshots открыть и проверить нужное состояние. Установка и production status change не включены в проверки. Не повторять зелёные suites без новых изменений/failure или незакрытого риска; skipped и runner/environment errors не считать GREEN.

### Acceptance-to-Test Matrix

| AC | Automated test | Visual/manual check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | Existing picker/status + new recovery scenario | Icon/options после выбора | test log + synthetic file read-back | SPEC: исполнение после approval |
| AC2 | Reason mapping и refresh visibility | Error area и button доступны | before/after PNG/video | То же |
| AC3 | Temporary failure/reload/retry UI; no-journal Save count=0; pending-journal recovery test | Та же открытая карточка | targeted UI report/video | То же |
| AC4 | Pending fields parameterized tests; edit during read; already-running autosave delayed success/failure → reload → normal later write | Title/description retained | unit report + UI assertion | То же |
| AC5 | Unknown persisted outcome/read failure | Confirmed status без второй записи | history/read-back assertions | То же |
| AC6 | Validation/lease/missing negative tests; server 404 vs transport/auth/mapping failure; local empty/corrupt vs deleted | Сообщения и disabled status | UI screenshots + contract report | То же |
| AC7 | Local read→watcher update/delete→publish с domain+facade assertions; server read→hub update/delete→response с revision=0; source/lifecycle; deleted dirty task cannot autosave/final-save; retry exhaustion | Новый source не меняется | storage/VM test report | То же |
| AC8 | Headless/FlaUI recovery, локализация/layout | Просмотр narrow light/dark RU/EN | inspected PNG и before/after video | То же |

## 12. Риски и edge cases

Основные риски: reload прочитает cache; исчезнувший файл будет принят за parse failure/наоборот; незавершённый autosave запишет старый статус; merge потеряет локальные поля; delayed result пересечёт source lifetime; ошибка production отличается от fixture. Для каждого задан verifier выше. Не отдавать пользователю полный raw error, если он содержит данные конфигурации; безопасные structured details достаточны.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation | Status |
| --- | --- | --- | --- |
| «Добавили кнопку, а статус всё ещё не меняется» | Исходная жалоба включает смену статуса | AC1/AC3 проверяют реальную запись; точная production причина не заявляется установленной | mitigated |
| «Обновление стёрло описание» | Карточка autosaves с задержкой | AC4 покрывает все pending поля и edit during read | mitigated |
| «Сообщение исчезло, где обновлять?» | Toast временный | Постоянная кнопка и error state карточки | mitigated |
| «Ошибку замаскировали успешным статусом» | Outcome может быть неизвестен | Authoritative read-back и AC5 | mitigated |
| «На моём компьютере всё осталось как было» | Установленное приложение отдельно от source | Final различает локальный код и установленную версию | mitigated |

Rework Prevention Checklist: исходный сценарий сохранён; каждая видимая операция имеет AC/evidence; допущения названы; objections закрыты тест-планом; применимые роли проверяются ниже; AC описывают результаты; evidence path до EXEC определён.

## 13. План выполнения

1. После exact approval добавить failing UI/unit regression для отсутствующего recovery, reason mapping и временного отказа; сохранить before evidence на fixture.
2. Реализовать facade reload, ViewModel recovery и UI; уточнять внутренние имена без изменения результата/риска.
3. Проверить negative/race cases и получение подтверждённого storage status после explicit retry.
4. Выполнить targeted → normal build → full Main/Headless → targeted FlaUI, after evidence и визуальный review.
5. Сопоставить S1–S7/AC1–AC8 с фактическими результатами и выполнить full post-EXEC review. При объективном blocker честно отразить incomplete; не утверждать исправление installed app.

## 14. Открытые вопросы

Блокирующих design-вопросов нет. Название/ID и целевой статус пользователя ожидаются для индивидуального воспроизведения; отсутствие ответа не позволяет назвать точную причину production отказа, но не блокирует исправление подтверждённого recovery-контракта. Exact approval остаётся обязательным фазовым условием, не design choice.

## 15. Соответствие профилю

Desktop: асинхронное чтение, отсутствие UI blocking, stable selectors, normal build и runner workflow. UI automation: конкретный flow, visual wireframe, обновление UI coverage, mandatory runs, synthetic before/after video либо объективно обоснованный fallback. Локальный override сохранён.

## 16. Таблица изменений файлов

| Файл | План изменений | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/ITaskStorage.cs`, reload result при необходимости | Асинхронный reload capability | Explicit recovery contract |
| `src/Unlimotion/UnifiedTaskStorage.cs` | Typed read, cache/revision/source guards, source-lifetime event epoch для server reload | Authoritative recovery без позднего overwrite |
| `src/Unlimotion/FileStorage.cs`, `src/Unlimotion.FileStorage/FileTaskStorage.cs`, релевантные FileStorage tests | Typed detached reload с generation/mapping/hash validation до publication | Bare forced Load может опубликовать старые bytes с новой revision |
| `src/Unlimotion/ServerStorage.cs`, `src/Unlimotion.Test/ServerStorageStatusCommandTests.cs` | Typed recovery read; not-found/transport/auth/mapping cases | Ordinary Load глотает ошибки и не годится для deletion evidence |
| `src/Unlimotion.ViewModel/TaskItemViewModel.cs` | Command, busy/error state, pending merge, denial mapping | Рабочее восстановление |
| `src/Unlimotion/Views/MainControl.axaml`, Strings RU/EN | Button/error/details | Доступность и понятность |
| `src/Unlimotion/TaskStatusPicker.cs` при необходимости | Busy binding/enablement | Нет concurrent clicks |
| `src/Unlimotion.Test/TaskItemViewModelStatusCommandTests.cs`, `UnifiedTaskStorageStatusCommandTests.cs`, picker UI tests | Recovery/reason/race coverage | Regression protection |
| `tests/Unlimotion.UiTests.Authoring`, TestHost, Headless/FlaUI adapters | Shared recovery scenario, synthetic failures | User scenario evidence |
| `scripts/record-status-contract-evidence.ps1` либо узкий recovery driver | Recording hook для нового flow | Реальное before/after |
| Текущая SPEC | Review, phase, factual evidence | Audit |

Прочие production-файлы изменять только при доказанной необходимости того же outcome; другие bugs/design changes проходят scope gate.

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Recovery | Невыполнимое «обновите» | Видимая кнопка, authoritative reload, explicit retry |
| Failure reason | Общая ошибка для разных denied kinds | Причина и подходящее действие |
| Dirty editor | Existing merge только на storage/status update | Тот же инвариант при explicit reload |
| Unknown outcome | Toast и внутренняя попытка reread | Пользователь может подтвердить реальный статус |

## 18. Альтернативы и компромиссы

- Только исправить текст: дешевле, но AC3 не выполнен — recovery остаётся недоступным.
- Автоматически повторять запись: скрывает interaction, опасно при unknown outcome и каскаде — отклонено.
- Полностью переоткрыть источник/перезапустить приложение: затрагивает navigation/pending writes и весь dataset — непропорционально.
- Выбран explicit read-only refresh с сохранением локальных правок и отдельным осознанным повтором статуса. Требует race/UI coverage, зато оставляет доменные ограничения прозрачными.

## 19. Результат quality gate и review

### SPEC Linter Result

Итог: ГОТОВО к запросу exact approval. Проверены критерии 1–20 и окончательный post-SPEC review ниже; это оценка плана, не реализации.

| № | Проверка | Статус | Основание |
| --- | --- | --- | --- |
| 1 | Цель/outcome | PASS | §1, S1–S7 |
| 2 | AS-IS | PASS | Классы, strings, menu и live read-only boundary §2 |
| 3 | Корневая проблема | PASS | Подтверждённый recovery дефект; индивидуальная storage причина не выдумана |
| 4 | Design goals | PASS | §4 |
| 5 | Non-Goals | PASS | §5 |
| 6 | Responsibility | PASS | §6.1 |
| 7 | Integration | PASS | §8 |
| 8 | Invariants | PASS | §7, revision и lease guards |
| 9 | Errors/recovery | PASS | §6.2, §6.4 |
| 10 | Performance | PASS | Async forced task read без cold full init |
| 11 | Data/state | PASS | §9, transient UI state |
| 12 | Compatibility/migration | PASS | §10, JSON/schema без изменений |
| 13 | Rollback | PASS | Полный локальный code revert, reload без записи |
| 14 | AC | PASS | AC1–AC8 |
| 15 | AC evidence | PASS | Matrix и negative/race tests |
| 16 | Commands/stop | PASS | §11, runner/SDK preflight |
| 17 | Plan/dependencies | PASS | §13 |
| 18 | Decisions/questions | PASS | §6.5/§14; phase gate отдельно |
| 19 | Scale/form | PASS | Expanded, storage/cache uncertainty |
| 20 | Profiles | PASS | §15, mandatory UI/video/full tests |

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель/границы | 5 | Исходная жалоба, разрешённый локальный outcome и Non-Goals |
| AS-IS | 5 | Проверен flow; production repro boundary честно ограничен |
| TO-BE | 5 | Action, state, merge и structured errors описаны |
| Безопасность | 5 | Read-only reload, no autoretry/migration, rollback |
| Проверяемость | 5 | UI/read-back/failure/race matrix и video plan |
| Автономность | 5 | Нет design choice, требующего пользователя |

30/30 — оценка содержания плана; не approval и не доказательство реализации.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / workflow | applicable | Есть ли смена статуса/recovery без обхода правил? | PASS | S1/S3 и policy guard |
| UX / designer | applicable | Действие доступно после toast, error понятен? | PASS | Wireframe, постоянная кнопка |
| Tester / validation | applicable | Проверяется ли настоящий отказ и повторная запись? | PASS | RED/GREEN, read-back, unknown/races |
| Developer / architect | applicable | Cache bypass/merge/revisions/lifecycle coherent? | PASS | Detached/typed reads, event epoch и no resurrection |
| Delivery / operations / security | applicable | Не смешаны code, install и user data? | PASS | Local-only synthetic evidence, no live writes |

### Post-SPEC Review

- Scope/Evidence pass: прочитаны owner-documents, canonical template, local override, appautomation skill, status picker/ViewModel/storage facade/domain command/validation/interfaces, карточка и RU/EN resources, UI Authoring/TestHost, runner project files, recording script параметры; выполнен read-only live data scan.
- Contract pass: исходное поручение сведено к S1–S7 и AC1–AC8; сохранены policy/lease, edits и source guards. Phase SPEC допускает изменение только этого файла.
- Adversarial risk pass: forced cache bypass, pending save failure before status, unknown persisted result, missing versus corrupt, double click, changed source и deleted task покрыты планом. Постоянные причины отказа не обещано лечить refresh.
- Role-Based pass: таблица выше; отдельный reviewer `/root/spec_review` подтвердил UX/validation/architecture/operations contract. Фактический child sandbox `danger-full-access`, approval `never`: технически enforced read-only reviewer недоступен. Он выполнял только чтение по ownership, но результат учтён как отдельный adversarial fallback, а не технически read-only review. Основной агент дополнительно сверил Load/null/error, autosave и revision guards непосредственно с кодом.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Server read | Ordinary Load возвращает null также при backend/auth/transport/mapping failure | Typed recovery read и отдельный not-found; negative tests | fixed in plan §6.2/AC6 |
| HIGH | Local ordering | Bare forced Load может назначить старым bytes новую revision после watcher update/delete | Detached read, generation/mapping/hash guard до publication; domain+facade tests | fixed in plan §6.2/AC7 |
| HIGH | Server ordering | Hub updates без revision, default 0 не защищает от late read response | Source-lifetime event epoch; atomic check/apply; hub update/delete races | fixed in plan §6.2/AC7 |
| MEDIUM | Autosave/lifecycle | Недостаточно explicit already-running autosave cases | Delayed success/failure → reload; no extra save; deleted dirty object не final-saves | fixed in plan §6.2/AC4/AC7 |

- Fix and re-review: typed server read, local detached read, server epoch и autosave/final-save случаи добавлены в дизайн, файловую таблицу и AC/test matrix. Reviewer перечитал затронутые разделы и вернул PASS по содержанию; open BLOCKER/HIGH/MEDIUM отсутствуют. Никакая реализация этим не подтверждается.
- Depth checklist: outcome/scope и Non-Goals сохранены; S1–S7 сопоставлены AC1–AC8; unknown/missing/read failure/local and server races покрыты планом; никакие existing policy guards не ослаблены; UI wireframe/video/runner команды доступны; source и installed version различаются. Структурная проверка документа: секции 0–20 на месте, все AC объявлены и отображены в matrix, whitespace/conflict markers отсутствуют. `git diff --check` прошёл; untracked SPEC отдельно проверена текстовым scan, поскольку tracked diff её не включает.
- Scope/unrelated changes: основной агент изменил только эту SPEC. Во время работы появился чужой untracked `specs/2026-10-02-emoji-title-refresh.md`; он не изменялся и не включён в outcome.
- No-findings justification: после адресных исправлений и re-review новых обязательных нарушений плана не обнаружено; AS-IS подтверждён точками вызова, UI/resources и storage кодом. Тест-план покрывает контрпримеры, обнаруженные reviewer, а не только счастливый путь. Runtime evidence и production repro не выданы за выполненные.
- Manual-review challenge: «Кнопка есть, но статус не меняется / теряется edit / stale read воскресит задачу». Ответ плана проверяется AC1/AC3, AC4 и local/server AC7; фактические доказательства обязательны в EXEC.
- Stop decision: **PASS для фазы SPEC**, можно запросить exact approval. Post-EXEC PASS отсутствует. Требуется фраза пользователя «Спеку подтверждаю».
- Остаточный риск: индивидуальный отказ не воспроизведён; установленный бинарник отличается от source HEAD; никаких claims об исправленном installed app.

### Post-EXEC Review

#### Scope reviewed / Evidence inspected

Проверены approved SPEC, production diff в `TaskItemViewModel`, `MainWindowViewModel`, `TaskStatusPicker`, `MainControl`, RU/EN resources, `ITaskStorage`, typed reload result, File/Server/Unified storage; unit/UI regression diff, Authoring/TestHost, Headless/FlaUI adapters и recording driver. Instruction owners: central stack из §0, `review-loops`, `github-delivery-policy`, `commit-message-policy`, local UI override; skills `appautomation`, `run-tunit-tests`, `record-app-screen`.

Просмотрены финальные test logs/HTML и реальные native before/error/after-retry PNG. Все восемь итоговых rendered RU/EN × Light/Dark × 1400/760 PNG просмотрены; они содержат непустую читаемую карточку. Для GitHub сохранены выбранные синтетические изображения и проверяемые выдержки логов: [evidence](../docs/testing/task-card-status-recovery/README.md). Полные локальные материалы — `artifacts/status-recovery/` implementation worktree.

#### Scope/Evidence pass

Изменён один связный recovery flow: постоянная кнопка и сообщение, typed authoritative read, сохранение pending editor fields, запрет status/autosave после неизвестного результата до успешного read-back, guards от stale reload и resurrection. MainWindow удерживает исчезнувшую открытую карточку для копирования draft; явная навигация/удаление используют существующие команды. JSON/schema, installed app и пользовательские файлы не менялись. Исходный HEAD и свежий `origin/main` 2026-10-03 совпадают: `46711e60`. Посторонний emoji bugfix остаётся вне worktree/diff.

| Проверка | Фактический результат | Evidence |
| --- | --- | --- |
| Обычная Desktop-сборка latest production | PASS: 0 предупреждений / 0 ошибок | `pr-desktop-build.log` |
| Full Main 2026-10-02 | 1193/1194 PASS, 1 FAIL, 0 SKIP, 32м34с; до последнего раннего Missing guard | `final-main-full.log`, HTML |
| Неуспешный Workspace executable spec отдельно | 1/1 PASS, 38с; причина full-suite failure не установлена | `pr-workspace-focused.log`, HTML |
| Headless затронутый класс | 12/12 PASS, 1м57с | `final-headless-class.log`, HTML |
| Native FlaUI recovery | 1/1 PASS; `FlowCompleted=true`, `FailureIds=[]`; JSON NotReady и ровно одна новая запись истории | `final-flaui.log`, observations, inspected PNG |
| Rendered matrix | 8/8 PASS, 3м01с; Skia frame и non-flat pixels обязательны | `final-rendered-matrix.log`, inspected PNG |
| Missing dirty card через настоящий MainControl latest | 1/1 PASS, 8с; нет final-save/resurrection, текст виден, навигация работает | `pr-missing-ui-final.log`, HTML |
| Full Headless latest | 52/52 PASS, 0 SKIP, 3м40с; предыдущие/следующие классы проходят после recovery | `pr-headless-final-full.log`, HTML |
| Status/reload ViewModel latest | 41/41 PASS; два delayed TaskNotFound race variants после RED | `pr-vm-fixed.log`, HTML |
| Whitespace / unrelated changes | `git diff --check` PASS; только файлы recovery и evidence | Git diff/status |

Пара проверенных before/after MP4 не получена: recording падал на readiness/частоте кадров/раннем выходе ffmpeg. SPEC допускает objective fallback; использованы чистый baseline RED с `RefreshUnavailable`, итоговый native GREEN и persisted read-back, просмотренные PNG и 8/8 rendered matrix. Неудачные MP4 не названы evidence успешного flow. Полные причины и пути сохранены в evidence README; screenshot fallback не подменяет обязательные тесты.

#### Contract pass

| Scenarios / AC | Сверка результата |
| --- | --- |
| S1/S3, AC1–AC3 | Допустимые переходы/recovery проверены unit и Headless/FlaUI; ошибка остаётся в карточке, после read-only Reload нужен явный повтор. Native read-back подтверждает запись и отсутствие дублированной истории. |
| S2/S6, AC2/AC6 | Policy/graph/agent/marker denied kinds имеют отдельные безопасные сообщения; backend/auth/null/map failures не принимаются за Missing; storage/domain guards сохранены. |
| S4, AC4 | Pending title/description/criteria/planning/repeater fields сохраняются через existing merge; running autosave success/fault ожидается, edit during read сохраняется; один reload не создаёт save/status writes. |
| S5, AC5 | Unknown outcome блокирует новые status/autosave/final-save до authoritative read-back; actual stored status/historical entry принимается без автоматического повторения команды. |
| S7, AC7 | Local detached bytes/hash/mapping/generation/revision guard, retry exhaustion; revision=0 server event epoch; source disposal и confirmed deletion covered. Empty/corrupt/access failure не считается deletion. Missing dirty draft остаётся copyable и не записывается обратно. |
| AC8 | Recovery UI пройден в Headless и native; 8/8 locale/theme/width rendered matrix просмотрены. Обязательный full-suite gate остаётся открытым. |

Non-Goals сохранены. Expected User Review Objections из §12 сопоставлены проверкам: кнопка и реальная повторная запись — native; потеря edit — VM/UI tests; исчезновение toast — permanent card error; неизвестный статус — read-back tests; установленная версия — отдельно от source. Индивидуальная production причина остаётся неизвестной и не объявлена устранённой.

#### Adversarial risk pass / findings

Отдельный reviewer `/root/spec_review` выполнил adversarial fallback и адресный re-review. Его фактический sandbox `danger-full-access`, approval `never`: технически read-only review недоступен; writable pass не называется независимым. Основной агент проверил соответствующие counterexamples и тесты непосредственно. Reviewer выявил позднее применение TaskNotFound и подтвердил адресное исправление; итог остаётся NEEDS-FIX из-за полной Main-серии.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Read/UI thread | Directory lock/hash/read запускались до первого async yield и могли блокировать UI | Выполнить primitive через Task.Run; проверить hook не на UI thread | fixed; focused test и full Main соответствующие tests PASS |
| HIGH | Missing draft/lifetime | Confirmed watcher delete закрывал открытую dirty card; disposed CTS мог падать при Seal | Удержать detached missing card; исключить запись и guard disposed CTS; настоящий MainControl regression | fixed; RED→GREEN 1/1 и full Main PASS для affected tests |
| HIGH | Empty/corrupt source | Нулевая длина трактовалась как deletion | Только FileNotFound/DirectoryNotFound означают Missing; invalid source оставляет cached projection и write validation | fixed; storage/VM tests PASS |
| HIGH | Full Headless/session isolation | Shared startup получил Dispatcher предыдущего worker; worker fault оставил Dispatch completion pending; async handoff запускал следующий synchronous launch на worker | Worker-local reset, bounded synchronous bootstrap/handoff, повторить весь suite | fixed; full Headless 52/52 PASS |
| HIGH | Status result / missing source | TaskNotFound применялся после post-command editor drain, который мог восстановить удалённую задачу | Установить Missing до drain; delayed status→edit→Missing с/без concurrent Seal | fixed; 2/2 RED до guard, затем оба GREEN; VM class 41/41 |
| MEDIUM | Dirty-read test clock | Assert no-write конфликтовал с штатным 10ms user autosave, созревшим во время setup/read | Явный long throttle при создании VM с immediate restore; отдельный matured-autosave case сохранить | fixed; VM class 41/41 PASS |
| MEDIUM | Full Main validation | Workspace scenario упал на ShiftDelete: Task4 осталась на диске | Разобрать full-suite failure и подтвердить общий gate | open; isolated rerun PASS не является full PASS |

#### Role-Based pass

| Role | Сверка | Verdict |
| --- | --- | --- |
| Business analyst / workflow | Допустимый статус записывается, запреты сохраняются, повтор явный | PASS по focused evidence |
| UX / designer | Постоянное действие, безопасная ошибка/details, читаемые RU/EN theme/width frames, copyable missing draft | PASS по inspected UI evidence |
| Tester / validation | RED/GREEN и storage/race/dirty/unknown coverage есть; full gates не закрыты | NEEDS-FIX |
| Developer / architect | Typed detached reads, revision/epoch/source/lifetime, no resurrection; late TaskNotFound guard проверен | PASS адресного review; общий validation gate открыт |
| Delivery / operations / security | Синтетические данные, rollback без migration, installed app не менялся; draft нужен до закрытия gates | PASS для draft publication, не для ready/merge |

#### Fix and re-review / Depth checklist

После review исправлены off-UI read, empty-file classification, missing-card retention, disposed-CTS Seal и поздний TaskNotFound guard. Перепроверены corresponding unit/semantic UI cases, Missing card regression, native recovery и rendered matrix; успешные focused проверки не выданы за full Main green. Отдельно повторены Desktop build и упавший Workspace spec. Повторные full Headless/dump inspections локализовали shared startup и inline-handoff failures; worker reset и bounded synchronous dispatch дали итоговые 52/52. Для последней VM-правки пройдены весь класс 41/41 и настоящий missing-card UI 1/1, Desktop build 0/0. Полный Main latest ещё не подтверждён.

Depth checklist: outcome/scope/Non-Goals, все S1–S7/AC1–AC8, actual persisted status/history, editor pending fields, running save faults, read failure versus deletion, stale local/hub reads, lifetime/disposal, UI busy/menu states, safe enum details, localization/layout/real pixels, UIA evidence, schema/rollback и publication boundary сверены. Open validation findings перечислены, unrelated edits исключены. No-findings justification не применяется: открытые findings есть.

Manual-review challenge: «Кнопка есть, но повтор не пишет / reload стирает draft / stale read воскресит задачу». Native storage read-back и VM/File/Unified race tests отвечают на эти контрпримеры; full Headless/Main series остаются обязательной дополнительной проверкой.

#### Stop decision

**NEEDS-FIX.** Post-EXEC completion/ready for review не подтверждены из-за открытого full Main gate. Пользователь 2026-10-03 отдельно поручил «Оформи pr», поэтому разрешены commit/push и **draft PR** с честным evidence и открытой проверкой согласно GitHub delivery policy. Merge/release/install этим не разрешены.

## Approval

Получена точная фраза пользователя: «Спеку подтверждаю» (2026-10-02).

## 20. Журнал действий агента

| Фаза/событие | Решение и основание | Evidence/остаток работы | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC: диагностика 2026-10-02 | Подтверждён recovery UX gap и потеря некоторых denied reasons | Исходники + структурный read-only scan; exact production failure пока неизвестен | Independent review плана | Исходная жалоба, без exact approval | Только эта SPEC |
| SPEC: review/re-review 2026-10-02 | Закрыты 3 HIGH и 1 MEDIUM в плане; separate adversarial fallback из-за writable child sandbox | PASS плана, AC matrix и текстовая проверка; code/tests не менялись | Запрос exact approval | Exact approval отсутствует | Только эта SPEC |
| EXEC: начало 2026-10-02 | Exact approval получен; worktree отделяет параллельный emoji bugfix | HEAD совпадает с approved baseline; отдельная ветка | Failing regression и реализация | «Спеку подтверждаю» | Worktree и текущая SPEC |
| EXEC: реализация и review 2026-10-02 | Typed reload, UI recovery, pending/lifetime/epoch guards; исправлены найденные off-UI/missing/empty cases | Native 1/1, Headless class 12/12, rendered 8/8, missing card 1/1; full Main 1193/1194 | Разобрать full-suite findings | Exact approval действует | Исходники/tests, локальные логи/PNG |
| Delivery: 2026-10-03 | Пользователь отдельно поручил создать PR; draft обязателен пока gates открыты | Desktop 0/0, Workspace isolated 1/1; full Headless startup fault диагностирован | Commit/push/draft PR после review, attach artifact | «Оформи pr» | SPEC и docs/testing/task-card-status-recovery |
| EXEC: адресный fix/re-review 2026-10-03 | Закрыты late TaskNotFound resurrection и Headless startup/handoff; test clock отделяет reload от user autosave | Full Headless 52/52, VM class 41/41, missing UI 1/1, Desktop 0/0; Main full gate открыт | Опубликовать draft с открытым Main gate | PR отдельно разрешён | Итоговые логи и SPEC |
