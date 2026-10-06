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
- View: доступное действие в меню ⚙ и объяснение результата. Локализованные сообщения выбираются по structured reason.
- UI Authoring/TestHost: детерминированная fixture отказа и recovery; Headless/FlaUI адаптеры проверяют реальную карточку.

### 6.2 Детальный дизайн

1. Добавить reload capability на уровне `ITaskStorage`/`UnifiedTaskStorage`. Результат различает успешный snapshot, физическое отсутствие и ошибку чтения. Метод не создаёт новую `Update`/`Save`/status operation и не запускает миграцию. Existing journal recovery под storage lock сохраняется: он может завершить ранее начатую транзакцию, поэтому read-only означает отсутствие новой бизнес-записи, а не обещание неизменных filesystem bytes при pending journal. Default для неподдерживающего storage возвращает контролируемую ошибку, а не успешный no-op. Конкретное имя result-типа — внутренняя деталь; wire JSON/task model не меняются.
2. Локальное чтение выполнить через узкий typed reload primitive в `FileStorage`/`FileTaskStorage`, а не bare `Load(forced:true)`. Под existing directory/command boundary прочитать detached snapshot и диагностику mapped source-файла без преждевременной публикации. До чтения захватить source/watcher generation и mapping; перед публикацией проверить их и актуальность source hash в согласованном cache/domain boundary. Изменение/удаление во время read не может назначить старым bytes новую revision: stale snapshot отклонить и перечитать/применить newest watcher state. Retries ограничить; непрерывные внешние изменения завершаются ReadFailed с доступной повторной попыткой и снятым busy. Не менять ordinary `Load` для других consumers без доказанной необходимости. При null/empty проверить diagnostics и физическое отсутствие mapped file: пустой/повреждённый/недоступный файл — ReadFailed, не Missing. Не удалять UI snapshot на основании одного null. В `ServerStorage` добавить узкий typed recovery read напрямую через существующий ServiceStack client и mapper: только подтверждённый backend not-found даёт Missing; network/auth/mapping error и неполный/невалидный response дают ReadFailed. Existing swallowing `Load` нельзя использовать как доказательство удаления. Связи и доступность синхронизируются существующим путём; ограничиться необходимым read-back, не выполнять холодный full-directory init.
3. Reload и status согласуются существующим command/cache boundary; повторное нажатие во время операции не запускает конкурентное обновление. UI не блокируется. Edit во время чтения сохраняется как pending local field; результат применяется только к исходным task/source/context и с актуальной revision. Для server recovery захватить source-lifetime cache/event epoch до запроса; server hub Saved/Removed, status/autosave cache updates и dispose/source switch изменяют этот epoch до применения соответствующих событий. Проверку epoch и применение read response выполнить в одном cache synchronization boundary. Изменившийся epoch запрещает позднему response перезаписать более новое hub/cache update/delete; discard/re-read ограничены тем же retry budget. Default server StorageRevision=0 не является protection и не сравнивается как локальная backend revision. Не менять semantics существующих hub/CRUD consumers ради новой глобальной versioning схемы.
4. Reload вызывает existing snapshot merge, сохраняя все pending editable fields: title, description, planning, importance, wanted, repeater, completion criteria. Сам reload не должен пытаться предварительно сохранить dirty revision: иначе временная ошибка записи сделает recovery недоступным. Уже допущенный autosave согласовать с чтением: дождаться его завершения, но обработать fault отдельно и продолжить reload; failed editable revision остаётся dirty. На время reload новые autosave producers не должны конкурировать с read/merge. После него штатный autosave, вызванный пользовательским edit, может продолжиться с актуальным authoritative status; reload не создаёт отдельный save/retry для dirty полей. Последующий осознанный status command выполняет штатный editor drain и запись. Проверить delayed autosave success и failure до reload: нет повторной записи, pending поля и свежий статус сохранены.
5. По уточнению пользователя разместить `Обновить` первым пунктом меню шестерёнки `CurrentTaskActionsMenuButton`, без отдельной кнопки в command bar. Accessible name `Обновить задачу`, tooltip и стабильный `CurrentTaskReloadButton` сохраняются на MenuItem для совместимости automation. Пункт доступен и без ошибки, чтобы действие не исчезало вместе с toast. На reload/status он disabled; закрытая/сменённая карточка не принимает старый результат.
6. Сохранить ошибку status/reload в состоянии карточки до следующего успешного восстановления/операции. В коротком сообщении обозначить причину; для диагностики дать детали structured error через раскрываемые `Подробности` без сырых JSON, настроек, токенов или stack trace. Отказ в доменном правиле не называется ошибкой записи. Не добавлять общую подсистему логирования.
7. `OutcomeUnknown` сообщает, что статус ещё не подтверждён; после успешного reload показать фактический статус. Не предлагать автоматическое повторение и не утверждать, что запись отменена. `StorageFailed` предлагает доступное обновление и повторный выбор. `ValidationFailed` сообщает о некорректном графе; `ExecutionStateDenied` — об активном агенте; `TaskNotFound` — об отсутствии задачи. Обновление не обещает снять постоянные ограничения.
8. После успешного refresh убрать прежнюю storage/reload ошибку, пересчитать status options и показать подтверждённый статус. При ошибке чтения сохранить последнюю карточку и правки, оставить пункт меню для новой попытки. При подтверждённом удалении показать «Задача удалена из хранилища», запретить status, autosave и lifecycle final-save для этого объекта, не воскресить его. Dirty текст оставить доступным для копирования; встроенная навигация остаётся доступной. Не обещать сохранение удалённой задачи или persistence несохранённых правок после закрытия приложения.

Visual planning artifact — текстовый wireframe в этой SPEC:

```text
┌ Карточка задачи ─────────────────────────────────────┐
│ [значок статуса ▼] Название                          │
│ …существующие метаданные…                  [⚙]       │
│                         меню ⚙: [⟳ Обновить]       │
│                                 [Переместить…]     │
│                                 [Архивировать]     │
│                                 [Удалить]          │
│ Ошибка: не удалось подтвердить статус.              │
│ Нажмите «Обновить», затем проверьте текущий статус.  │
│ [Подробности ▸]                                    │
│ …существующие описание, критерии, планирование…      │
└─────────────────────────────────────────────────────┘
```

Состояния: обычное — error area скрыта, обновление доступно через ⚙; busy — пункт refresh и status disabled, данные остаются читаемыми; error — краткая причина и доступное восстановление через ⚙; successful reload — фактический статус и сохранённые local edits; missing — объяснение и disabled status/reload. Проверить узкую карточку, light/dark и RU/EN: меню/сообщение не перекрывают редактор после закрытия popup.

UI video evidence: repository имеет `scripts/record-status-contract-evidence.ps1`, FlaUI и ffmpeg/ffprobe. После approval добавить recovery-сценарий и обеспечить recording hook на принятом harness. Сохранить failing/repro `before` и passing `after` на synthetic tasks, в `artifacts/task-card-status-recovery/`, local-only. Existing terminal/unarchive video не доказывает этот дефект. Fallback допускается только при конкретной технической причине; next-best — отрисованные Headless PNG и test/log report. Без реально записанного/просмотренного артефакта не заявлять визуальную проверку.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result | Evidence required | AC |
| --- | --- | --- | --- | --- |
| S1 | В карточке выбрать допустимый статус | Значок/выбранный статус, storage и история согласованы | UI assertion и file read-back | AC1 |
| S2 | Status command получает временный storage failure | Понятная ошибка и доступный refresh через ⚙; успешного статуса нет | RED/GREEN regression и before/after | AC2, AC3 |
| S3 | После устранения временного сбоя открыть ⚙, выбрать «Обновить», затем выбрать статус | Фактический статус перечитан, новая запись подтверждена | End-to-end fixture recovery через menu item | AC3 |
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
| Recovery action | user | Первый пункт «Обновить» в меню ⚙ | 1.0 | Доступность и disabled-состояния проверяются UI | Нет; прямое уточнение пользователя |
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

`TaskStatusPicker → TrySelectStatusOptionAsync → ExecuteStatusOperationAsync`; `CurrentTaskActionsMenuButton → CurrentTaskReloadButton (MenuItem) → ReloadCommand → storage read → existing snapshot merge/cache synchronization`. Ошибка обновляет presentation state; следующая успешная операция снимает устаревшую ошибку. Existing watcher обработка остаётся независимым путём изменения cache.

## 9. Изменения модели данных / состояния

Только transient ViewModel presentation state: busy, last operation summary/details, missing/recovery. Reload result/capability — внутренний .NET facade contract, не task JSON и не persisted settings. Pending editor revision, status history и source revisions сохраняют значение.

## 10. Миграция / Rollout / Rollback

Миграция отсутствует. Локальный код можно откатить целиком; refresh не создаёт новую бизнес-запись, settings не меняет. Штатное завершение ранее начатой транзакции не откатывается произвольным возвратом cache. Внешний rollout не входит в поручение. Existing desktop продолжает работать до отдельно согласованной установки.

## 11. Тестирование и критерии приёмки

- **AC1:** допустимый переход из реальной карточки подтверждается storage, обновляет icon/options и сохраняет одну корректную history entry.
- **AC2:** ошибка не предлагает недоступного действия; обновление доступно через меню ⚙ и отсутствует как отдельная кнопка карточки, reason соответствует фактическому failure, busy не оставляет карточку навсегда disabled.
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
| AC2 | Reason mapping и refresh через ⚙ | Error area видна, menu item доступен; отдельной кнопки нет | before/after PNG/video | То же |
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
| «Сообщение исчезло, где обновлять?» | Toast временный | Постоянно доступное меню ⚙ и error state карточки | mitigated |
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
| Recovery | Невыполнимое «обновите» | Пункт «Обновить» в ⚙, authoritative reload, explicit retry |
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
| UX / designer | applicable | Действие доступно после toast, error понятен? | PASS | Wireframe, постоянно доступное действие |
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

Первый блок ниже фиксирует исходное исправление до переноса Reload в ⚙ (commit `11808873`). Актуальная проверка размещения приведена в отдельном дополнении; прежний native PASS не считается подтверждением нового меню.

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

### Дополнение post-EXEC: размещение в ⚙, 2026-10-03

Scope: только размещение существующего Reload первым MenuItem в меню ⚙, его UI assertions, адаптеры recovery-сценария и evidence. Storage/VM contract и approved risk не меняются. Exact approval и отдельная PR authorization действуют; по quest-mode прямое уточнение размещения сохраняет EXEC. `origin/main` повторно проверен: `46711e60`.

| Проверка | Результат | Evidence в `artifacts/status-recovery/` |
| --- | --- | --- |
| Отсутствие отдельной кнопки, actual menu binding/name, busy | RED на прежнем заголовке → GREEN 1/1 | `menu-red.log`, `menu-action-green.log` |
| Full Headless после усиления ожидания error clear | 52/52 PASS, 0 SKIP, 3м09с | `menu-headless-final-full.log`, HTML/TRX |
| Failure → ⚙ → Reload → явный retry | PASS внутри full Headless; `FlowCompleted=true`, `FailureIds=[]`; read-only Reload и JSON NotReady/history +1 | `menu-headless-final/status-contract-recovery-observations.png.json` |
| Rendered RU/EN × Light/Dark × 1400/760 | 8/8 PASS, 1м01с; все восемь новых menu frames просмотрены | `menu-rendered-matrix.log`, `menu-rendered/*.png` |
| Missing dirty card с пунктом меню | 1/1 PASS, 11с; disabled menu item, copyable draft, no resurrection | `menu-missing-final.log` |
| Desktop/phone layout с четырьмя пунктами ⚙ | 4/4 PASS: desktop 1, phone 3 | `menu-layout-desktop.log`, `menu-layout-phone.log` |
| Desktop build после переноса | PASS, 0 предупреждений / 0 ошибок | `menu-desktop-build.log` |
| Native recovery последнего меню | Не подтверждён; SendInput получил Win32 Access denied при первом status click, до меню. Текущий Windows-сеанс Disc/LogonUI; повтор до появления active desktop не имеет нового основания | `menu-native.log` |

Отдельный adversarial fallback `/root/spec_review` выявил MEDIUM: enabled status не доказывал выполнение Reload, поскольку статус уже доступен после StorageFailed. Исправление требует одновременно enabled status и исчезновения постоянной ошибки до retry. Headless читает `HasTaskOperationError`, FlaUI проверяет error UI. Последний full Headless подтвердил усиленный сценарий. Native screenshot adapter сохраняет фокус открытого меню, чтобы capture не закрывал его; этот adapter компилируется, но native flow остаётся открытым.

Адресный source re-review: PASS, MEDIUM driver assertion CLOSED, новых продуктовых BLOCKER/HIGH/MEDIUM не найдено. Фактический child sandbox `danger-full-access`, approval `never`; reviewer выполнял только чтение, это adversarial fallback без enforced read-only. Scope/Evidence и Contract pass подтверждены focused UI tests и rendered frames. Role-Based: UX/workflow/architecture PASS адресно; tester/delivery сохраняют NEEDS-FIX до native/menu и full Main gates.

**Общий post-EXEC verdict остаётся NEEDS-FIX.** Открыты прежний full Main finding и native recovery последнего меню в active desktop. Обновление разрешённого draft PR #314 допустимо с точным разделением нового Headless/rendered evidence и прежнего native результата. Новые снимки/логи сохранены в [evidence README](../docs/testing/task-card-status-recovery/README.md).

### Интеграция свежего main, 2026-10-04

Координатор передал поручение пользователя продолжить интеграцию в рамках прежнего EXEC. Рабочая копия recovery перед merge была чистой, HEAD `41bd9a78`; fetched `origin/main` — `5a780b2e` (#313 AppAutomation 1.9 и #315 emoji). Merge выполняется только в `fix/task-card-status-recovery`, не в main. Approval/PR authorization действуют; merge PR, install и release не разрешены этим действием.

Два конфликтных файла разрешены по смыслу: canonical metadata/pumping fixture из main в `FileStorageTaskStatusTests` сохраняется вместе с новыми Reload cases; FlaUI объединяет pointer lifecycle/owned tooltip из #313 и recovery/menu flow. Все физические status/reload clicks используют `DesktopPointer`, stable ID и error-clear assertion сохранены. Канонический API-based `WaitForTaskInPageAsync` остаётся из main.

Importance передал `artifacts/task-importance/upstream-fixtures.patch` со snapshot manifest и full evidence 1150/1150. Его wrapper-delete hunk перенесён отдельно: ожидание cache count и отсутствия файла вместо autosave debounce. Старый ShiftDelete hunk не применён, поскольку main уже содержит согласованный `WaitForDeletedTasksAsync`, проверяющий cache и storage для single/batch удаления. Остальные старые файлы целиком не копируются.

В интеграционном Main у Importance воспроизвёлся `SwitchRemoteConnectionTypeCommand_KeepsSshKeyRequirementWhenNoKeyIsSelected`. Перенесён авторский CLI patch ожидания завершения typed `ReactiveCommand.Execute().ToTask().WaitAsync(3s)` без изменения product-кода; конечные SSH-key assertions сохранены. SHA256 patch: `DCADF0DF86DD74D720B9D266E12E3B975EC24DDC31407813270EF916F6C3F51E`. Адресный case прошёл 1/1; Main после этого пересобран, 51 warning / 0 errors. Этот результат не заменяет full-suite gate.

Последний опубликованный HEAD `41bd9a78` имеет CI Main 1196/1196 и Headless 52/52 PASS. Это снимает устаревшее утверждение о последнем full Main failure для той головы, но не подтверждает текущую интеграцию. После merge нужны Desktop/Main/Headless builds, полный Main и Headless, targeted recovery/emoji/cache/delete checks и native menu recovery с inspected evidence. Full/native запускаются по очереди координатора после Importance; до передачи слота выполняются diff/review/build preparation. Продуктовые recovery semantics и схема не меняются; безусловный flush перед Reload не вводится.

Адресный source re-review выявил MEDIUM: capture helper менял foreground через Focus внутри read/assert-only callback `DesktopPointer.HoverAsync` 1.9. Убран Focus из helper; capture теперь только читает кадр и сохраняет artifact, открытое меню не закрывается helper-ом. Reviewer подтвердил CLOSED по актуальному source, source verdict PASS; фактический child sandbox writable, поэтому это adversarial fallback без enforced read-only.

Подготовительные проверки интеграционного снимка: Main build PASS, 95 существующих warnings / 0 errors; Headless/FlaUI и обычный Desktop build PASS, 0/0. Wrapper-delete, main ShiftDelete, прежний Workspace executable scenario и canonical cache hydration прошли 4/4. Rendered RU/EN/theme/width menu matrix прошла 8/8, все восемь новых PNG просмотрены. На этом этапе полные/native проверки ожидали слот Importance; адресное evidence не выдавалось за общий PASS. Итог следующего этапа приведён ниже.

### Итог интеграционных проверок, 2026-10-05

#### Scope / contract pass

Approved outcome и Non-Goals сохранены. Recovery production semantics относительно `41bd9a78` не расширялись; входящие AppAutomation/emoji изменения сохранены из main. S1–S7/AC1–AC8 сопоставлены storage/VM/UI coverage и реальному menu recovery. Task status/history read-back проверен native-сценарием; read-only Reload сохраняет исходные bytes, изменение требует явного retry. Guard и pending-edit contracts сохраняются; source switch/disposal/cache/API/delete cases входят в общую серию. Индивидуальная production причина остаётся неизвестной.

| Проверка | Итог |
| --- | --- |
| Full Main | **1229/1230, 1 FAIL, 0 SKIP**, 36м03с. Единственный FAIL — `WorkspaceTreeCommandsScenario_ExecutesFeatureSteps`, `PasteOutlineCommandWorked=false` |
| Full Headless | **52/52 PASS, 0 SKIP**, 3м53с; recovery observations `FlowCompleted=true`, `FailureIds=[]` |
| Native affected flows | **4/4 финальных PASS** в отдельных запусках: menu failure/reload/retry; RU Dark future; RU Dark blocked+owned tooltip; EN Light terminal picker/unarchive |
| Rendered matrix | **8/8 PASS**, RU/EN × Light/Dark × 1400/760; все восемь новых menu PNG просмотрены |
| Адресные общие fixtures | Wrapper-delete, ShiftDelete, Workspace graph commands, cache hydration и SSH wait **5/5 PASS**; все эти cases также PASS в full Main |
| Build | Main после failure diagnostics: **PASS, 51 существующее предупреждение / 0 ошибок**; обычный Desktop, Headless и FlaUI: **PASS, 0/0** |
| Workspace paste с диагностикой | **1/1 isolated PASS**, 13с; этот результат не закрывает full gate |

Все шесть новых native PNG просмотрены. Логи/снимки опубликованы рядом с [evidence README](../docs/testing/task-card-status-recovery/README.md); локальные HTML/TRX, invocation/binary hashes и snapshots сохранены в `artifacts/status-recovery/integration-2026-10-04/`. Проверенная MP4 pair отсутствует: прежние recording attempts не прошли FPS/readiness/ffmpeg проверки; approved fallback дополнен новыми native PNG/read-back и rendered matrix. Full/native validation processes завершены, сообщение «Слот полных проверок освобождён» передано в чате для следующего агента.

#### Adversarial pass / fix and re-review

Фактический reviewer sandbox `danger-full-access`, approval `never`; `/root/spec_review` выполнял только чтение как adversarial fallback, без технического enforced read-only. Source re-review подтвердил semantic union и авторский typed SSH command wait. Новых продуктовых BLOCKER/HIGH/MEDIUM не найдено.

| Severity / area | Finding / действие | Status |
| --- | --- | --- |
| MEDIUM / Hover callback | Focus внутри read/assert callback убран; helper только читает/saves frame | **CLOSED**, source review и native evidence |
| MEDIUM / Hover action budget | Первый Blocked run получил `PointerOperation.Check` cancellation после успешных owned-tooltip assertions и capture; настоящий popup есть в PNG | **CLOSED**: tooltip wait сохраняет linked 15s deadline, owner/UIA/capture action получает 30s; cleanup/restoration имеет отдельный framework budget. Повтор 1/1 PASS |
| MEDIUM / Paste failure evidence | Bool assertion скрывала, какой из clipboard/count/title conjuncts не выполнен | **CLOSED по source**: failure-only snapshot до следующего add/delete; mock preview/confirmation/errors, cache/persisted tasks, focus/selection. Исходные predicate, 5s и IsTrue сохранены; build и isolated 1/1 PASS |
| MEDIUM / Full Main gate | Workspace paste full-only failure пока не объяснён; total36с включает setup/cleanup и не доказывает превышение import timeout | **OPEN**. Paste/parser/UI route не менялись recovery; related paste tests PASS. Следующее воспроизведение должно дать причину через новую диагностику и новый полный Main |

Ожидание typed tree-command не добавлялось к paste: реальный UI route запускает `async void` import, и completion этой ReactiveCommand не означал бы завершение импорта. Причина сбоя не подменена гипотезой о timeout либо product regression. Изолированная пересдача не названа общим зелёным результатом.

#### Role-based / depth / stop

Business/workflow, UX и developer/architect: **PASS по affected source/evidence**, включая explicit retry, persisted history, menu binding/busy, safe errors, retained dirty/missing draft, stale read/lifetime и прочитанные реальные пиксели. Tester/validation: **NEEDS-FIX**, общий Main gate открыт. Delivery/operations: **PASS для draft publication**, rollback через revert без migration; ready/merge не подтверждаются.

Depth checklist: scope/Non-Goals, AC/scenario matrix, denial versus storage failure, pending edits/unknown outcome, missing/corrupt/access classification, revision/epoch/source/disposal, popup/input lifecycle, localization/layout, persisted read-back, history uniqueness, source/evidence freshness, schema/rollback и publication boundary сверены. Manual challenge «Reload есть, но не работает/стирает draft/автоматически повторяет статус» сопоставлен native/file/VM assertions. Open finding указан явно; no-findings justification не применяется.

**Stop decision: NEEDS-FIX для ready for review.** Опубликовать проверенный интеграционный снимок в уже разрешённый **draft PR #314**, сохранить открытый Main finding и failure-only диагностику. Exact approval и PR authorization действуют. Source review PASS не заменяет validation gate; merge/release/install не выполняются.

### Обновление базы на main 92cf8c1e, 2026-10-05

Пока выполнялась предыдущая validation, main получил #316 Importance. Tested snapshot на `5a780b2e` сохранён отдельным merge commit `35924686`; затем main `92cf8c1e` объединён в recovery-ветку без конфликтов. Approved outcome не расширен. Importance styles, 24 baseline-пары, тесты, CI и canonical fixtures сохранены. Recovery storage/VM guards, dirty/Missing/OutcomeUnknown/revision/source protections и permanent error не менялись относительно checkpoint. Reload остаётся первым пунктом ⚙ с command binding и CanReloadTask.

| Проверка именно union 92cf8c1e | Итог |
| --- | --- |
| Builds | Main **PASS, 95 warnings / 0 errors**; обычный Desktop, Headless, FlaUI **PASS, 0/0** |
| Menu binding/name/busy; external deletion copyable draft; off-UI read | **3/3 PASS** в отдельных запусках |
| Rendered menu RU/EN × Light/Dark × 1400/760 | **8/8 PASS**; все восемь новых PNG просмотрены, Importance виден |
| Importance visual/input/persistence | **3/3 PASS**, включая 24 reviewed component baselines и отрицательные проверки скрытой цифры/горизонтальных buttons |
| Headless menu recovery | **1/1 PASS**, error clear до retry, read-only bytes invariant, NotReady и ровно одна новая запись истории; FlowCompleted=true, FailureIds=[] |
| Desktop/phone card layout | **4/4 PASS**: desktop 1, phone 3 |
| Полный Main/native повтор на новой базе | **PENDING**; текущий согласованный слот у CLI, затем у history. Повтор не запущен; предыдущие 1229/1230 и native4/4 относятся только к 5a780b2e |

Evidence опубликован в [README](../docs/testing/task-card-status-recovery/README.md), filters/timestamps/binary hashes — в [validation snapshot](../docs/testing/task-card-status-recovery/integration-92cf-2026-10-05/validation-snapshot.json). Локальные HTML/TRX и Importance rendered images сохранены отдельно. Source re-review выполнен тем же отдельным adversarial fallback `/root/spec_review` (danger-full-access, approval never; только чтение, без технического read-only enforcement). Из 60 входящих upstream-путей только MainControl.axaml отличается от main recovery-элементами; потери Importance/fixtures нет. Новых source BLOCKER/HIGH/MEDIUM/LOW не найдено. Необычное положение rendered EN Light 760 popup не названо доказательством native positioning; source ShowAt(actions) корректен.

Role/depth pass: workflow/UX/developer — PASS для affected source/targeted evidence и сохранённых AC/guards; tester — NEEDS-FIX. MEDIUM validation остаётся OPEN: причина прежнего full-only Workspace paste failure неизвестна, актуальные полные проверки ещё не выполнены. Исходные paste predicate/5s/assertion и failure diagnostics сохранены. Stop decision — **NEEDS-FIX для ready; PASS для обновления draft PR #314** в рамках прежней PR authorization. Полные/native результаты старой базы не переназначены новой. Video fallback и production-cause limitation сохраняются; merge/release/install не выполняются.

Docs re-review нашёл LOW readability в decoded Importance child stdout (U+FFFD в русских footer labels). Raw logs сохранены локально без угадывания кодировки. Непубликовавшиеся нечитаемые копии заменены [JSON evidence из исходных child TRX](../docs/testing/task-card-status-recovery/integration-92cf-2026-10-05/importance-child-results.json): три names/outcomes/counts/times и SHA256TRX. Основной readable Importance log подтверждает3/3. Product/harness source и прошедшие tests не менялись; LOW CLOSED после проверки JSON/ссылок.

### Финальная интеграция с history 4a2f781e, 2026-10-05

Координатор передал слот после завершения history processes. Main пока `92cf8c1e`; history #312 проверяется на `4a2f781e`. History объединяется только в recovery-ветку для одного полного прогона итогового кандидата. Incoming `123d7fb6` задаёт accessible name из Header; `4a2f781e` перемещает те же экземпляры actions между compact и wide header. Recovery guards/command/error state сохраняются. В existing resize test добавляются Reload instance/command/first-item assertions; rendered recovery matrix проверяет настоящие header ToggleButtons Git history и error Details, а не синтетический Expander.

План validation: последовательные builds, адресные resize/menu/header UI checks, полный Main с failure-only paste diagnostics и полный Headless, затем четыре native affected flows. Rendered matrix выполняется отдельным Skia process. Current published `0da758f6` CI Main1233/1233 и Headless52/52 PASS относится к его exact tree; старый full-only paste cause остаётся неизвестным. Финальный local PASS даст bounded disposition historical finding без заявления о causal fix. Результаты кандидата пока PENDING; main/PR merge не выполняется.

Кандидат сохранён в `8ff819b14ebbaec441fe07c8b7584da8d261ad8d`, parents `0da758f6` и `4a2f781e`, tree `555998d7c506bd4845fdb48bc1b72dc86d7fa226`. Builds: Main95 warnings/0errors; Desktop/Headless/FlaUI0/0. Existing resize с дополнительными Reload assertions1/1 PASS; rendered real-header matrix8/8 PASS; все восемь новых menu PNG просмотрены. Отдельный source re-review `/root/spec_review` подтвердил semantic union и отсутствие новых B/H/M/L; фактический sandbox writable, только read-only действия, adversarial fallback. Main full запущен на этом кандидате, не на main92cf отдельно. Raw invocation содержит неудачный PowerShell unquoted `HEAD^{tree}`; значение tree исправлено отдельным source-snapshot supplement, raw manifest сохранён. Executable/arguments/head/binary hashes были записаны правильно; исходники тестов после build/commit не изменялись.

После SUCCESS всех checks #312 history реально влит в main `9150ac0170313c4a6068888c88b2d9f7e85bd0c7`. Main объединён только в recovery-ветку merge commit `136ac8baa762b35e8ff63184732c3d5fc39ace97`. До и после sync полный tree совпадает с кандидатом `555998d7c506bd4845fdb48bc1b72dc86d7fa226`; повторного build/full Main нет. [Main sync evidence](../docs/testing/task-card-status-recovery/integration-history-2026-10-05/main-sync.json) фиксирует SHA и equality. Headless/native последовательно используют тот же tree после Main PASS. Дальнейшие изменения текущего этапа — только SPEC/evidence/PR description.

#### Финальный post-EXEC review: local validation

Scope reviewed: approved S1–S7/AC1–AC8 и первоначальная жалоба, уточнение про ⚙, runtime source candidate8ff/sync136, diff относительно main9150, original TRX/results/hashes и свежие pixels. Потерь входящих history/Importance/emoji/fixtures нет. Guards, editor drain, UnknownOutcome/Missing/no-resurrection и existing journal boundary сохраняются.

| Проверка текущего tree555998d7 | Итог |
| --- | --- |
| Full Main | **1269/1269 PASS, FAIL/SKIP0, 28м13с** |
| Full Headless | **52/52 PASS, FAIL/SKIP0, 2м43с**; recovery observations FlowCompleted=true, FailureIds=[] |
| Native affected flows | **4/4 PASS**: failure→⚙Reload→explicit retry; RU Dark future; RU Dark blocked/owned tooltip; EN Light terminal/unarchive |
| Resize / actual localized header matrix | **1/1 +8/8 PASS**; те же actions/flyout/Reload, первое место и command сохранены; реальные Details/history HeaderSite корректны |
| Main guards | VM status41/41, typed reload9/9, Unified status17/17; Importance24 states/input/persistence/negative controls входят в full PASS |
| Builds | Main95warnings/0errors; Desktop/Headless/FlaUI0/0 |

Все восемь новых rendered menu PNG и все шесть native PNG просмотрены. [Evidence README](../docs/testing/task-card-status-recovery/README.md), [original-TRX validation snapshot](../docs/testing/task-card-status-recovery/integration-history-2026-10-05/validation-snapshot.json) фиксируют source tree, filters, counters, times и binary/TRX hashes. Full/native процессы завершены, ownedValidationProcesses=0; слот явно освобождён в чате. No-journal native fixture подтверждает unchanged JSON/text на Reload и NotReady/history+1 только после retry; harness сравнивает File.ReadAllText, raw-byte hash отдельно не измеряет. Прежние native/headless формулировки «unchanged bytes» уточняются этим измерительным пределом. Journal recovery остаётся разрешённым завершением прежней операции. Проверенной MP4 pair нет из-за прежних FPS/readiness/ffmpeg failures; принятому fallback добавлены актуальные автоматизированные native PNG/read-back и logs. Установленная версия и пользовательское хранилище не менялись.

Adversarial validation re-review `/root/spec_review` сверил original Main/Headless TRX, exact old paste test, runtime tree equality и binary hashes. Raw tree field исправлен supplement без перезаписи исходника; два U+FFFD — намеренные invalid-surrogate emoji arguments. Naming finding CLOSED по source+actual headers; прежний **full Main blocker CLOSED**. Точный `WorkspaceTreeCommandsScenario_ExecutesFeatureSteps` прошёл **7.7011825s**, failure snapshot не вызван. Historical cause остаётся **UNKNOWN, non-blocking residual**; доказанного causal paste fix нет, исходные predicate/5s/assertion сохранены. Полный old isolated PASS не переименован в broad PASS задним числом.

Contract/scope pass: исходный failure→accessible recovery→explicit retry выполнен в реальном desktop и Headless; S2 denied reasons проверены native future/blocked/terminal, S3/S4 pending/unknown/missing и S5–S7 source/lifetime/dirty/read классификации — file/VM/cache tests. UX pass: item в ⚙, постоянная ошибка/Details, distinct history name, RU/EN/theme/width, copyable missing draft; pixels прочитаны. Developer/architect pass: storage/VM source guards unchanged при history sync, Move сохраняет control/context/command, API/schema не расширены. Tester pass: все обязательные local/full/native проверки PASS без пропусков. Delivery: commit/push/обновление PR разрешены прежним «Оформи pr»; fresh PR CI отдельно, merge/release/install не разрешены.

Depth checklist: scope drift/unrelated changes, initial outcome/scenario→AC coverage, denial vs storage failure, late edits/revisions/epoch/source/disposal, no-resurrection, lock/journal/no-new-write, popup/input lifecycle, localization/layout/Importance/history, persisted read-back/history uniqueness, source/evidence freshness и rollback проверены. Manual challenge «пункт есть, но не работает/стирает draft/сам повторяет статус» покрыт actual binding/busy, file/VM guards и fresh native JSON/error-clear/retry assertions. Новых source/validation B/H/M/L нет; justification — конкретные failure/missing/dirty/stale/journal и UI cases сверены с исходными результатами, а не только со статическим diff.

Final docs/native evidence re-review завершён: original TRX и hashes всех восьми запусков, четыре build-log hashes и все 14 новых PNG согласованы; 83 сравнения общих binary hashes совпали. Единственный LOW — устаревший остаток в конце README — исправлен на fresh published-head CI и **CLOSED** отдельным re-review. `/root/spec_review` подтвердил **docs PASS, local post-EXEC PASS**, новых findings нет. Sandbox остаётся writable adversarial fallback, технически enforced read-only не заявляется. Код/тесты после проверенного source tree не менялись.

**Stop decision: local implementation/validation PASS.** Обновить ранее разрешённый PR с fresh evidence; CI финального published head указать отдельно и сохранять draft, пока fresh CI gate не подтверждён. Historical production/paste cause и video limitation остаются честно записанными residuals, обязательные UI tests выполнены.

### Pre-merge review corrections, 2026-10-05

Пользователь разрешил merge: «Влей в мейн если всё ок». Live preflight опубликованного `0b673274` подтверждает green CI/CLEAN, но выявил три незакрытых P2 GitHub review threads. Source подтверждает: Reload Missing без watcher notification оставляет задачу в live cache/Relations; ArchiveCommand не учитывает CanChangeTaskStatus; Ctrl+D command не учитывает availability текущей карточки. Это corrective уточнение утверждённого recovery outcome в фазе EXEC, без изменения storage schema и области продукта.

Исправления: mark Missing до detach, удалить confirmed-missing task из cache и перестроить Relations, сохранить detached copyable card; Archive/Unarchive и CompleteCurrentTaskCommand должны наблюдать CanChangeTaskStatus. Ctrl+D availability должна переключаться вместе с CurrentTaskItem и отписываться от прежней карточки. Guards и explicit retry сохраняются. Regression: external delete с/без Removed event; actual archive menu и Ctrl+D binding disabled during read/Missing и снова enabled after Loaded; unknown-result Archive availability через existing VM case.

Отдельный adversarial review подтвердил HIGH race: старый async Saved Load с revision=0 мог вернуть задачу после confirmed Missing. Captured event epoch теперь проверяется перед hydration под тем же lock; file event ID нормализуется к task ID. Детерминированный RED с управляемым synchronization context подтверждает resurrection на прежнем callback. После собственного confirmed Missing detached/disposed card возвращает typed Missing; поздние Loaded/Missing после unrelated lifetime disposal по-прежнему отвергаются. UI coverage проверяет отсутствие deleted task из cache и resolved ParentsTasks/BlockedByTasks при watcher/no-watcher.

Validation: сначала regression RED на старом runtime, затем targeted UI/VM/facade checks и affected builds. Из-за cache mutation требуется полный Main и Headless на новом source; native recovery и affected terminal/unarchive проверяются в свободном слоте. До merge обязательны post-EXEC re-review и fresh exact-head CI. Прежний CI относится только к `0b673274`; его не переназначать новому кандидату. Merge выполняется через PR без admin bypass после закрытия замечаний; разрешение пользователя получено отдельно.

Дополнительный lifecycle HIGH обнаружен адресным VM прогоном: completed failed autosave ещё находится в pending set до async cleanup и может отравить Seal даже после successful explicit retry. Controlled-context regression удерживает реальную cleanup continuation: no-retry dirty case PASS, persisted-retry case RED на старом Seal. TrackPendingSave выделен из существующей регистрации для этой boundary; admission lock сохранён. Seal игнорирует только completed saves при отсутствии pending editable fields; pending/unresolved saves, write producers и OutcomeUnknown guards сохраняются. Первый fixture attempt не удерживал cleanup и не считается business RED. Old tested VM DLL с current harness дал диагностический PASS1/1, без вывода о причинности.

Final source re-review `/root/spec_review` — PASS: все три P2 и оба HIGH, typed Missing и Relations coverage закрыты source; lock inversion/new findings не обнаружены. Фактический sandbox writable (`danger-full-access`, approval never), adversarial fallback выполнял только чтение. VM44/44, Unified18/18, actual menu1/1, Missing2/2 PASS. Последние builds после переноса ID normalization внутрь try: Main62 warnings/0errors; Desktop/Headless/FlaUI0/0. Full Main/Headless/native и fresh published-head CI пока PENDING; прежний CI не является новым validation.

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
| EXEC: уточнение размещения 2026-10-03 | Обновление переносится в ⚙; тот же recovery outcome/storage contract/risk, фаза EXEC сохраняется по quest-mode | Нужны menu placement/busy UI assertions и исходный failure→menu reload→retry, новые снимки | Реализовать и обновить PR #314 после relevant UI checks | «Кнопку перезагрузки задачи надо спрятать в меню шестерёнки»; прежние exact approval и PR authorization действуют | Текущая SPEC |
| EXEC: menu re-review 2026-10-03 | Reload первым в ⚙; исправлен driver false-positive ожиданием error clear; actual binding/busy/Missing assertions | Full Headless 52/52, rendered 8/8, menu 1/1, Missing 1/1, layout 4/4, Desktop 0/0; native заблокирован отключённым desktop; full Main открыт | Commit/push и обновление draft PR #314; ready не подтверждать | Прямое уточнение и PR authorization действуют | Menu frames, логи, evidence README |
| EXEC: main integration 2026-10-04 | Координатор передал следующий этап после emoji #315; merge main только в recovery-ветку | Main `5a780b2e`, 2 source conflicts resolved по смыслу; авторский wrapper-delete wait, main ShiftDelete/cache/API fixtures сохранены | Builds/review, затем обязательные checks по очереди после Importance | Прежние exact approval и PR authorization действуют; main merge не разрешён | Текущая SPEC, integration logs |
| EXEC: integration validation 2026-10-05 | Full/native пройдены в своём слоте; source findings по hover/diagnostics закрыты, paste cause остаётся неизвестной | Main1229/1230, Headless52/52, native4/4 final, rendered8/8; isolated paste1/1 не закрывает Main gate | Обновить draft PR с evidence; новый full требует согласованного слота | Прежние exact approval и PR authorization действуют | Integration logs/PNG, source snapshots, transferable diagnostic patch |
| EXEC: Importance union 2026-10-05 | Main92cf8c1e объединён после сохранения tested5a checkpoint35924686; source re-review PASS для draft | Новые buildsPASS; menu8/8, semantic3/3, Importance3/3, Headlessrecovery1/1, layout4/4; full/native92cf PENDING | Commit/push и обновить draft PR с точными границами evidence; ready gate OPEN | Прежние exact approval и PR authorization действуют | Отдельные integration-92cf logs/PNG/snapshot |
| EXEC: final history union 2026-10-05 | Слот передан recovery; history4a2f781e объединён в свою ветку без конфликтов | Existing template/header-order fixes сохранены; coverage реальных error/history names и Reload identity/command обновляется | Один финальный full Main/Headless и native4, source/validation re-review | Прежние exact approval и PR authorization действуют | Fresh integration-history artifacts |
| EXEC: final validation/sync 2026-10-05 | Candidate8ff и synced136 имеют одинаковый tree555998d7; main9150 history merge сохранён без повторного full | Main1269/1269, Headless52/52, native4/4, matrix8/8, resize1/1; fresh PNG просмотрены; historical paste current gate CLOSED/causeUNKNOWN; слот освобождён | Final docs/evidence re-review | Прежняя PR authorization действует; main merge/release/install отсутствуют | integration-history logs/PNG/snapshot/main-sync |
| Delivery: final re-review 2026-10-05 | Source/validation/docs PASS; LOW stale README gate исправлен и CLOSED отдельным re-review; source неизменен | Восемь original TRX/hashes, 14 PNG и ссылки сверены; local quality gates закрыты | Commit/push и update draft PR #314; fresh published-head CI — отдельный ready gate | Прежнее «Оформи pr» действует; merge/release/install отсутствуют | Итоговые SPEC, README и integration-history evidence |
| EXEC: pre-merge corrections 2026-10-05 | Fresh CI green, но три P2 GitHub review threads подтверждены source; phase EXEC сохранена | Missing cache detach и availability Archive/Ctrl+D требуют regression/fix | RED, narrow fixes, affected/full/native validation, re-review, push и fresh CI; затем PR merge | «Влей в мейн если всё ок» | Текущая SPEC и fresh pre-merge evidence |

### Pre-merge: полный Main и Headless teardown, 2026-10-05

Полный Main на immutable `72dfbccd` завершился **1274/1274 PASS, 0 FAIL, 0 SKIP**, exit0; оригинальный TRX и invocation/result сохранены. Старый Workspace paste case прошёл за8.8892288s; историческая причина его прежнего отказа остаётся UNKNOWN.

Первый full Headless на том же source напечатал52 Passed, затем упал в AfterTestSession: Avalonia.Headless12.0.4 `DisposeAsync` await null `_dispatchTask`. Exit=-532462766, TRX отсутствует; этот прогон **FAIL**, не52/52 PASS. Повтор без изменений дал52/52 PASS с TRX/exit0, но не закрывает подтверждённую factory race. Декомпиляция pinned factory выявила Task.Run worker, публикующий session до присваивания переменной Task.

Test-only HeadlessSessionFactory присваивает cold `new Task` до `Start(TaskScheduler.Default)`, сохраняя Headless/HarfBuzz defaults, PerTest/PerAssembly, ExecutionContext queue и штатный framework DisposeAsync. Два вызова StartNew заменены только в Headless hooks. Нет catch, скрывающего teardown NRE. Три новых проверки: настоящий Dispatch worker/VerifyAccess/CTS disposal8циклов; ожидание выполняющегося action и ExecutionContext; распространение настоящего worker NRE. Targeted **3/3 PASS**; final Headless build **0 warnings / 0 errors** (первый compile failure из-за hidden Configure(Type) сохранён; точный runtime overload вызывается reflection).

Main/native product binaries сохранены без изменений. Headless rebuild поменял SourceLink metadata при новом HEAD; в Headless bin восстановлены исходные общие DLL/PDB из уже проверенных Main/native, новый Headless harness оставлен. Replacement manifest фиксирует before/tested SHA256; VM hash во всех трёх runtime `0460615280FEA5B1FF8CDC4821CD65798EB3983F17A401278976CD75366A54A5`. Production/Main/Authoring/TestHost/FlaUI source совпадает с72df. Полный Main повтор из-за отдельного Headless adapter не требуется.

Отдельный adversarial `/root/spec_review`: source PASS, worker publication/drain HIGH CLOSED по source; новых findings нет. Фактический sandbox danger-full-access/approval never, проверка только чтением как fallback, технический read-only не обеспечен. Полный Headless на новой factory, native4 и fresh CI **PENDING**. PR остаётся draft, merge gate открыт.

### Pre-merge: native input desktop blocker, 2026-10-05

Новый full Headless на immutable `f2a77c79` завершился **55/55 PASS, 0 FAIL/SKIP**, штатный AfterTestSession, TRX, exit0 (4м04с). Production/Main/Authoring/TestHost/FlaUI исходники по git diff совпадают с72df; Main/native binaries неизменны, Headless common runtime явно восстановлен из них, provenance сохранён.

Fresh targeted FlaUI recovery действительно запущен на f2a: **0/1 FAIL**, exit2. Primary failure `Win32Exception: The input desktop is unavailable` в WindowsPointerBackend.EnsureDesktopAvailable при OpenStatusPicker; product flow ещё не исполнен. Остальные три native case не запускались после этого environment failure. Нельзя выдать предыдущие native4/4/PNG за новые. Все owned full/native процессы завершены, общий слот явно освобождён. Пользователю отправлен запрос открыть input desktop; Windows/RDP не разблокировывается агентом.

Source published в draft PR #314; fresh CI f2a запущен. CI старого0b не переназначен f2a. Evidence/public snapshot отражает completed55/55, failed session без TRX и native environment failure отдельно. Stop decision **NEEDS-FIX для merge/ready**, пока четыре mandatory native flow (recovery, future, blocked, terminal/unarchive) и fresh final-head CI не завершены. Прежние exact SPEC approval, PR authorization и прямое «Влей в мейн если всё ок» действуют; новых разрешений на merge не требуется.

### Post-EXEC audit перед обновлением draft, 2026-10-05

Отдельный adversarial `/root/spec_review` завершил полный pass: **source, completed Main/Headless и docs PASS; overall ready/merge NEEDS-FIX**. Actual sandbox danger-full-access/approval never, только чтение как fallback; enforced read-only отсутствует. Проверены210 original/public counters/hash/test entries,119 local links,26 sanitized logs,27 shared runtime hash comparisons,11 restored DLL и11 pairedPDB. PortablePdb document checksums MainWindowViewModel/TaskItemViewModel совпадают с текущими source SHA; восстановление проверенного runtime допустимо. Новых source findings нет.

- Scope/Evidence: approved recovery outcome и уточнение ⚙ сохранены; текущий tracked diff перед delivery только SPEC/evidence, product/Main source72df и test-only Headless sourcef2a неизменны. Primary checkout и foreign worktrees не менялись.
- Contract/AC: typed read, authoritative status/explicit retry, сохранение dirty draft, Missing/cache/Relations, source/revision/lifetime и completed-fault retry guards подтверждены адресными/full UI/storage tests. Обязательный native remainder не подменён прежними результатами.
- Adversarial: stale-Saved resurrection, Archive/Ctrl+D во время busy/unknown/Missing, delayed failed-save cleanup и true Headless worker drain имеют source fix и passing coverage. Реальный worker NRE negative control распространяется.
- Roles: workflow/UX/developer PASS по source и completed evidence; tester/delivery NEEDS-FIX по environment-dependent native4 и fresh final-head CI.
- Depth: hidden storage schema/status policy изменений нет; lifecycle lock ordering/source epochs reviewed; current user scenario проверен Headless/actual Avalonia menu. Historical paste cause и индивидуальная production cause остаются UNKNOWN; raw-byte equality и свежие native PNG не заявлены. Video fallback ограничен ранее просмотренными PNG/read-back/TRX после объективных recording failures.

| Severity | Area / finding | Required action | Status |
| --- | --- | --- | --- |
| HIGH | Native input desktop unavailable до product flow | Запустить recovery, future, blocked, terminal/unarchive при доступном Windows input desktop | OPEN |
| HIGH | CI итоговой головы после docs commit отсутствует | Проверить exact final published-head CI, без переназначения старого green | OPEN |
| LOW | nativePngs=[null]; не все remaining gates названы | Schema теперь[], все4native явно перечислены; re-review подтвердил | CLOSED |

Live GH GraphQL на published f2a подтвердил все3original review threads isResolved=true. Read-only Win32 input desktop probe подтвердил недоступность, error5. Stop decision **PASS для разрешённого draft publication; NEEDS-FIX для ready/merge**. У пользователя запрошена только доступность desktop, не повторное разрешение на слияние. User authorization «Влей в мейн если всё ок» сохраняется.

### Возобновление native gate, 2026-10-06

Пользователь прямо поручил «Вливай»; прежнее разрешение на слияние действует. Published6a7b45fb имеет все шесть GitHub checks SUCCESS: original CI Main1274/1274 и Headless55/55, exit0, без FAIL/SKIP; Android и CodeQL прошли. Synthetic merge checkout c9d31e42 имеет parents9150+6a и treeb92f, точно совпадающий с branch tree. Input desktop теперь доступен, error0; новая read-only probe сохранена отдельно от исторического error5.

Fresh native на6a: future1/1 и blocked1/1 PASS. Recovery-repeat0/1 FAIL после успешного Reload/read-back на втором физическом клике: header/status оказался clipped собственным CurrentTaskDetailsScrollViewer, несмотря на IsOffscreen=false. ErrorPNG и соседний futurePNG просмотрены; точный center hit в отказавшем прогоне не записан, поэтому причинное подтверждение ожидает диагностический повтор. Terminal0/1 FAIL в BeforeTest: placement1280x800 не достигнут при текущем DPI, actual2880x1497 на основном4K мониторе. Все оригинальные логи/TRX и первые native frames сохранены.

In-scope test-only corrective change: native OpenStatusPicker сначала ограниченно ждёт собственные viewport/button, при clipped bounds прокручивает owned ScrollPattern к началу и заново проверяет полное попадание button в viewport. Before/after PNG, rectangles и read-only center FromPoint фиксируют preparation; неизменённый DesktopPointer.Click по-прежнему проверяет физическое попадание, semantic status Invoke и catch/retry не добавлены. Production/Main/Headless source не менялся. Для final native используется существующий UNLIMOTION_AUTOMATION_DESKTOP_MONITOR=primary: fixture800x400 logical и placement без принудительного physical resize; Windows display settings не меняются. Финальная FlaUI сборка0 warnings/0 errors; предыдущая incremental сборка4 warnings/0 errors сохранена.

Четыре final native flow должны пройти на одном финальном harness DLL hash. Ранее прошедшие future/blocked не переназначаются новому adapter. Stop decision пока **NEEDS-FIX для ready/merge**: final native4, diagnostic/PNG review и exact final-head CI остаются обязательными. Main/Headless full не повторяются только из-за отдельной native viewport preparation; их source invariants проверяются по git diff и финальному CI.
