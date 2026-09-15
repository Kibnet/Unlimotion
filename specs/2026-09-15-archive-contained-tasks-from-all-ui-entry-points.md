# Предлагать архивацию невыполненных вложенных задач из любого UI-способа архивации

## Цель и границы

- Владелец: пользователь / Unlimotion desktop.
- Профили: `dotnet-desktop-client`, `ui-automation-testing`; форма SPEC — Short, потому что изменение даёт один локальный обратимый UI-outcome, не меняет модель данных, storage schema, security, публичный API или внешние side effects.
- Связанная задача: `1d8922e7-9c08-4fed-a3de-24afd2da22de` — «При архивации задачи через статус с вложенными невыполненными не появилось предложение об архивации вложенных задач».
- AS-IS: `TaskItemViewModel.ArchiveCommand` после успешной архивации родителя предлагает архивировать активные вложенные задачи. `TaskItemViewModel.StatusOption` и `TaskStatusPicker` имеют отдельные пути: setter раньше вызывал `TryTransitionToStatusAsync`, а реальный click `TaskStatusPicker` также напрямую вызывал его, поэтому архивация через статус обходила предложение. `TaskStatusPicker` используется во всех desktop-представлениях, включая `MainControl` и граф.
- Проблема: одинаковое пользовательское действие имеет разное каскадное поведение в зависимости от UI-контрола.
- Наблюдаемый outcome: при архивации активной задачи как через меню действий, так и через выбор статуса `Archived`, после успешной архивации родителя появляется один и тот же диалог с предложением архивировать её активные вложенные задачи; согласие архивирует их, отказ оставляет их без изменения.
- Non-Goals: не менять правила разархивации; не архивировать `Completed`/уже `Archived` descendants; не менять storage/domain transition policy; не добавлять интерактивный prompt в CLI, Telegram, server sync или фоновые обновления; не менять формулировки локализации без выявленной необходимости; не коммитить, не пушить и не создавать PR без отдельной просьбы.
- Effective runtime: Codex desktop, текущая модель/effort не влияют на продуктовый контракт. Целевой runtime продукта — Avalonia desktop на поддерживаемых Windows/Linux/macOS; проверка реального окна на текущем Windows-хосте.

## Результат, решения и проверки

Visual planning artifact — компактный storyboard состояний (новая компоновка не требуется):

```text
[Активная родительская задача + активные вложенные]
        | меню действий -> «Архивировать»
        | ИЛИ пикер статуса -> «Архивировано»
        v
[Родитель успешно архивирован]
        v
┌ Архивировать вложенные задачи? ┐
│ Архивировать N вложенных задач │
│ из «Название»?                 │
│       [Нет]       [Да]         │
└────────────────────────────────┘
   | Нет                 | Да
   v                     v
[дети без изменений]  [активные дети архивированы]
```

| Observable scenario / решение (owner) | AC / ожидаемый результат | Команда / evidence |
| --- | --- | --- |
| Пользователь выбирает `Archived` в `TaskStatusPicker` у родителя с активными descendants; agent выбирает единый интерактивный archive workflow | AC1: родитель архивируется, затем показывается ровно одно подтверждение с тем же EN/RU contract, что у команды меню | Targeted `TaskItemViewModelStatusCommandTests`; UI-тест через реальный status picker; screenshot/trace диалога |
| Пользователь подтверждает предложение | AC2: все descendants со статусами `NotReady`, `Prepared`, `InProgress` получают `Archived`; `Completed` и `Archived` не затрагиваются; итоговый toast честно сообщает success/failure counts | TUnit ViewModel regression с mixed statuses и UI flow |
| Пользователь отклоняет предложение | AC3: родитель остаётся архивированным, статусы descendants не меняются | TUnit ViewModel regression + UI flow |
| Пользователь архивирует через меню действий | AC4: существующее поведение и диалог сохраняются без двойного prompt | Существующие archive-command тесты + общий parity regression |
| Архивация родителя отклонена storage/policy либо lifecycle закрывается | AC5: prompt и cascade не запускаются; stale result не перезаписывает более новое состояние | Существующие status/lifecycle tests и targeted regression |
| У задачи нет активных descendants или нет UI notification manager | AC6: родитель архивируется без prompt; фоновые/CLI вызовы остаются неинтерактивными | Unit regression; существующие CLI integration tests не меняют output contract |

- Решение агента: добавить единый awaitable entry point выбора статуса в `TaskItemViewModel` и направить в него как setter `StatusOption`, так и реальный click `TaskStatusPicker`. Для target `Archived` он запускает существующий tracked archive workflow; другие target statuses продолжают идти через обычный `TryTransitionToStatusAsync`.
- Решение агента: каскад остаётся post-parent — предложение появляется только после подтверждённой успешной архивации родителя, как в текущем `ArchiveCommand` contract.
- Решение агента: «вложенные» означает текущий результат `GetChildrenTasks`, то есть все доступные descendants, а не только непосредственные дети; фильтр остаётся `Status.IsActive()`.
- Решение агента: «любой способ» ограничен интерактивными desktop UI entry points. Прямой публичный `TryTransitionToStatusAsync`, CLI/Telegram/server/background mutations не должны неожиданно показывать диалог.
- Изменяемые production-файлы: `src/Unlimotion.ViewModel/TaskItemViewModel.cs` — awaitable маршрутизация выбора статуса в единый archive workflow; `src/Unlimotion/TaskStatusPicker.cs` — использование маршрута вместо прямого status mutation. Локализации изменяются только если текущий текст не переиспользуется.
- Изменяемые test-файлы: `src/Unlimotion.Test/TaskItemViewModelStatusCommandTests.cs` — command parity и edge cases; релевантный AppAutomation Headless/FlaUI scenario/data/page object — пользовательский проход через status picker и подтверждение. Точный минимальный набор определяется при EXEC после проверки существующего harness, без изменения unrelated status-contract сценария.
- Ошибки: сохраняются текущие `TaskStatusCascadeConfirmationFailed` и `TaskStatusCascadeSummary`; storage denial не маскируется диалогом.
- Rollback: вернуть маршрутизацию `StatusOption -> TryTransitionToStatusAsync`; данные и схема не мигрируются.
- UI video evidence: на EXEC записать автоматизированный before/after flow, если текущий FlaUI runner/recorder поддерживает modal interaction. Если baseline-видео уже невозможно получить после начала реализации либо recorder технически недоступен, явно зафиксировать fallback: headless semantic assertions + screenshot диалога + test log. Артефакты local-only, не коммитятся по умолчанию.
- Stop rules: остановиться и обновить SPEC, если единый путь требует изменения публичного CLI/API contract, storage schema или семантики non-UI status mutations; падающий релевантный UI-тест блокирует завершение.

## Риск / возражения / решения

- Вероятное возражение: «любой способ» должен включать CLI. Mitigation: CLI — неинтерактивная поверхность без безопасного механизма prompt; текущая задача описывает исчезнувшее UI-предложение при выборе статуса. CLI/API остаются вне scope, чтобы не ломать automation contract.
- Вероятное возражение: диалог может появиться до фактической архивации родителя. Mitigation: сохранить post-parent последовательность и отдельно проверить storage denial.
- Вероятное возражение: повторное использование команды породит двойной prompt или две записи history. Mitigation: оба UI-входа вызывают один workflow ровно один раз; тесты считают confirmation и status calls/history.
- Риск: fire-and-forget setter `StatusOption` скроет async race. Mitigation: использовать уже существующий tracked producer/lifecycle boundary, не создавать второй независимый pipeline.
- Открытых вопросов, блокирующих EXEC, нет.

## Quality gate и review

### Linter

| Блок | Статус | Обоснование |
| --- | --- | --- |
| A. Полнота | PASS | Цель, AS-IS, scope, non-goals, observable scenarios и approval определены |
| B. Дизайн | PASS | Указан единый owner workflow, entry points и сохранение non-UI contract |
| C. Безопасность | PASS | Нет миграции; denial/lifecycle/rollback описаны |
| D. Проверяемость | PASS | AC1–AC6 имеют automated/manual evidence |
| E. Автономность | PASS | User-owned решений перед EXEC нет |
| F. Профиль | PASS | Обязательные UI tests и visual evidence включены |

### Rubric

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели и границ | 5 | Один outcome и явная граница UI/non-UI |
| Понимание AS-IS | 5 | Найдены расходящиеся `ArchiveCommand` и `StatusOption` paths |
| Конкретность дизайна | 5 | Определена маршрутизация и неизменяемые contracts |
| Безопасность | 5 | Post-parent, denial, lifecycle и rollback закрыты |
| Тестируемость | 5 | Есть unit/UI/visual mapping и negative cases |
| Автономная реализация | 5 | Блокирующих решений нет |

Итого: 30/30, готово к автономному выполнению после approval.

### Role-Based Review

| Role | Verdict | Review result |
| --- | --- | --- |
| Business analyst / domain workflow | PASS | Одинаковая архивация из двух desktop UI-входов; descendants отбираются по действующему правилу |
| UX / designer | PASS | Сохраняется знакомый диалог после успешной архивации; новый layout не вводится |
| Tester / validation | PASS | Покрыты confirm/decline/no-children/denial/mixed-status/double-call risks и реальный UI control |
| Developer / architect | PASS | Общий workflow локализован во ViewModel; domain/storage/public CLI contract не расширяется |
| Delivery / operations / security | Не применимо | Нет deploy/config/secrets/publication; commit/push вне разрешённого scope |

### Post-SPEC

- Scope/Evidence: прочитаны задача CLI, `TaskItemViewModel.StatusOption`, `ArchiveCommand`, tracked archive workflow, cascade localization и существующие unit/AppAutomation tests.
- Contract pass: AC покрывают оба UI-входа и сохраняют non-UI behavior.
- Adversarial pass: проверены storage denial, decline, empty descendants, mixed terminal states, lifecycle cancellation и double-prompt/history risks.
- Finding: первоначальная формулировка «любой способ» могла неограниченно включить CLI/фоновые mutations. Исправлено явной desktop UI boundary и stop rule.
- Re-review: PASS; объективных незакрытых findings и user-owned решений нет.
- Manual-review challenge: подтвердить, что UI-only трактовка «любого способа» соответствует намерению; approval этой SPEC подтверждает такую границу.
- Stop decision: PASS, ожидать approval.

### Post-EXEC

- Реализация: `TaskItemViewModel.TrySelectStatusOptionAsync` стал единым awaitable маршрутом интерактивного выбора статуса. Для `Archived` он повторно использует существующий `ExecuteTrackedArchiveCommandAsync`; остальные статусы по-прежнему идут в `TryTransitionToStatusAsync`.
- Entry-point audit: `StatusOption` и реальный обработчик click в `TaskStatusPicker` направлены в этот маршрут. Поиск не выявил оставшегося вызова `TryTransitionToStatusAsync(option.Status)`.
- Regression coverage: добавлены unit-сценарии для согласия и отказа с mixed descendants; добавлен Avalonia.Headless UI-тест, который открывает реальный `TaskStatusPicker`, нажимает `TaskStatusOptionArchived`, проверяет одно подтверждение и сохранённые статусы родителя/ребёнка.
- Validation: `TaskItemViewModelStatusCommandTests` — 26/26 PASS; `MainControlTaskStatusIconUiTests` — 22/22 PASS; полный `Unlimotion.Test` — 1013/1013 PASS; `dotnet build src\\Unlimotion.sln --no-restore -p:UseSharedCompilation=false` — PASS. Сборка содержит существующие Android warnings `XA0141`/`XA4301` для `LibGit2Sharp.NativeBinaries`, ошибок нет.
- Visual evidence fallback: Avalonia.Headless harness не предоставляет recorder и использует mock notification boundary, поэтому видео/скриншот нативного modal не были получены — такой скриншот не был бы доказательством реального диалога. Вместо этого приложен автоматизированный UI trace через реальный picker и HTML test report. Следующее необязательное evidence для ручного product-demo: записать окно desktop приложения в изолированном task space с активным вложенным child.
- Adversarial re-review: PASS. Архивирование родителя не меняет порядок post-parent prompt, отказ оставляет children без изменений, terminal descendants не касаются, прямые non-UI status mutations не получают prompt.

## Approval

Approved: пользователь написал «Спеку подтверждаю».

## Журнал действий агента

| Фаза / блок | Решение / уверенность | Evidence / что неизвестно | Следующий шаг | Передача человеку / фактическое решение |
| --- | --- | --- | --- | --- |
| SPEC / intake | Воспроизвести intent задачи по полному CLI contract / 0.98 | Task `Prepared`, canStart; описание прямо называет status path | Найти все archive entry points | Не требуется / задача выбрана пользователем |
| SPEC / code audit | Объединить два desktop UI archive paths / 0.96 | `ArchiveCommand` имеет cascade prompt; `StatusOption` вызывает direct status transition | Зафиксировать UI/non-UI boundary и тесты | Не требуется |
| SPEC / review | Short SPEC готова; UI-only boundary выбран / 0.92 | CLI не имеет интерактивного prompt contract; blocking unknowns отсутствуют | Запросить точное approval | Ожидается «Спеку подтверждаю» / обращение выполнено |
| EXEC / approval | Войти в EXEC после точной фразы пользователя / 1.00 | Пользователь написал «Спеку подтверждаю» | Реализовать единый UI archive path и regression tests | Approval получен / «Спеку подтверждаю» |
| EXEC / implementation | Направить `StatusOption -> Archived` в существующий tracked archive workflow / 0.98 | Существующие `ArchiveCommand` и cascade contract сохранены; добавлены unit и Avalonia.Headless tests | Запустить compile и targeted tests | Не требуется |
| EXEC / UI-path audit | Исправить фактический `TaskStatusPicker`, а не только setter / 1.00 | Headless click показал, что picker напрямую вызывал `TryTransitionToStatusAsync`; это объяснило отсутствие prompt | Добавить awaitable ViewModel entry point, направить picker в него и повторить tests | Не требуется |
| EXEC / validation | Подтвердить общий UI workflow и отсутствие регрессий / 0.99 | Unit 26/26, UI 22/22 и полный TUnit 1013/1013 прошли; solution build PASS с известными Android package warnings | Провести final diff review | Не требуется |
| EXEC / final review | Изменение готово локально, без delivery side effects / 0.98 | Route scan не оставил direct picker transition; `git diff --check` PASS. Headless harness не может дать нативное visual recording | Передать результат пользователю без commit/push | Не требуется |
