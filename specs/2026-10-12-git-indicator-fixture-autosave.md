# Ожидать обычное сохранение в проверке Git-индикатора

## Контекст

Дополнение проверки первой Android-задачи e74c5d1a. Форма Short: один тестовый файл, без изменения продукта, API или данных пользователя. Owner — агент; central testing-baseline/review-loops и локальный UI MUST. Разрешение пользователя на последовательное выполнение и автоматическое подтверждение SPEC после review действует.

Full Headless на bb12868c + migration delta: 65/66 FAILED, ожидание Idle после Commit. Isolated retry 1/1 PASS не заменяет полный gate. Независимый reviewer обнаружил проверяемую возможность позднего autosave, но причина исторического сбоя не доказана и temp fixture уже очищена.

## Пять содержательных проверок

| Проверка | Решение / ожидаемый результат | План проверки → фактическое evidence |
| --- | --- | --- |
| 1. Результат / границы | Изменение Title сохраняется обычным autosave; тест ждёт именно это завершение до Git matrix/commit. Только tests/Unlimotion.UiTests.Headless/Tests/GitStatusIndicatorHeadlessTests.cs. Не менять Git product behavior, reader, throttle, Idle timeout, lifecycle или API | Старый тест меняет Title и дополнительно запускает repository.Update; ожидание этого Task не дренирует отложенный autosave. Проверить сохранённый Title и Dirty через обычный поток |
| 2. Решения / разрешения | Подписка на SaveItemCommand до Title edit; ожидание успешного completion с UI dispatcher pumping и сроком actual throttle + фиксированный allowance существующего UI wait. Убрать ручной Update. Seal и reflection private drain не использовать | TaskItemViewModel:113,303–319; TaskTreeManager:437. User decision не нужен; review → automatic approval |
| 3. AC→evidence | AC1 обычный autosave завершён и Title прочитан с диска до Dirty. AC2 прежние assertions/screenshots/keyboard/failure/retry/source switch остаются; Idle wait остаётся прежним. AC3 диагностика до/после Commit и при failure показывает синтетические Git status entries, HEAD, timestamps; observer start/finish/request/dirty позволяет отличить late write и late sample. AC4 targeted и full Headless выполнены после изменения; исходные failure сохранены | Сериализованные Release build, TUnit GitIndicator_SaveSyncFailureSwitchKeyboardAndRenderedMatrix, затем весь Headless. Runtime screenshot artifacts/flow trace просмотреть. Полный Main уже выполняется, не запускать UI suites параллельно |
| 4. Существенный риск / rollback | Command subscription должна быть disposed, ждать успешного save, не только IsExecuting=false/Task started. Failed save/неработающий autosave должен провалить тест. Диагностика только synthetic fixture, не пользовательские пути/задачи. Не придумывать причинность старого dirty1 | Counterexample: ручной Update завершён, .Throttle ещё не исполнился; новая проверка такой результат не принимает. Rollback — revert отдельного test commit |
| 5. Findings / disposition / stop | Root source pass и отдельный writable reviewer установили race possibility и отсутствие historical filename. Сам autosave validation уточняет исходный смысл теста и не ослабляет product assertions. До review и запусков stop NEEDS-FIX | Reviewer фактически read-only, FS danger-full-access; sandboxed independence не заявляется. Новый full необходим, isolated прежний не засчитывается |

## Quality gate и review

Post-SPEC: PASS. Отдельный reviewer сверил workable completion/error subscriptions до Title edit, disposal, dispatcher pumping, actual throttle + existing allowance и запрет ослабления Idle. Root fallback проверил false pass от ручного Update, ошибки autosave и только synthetic evidence. Effective reviewer FS unrestricted; техническая sandbox isolation отсутствует. Общее завершение Android-задачи всё ещё NEEDS-FIX.

Post-EXEC: PASS только для test-only дополнения. Отдельный reviewer сверил diff, short AC1–4, actual Release build0errors/4warnings, targeted1/1 и full Headless66/66 без skips, snapshots/observer trace и rendered PNG. Root fallback проверил успешный command result вместо manual Update, точный disk Title, scoped disposal/errors, неизменённые Idle5s/keyboard/assertions и best-effort диагностику без влияния на observation. До Commit один synthetic file dirty, после Commit entries=[]; свежие reads/UI sample подтверждают Idle/dirty0. Новых находок нет: все условия этой короткой SPEC подтверждены; историческая причина прежнего dirty1 не объявляется доказанной. Effective reviewer FS unrestricted, технической sandbox isolation нет. Whole e74 всё ещё NEEDS-FIX: Main1527/1528 FAILED по Raven и Android5/1/5 не подтверждены; это не разрешение merge/complete основной задачи.

## Approval

Автоматически подтверждена после PASS review по текущему прямому поручению пользователя; EXEC для test-only delta разрешён. Отдельное разрешение изменения product Git behavior не выводится из этой SPEC.

## Журнал

12.10 SPEC: гипотеза late autosave зафиксирована как возможность, историческая причина неизвестна. Source/tests ещё не изменены; полный Main интегрированного head идёт отдельно.
12.10 preliminary code review NEEDS-FIX: MEDIUM diagnostic IO мог замаскировать исходный timeout или изменить observation; LOW Fail читался дважды. Исправлено in-scope: best-effort snapshots/observer tracing с явным Console error и исходным rethrow/observation, один captured injectedFailure. Required targeted re-review; build/tests всё ещё queued после Main. Missing diagnostic artifacts не засчитываются как AC3 PASS даже при успешном тесте.
12.10 targeted code re-review PASS: MEDIUM и LOW закрыты, новых находок на изменённой поверхности нет. Root fallback проверил original exception/observation и один captured Fail. Build/targeted/full и просмотр diagnostic artifacts остаются pending, общий Android gate не закрывается этим review.
12.10 Release Headless build0errors/4warnings, targeted1/1 PASS27s. Evidence до commit: один synthetic task ModifiedInWorkdir, timestamp после ожидаемого autosave; после commit entries пусты; observer read4/5 dirty0 и UI accepted Idle0, old failed read сохраняетAttention/Dirty. PNG dirty-save-ru-light просмотрен; копия snapshots/trace/screenshots сохранена внеGit fixture-autosave-targeted-evidence. Историческая причина первого fullfailure не объявляется доказанной. Full Headless выполняется; postEXEC ещё pending до full/artifact review. Main1527/1528 FAILED по Raven revisionslicense, это отдельный validator prerequisite, не PASS.
