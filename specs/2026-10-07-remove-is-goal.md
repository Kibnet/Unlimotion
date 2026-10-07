# Удаление признака «Цель» (IsGoal)

## 0. Метаданные

- Тип: delivery-task, dotnet-desktop-client + ui-automation-testing + ui-feature-parity; context testing-dotnet.
- Владелец: пользователь — продуктовый scope; агент — реализация после exact approval.
- Масштаб: medium, Expanded SPEC: меняются модель, persisted данные, UI, Notes и публичные DTO.
- Ветка: feat/daily-feed; текущий base 5db0b05a, незакоммиченный перенос recovery/history сохраняется отдельно.
- Поверхность: Codex, Windows/PowerShell; effective runtime из API не установлен. Model behavior baseline не является заявлением доступности модели. Model eval — не применимо: это изменение приложения, не модели.
- Instruction stack: central AGENTS/routing; creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration/testing baseline, quest-governance/mode; spec-linter/rubric/review-loops; desktop/UI profiles и локальный MUST UI tests. Canonical expanded template из центрального каталога.
- Связанные SPEC: workspace-independent-documents; workspace-card-recovery-history. Прежнее approval не распространяется на это удаление.

## 1. Цель / Outcome contract

Поручение: «выпилим IsGoal и всё что с ним связано». Удалить техническое различение цели и обычной задачи: поле, переключатели, значки, фильтры, обработку сохранения и обмена. Цель как смысл большой задачи остаётся возможной через её название, описание и иерархию — без отдельного типа.

Success: ни одна задача не скрывается по бывшему признаку; в карточке, списках и разборе нет управления «Цель». Области, родители, статусы, сроки, длительность и навигация работают как прежде. Старые файлы и незавершённые операции читаются безопасно.

Output: код и актуальные тесты, просмотренные UI PNG, результаты обычных сборок и регрессии. Stop: до exact approval — только SPEC; после — без пропущенных обязательных проверок. Push/merge только по отдельно сохранённому разрешению и его условиям, не автоматически из approval новой SPEC.

## 2. AS-IS

Проверены поиском и чтением:

- Domain TaskItem.IsGoal + JsonExtensionData; TaskItemViewModel scalar tracking/merge/persistence.
- MainWindowViewModel TaskGoalFilterMode/Option, AllTasks:GoalFilter, фильтруемые DynamicData projections; TaskListFilterScope и TaskListFilterSnapshot.
- TaskCardView/MainControl/TaskListDocumentView/MainScreen, TaskClassificationControl/FeedReviewDialog, общие ресурсы и RU/EN строки.
- FeedTaskDraft, FeedTaskCaptureRequest, FeedTaskConversionRecoveryDescriptor; FeedTaskCaptureService сверяет прежний признак при повторе операции; FeedViewModel и task creation target передают его.
- TaskItemHubMold, ReceiveTaskItem, server TaskItemMold, AppModelMapping/ServerStorage capability checks; TaskClassificationCompatibilityTests проверяют совместимость клиентов.
- TaskApplicationCommandService.CreatedTaskMatches учитывает false; TaskTreeManager clone сохраняет признак.
- UI tests/Headless UX stories и TestHost данные ожидают goal controls/indicators. AreaIds — отдельная сохраняемая возможность.

Это не локальное удаление checkbox: без обработки зависимостей останутся скрытые фильтры и проблемы возобновления операций.

## 3. Проблема

Отдельный бинарный признак добавляет UI и технические зависимости без нужного пользователю самостоятельного сценария.

## 4. Цели дизайна

Удалить feature целиком, не заменять другим признаком. Не смешивать удаление цели с удалением областей или схемы классификации областей. Не переписывать всю пользовательскую базу при запуске. Не ломать историю вкладок, recovery и idempotency.

## 5. Non-Goals

- Не удалять AreaIds, назначения областей и корневую задачу области.
- Не переименовывать содержимое задач/Markdown и не удалять пользовательские упоминания слова «цель».
- Не удалять прошлые Git commits и исторические SPEC/evidence. Старые планы остаются историческими, не действующим требованием реализации.
- Не менять CLI preview/schema по параллельной SPEC и не включать независимый null/clone cleanup AreaIds.
- Не выполнять массовый backfill, установку или server deployment.
- Не объявлять предыдущий recovery/history этап завершённым: причина intermittent save-guard failure и полные gates пока открыты.

## 6. TO-BE

### 6.1 Ответственности

Domain/VM: убрать public IsGoal, pending-field mask/merge/clone. UI: убрать controls/indicators/filters и пустые отступы. Notes: убрать поле из новых drafts/requests/descriptors и сравнения intent. Server/Interface: убрать DTO/mapping; capability contract остаётся для AreaIds. Tests: заменить feature-specific проверки регрессиями удаления и сохранения областей.

Runtime-совместимость: FileTaskStorage.SaveCore использует Newtonsoft.Json и TaskItemSnapshot.Clone; Notes journal использует System.Text.Json с Web defaults. Retired-key обработка должна работать на правильной границе каждого сериализатора, а не полагаться на один глобальный converter. Удаляется только верхнеуровневый исторический ключ задачи с именем IsGoal (сравнение OrdinalIgnoreCase: IsGoal/isGoal/ISGOAL/смешанный регистр) на deserialize и serialize/clone, в том числе из программно заполненного ExtensionData. Одноимённые ключи внутри произвольной пользовательской metadata не удаляются. Оригинальный объект ExtensionData не мутировать при подготовке snapshot/serialize; фильтровать в независимой копии/контракте. Исторические Git diffs с этим полем остаются историей, не активным feature.

### 6.2 Детальный дизайн и visual planning

Карточка до: `[Название] [значок цели]`, свойства `[Цель вкл/выкл] [Области]`.
Карточка после: `[Название]`, свойства `[Области]`; освободившееся место занимает существующая компоновка без пустого slot.
Список до: `[Поиск] [Цели / Обычные / Все] [другие фильтры]`.
Список после: `[Поиск] [другие фильтры]` с прежним responsive overflow.
Разбор до: `[решение Задача] [Цель] [Области] [Родители] [Подтвердить]`.
Разбор после: `[решение Задача] [Области] [Родители] [Подтвердить]`.

Сохраняются остальные команды и порядок подтверждения решения. TaskClassificationEditorViewModel остаётся для областей; не удалять его целиком. RU/EN resources goal-specific убираются; общие содержательные термины «цель» не заменяются глобально.

Видео до/после из UI runs при доступном native desktop; при объективной недоступности фиксировать проверку/причину и rendered Headless PNG fallback, не выдавая его за native. Каждый новый PNG открыть и проверить.

### 6.3 Наблюдаемые сценарии

| Сценарий | Действие | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Старая база | Открыть бывшую цель и обычную задачу | Обе обычные карточки, все данные кроме снятого признака сохранены | UI + JSON roundtrip | 1,2 |
| Старый фильтр | Запуск с AllTasks:GoalFilter=Goals/Regular | Нет скрытого ограничения; остальные фильтры сохранены | UI всех list kinds | 3 |
| Разбор/быстрый захват | Создать задачу из блока, назначить область/родителя, подтвердить | Нет переключателя цели, задача создана один раз, связи сохранены | actual UI + persisted files | 4 |
| Незавершённая операция | Возобновить старый pending journal с IsGoal=true/false | Нет отказа только из-за снятого признака; прочие конфликты остаются | Notes recovery tests | 5 |
| Server | Обновить задачу старым/новым клиентом | AreaIds не теряются при omitted field/unsupported capability | mapper/DTO tests | 6 |
| Workspace | Вернуться назад, открыть рядом, сменить список | Нет goal filter/indicator; изолированные остальные фильтры и история сохранены | Headless UX stories + PNG | 3,7 |

### 6.4 State / interaction matrix

| Состояние | Trigger | Результат | Ошибка/конкуренция |
| --- | --- | --- | --- |
| Старый IsGoal true/false/отсутствует | Read | Одинаковая task semantics | Не менять файл/mtime чтением |
| Старый task JSON с IsGoal | Обычное успешное save | Новый формат без IsGoal, unknown extension data сохраняются | Не удалять ключ до успешного write; при failure старый файл остаётся |
| Сохранённый goal filter | Startup/restore history | Игнорируется только снятый фильтр | Другие filters/statuses не сбрасываются |
| Старый conversion pending/task-created/completed | Retry/recovery | Прежний stable TaskId, goal не влияет на intent/equivalence | Source hashes/locators/ownership/AreaIds/parents по-прежнему обязательны |
| Несохранённый title/AreaIds | Navigation/Reload | Прежние guards | Late drafts/failed writes не теряются |

### 6.5 Decision ledger

| Решение | Владелец | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Убрать весь goal feature | user | Поручение выше | 1.0 | Scope шире одного UI control | Нет, кроме exact approval |
| Оставить области/иерархию | agent | Это независимые возможности | 0.99 | Случайное удаление TaskClassification целиком | Нет |
| Старые данные | agent | Читать, игнорировать retired key; удалить только при штатном save, без scan/backfill | 0.9 | Обратный запуск старой версии уже не восстановит снятый признак | Нет: последствия показаны в §10 |
| Исторические материалы | agent | Не переписывать историю; обновить действующие docs/assertions | 0.99 | Глобальная замена повреждает записи | Нет |
| Параллельная CLI SPEC | agent | Удалить только текущий goal dependency; preview отдельно | 0.99 | Будущий общий контракт должен больше не требовать IsGoal | Нет; не отправлять внешние сообщения без разрешения |

### 6.6 Runtime/config/data contracts

| Контракт | Source of truth | Изменение | Совместимость | Verification |
| --- | --- | --- | --- | --- |
| Task JSON | TaskItem + JsonExtensionData/serializers | Нет active поля и новых IsGoal writes | Retired key не превращается в произвольный extension и не возвращается на save; прочие unknown keys целы | old/new raw fixtures, bytes/mtime on read |
| Settings/history | MainWindow/task-list snapshots | Нет goal filter state | Старый config ключ игнорируется, остальные настройки не меняются | startup/snapshot/UI |
| Notes journal | Conversion/Capture records | Новые записи без поля | Старые записи читаются; intent matching без goal; guards остального payload сохранены | legacy pending/task-created/completed tests |
| Server exchange | DTO/mapping/capabilities | Goal отсутствует, AreaIds остаётся | Не повышать schema version лишь за удаление optional field; legacy omitted AreaIds не сбрасывает серверные области | compatibility tests |
| CLI application create | CreatedTaskMatches | Нет goal requirement | AreaIds check остаётся; receipt/idempotency guards не ослабляются | command service tests |

## 7. Правила

1. Бывшая цель не становится новой задачей и не меняет ID/статус/связи.
2. Goal не участвует в фильтрации, persistence tracking, бизнес-решениях или equivalence.
3. Retired-key compatibility допускает упоминание `IsGoal` только в узкой обработке старого формата и её тестах; не сохраняет скрытый goal feature.
4. Не создавать миграционные записи при read. Не удалять неизвестные поля целиком.
5. После удаления фильтра показать прежний набор без него, не сбросив другие ограничения.

## 8. Интеграция

Удаление затрагивает model construction/hydration/snapshot/clone, task list projection/reset/capture/restore, feed capture/conversion/recovery, server mappings и application create-equivalence. Проверка простым rg недостаточна: runtime и старые форматы обязательны.

## 9. Данные и состояние

Удаляется persisted IsGoal и derived UI/filter state; новых полей нет. AreaIds/status/history/AgentExecution/extension data остаются. Не добавлять противоположный `IsRegular` или иной заменитель типа.

## 10. Migration / rollout / rollback

Первый read ничего не переписывает. Новые задачи и штатные обновления сохраняются без retired key. Нет массового удаления старых значений. Journal recovery не меняет исходные Markdown только ради migration.

Код откатывается revert собственного commit. Обновлённая задача после rollback старой версии будет иметь её default IsGoal=false: потерянный признак не восстановится автоматически. Для восстановления старых значений нужен пользовательский Git/history/backup данных; данный scope не выполняет live migration или очистку backup. Server/client mixed-version fixtures обязательны, live развёртывание не входит.

## 11. AC и acceptance-to-test matrix

| AC | Критерий завершения | Проверка |
| --- | --- | --- |
| 1 | В Domain/VM/DTO нет активного IsGoal; отсутствуют goal-only masks, options, bindings, resource keys | Source search с allowlist только compatibility/tests/historical docs, ordinary build |
| 2 | Старый JSON читается без rewrite; save/new/clone не пишут goal и не повреждают остальное | Domain/storage/roundtrip: все casing retired key, программный ExtensionData, nested metadata; AreaIds nonempty/empty/null fixtures с сохранением прежней null-policy, без нового cleanup |
| 3 | Все 9 list kinds не содержат goal filter/indicator; старый config не скрывает задачи | Relevant UI tests вместо TaskGoalFilterUiTests; history/filter isolation и responsive layout |
| 4 | Card/feed/review/quick capture позволяют работать с AreaIds/parents без goal control | Actual menu/button/edit UI, persisted result, wide/narrow inspected PNG |
| 5 | Legacy conversion/capture retry не дублирует task и source; несовпадение остальных полей всё ещё отвергается | Journal old true/false + all states, FeedTaskConversionTests/creation target |
| 6 | Области проходят server/client roundtrip и old-client omitted-field semantics | Classification compatibility/roundtrip tests; no new goal outbound JSON |
| 7 | Workspace navigation, late-save guards и 13 актуальных UX stories не регрессируют | Full Main и rendered Headless suites; relevant native flows при доступном desktop |
| 8 | Ordinary Debug solution и Release Desktop собираются; mandatory tests текущего кандидата проходят | Точные logs/TRX/head; предупреждения отдельно, no stale PASS |

Команды после approval (direct TUnit runner, сериализация):

```powershell
dotnet build src/Unlimotion.sln -c Debug
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --treenode-filter '/*/*/*/*[Category!=ServiceIntegration]' --maximum-parallel-tests 1 --report-trx
$env:UNLIMOTION_RENDERED_HEADLESS_SCREENSHOTS='1'
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --maximum-parallel-tests 1 --report-trx
dotnet test --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --maximum-parallel-tests 1 --report-trx
```

Перед full/native — проверить общий test slot и реальные процессы; не вмешиваться в установленное приложение. Адресные filters уточняются по существующим/новым классам. Недоступный desktop не делает native gate зелёным. Generated evidence не коммитить автоматически. Не выдавать UI snapshots/сборку за full/CI/live server acceptance.

## 12. Риски и ожидаемые замечания

| Возражение | Почему вероятно | Смягчение | Статус |
| --- | --- | --- | --- |
| «Удалили и области» | Общий classification control/DTO | Явно сохранить AreaIds и отдельные tests | mitigated |
| «Фильтр исчез, а задачи не вернулись» | Старый persisted Goals/Regular | Удалить predicate, проверить startup config и все list kinds | mitigated |
| «Старый разбор сломался» | Pending journal содержит поле | Legacy fixtures всех состояний, сохранить idempotency | mitigated |
| «Зачем переписали базу?» | Поле persisted | Нет scan/backfill, read без writes/mtime | mitigated |
| «Как откатить потерянные признаки?» | На save поле удаляется | Точное ограничение rollback §10; не обещать восстановление без backup | disclosed |

Rework prevention: видимые controls названы; scenarios→AC заполнены; предполагаемые решения названы; UI/данные и отрицательные случаи покрыты планом; evidence до EXEC не объявлен выполненным.

## 13. План

После approval: убрать goal contracts/model/VM и projections; сохранить AreaIds; убрать UI/ресурсы; обеспечить узкую old-format compatibility; обновить тесты/живые docs; адресный RED→GREEN, rendered visual review, обязательная текущая регрессия; post-EXEC review. Незавершённый recovery/history diff не сбрасывать и не засчитывать как завершённый.

## 14. Открытые вопросы

Продуктовых решений кроме exact approval не требуется. Runtime serializer compatibility проверяется при реализации; если обнаружится необходимость необратимого backfill/нового deployed API, остановиться и согласовать расширение.

## 15. Соответствие профилю

Expanded storage/public/UI change; AppAutomation/Avalonia.Headless coverage обязательно обновляется и запускается. Visual storyboard §6.2 и фактические PNG/video boundaries обязательны. No reverse UI dependency в ViewModel. TUnit skill применяется к исполнению; AppAutomation skill — к UI regression/visual evidence, не повод менять установленный framework.

## 16. Файлы

| Группа | Изменение | Причина |
| --- | --- | --- |
| Domain/Interface/Server.ServiceModel; AppModelMapping/ServerStorage | Удалить поле, сохранить areas contract | Model/transport |
| TaskItemViewModel/TaskTreeManager/TaskApplicationCommandService | Удалить tracking/clone/equivalence goal | No hidden semantics |
| MainWindowViewModel/TaskListFilterScope/TaskListDocumentViewModel | Удалить mode/options/projections/settings/snapshot goal | No stale filter |
| FeedViewModel/TaskClassificationEditorViewModel/TaskStorageFeedTaskCreationTarget, Notes Operations | Удалить drafts и goal decision/recovery comparison | Capture/review consistency |
| Views/TaskPresentationResources, RU/EN Strings | Удалить switch/filter/icon/empty slots | UI simplicity |
| Main tests, Headless stories, TestHost, live docs | Замена goal expectations, legacy/negative/UI regressions | Evidence |

## 17. Было → стало

IsGoal bool → отсутствует; goal filter → отсутствует; draft goal switch → отсутствует; AreaIds/иерархия → без изменения; old task/journal → совместимое чтение, снятый признак не влияет на сценарий.

## 18. Альтернативы

Только скрыть UI: оставляет нежелательную модель/фильтрацию, не соответствует поручению. Массовая очистка файлов при старте: лишний риск и IO, не требуется. Выбранное полное удаление feature с узкой compatibility старого формата не поддерживает новый goal feature и не переписывает базу чтением.

## 19. Quality gate

### Root review: scope/evidence, contract, adversarial

Scope reviewed: эта SPEC и canonical template; central routing/QUEST/linter/rubric/review owners; desktop/UI/parity profiles; source §2, дополнительно FileTaskStorage.SaveCore, TaskItemSnapshot.Clone, client/server AutoMapper и TaskStorageCapabilities. Source прочитан/поиск выполнен; removal code не изменён, runtime ещё не проверялся.

Contract pass: feature удаляется из всех видимых точек и runtime semantics, без удаления areas/иерархии; прежний approval относится только к recovery/history. Adversarial pass: (1) удалённое свойство попадёт в JsonExtensionData и снова сериализуется — уточнена узкая граница §6.1; (2) общий classification UI/DTO удалит области — запрещено, AC6; (3) старый goal filter останется в projections/history — AC3; (4) старый journal intent будет ошибочно отклонён/повторён — AC5; (5) clone/prepare-write поменяет исходный unknown metadata — запрещено в §6.1, roundtrip tests обязательны. Fix and root re-review: сверены разные сериализаторы и mixed-version mapping; эти контрпримеры теперь имеют явные инварианты и тесты.

### Linter

| № | Статус | Evidence |
| --- | --- | --- |
| 1 | PASS | §1 outcome |
| 2 | PASS | §2 прочитанные source |
| 3 | PASS | §3 лишний feature |
| 4 | PASS | §4 полный scope с сохранением areas |
| 5 | PASS | §5 без backfill/deploy/CLI expansion |
| 6 | PASS | §6.1 ownership boundaries |
| 7 | PASS | §8 integration |
| 8 | PASS | §7 invariants |
| 9 | PASS | §6.4 failure/recovery |
| 10 | PASS | No startup scan; benchmark не требуется без performance claim |
| 11 | PASS | §9 model/state |
| 12 | PASS | §6.6/10 old formats |
| 13 | PASS | §10 rollback и потеря retired marker явно названы |
| 14 | PASS | AC1–8 измеримы |
| 15 | PASS | §11 scenarios/tests, negative cases |
| 16 | PASS | Commands/slot/stop rules |
| 17 | PASS | §13 execution dependencies |
| 18 | PASS | §6.5/14 решения определены |
| 19 | PASS | Expanded medium public/storage |
| 20 | PASS | UI tests/visual/native/build boundaries |

Rubric: цель/границы 5 (§1/5), AS-IS 5 (§2), дизайн 5 (§6–9), безопасность 5 (§10, отсутствие backfill и точный rollback), тестируемость 5 (§11), автономность 5 (§13/14). 30/30 оценивает SPEC, не выполненную реализацию.

### Role-based review

| Роль | Root verdict | Проверка |
| --- | --- | --- |
| Business analyst | PASS | Убрать тип без потери структуры/содержимого задач |
| UX/designer | PASS | Три storyboard, нет пустых slots, responsive/навигация сохранены |
| Tester | PASS | Old formats/negative recovery/actual UI и AC→tests |
| Architect | PASS | Два serializer boundary, nullable AreaIds capability остаётся, no replacement type |
| Delivery/operations | PASS | Нет live deployment/backfill; чужой diff сохранён; gates/rollback явные |

Depth checklist: scope/dirty recovery отделены; approval новой задачи pending; runtime PASS не заявлен; comments/live docs обновляются без переписывания исторических SPEC; hidden API/storage changes явно названы; rollback не обещает восстановить retired value. Manual challenge: открыть старую цель с persisted Goals filter и затем восстановить pending capture — обе исходные пользовательские ситуации имеют AC, а не только compile check.

| Severity | Area | Finding | Action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Serializer | Просто удалить bool недостаточно: unknown IsGoal вернётся из ExtensionData | Узкая нормализация без source mutation, два serializer tests | fixed in SPEC/root re-reviewed |
| MEDIUM | External review / retired key | Case-insensitive старый ключ и программный ExtensionData были недоопределены | OrdinalIgnoreCase top-level на deserialize/serialize/clone; nested metadata сохраняется, casing fixtures | fixed/external re-reviewed |
| LOW | Test matrix | AreaIds ошибочно назван true/empty/null | Nonempty/empty/null с прежней null-policy; не добавлять cleanup | fixed |

No-findings justification root после уточнения: выявленные зависимости покрыты конкретными data/UI invariants и old-format negative tests; критичных неизвестных продуктовых решений не осталось. Procedural external reviewer `card_recovery_spec_review` подтвердил закрытие MEDIUM и LOW по изменённым §6.1/AC2/19; выполнил только чтение, sandbox danger-full-access, approval never. Технической read-only изоляции нет; отдельный root adversarial fallback описан выше, это не технически независимый review. Итоговый post-SPEC stop decision PASS: ГОТОВО к exact approval, не EXEC/runtime/merge PASS.

### Post-EXEC — ASK-HUMAN, не PASS

Работа выполняется одним агентом по прямому ответу пользователя «Нет, работай один»; post-EXEC использует root adversarial fallback, не объявляется независимым review. Старый recovery/history diff сохранён отдельно от удаления IsGoal.

Реализовано удаление активных model/DTO/VM/filter/UI/resource/draft/journal полей. Узкий compatibility boundary отбрасывает только top-level retired key с OrdinalIgnoreCase на чтении/сериализации/clone, не переписывает файл при чтении, не изменяет программно переданный исходный ExtensionData, сохраняет nested metadata. AreaIds, capability schema, parents/status и обычные фильтры сохранены. Независимый AreaIds null/clone P1 не исправлялся.

Первичные проверки: 39/39 адресных data/transport/conversion tests, 4/4 новых rendered UI cases (Goals/Regular × wide/narrow). Широкий и узкий PNG реально просмотрены: области доступны, goal control отсутствует. Это не native proof. Обычная Debug solution сборка: 0 ошибок/197 предупреждений; Release Desktop: 0 ошибок/50 предупреждений, текущий product code. Дополнительные assertions в capture tests после этой сборки требуют свежей тестовой сборки; она выполнена с 0 ошибок/122 предупреждениями.

Промежуточная фиксация проверок и findings (итоговое состояние приведено ниже):

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Legacy capture fixture | Новый тест с родителем не задавал source identity и останавливался на существующей защите | Задать детерминированный identity/provider, не ослаблять production guard | fixed, fresh full run pending |
| MEDIUM | Retired UI text | Остались две подписи «задача или цель»/«goal and area» | Заменить на task/areas, проверить поиск и актуальную сборку | fixed |
| HIGH | Full Headless validation | Monolithic rendered run завис после 7 результатов; dump фиксирует DesktopAppSession.Launch в следующем lifecycle test, без завершения | Сохранить dump/log, выполнить весь набор по классам в отдельных процессах; не объявлять зависший прогон PASS | pending |
| HIGH | Full Main/current candidate | Обязательная регрессия ещё не завершена; ранние прерванные snapshots не PASS | Полный fresh run после исправления fixture и новых assertions | running |
| HIGH | Full Main/rename collision | RenameOverOpenTargetPreservesBothVersionsAndOneFinalEditor(True, True): после решения конфликтов Documents.Count=2 вместо 1 | Отдельная диагностика; не исключать тест, не объявлять PASS на повторе без установления причины | observed, outside removal code paths |
| HIGH | Full Main/legacy UI harness | MainControlTaskCardLayoutUiTests ищет MainControl внутри современного MainScreen (Single: no elements); часть MainControlTreeCommandsUiTests обращается к полям TaskPresentationControl через MainControl | Отдельно обновить устаревшую тестовую topology без ослабления проверок; остальные failures разобрать по фактическим причинам | observed, no automatic scope expansion |
| MEDIUM | Previous recovery scope | Ранее OrdinaryTransition late-save guard падал; последующие повторы не объяснили причину | Не выдавать прежний scope за завершённый; учитывать результат общей регрессии | remains open outside removal claim |

Полный CI/live server/native acceptance не заявлен. Read-only input desktop probe доступен: OpenInputDesktop(0,false,0x0101), Win32Error=0, SessionId=1; native flow ещё не запускался. Source search оставляет IsGoal только в compatibility boundary и negative/legacy tests; generated evidence не коммитится.

После исправления capture fixture адресный FeedQuickTaskCaptureTests — 17/17, включая legacy true/false × Pending/TaskCreated/Completed и negative draft substitutions, без изменения production guard. В Pending исходный файл ещё не создан — nullable checkpoint assertion фиксирует именно это состояние. Артефакт: `chat-artifacts/remove-isgoal/capture-final`.

Для native before-reference проверен существующий автоматизированный ролик `chat-artifacts/workspace-documents/native-after/workspace-independent-documents-after.mp4` (20 секунд, 3826×1462); кадр на 17-й секунде реально просмотрен, Goal checkbox виден рядом с Areas. Это прежний run того же сценария, не exact baseline объединённого dirty recovery кандидата. Текущий native тест дополнен negative goal-ID check и positive AreaPicker check; новый run/video ещё pending.

#### Итог текущего EXEC 07.10.2026

- Fresh `TaskGoalRemovalUiTests`: **4/4**, 35.849 секунды, `chat-artifacts/remove-isgoal/ui-final/*.trx`. Все 9 режимов списка проверены при старых Goals/Regular настройках; wide/narrow PNG заново получены и реально просмотрены (01:38–01:39 UTC): Goal отсутствует, Areas доступны. Это отрисованный Headless UI, не native desktop.
- Fresh `FeedQuickTaskCaptureTests`: **17/17**, 2.136 секунды; true/false × все checkpoints и подмена capture/areas проверены без ослабления guards. Новые DTO legacy true/false cases прошли в Main; остальные compatibility/roundtrip/conversion адресные результаты сохранены в ранее перечисленных логах.
- Main остановлен после выявленных out-of-scope blockers и перед дальнейшими мутациями: runner сообщил **1821 passed / 42 failed / 1863 результатов**, 25m57s, exit -1 вследствие прерывания. Это **частичный красный прогон, не завершённая полная регрессия**. 34 failures в `MainControlTaskCardLayoutUiTests` (не найдена старая topology), 7 в `MainControlTreeCommandsUiTests` (в том числе reflection target и inline/relation flows), 1 rename-collision. Лог `chat-artifacts/remove-isgoal-main-accepted.log` не является PASS независимо от имени файла. Новые removal UI cases после этого выполнены отдельно.
- Полный rendered Headless run ранее завис после 7 результатов; dump/stacks сохранены. Предложенный запуск по классам **не выполнен**, mandatory AC7/8 этим не закрыты. 13 UX stories и прежний late-save failure нельзя объявлять заново проверенными.
- FlaUI проект собирается: **0 ошибок/47 предупреждений**. Native removal/navigation test запущен и **упал до пользовательского сценария**: Resize требует 3400×1300 physical pixels при DPI=200%, реальное native окно ограничено 1886×1300 (UIA client 1860×1229). Native/video/AreaPicker assertions не достигнуты; запись не началась. Лог `chat-artifacts/remove-isgoal-native-final.log`, TRX `chat-artifacts/remove-isgoal/native-final`. Desktop доступен, но wide precondition не выполнен; разрешение/DPI пользователя не менялось. Это объективный video fallback, next-best evidence — свежие inspected Headless PNG + behavioral assertions; **native PASS не заявлен**.
- Свои test hosts завершены, shell sessions закрыты, общий UI test slot свободен. Установленное приложение пользователя PID21640 не закрывалось. `git diff --check` прошёл; commit/push/merge/deploy не выполнялись. Generated evidence остаётся вне commit.

Root review: source/model/DTO/filter/resource removal и compatibility boundary проверены; shared areas/parents/status ownership сохранён. Adversarial counterexamples: root key casing/source metadata mutation, old persisted filters и legacy partial-operation retries покрыты тестами; это не снимает красные общие gates. Business/domain и architecture — PASS для удаления; UX — PASS только для просмотренных wide/narrow состояний; tester/delivery — NEEDS-FIX. Независимого post-EXEC review нет по прямому запрету делегирования; использован root adversarial fallback.

AC1–6 имеют адресную evidence; AC7/8 **не выполнены целиком**. Stop decision **ASK-HUMAN**: требуется отдельное согласование scope для обновления старой UI-test topology, диагностики/исправления rename-collision и закрытия общего validation harness; завершение, интеграция и релиз не объявляются. Прежний recovery/history scope остаётся незавершённым и не смешивается с результатом removal.

## Approval

Получено «спеку подтверждаю» 07.10.2026. Фаза EXEC разрешена для этой SPEC.

## 20. Журнал

| Фаза | Решение | Evidence / остаток | Следующее действие | Пользователь | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC 07.10.2026 | Новый scope удаляет goal feature, сохраняет areas/hierarchy; старый EXEC приостановлен | Read-only поиск/чтение зависимостей; runtime удаления отсутствует | Review draft, затем approval | «Давай выпилим IsGoal...» | Эта SPEC |
| Переключение scope | Свой повтор recovery suite остановлен; общий слот свободен, чужие процессы/installed Desktop не тронуты | Последний diagnostic 7/7 и repeat1 7/7 не объясняют предыдущий failure; repeat2 прерван, не PASS. Незакоммиченный код сохранён | Старый этап остаётся незавершённым | Новое поручение заменило текущую работу | `card-recovery-save-diagnostic.log`, `card-recovery-save-repeat-1.log`, `card-recovery-save-repeat-2.log` |
| Post-SPEC review | Root audit + procedural external review; casing/ExtensionData/null-matrix уточнены, re-review PASS | Runtime отсутствует; нет открытых spec findings, технической read-only изоляции reviewer нет | Запрос exact approval новой SPEC | Ожидается | §19 |
| EXEC | Получено exact approval новой SPEC; прежний recovery diff сохраняется | Удаление ещё не реализовано, tests pending | Реализация и текущие проверки | «спеку подтверждаю» | Эта SPEC |
| EXEC validation | Removal реализован; работа одним агентом | Первичные 39/39 contracts, 4/4 UI; ordinary builds; full suites ещё не приняты | Довести текущие full/native gates и обновить post-EXEC verdict | «Нет, работай один» | `chat-artifacts/remove-isgoal-*`, §19 |
| EXEC stop | Код удаления готов, общая приёмка ASK-HUMAN | Fresh UI 4/4 и capture 17/17; partial Main 1821/42, stalled Headless, native wide precondition failed; слот освобождён | Согласовать отдельное исправление выявленных общих blockers | Нового согласования нет | Итог §19, logs/PNG/TRX |
