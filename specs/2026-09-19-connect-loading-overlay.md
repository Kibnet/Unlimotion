# Анимация загрузки при «Подключить»

## Цель и границы

- Owner: пользователь; delivery-task, short SPEC по центральному `_template-small.md`: один локальный обратимый UI outcome, без изменения хранения, конфигурации и публичных API.
- Profiles: dotnet-desktop-client + ui-automation-testing; context testing-dotnet. Core: creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration/testing-baseline, QUEST и review gates. Среда: Windows, PowerShell, Avalonia/.NET; модельные настройки не меняются.
- AS-IS: `App.axaml.cs:404` назначает `Settings.ConnectCommand`. Команда выставляет `StorageConnectionState.Connecting`, но не `IsTaskSpaceSwitching`. Длительная работа проходит через `TaskSpaceCoordinator.ReconnectActiveAsync` или fallback `SwitchStorageAsync`, до позднего связывания MainWindowViewModel.
- `MainControl.axaml` показывает `TaskSpaceSwitchOverlay` по `Settings.IsTaskSpaceSwitching`. `MainScreenLoadingUiTests` проверяет прямой `vm.Connect()`, а не команду кнопки; поэтому этот пробел остаётся непроверенным.
- Outcome: при подключении из настроек видна существующая анимация до завершения операции; она не остаётся висеть после ошибки или раннего выхода.
- Non-Goals: изменение дизайна анимации/иконок, механики подключения/Git/recovery, форматов настроек, миграций, публикация PR или релиза.

## Результат, решения и проверки

Visual planning artifact — текстовый storyboard существующего интерфейса (нового layout нет):
`Настройки → Подключить → существующий overlay с анимацией поверх рабочей области → задачи либо сообщение ошибки/recovery без зависшего overlay`.
Диалоги подготовки/конфликтов не должны оказаться перекрыты и недоступны.

| Observable scenario / решение (owner) | AC / ожидаемый результат | Команда / evidence |
| --- | --- | --- |
| Нажать настоящую кнопку «Подключить», задержать источник тестовым gate | Overlay и SeamlessLoadingIndicator реально видимы, пока операция не завершена; UI dispatcher отвечает | Новый regression UI test через Settings.ConnectCommand; сначала RED, затем GREEN |
| Успех coordinator и fallback | Индикатор охватывает подготовку и загрузку, снимается после завершения | UI/командные тесты с контролируемым ожиданием |
| Ошибка, recovery, ранний выход подготовки | Снятие busy в finally; сохранены ошибки, recovery и возможность повторить | Негативные тесты SettingsViewModelTests / UI |
| Первичная загрузка и переключение пространства | Старые индикаторы и automation IDs работают | MainScreenLoadingUiTests и существующие task-space UI tests |

Решение агента: использовать существующий `Settings.IsTaskSpaceSwitching` в границах ConnectCommand, включать до длительных await, гарантированно снимать в finally; учитывать уже занятую операцию, не сбрасывать чужой busy-state. Не добавлять искусственную задержку пользователю. Не менять ownership `MainWindowViewModel.IsTasksLoading` для прямого Connect. Если реальные UI-тесты выявят перекрытие диалогов, ограничить область overlay существующим рабочим слоем, сохранив доступность диалогов; не перестраивать navigation.

Файлы: `src/Unlimotion/App.axaml.cs` — жизненный цикл команды; `src/Unlimotion.Test/SettingsViewModelTests.cs` и релевантный UI test class — воспроизведение через реальную команду/кнопку и проверка effective visibility; при необходимости точечный selector в SettingsControl без смены существующих IDs.

Риски/ожидаемые возражения: «анимация есть только в тесте» — не вызывать vm.Connect напрямую; «ошибка оставляет окно навсегда» — отрицательные случаи; «второе подключение гасит первое» — busy ownership и повторный запуск; «анимация замёрзла» — проверка dispatcher без синхронной блокировки. Данные/миграции неприменимы: меняется только presentation lifecycle. Rollback — откат локального UI-фикса и его тестов, без изменения хранилища.

План: после approval добавить failing regression; исправить команду; targeted UI/command tests; Desktop build; полный набор тестов решения с принятым TUnit runner. Перед длительным запуском проверить SDK/restore и сохранить progress/log. Пример targeted: `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/MainScreenLoadingUiTests/*' --maximum-parallel-tests 1 --output Detailed` (дополнить классом нового теста). Build: `dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug`. Full: все solution test projects, serial TUnit, без targeted filter; сначала inventory. Если полный прогон/видеозапись недоступны — явно сообщить blocker и next-best evidence, не объявлять полный green.

Video до/после: использовать существующий безопасный UI harness после проверки возможности записи. Предыдущий desktop capture в этой ветке завершался Access denied; это исторический риск, не доказательство текущего отказа. При объективной невозможности записи — точная команда/ошибка, failing/passing UI logs и headless screenshot как fallback. Stop: не расширять изменение до других операций или дизайна; не отправлять ветку автоматически.

## Quality gate и review

Linter: 1 PASS outcome; 2 PASS inspected command/XAML/tests; 3 PASS missed state; 4 PASS reuse overlay; 5 PASS scope; 6 PASS command owner; 7 PASS bindings/coordinator/fallback; 8 PASS finally/busy ownership; 9 PASS error/recovery/early return; 10 PASS nonblocking/no artificial delay; 11 PASS presentation-only; 12 PASS no migration; 13 PASS local rollback; 14 PASS observable cases; 15 PASS positive/negative mapping; 16 PASS commands/full suite/stop; 17 PASS staged TDD; 18 PASS decisions fixed; 19 PASS short eligibility; 20 PASS UI/build/full tests/video contract.

Rubric: цель/границы 5, AS-IS 5, дизайн 5, безопасность 5, проверяемость 5, автономность 5 = 30/30, относится к готовности плана, не к выполнению.

Post-SPEC full self-review: Scope/Evidence — App command, coordinator reconnect, manager fallback, MainScreen/MainControl overlay, MainScreenLoadingUiTests, SettingsViewModelTests entry points и central owners. Contract — существующий рисунок и storage semantics неизменны. Adversarial — проверены риски позднего busy, early return, исключений, конкуренции и диалогов. Role-based: UX PASS (существующий overlay/storyboard); Tester PASS (реальная команда, effective visibility и негативные случаи); Developer PASS (finally/ownership); Delivery PASS (нет push, validation bounds); Business N/A (бизнес-логика неизменна). Отдельный adversarial self-review; независимый read-only sandbox не подтверждён, независимая проверка не заявляется.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Tests | Прямой vm.Connect не покрывает кнопку | Реальная команда и effective visibility | fixed in plan |
| MEDIUM | Lifecycle | Busy может остаться при early return/error | finally и негативные случаи | fixed in plan |
| MEDIUM | UX | Overlay может перекрыть подготовительные диалоги | Проверить доступность диалогов в UI | fixed in plan |

Re-review: все actionable findings учтены в AC. Depth: рабочее дерево до spec чистое; нет скрытых config/API изменений; документы — эта spec; performance claims отсутствуют. Manual challenge — проверить реально видимый overlay, а не одно bool-свойство. Stop decision PASS для запроса approval. Открытых продуктовых вопросов нет.
EXEC progress: RED — 2 новых UI-кейса упали на overlay.IsEffectivelyVisible до исправления.
Команда теперь владеет IsTaskSpaceSwitching до подготовки и освобождает его в finally;
если пространство уже занято, новый Connect не запускается и чужое состояние не снимается.
GREEN — MainScreenLoadingUiTests 4/4 (включая binding реальной кнопки, видимость,
dispatcher, успех/ошибку и busy guard), SettingsViewModelTests/ConnectCommand* 4/4
(включая ранний выход при Git-конфликте). Desktop Debug и Headless test host build PASS.
Полные main/headless запущены через scripts/ci/Invoke-TestStage.ps1, результаты
artifacts/validation/connect-loading-full. Headless 42/42 PASS за 3m05s.
Нативный FlaUI full run с --fail-fast: 7 passed, затем
Current_task_card_exposes_redesigned_sections_on_launch — отсутствует
CurrentTaskRepeaterSection; последующие 10 cancelled, host завершился с ошибкой.
Этот launch-сценарий не вызывает ConnectCommand, поэтому его ошибка не подтверждает
регрессию изменённого пути; исправление карточки задач вне scope. Полный зелёный
прогон решения не получен. Нативные Desktop/Headless/FlaUI test host сборки PASS.

Visual fallback: новый regression работает в Avalonia Headless без нативного окна;
CaptureRenderedFrame при UNLIMOTION_CONNECT_EVIDENCE не предоставил кадр (PNG не создан),
поэтому video/screenshot этого сценария не заявляются. Next-best evidence — TDD RED
на IsEffectivelyVisible, GREEN assertions реального visual tree и UI dispatcher,
полный Headless report. Нативный прогон не заменяет отсутствующую парную запись.

Post-EXEC review: Scope — App.axaml.cs diff, оба test diff,
spec, git status, build/test output. Contract — существующие индикаторы и IDs,
единственный owner команды, invariant finally, без storage/data/design изменений.
Adversarial — busy guard проверен до нового запуска, ошибка/успех gated, ранний return
проверен на Git-конфликте; blocker validation: FlaUI full red. Role-based: UX —
effective visibility PASS, visual capture limitation; Developer — lifecycle PASS;
Tester — targeted/full Headless PASS, full solution NEEDS-FIX; Delivery — локально,
без commit/push, blocker не скрыт; Business N/A. Отдельный self-review, не independent.
Manual challenge — новый UI-test исполняет команду, связанную с ConnectLocalStorageButton,
но не эмулирует физический клик и не подменяет нативную проверку. Coordinator и fallback
охвачены одним внешним finally; новый delayed UI regression использует fallback factory.
Существующие transaction tests проверяются основным набором. Runtime diff 13 строк,
лишних изменений нет. Отдельного исправления тестов карточки задач не предпринималось.

Финальная проверка 2026-09-20 (локальная дата): main 1074/1074 PASS за 21m32s,
Headless 42/42 PASS; Desktop build и оба UI test host build PASS; diff --check PASS.
TRX/HTML и диагностика сохранены в artifacts/validation/connect-loading-full/main
и headless; сведения о падении native run — flaui-failure.md в том же каталоге.
Re-review: фактический outcome соответствует AC для команды кнопки; ошибки и ранний
выход освобождают overlay, чужой busy сохраняется. Код анимации и хранилища неизменен.
Stop decision: локальное исправление реализовано, но полная нативная валидация
NEEDS-FIX из-за отдельного launch-сценария карточки. Не заявлять full solution green
или успешное видео до/после. Следующий независимый шаг — диагностировать отсутствие
CurrentTaskRepeaterSection в FlaUI, если пользователь разрешит эту отдельную работу.
Commit/push не выполнялись.

## Approval

Подтверждено пользователем «Спеку подтверждаю» 2026-09-19. Фаза EXEC.

## Журнал действий агента

| Фаза / блок | Решение / уверенность | Evidence / что неизвестно | Следующий шаг | Передача человеку / фактическое решение |
| --- | --- | --- | --- | --- |
| SPEC / диагностика | Пропущен busy в ConnectCommand / 0.98 | App:404, coordinator, XAML; reproducing test ещё не запускался | согласовать план | требуется exact approval |
| SPEC / review | Short, существующий overlay / 0.96 | сценарии успех/error/early return, test gap и диалоги учтены | TDD после approval | пользователь попросил исправить; exact approval этой spec пока нет |
| EXEC / TDD и исправление | локальный lifecycle fix / 0.98 | RED 2/2, GREEN targeted 8/8, builds PASS | полные тесты и review | approval получено, уточнений не требуется |
| EXEC / итоговая проверка | fix подтверждён, native gate не зелёный / 0.98 | main 1074/1074, Headless 42/42; FlaUI 7 passed и launch failure | передать локальный результат и ограничение | в итоговом ответе; native fix вне scope |
