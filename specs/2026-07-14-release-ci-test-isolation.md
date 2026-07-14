# Стабилизация релизного CI перед публикацией Unlimotion 1.27.0

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`
- Владелец: Codex / Unlimotion
- Масштаб: small
- Целевая модель: gpt-5
- Целевой релиз / ветка: `1.27.0`; рабочая ветка `fix/release-ci-test-isolation` от актуального `origin/main`
- Ограничения: до утверждения спеки не менять код и тесты; не публиковать tag/release при красном CI; не менять поведение приложения, формат файлов задач и публичные контракты
- Связанные ссылки: GitHub Actions run `29357745445` — https://github.com/Kibnet/Unlimotion/actions/runs/29357745445; текущий release `1.26.0` — https://github.com/Kibnet/Unlimotion/releases/tag/1.26.0

Если секция не применима, указано `Не применимо` и причина.

## 1. Overview / Цель
Устранить подтверждённые гонки в test harness, получить зелёный полный релизный CI и только после этого опубликовать GitHub Release `1.27.0` и подготовленное русское сообщение в Telegram.

Outcome contract:
- Success means: test helpers дожидаются фактического завершения асинхронной команды и отложенных сохранений; связанные regression tests устойчиво проходят; полный GitHub Actions workflow на commit, предназначенном для релиза, зелёный; tag и GitHub Release `1.27.0` опубликованы; Telegram-сообщение отправлено только в явно подтверждённый пользователем канал.
- Итоговый артефакт / output: test-only fix, PR с validation evidence, GitHub Release `1.27.0` с русскими release notes, русское Telegram-сообщение со ссылкой на релиз.
- Stop rules: не создавать и не перемещать release tag при красном CI; вернуться в SPEC, если для исправления потребуется product/runtime/UI change; перед отправкой в Telegram запросить точное подтверждение адресата и текста в соответствии с confirmation policy.

## 2. Текущее состояние (AS-IS)
- `HEAD` и `origin/main` указывали на `9803079`; рабочее дерево было чистым до создания этой спеки.
- Последний GitHub Release — `1.26.0`; следующий релиз по составу изменений является feature release `1.27.0`.
- GitHub Actions run `29357745445` дважды завершился с `598/600` в `Unlimotion.Test`.
- Попытка 1:
  - `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls`: `CurrentTaskDetailsPanelFrame` получил нулевую ширину;
  - `CopyTaskOutline_WritesCurrentTaskSubtreeToClipboard`: в outline отсутствовал grandchild.
- Попытка 2:
  - повторился сбой `CopyTaskOutline_WritesCurrentTaskSubtreeToClipboard`;
  - `CreateBlockedSibling_ShouldRequestTitleFocusAndOpenDetails` увидел `TitleFocusRequestVersion == 0` вместо инкремента;
  - после summary runner вывел фоновые `FileNotFoundException` при чтении уже удалённых fixture-файлов.
- Каждый из двух повторяющихся тестов проходит изолированно, что указывает на незавершённую фоновую работу и нарушение lifecycle тестовой fixture, а не на устойчивый product defect.
- `TestHelpers.CreateAndReturnNewTaskItem(ICommand, ...)` вызывает `command.Execute(null)` и возвращается после увеличения `Tasks.Count`, хотя продолжение `ReactiveCommand` ещё может устанавливать `CurrentTaskItem`, открывать details и запрашивать title focus.
- `WaitForPendingSavesAsync` ждёт уже зарегистрированные saves, но не ожидает throttle, который ещё не сработал.
- В outline-тесте title save дочерней задачи может пересечься с последующим `AddChild` и записью связи с grandchild.
- Release tag и GitHub Release `1.27.0` не создавались; Telegram-сообщение не отправлялось.

## 3. Проблема
Одна корневая проблема: тесты считают async command/save sequence завершённой по промежуточному observable state (`Tasks.Count` или завершившемуся явному `Update`), после чего fixture может продолжить следующий шаг либо удалить временные файлы, пока фоновые продолжения ещё работают.

Это делает релизный gate недостоверным и не позволяет безопасно публиковать `1.27.0`.

## 4. Цели дизайна
- Ждать семантического завершения операции, а не использовать blanket retry или увеличенный общий timeout.
- Сохранить production-код и пользовательское поведение без изменений.
- Ограничить fix общими test helpers и одним тестом, где явно создаётся конфликт throttle save с изменением связей.
- Проверить исправление на точечных сценариях, полном локальном suite и GitHub Actions.
- Публиковать release только от проверенного commit в основной ветке.

## 5. Non-Goals (чего НЕ делаем)
- Не менять `MainWindowViewModel`, `TaskItemViewModel`, storage/runtime и Avalonia layout ради прохождения тестов.
- Не добавлять retry для упавших тестов и не маскировать исключения runner.
- Не увеличивать произвольно timeout ожиданий.
- Не менять release workflows, versioning convention или persisted task schema.
- Не менять production layout/XAML для `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls`; после повторного remote failure допускается только deterministic test-harness arrangement, описанный в SPEC amendment ниже.
- Не отправлять Telegram-сообщение в личный чат или предполагаемый канал без явного подтверждения адресата.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/TestHelpers.cs`:
  - overload для `ICommand` должен после запуска дождаться появления ожидаемых задач;
  - известные типизированные `ReactiveCommand<Unit, Unit>` и `ReactiveCommand<bool, Unit>` запускать через их observable `Execute(...).ToTask()`, чтобы await представлял полное выполнение команды;
  - затем дождаться pending saves и проверить итоговое количество задач.
- `src/Unlimotion.Test/MainWindowViewModelTests.cs`:
  - outline regression test должен завершить throttle/pending save дочерней задачи до добавления grandchild;
  - перед copy проверить, что связи и title persisted/in-memory state готовы.
- Production-файлы: без изменений.
- Delivery:
  - branch -> commit -> push -> PR -> зелёные checks -> merge в `main`;
  - повторный зелёный release gate на release commit;
  - numeric tag `1.27.0` и GitHub Release с русскими notes;
  - Telegram send после action-time подтверждения адресата и итогового текста.

### 6.2 Детальный дизайн
#### Ожидание ReactiveCommand
1. Зафиксировать `taskCountBefore`.
2. Для известных repository commands вызвать типизированный `ReactiveCommand.Execute(...).ToTask()`; подписка `ToTask()` одновременно запускает command и даёт task полного выполнения.
3. Дождаться ожидаемого увеличения `Tasks.Count`, чтобы подтвердить фактический старт/эффект команды.
4. Дождаться command task. Это покрывает продолжение после `Tasks.Add`, включая выбор новой задачи, открытие details и запрос focus.
5. Дождаться pending saves всех текущих task view models.
6. Проверить count и вернуть созданную задачу.

Обычные non-reactive `ICommand` сохраняют текущий путь `Execute(null)` + ожидание по count и pending saves.

#### Стабилизация outline fixture
- После изменения title и явного `Update(child)` выполнить ожидание throttle window и pending saves до `AddChild(child)`.
- После изменения grandchild выполнить такой же flush до `CopyTaskOutline`.
- Проверять итоговый outline без retry: отсутствие grandchild после явного flush остаётся реальным failure.

#### Layout failure
- Отдельного изменения layout test не планируется: он прошёл во второй попытке и изолированно, а zero bounds совместимы с незавершённой/утёкшей фоновой работой соседних fixtures.
- Если после planned fix он снова упадёт в полном serial suite или GitHub Actions, релиз останавливается и scope возвращается в SPEC с новой evidence; добавлять delay в UI test без воспроизведения запрещено.

#### Visual planning artifact
Не применимо: production UI и visual flow не меняются.

#### UI test video evidence
Не применимо: изменение test synchronization не меняет UI behavior. Для релиза используются TUnit/GitHub Actions logs.

## 7. Бизнес-правила / Алгоритмы
- GitHub Release разрешён только при зелёных обязательных checks на commit, вошедшем в `main`.
- Tag `1.27.0` создаётся один раз и не перемещается после публикации.
- Формат tag сохраняет репозиторную numeric convention: `1.27.0`.
- Release notes и Telegram copy публикуются на русском; технические имена и ссылки сохраняются без перевода.
- Telegram отправляется только после явного выбора целевого чата/канала пользователем и подтверждения финального текста непосредственно перед send.

## 8. Точки интеграции и триггеры
- Все тесты, использующие `CreateAndReturnNewTaskItem(ICommand, ...)`, получают более строгий completion contract.
- `CopyTaskOutline_WritesCurrentTaskSubtreeToClipboard` фиксирует lifecycle title/relations внутри своей fixture.
- GitHub Actions workflow `.github/workflows/tests.yml` остаётся без изменений и выполняет оба test projects с `--maximum-parallel-tests 1`.
- После merge зелёный commit в `main` становится source для release workflow/tag.

## 9. Изменения модели данных / состояния
- Persisted schema: без изменений.
- Runtime state приложения: без изменений.
- Public API: без изменений.
- Test-only synchronization contract: helper возвращает управление только после полного завершения reactive command и зарегистрированных saves.

## 10. Миграция / Rollout / Rollback
- Миграция: не требуется.
- Rollout:
  1. test-only fix в отдельной ветке;
  2. targeted/repeated и full validation;
  3. PR и GitHub checks;
  4. merge;
  5. release `1.27.0` после зелёного `main`;
  6. Telegram publication после подтверждения адресата.
- Rollback code fix: revert test-only commit; product binaries и task data не затрагиваются.
- Rollback release: опубликованный tag не перемещать; при критической ошибке пометить release/создать patch release по отдельному решению.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
1. `CreateAndReturnNewTaskItem(ICommand, ...)` не возвращается до завершения task типизированного `ReactiveCommand`.
2. `CreateBlockedSibling_ShouldRequestTitleFocusAndOpenDetails` проходит без дополнительного локального ожидания focus в самом тесте.
3. `CopyTaskOutline_WritesCurrentTaskSubtreeToClipboard` стабильно включает grandchild после явного flush отложенных saves.
4. Targeted affected tests проходят не менее трёх последовательных запусков без retry-on-failure.
5. Полный `src/Unlimotion.Test` проходит `600/600` локально exact/next-best CI command с serial execution.
6. `tests/Unlimotion.UiTests.Headless` проходит полностью, как требует локальный UI testing override и release workflow.
7. GitHub Actions checks на PR и на release source commit зелёные.
8. Diff не содержит production/UI/runtime изменений.
9. GitHub Release `1.27.0` опубликован с русскими notes и ссылкой на compare `1.26.0...1.27.0`.
10. Telegram-сообщение содержит актуальную release URL и отправлено только в подтверждённый адресат.

### Characterization / contract checks
- До fix evidence уже получен двумя CI attempts: focus continuation и nested outline иногда не завершены до assertions/cleanup.
- Добавлять новый production-facing test не требуется; исправляются synchronization contracts существующих tests.
- При необходимости добавить focused test для helper, только если существующие affected tests не доказывают ожидание command completion однозначно.

### Команды проверки
- Targeted tests:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowViewModelTests/CreateBlockedSibling_ShouldRequestTitleFocusAndOpenDetails" --maximum-parallel-tests 1 --output Detailed`
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --treenode-filter "/*/*/MainWindowViewModelTests/CopyTaskOutline_WritesCurrentTaskSubtreeToClipboard" --maximum-parallel-tests 1 --output Detailed`
- Repeated validation: выполнить оба targeted tests последовательно минимум три раза отдельными process runs.
- Full exact CI route:
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed`
  - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed`
- Если локальный SDK снова сообщает, что test projects не найдены, использовать TUnit runner через `dotnet run` с теми же TUnit arguments и отдельно зафиксировать отличие от CI.
- Static checks: `dotnet build Unlimotion.sln -c Debug --no-restore -p:UseSharedCompilation=false`, `git diff --check`.
- Delivery check: GitHub Actions run должен завершиться success; частичный rerun допускается только для диагностики, но не заменяет final green workflow на release source commit.

### Stop rules validation loop
- Не лечить новый failure бесконечным rerun: один повтор без code change допустим для классификации; повторяющийся failure требует root-cause analysis.
- Любая необходимость изменить product/runtime/UI code возвращает задачу в SPEC.
- Любой красный required check блокирует tag/release.

## 12. Риски и edge cases
- Типизированная обработка должна покрывать оба command shape, которые передаются helper в текущем suite: parameterless `Unit` и boolean sibling command.
- Некоторые `ICommand` не являются `IReactiveCommand`; для них сохраняется существующий count/pending-save contract.
- Ожидание throttle увеличит длительность одного outline test, но не всего suite существенно.
- Глобальный helper change затрагивает много тестов; поэтому обязателен полный suite, а не только два targeted tests.
- Zero-width layout failure может иметь отдельную причину; он не должен быть скрыт. Повтор после fix останавливает release и инициирует новый SPEC pass.
- GitHub Actions/packaging могут выявить независимый environment failure; он классифицируется отдельно и не обходится публикацией вручную.

## 13. План выполнения
1. После approval создать branch `fix/release-ci-test-isolation` от актуального `origin/main`.
2. Изменить `CreateAndReturnNewTaskItem(ICommand, ...)`, добавив deterministic wait завершения reactive command.
3. Добавить explicit save flush в outline test.
4. Запустить targeted tests три раза, полный `Unlimotion.Test`, headless UI suite, build и `git diff --check`.
5. Выполнить post-EXEC review; при PASS сделать Conventional Commit, push и PR с evidence.
6. Дождаться зелёных PR checks, merge и проверить зелёный `main`.
7. Опубликовать numeric tag/GitHub Release `1.27.0` с русскими notes; проверить release assets/workflows.
8. Показать пользователю точный Telegram-текст и запросить action-time подтверждение целевого канала; после подтверждения отправить и проверить видимость сообщения.

## 14. Открытые вопросы
- Блокирующих вопросов для code fix нет.
- Перед Telegram send потребуется точное имя/ссылка целевого канала или чата и подтверждение финального текста; это намеренно отложено до готовой release URL.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`; context `testing-dotnet`; governance `quest-governance`, `github-delivery-policy`, `versioning-policy`.
- Локальный UI override выполнен через обязательный полный запуск `tests/Unlimotion.UiTests.Headless`; UI behavior не меняется, поэтому новое UI coverage и video artifact не требуются.
- TUnit filtering использует `--treenode-filter`.
- Delivery идёт через отдельную branch и PR; release создаётся только от зелёного `main`.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/TestHelpers.cs` | Выполнять известные `ReactiveCommand` через awaitable observable result | Не возвращать управление на промежуточном состоянии команды |
| `src/Unlimotion.Test/MainWindowViewModelTests.cs` | Явно завершить throttled/pending saves в outline test | Исключить пересечение title save и изменения child relations |
| `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` | После повторного remote failure проверять arrangement внешнего frame и корректно arrange pane root в fallback | Не считать искусственную ширину inner ScrollViewer достаточным признаком готовности details pane |
| `specs/2026-07-14-release-ci-test-isolation.md` | QUEST spec, quality gates и журнал | Зафиксировать разрешённый scope и evidence |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| ICommand helper | Возвращается после изменения count | Возвращается после count, await typed reactive command и pending saves |
| Outline fixture | Title save может быть ещё в throttle при `AddChild` | Throttle и pending save завершены перед изменением relation |
| Production behavior | Без изменений | Без изменений |
| Release gate | Два красных запуска `598/600` | Требуется полный зелёный CI до tag |

## 18. Альтернативы и компромиссы
- Вариант: увеличить задержки/timeout во всех упавших тестах.
  - Плюсы: быстро.
  - Минусы: маскирует lifecycle bug, остаётся зависимость от скорости runner.
  - Решение: отклонено.
- Вариант: добавить retry упавших tests/workflow.
  - Плюсы: может временно озеленить CI.
  - Минусы: релизный gate перестаёт доказывать корректность; фоновые операции продолжают обращаться к удалённым fixtures.
  - Решение: отклонено.
- Вариант: изменить production commands/storage так, чтобы они завершались иначе.
  - Плюсы: может устранить симптом глобально.
  - Минусы: evidence указывает на test helper contract; product scope и риск несоразмерны.
  - Решение: отклонено, допустимо только после нового SPEC при доказанном runtime defect.
- Выбранный вариант: await observable result существующих типизированных `ReactiveCommand` и явный flush throttle boundary в одном тесте. Это минимально и проверяет реальное завершение операций.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, корневая проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, sequence, integration, данные и rollout определены. |
| C. Безопасность изменений | 11-13 | PASS | Test-only scope, stop rules и rollback ограничивают риск. |
| D. Проверяемость | 14-16 | PASS | Есть измеримые acceptance criteria, targeted/full/CI команды. |
| E. Готовность к автономной реализации | 17-19 | PASS | План, alternatives и file table достаточны; code blockers отсутствуют. |
| F. Соответствие профилю | 20 | PASS | TUnit, UI override, GitHub delivery и versioning gates отражены. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Release blocker и test-only scope сформулированы однозначно. |
| 2. Понимание текущего состояния | 5 | Учтены две CI attempts, isolated passes и конкретные lifecycle gaps. |
| 3. Конкретность целевого дизайна | 5 | Описаны точные completion conditions и последовательность flush. |
| 4. Безопасность | 5 | Product/data/API не меняются; tag запрещён до green CI. |
| 5. Тестируемость | 5 | Targeted repeated, full, headless UI и remote CI evidence обязательны. |
| 6. Готовность к автономной реализации | 5 | Planned files, delivery path и stop conditions определены. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: central/local instruction stack; GitHub Actions run `29357745445` attempts 1-2; `.github/workflows/tests.yml`; `TestHelpers.cs`; affected `MainWindowViewModelTests`; `MainControlTaskCardLayoutUiTests` arrangement helpers; release/version history.
- Decision: можно запрашивать подтверждение.
- Scope/Evidence pass: повторяющиеся failures связаны с промежуточным async state; isolated tests проходят; no tag/release side effects выполнены.
- Contract pass: spec не меняет production contract, запрещает retry masking и требует full local/remote validation.
- Adversarial pass:
  - Возражение «это может быть product bug»: isolated green + unobserved work after fixture cleanup указывают на test lifecycle; любое product change требует возврата в SPEC.
  - Возражение «почему не чинить layout test»: он не повторился во второй attempt; speculative delay запрещён, repeated failure после fix является stop condition.
  - Возражение «можно просто rerun»: две попытки дали `598/600`, одна failure повторилась; release без root-cause fix запрещён.
  - Возражение «helper change слишком широкое»: поэтому требуются repeated targeted tests и полный 600-test suite.
- Re-review after fixes: draft уточнён так, чтобы final CI run был именно на release source commit, а Telegram send имел отдельное action-time confirmation.
- Stop decision: BLOCKER/HIGH findings отсутствуют; до EXEC требуется human approval.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | layout evidence | Zero-width layout failure пока не воспроизведён повторно. | Не менять layout test спекулятивно; при повторе остановить release и вернуться в SPEC. | accepted-risk |
| LOW | Telegram target | Адресат сообщения пока не определён. | Запросить точный канал и финальное подтверждение непосредственно перед send. | deferred-to-human |

- Needs human: требуется фраза `Спеку подтверждаю`.
- Residual risk: независимый layout flake или внешний packaging failure может потребовать отдельного диагностического цикла; tag не создаётся до полного green evidence.

### Post-EXEC Review
- Статус: LOCAL PASS / REMOTE FAIL; первоначальное решение «можно коммитить и публиковать PR» отменено результатом GitHub Actions run `29361813320`.
- Scope reviewed: утверждённая spec; `src/Unlimotion.Test/TestHelpers.cs`; `src/Unlimotion.Test/MainWindowViewModelTests.cs`; полный diff/status; targeted, full TUnit и headless UI evidence.
- Decision: PR оставлен draft, merge/release запрещены; задача возвращена в SPEC amendment.
- Scope/Evidence pass:
  - Production/UI/runtime файлы не изменены.
  - `ICommand` helper выполняет используемые suite типы `ReactiveCommand<Unit, Unit>` и `ReactiveCommand<bool, Unit>` через awaitable observable result; другие commands сохраняют прежний fallback.
  - Outline test завершает throttle и pending saves до следующего изменения relation/copy.
- Contract pass:
  - Focus/details assertions выполняются после полного command completion.
  - Nested outline проверяется без retry.
  - Persisted schema, public API и app behavior не меняются.
- Adversarial pass:
  - Первый вариант observer по `IsExecuting` был отвергнут после targeted timeout; финальный вариант использует уже существующий repository pattern `Execute().ToTask()`.
  - Hardcoded command shapes проверены по всем call sites helper и полным suite; boolean command сохраняет прежнее значение `false`, которое раньше получалось из `Execute(null)`.
  - Спекулятивная правка layout test не внесена; полный `600/600` подтверждает, что отдельный delay не нужен.
- Validation evidence:
  - `CreateBlockedSibling_ShouldRequestTitleFocusAndOpenDetails`: 3/3 независимых process runs PASS после финального fix.
  - `CopyTaskOutline_WritesCurrentTaskSubtreeToClipboard`: 3/3 независимых process runs PASS.
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed`: PASS `600/600`, 0 failed, 17m 37s; фоновых исключений после summary нет.
  - После штатного restore: `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj ...`: PASS `31/31`, 0 failed, 1m 22s.
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -p:UseSharedCompilation=false /nodeReuse:false`: PASS, 0 errors; существующие warnings сохранены.
  - `git diff --check`: PASS; только существующие Windows line-ending warnings.
  - Full solution `--no-restore` fallback: не является зелёным, потому что у восьми несвязанных проектов отсутствовали `obj/project.assets.json` (Android, iOS, Server, TelegramBot, Browser, FlaUI, ReadmeMedia, Performance). Затронутые test/desktop dependency projects при этом собраны тестовыми командами; scope не расширен на restore всех платформ.
- Re-review after fixes: после замены `IsExecuting` observer повторно выполнены targeted tests, full suite, headless suite и diff review; новых findings не найдено.
- Remote evidence after publish:
  - PR #273: https://github.com/Kibnet/Unlimotion/pull/273
  - Commit: `ee586d9`.
  - Android и CodeQL checks PASS.
  - `All tests` run `29361813320`: FAIL `599/600`; повторился `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` с `CurrentTaskDetailsPanelFrame bounds=0,0,0,848`.
  - После summary остались три unobserved `FileTaskStorage.GetAll` `FileNotFoundException` по уже удалённым fixture directories.
- Stop decision: сработало предусмотренное правило возврата в SPEC при повторном layout failure; remote gate FAIL.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | build breadth | Full solution no-restore не имеет assets для восьми незатронутых проектов. | Использовать успешные affected builds/tests и GitHub platform workflows как release evidence. | accepted-risk |
| LOW | visual evidence | Video artifact не создан. | Не применимо для test-only synchronization; UI behavior не менялся, headless suite `31/31`. | not-applicable |

- Unrelated changes: не обнаружены.
- Needs human: нет для PR; перед Telegram send требуется action-time подтверждение адресата.
- Residual risk: remote runner всё ещё может выявить environment/platform failure; любой красный required check блокирует merge/release.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Release state и instruction routing | 0.92 | Нет approval для EXEC | Исследовать красный CI и сформировать fix scope | Да | Нет | Release `1.27.0` нельзя публиковать при красном required workflow; QUEST включён после необходимости code/test fix. | central/local instructions, GitHub release/run metadata |
| SPEC | CI root-cause analysis | 0.88 | Нужно подтвердить fix полным suite после EXEC | Зафиксировать deterministic synchronization design | Нет | Нет | Два CI attempts, isolated passes и фоновые file reads показывают незавершённые commands/saves. | `.github/workflows/tests.yml`, `src/Unlimotion.Test/TestHelpers.cs`, `src/Unlimotion.Test/MainWindowViewModelTests.cs` |
| SPEC | SPEC quality gate | 0.91 | Нет утверждения спеки | Запросить `Спеку подтверждаю` | Да | Нет | Linter/rubric/post-SPEC review PASS; scope test-only, release/Telegram gates определены. | `specs/2026-07-14-release-ci-test-isolation.md` |
| EXEC | Approval received | 0.95 | Нет | Реализовать test-only synchronization fix | Нет | Да: пользователь написал `Спеку подтверждаю` | Разрешённый переход SPEC->EXEC выполнен; scope и stop rules утверждены. | `specs/2026-07-14-release-ci-test-isolation.md` |
| EXEC | First targeted red and fix refinement | 0.92 | Нужен повторный targeted green | Перейти с `IsExecuting` observer на await типизированного `Execute().ToTask()` | Нет | Нет | Первый run показал timeout observer; прямой await уже является repository pattern и точнее выражает command completion без изменения scope. | `src/Unlimotion.Test/TestHelpers.cs`, `specs/2026-07-14-release-ci-test-isolation.md` |
| EXEC | Targeted validation | 0.97 | Нужны full suites | Запустить полный TUnit и headless UI suites | Нет | Нет | Оба исходно нестабильных сценария прошли по 3 независимых запуска без retry. | `src/Unlimotion.Test/TestHelpers.cs`, `src/Unlimotion.Test/MainWindowViewModelTests.cs` |
| EXEC | Full validation and post-EXEC review | 0.96 | Нужны remote PR checks | Commit, push и PR | Нет | Нет | `600/600` и `31/31` PASS, affected build и diff check PASS; solution-wide no-restore fallback классифицирован как missing assets незатронутых проектов. | planned three-file diff, local test reports |
| EXEC -> SPEC | Remote CI stop condition | 0.96 | Нужно новое approval для amendment | Дополнить SPEC deterministic layout arrangement и запросить подтверждение | Да | Нет | PR CI дал `599/600`: внешний frame имеет нулевую ширину, хотя helper считает pane готовой по inner ScrollViewer; merge/release остановлены. | GitHub Actions run `29361813320`, `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, current spec |
| EXEC | Amendment approval received | 0.97 | Нужна validation новой arrangement logic | Изменить readiness/fallback и выполнить targeted x5 | Нет | Да: пользователь повторно написал `Спеку подтверждаю` | Повторный переход SPEC->EXEC разрешает только test-harness arrangement из section 21. | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, current spec |
| EXEC | Amendment implementation and local gates | 0.98 | Нужны remote PR checks | Commit/push amendment и ждать GitHub Actions | Нет | Нет | Targeted 5/5, layout class 3x20/20, full 600/600 и headless 31/31 прошли; production scope не затронут. | `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`, current spec |

## 21. SPEC amendment: deterministic arrangement details pane

### 21.1 Новое evidence и уточнённая проблема
- Layout failure повторился в двух независимых remote full-suite runs: исходном run `29357745445` attempt 1 и PR run `29361813320`.
- `EnsureDetailsPaneArranged(...)` считает pane готовой, когда `CurrentTaskDetailsScrollViewer.Bounds.Width > 100`.
- Перед этой проверкой helper сам задаёт `Width/MinWidth/MaxWidth` внутреннему `ScrollViewer`, поэтому условие может стать true, даже если внешний `CurrentTaskDetailsPanelFrame` и его pane-root Grid не были arranged Avalonia Headless.
- Фактический CI state подтверждает ложноположительную готовность: frame `visible=True`, height `848`, width `0`.
- `TryArrangeDetailsPaneFallback(...)` также arrange только `ScrollViewer` и его content, но не внешний frame/pane root.
- Это test-harness arrangement defect: production XAML использует `SplitView.Pane -> Grid ColumnDefinitions="*,Auto" -> Border -> ScrollViewer`; пользовательский layout менять не требуется.

### 21.2 Расширенный scope
- Изменить только `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs` поверх уже опубликованного test-only fix.
- В `EnsureDetailsPaneArranged(...)` readiness predicate должен требовать ненулевую ожидаемую ширину и у `CurrentTaskDetailsPanelFrame`, и у `CurrentTaskDetailsScrollViewer`.
- В deterministic fallback измерить и arrange pane-root Grid с реальной test pane width; затем повторно выполнить layout jobs и проверить frame + scroll viewer.
- Не задавать успешный результат только по вручную установленной ширине descendant control.
- Не добавлять retry-on-failure, `Task.Delay` или production/XAML change.

### 21.3 Детальный дизайн fallback
1. Найти `CurrentTaskDetailsPanelFrame` и `CurrentTaskDetailsScrollViewer` по стабильным automation-id.
2. Определить pane root как visual parent frame; ожидать `Grid` с колонками `*,Auto`.
3. После обычных `SplitView.IsPaneOpen/OpenPaneLength` и layout jobs считать arrangement готовой только когда оба control имеют width > 100 и height > 0.
4. Если обычный headless layout не arrange pane:
   - measure/arrange pane-root Grid на `Math.Min(width, 600)` x `height`;
   - выполнить `UpdateTaskDetailsLayoutForTest` и layout jobs;
   - при необходимости оставить существующий content fallback для вертикального desired size.
5. Fallback возвращает success только по frame + scroll viewer predicate; иначе exception должен включить bounds обоих controls для диагностики.

### 21.4 Unobserved fixture reads
- Три post-summary `FileNotFoundException` остаются lifecycle evidence, но не являются причиной текущего assertion failure.
- В этом amendment не добавляется широкий sleep/cleanup rewrite: такой change затронул бы 147 fixture creations и требует отдельного воспроизводимого contract test.
- Если после layout arrangement все 600 assertions пройдут, но runner вернёт non-zero только из-за unobserved reads, это новый stop-condition и отдельный SPEC amendment.
- Если runner завершится `600/600`, исключения будут зафиксированы как existing cleanup residual; release notes их не упоминают, потому что product runtime не затронут.

### 21.5 Дополнительные acceptance criteria
1. `EnsureDetailsPaneArranged` не возвращает success при `CurrentTaskDetailsPanelFrame.Bounds.Width == 0`.
2. Fallback arrange pane-root Grid, а не только inner ScrollViewer.
3. `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` проходит минимум 5 независимых process runs.
4. Весь `MainControlTaskCardLayoutUiTests` проходит минимум 3 независимых process runs.
5. Полный `Unlimotion.Test` снова проходит `600/600`; headless UI проходит `31/31`.
6. GitHub Actions `All tests`, Android и CodeQL на новом head commit зелёные.
7. Diff остаётся test/spec-only; production XAML/runtime не меняются.

### 21.6 Validation commands
- Targeted x5:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls" --maximum-parallel-tests 1 --output Detailed`
- Affected class x3:
  - `dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-build --no-restore -- --treenode-filter "/*/*/MainControlTaskCardLayoutUiTests/*" --maximum-parallel-tests 1 --output Detailed`
- Затем повторить full `600/600`, headless `31/31`, build/diff check и remote PR checks из основной спеки.

### 21.7 Amendment quality gate
#### SPEC Linter Result
- Полнота: PASS — новое evidence, root cause, scope, Non-Goals, acceptance и stop rules зафиксированы.
- Безопасность: PASS — test-only arrangement; no delays/retries/product layout changes.
- Проверяемость: PASS — targeted x5, class x3, full local и remote gates.
- Готовность: PASS — planned file и exact helper behavior определены.

#### SPEC Rubric Result
- Ясность границ: 5/5.
- Понимание AS-IS: 5/5.
- Конкретность TO-BE: 5/5.
- Безопасность/rollback: 5/5.
- Тестируемость: 5/5.
- Автономная готовность: 5/5.
- Итог: 30/30, готово к повторному подтверждению.

#### Post-SPEC Review amendment
- Статус: PASS.
- Scope/Evidence pass: использованы exact PR logs, два remote reproductions и текущая helper implementation.
- Contract pass: readiness теперь соответствует assertion contract внешнего frame; automation-id и production layout сохраняются.
- Adversarial pass:
  - «Не маскирует ли это product bug?» — fallback существует только в headless test helper; production XAML не меняется, а реальный defect вызван ложным readiness predicate после ручного width inner control.
  - «Почему не delay?» — delay не гарантирует parent arrangement; выбран structural measure/arrange pane root и проверка postcondition.
  - «Почему не чинить cleanup noise вместе?» — нет отдельного failing contract и broad fixture impact; amendment содержит stop rule, если noise сам блокирует exit.
- Stop decision: no BLOCKER/HIGH findings; требуется повторное human approval перед изменением `MainControlTaskCardLayoutUiTests.cs`.

### 21.8 Approval amendment
Ожидается повторная фраза: "Спеку подтверждаю"

### 21.9 Post-EXEC Review amendment
- Статус: PASS локально; remote PR gate ожидается после push.
- Scope reviewed: amendment section 21; `src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs`; полный branch diff; targeted/class/full/headless evidence.
- Decision: можно создать второй test-only commit и обновить draft PR.
- Scope/Evidence pass:
  - Readiness теперь проверяет `CurrentTaskDetailsPanelFrame` и `CurrentTaskDetailsScrollViewer` по visibility, width и height.
  - Fallback получает visual parent frame как pane-root `Grid`, measure/arrange его на test pane width и только затем проверяет postcondition.
  - Failure diagnostics включают bounds обоих controls.
  - Production XAML/runtime/API/data не изменены.
- Contract pass:
  - Helper больше не может вернуть success только из-за вручную заданной ширины descendant ScrollViewer.
  - Существующие desktop/phone/card layout assertions сохранены без ослабления.
  - `Task.Delay`, retry-on-failure и blanket timeout не добавлены.
- Adversarial pass:
  - Возможный отсутствующий pane-root Grid приводит к явному fallback failure, а не ложному pass.
  - Phone width остаётся > 100 после вычета toggle column; три полных class runs покрыли 360/390/430 variants.
  - Cleanup noise проявился после последовательных class processes, но каждый runner завершился exit code 0; полный final run завершился `600/600` без post-summary exceptions.
- Validation evidence:
  - Test project build: PASS, 0 errors; существующие warnings сохранены.
  - `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls`: 5/5 независимых process runs PASS.
  - `MainControlTaskCardLayoutUiTests`: 3 независимых process runs по `20/20` PASS.
  - Full `Unlimotion.Test`: `600/600`, 0 failed, 11m 42s, exit code 0; post-summary cleanup exceptions отсутствуют.
  - `Unlimotion.UiTests.Headless`: `31/31`, 0 failed, 1m 03s, exit code 0.
  - `git diff --check`: PASS с Windows line-ending warnings.
- Re-review after fixes: проверены changed helper, visual-parent assumption, all fallback call sites и итоговый diff; findings BLOCKER/HIGH отсутствуют.
- Stop decision: local gate PASS; merge/release по-прежнему блокированы до зелёных GitHub checks на новом head commit.
- UI video evidence: не применимо — production UI behavior не менялся; test-harness arrangement подтверждён headless assertions.
- Unrelated changes: не обнаружены.
- Needs human: нет до Telegram action-time confirmation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | fixture cleanup | При трёх отдельных class runs TUnit после summary печатал существующие unobserved fixture reads, не меняя exit code; final full run был чистым. | Сохранить как residual; открыть отдельную задачу только если noise сам блокирует runner или воспроизводится как assertion failure. | accepted-risk |
