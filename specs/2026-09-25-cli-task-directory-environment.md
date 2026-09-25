# Каталог задач CLI через UNLIMOTION_TASKS

## 0. Метаданные
- Тип: delivery-task, config/public CLI behavior; профиль dotnet-desktop-client для интеграции с локальными настройками Unlimotion; контекст testing-dotnet.
- Форма: Expanded по центральному `templates/specs/_template.md`: меняется публичный configuration contract, поэтому Short неприменим.
- Масштаб: medium, локальное обратимое изменение выбора пути; storage format, permissions и миграции не меняются. Не large/high-risk.
- Владелец: основной агент, один writer; independent reviewer не обязателен для этого масштаба.
- Behavior baseline / поверхность: GPT-6 Astra / Codex; точный runtime модели не влияет на контракт CLI; model eval не применим.
- База: `165956f55fa1dc87a7d1f06bf726a50078c97eeb`, исходное дерево чистое, detached HEAD. Релиз/публикация не заказаны.
- Instruction stack: central AGENTS/routing-matrix, creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration-baseline, quest-governance, quest-mode, testing-baseline, testing-dotnet, dotnet-desktop-client, spec-linter, spec-rubric, review-loops; локальный AGENTS.override.md (UI testing неприменим: UI не меняется).
- Ограничения: до exact approval меняется только эта SPEC; пользовательские задачи, настройки приложения и окружение shell не изменяются.

## 1. Overview / Цель

Исходное поручение: «Думаю можно добавить специальную переменную окружения, которую CLI сможет использовать если не указан каталог».

Outcome contract:
- Пользователь задаёт `UNLIMOTION_TASKS` в своём окружении и запускает CLI без повторения `--tasks`, в том числе в Termux для доступной общей папки задач.
- Success means: все CLI-команды выбирают каталог в порядке explicit `--tasks` → непустая `UNLIMOTION_TASKS` → текущий desktop fallback.
- Итог: resolver, помощь CLI, документация и тесты Windows/Linux. Это не заявление о проверенном запуске CLI на Android.
- Stop rules: реализация только после «Спеку подтверждаю»; при невозможности обязательной проверки фиксируется blocker, а не PASS.

## 2. Текущее состояние (AS-IS)

- `src/Unlimotion.Cli/Program.cs`: CliOptions.Parse присваивает `TasksPath = tasksPath ?? TaskDirectoryResolver.Resolve(null)`; затем Main проверяет непустоту и существование каталога.
- `TaskDirectoryResolver.Resolve(explicitTasksPath, settingsPath)` читает TaskStorage.Path/IsServerMode, отклоняет серверный источник и ошибки конфига. Relative desktop path разрешается относительно Settings.json.
- Default resolver использует `Environment.SpecialFolder.Personal/Unlimotion/Settings.json`. Переменная окружения для выбора каталога отсутствует.
- `TaskDirectoryResolverTests` проверяют resolver с явным временным settingsPath. `UnlimotionCliIntegrationTests` запускают реальный дочерний процесс CLI; туда можно передавать отдельное окружение.
- В этой сессии на Ubuntu 24.04 WSL2/.NET 10.0.400 уже прошли сборка CLI, 12 smoke-проверок текущего выбора пути и 3 исходных resolver-теста в отдельном TUnit runner. Это baseline, не evidence новой функции.
- На Android Settings.json находится в app-specific storage. Переменная позволит указать доступную общую папку без чтения приватного конфига приложения; она не выдаёт разрешений.

## 3. Проблема

В окружениях без доступного desktop-конфига пользователь вынужден повторять один путь в каждом CLI-вызове.

## 4. Цели дизайна

Один предсказуемый порядок выбора, прежний приоритет явного аргумента, прежний desktop fallback при отсутствии переменной, тесты без зависимости от пользовательского окружения.

## 5. Non-Goals

Не меняем Android/desktop UI, storage format, блокировки и доступ к файлам; не создаём общую папку и не переносим задачи; не публикуем мост/Settings.json; не добавляем server storage; не устанавливаем и не публикуем CLI; не добавляем постоянные env vars пользователю; не расширяем CI-матрицу в этой задаче. Автоматическое следование переключению пространства в Android не обещается: env задаёт фиксированный путь.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Файл | Ответственность |
| --- | --- |
| src/Unlimotion.Cli/TaskDirectoryResolver.cs | Константа имени env, чтение процесса в production overload, детерминированный выбор и существующий desktop fallback |
| src/Unlimotion.Cli/Program.cs | Сохранить explicit-argument semantics; обновить --help и подсказки о выборе каталога |
| src/Unlimotion.Cli/README.md | Приоритеты, Bash/Termux/PowerShell, ограничения и rollback |
| src/Unlimotion.Test/TaskDirectoryResolverTests.cs | Матрица выбора с инъекцией значений вместо изменения process-wide env |
| src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs | Реальный запуск с отдельными env/cwd, проверки чтения и записи только тестовых задач |

### 6.2 Детальный дизайн

1. Имя: `UNLIMOTION_TASKS`. На Unix документируется точный uppercase. CLI не ищет альтернативные написания.
2. Если `--tasks` присутствует, его существующая семантика сохраняется, включая ошибку для пустого/whitespace аргумента. Непригодный explicit path не разрешает fallback на env/desktop.
3. Иначе production resolver читает `Environment.GetEnvironmentVariable("UNLIMOTION_TASKS")` один раз для вызова. Непустое, не состоящее целиком из пробелов значение используется как путь.
4. Env null/empty/whitespace означает отсутствие override, выполняется прежний desktop resolver.
5. Путь env используется без Trim, без удаления кавычек и без разворачивания `~`, `$VAR` или `%VAR%` внутри строки. Shell выполняет свои обычные подстановки до запуска CLI. Relative env path разрешается как explicit --tasks — относительно cwd процесса; для постоянной настройки рекомендуется абсолютный путь.
6. Выбранный env path проверяется существующим pipeline CLI. Ошибка пути/доступа возвращает ненулевой exit и существующий JSON error contract. Переход на desktop при ошибке запрещён, каталог не создаётся. Никакие другие значения env не выводятся.
7. Сохраняем детерминированный overload `Resolve(explicitTasksPath, settingsPath)` без чтения process environment; добавляем overload с явно переданным env value. Production overload использует его. Exact внутренняя сигнатура может уточняться при EXEC в этих границах.
8. Help и существующие configuration-error подсказки упоминают альтернативу `UNLIMOTION_TASKS`, не меняя error kinds. --help работает без валидного пути/env/конфига.
9. Все команды, включая запись, используют единый уже выбранный каталог. Не добавляем отдельную ветку только для status.
10. Visual planning / UI video: не применимо, UI и визуальный поток не меняются; проверяется консольный вывод. Performance: одно чтение env, дополнительных filesystem scans нет; benchmark не требуется.

Пример будущего использования в Bash/Termux (не проверенная инструкция установки runtime):

```bash
export UNLIMOTION_TASKS="/storage/emulated/0/Documents/Unlimotion/Tasks"
unlimotion-cli status
unlimotion-cli status --tasks /another/tasks
unset UNLIMOTION_TASKS
```

PowerShell: `$env:UNLIMOTION_TASKS = 'C:\Tasks'`. CLI читает окружение текущего процесса; сам не редактирует shell profile. Для сохранения между сессиями пользователь добавляет export в подходящий профиль своей shell.

### 6.3 User-Observable Scenarios

| Scenario | Действие | Результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Постоянный путь | Env задан, status/create без --tasks | Выбрана папка env, desktop не нужен | Дочерние CLI-процессы, временные задачи | AC-1 |
| Разовый override | Env A, --tasks B | Только B, включая ошибки B | Resolver + CLI process | AC-2 |
| Прежний desktop | Env отсутствует/пустой | Сохраняется текущий fallback | Unit + Linux HOME/XDG smoke | AC-3 |
| Ошибка env | Env указывает отсутствующую папку | Ошибка без чтения другого хранилища | CLI process + snapshot fixtures | AC-4 |
| Путь/документация | Пробелы, Unicode, relative cwd; --help | Предсказуемый путь и понятная помощь | Unit/process/help assertions | AC-5 |

### 6.4 State / Interaction Matrix

| --tasks | Env | Результат |
| --- | --- | --- |
| Передан | Любой | --tasks; пустой/ошибочный аргумент не подменяется env |
| Отсутствует | Непустой | Env; ошибка не вызывает fallback |
| Отсутствует | Null/empty/whitespace | Desktop settings и прежние ошибки |
| --help | Любой | Справка без открытия хранилища |

### 6.5 Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Имя | agent | UNLIMOTION_TASKS | 0.95 | Долгосрочный публичный контракт; отражён в SPEC | Нет |
| Порядок | user/agent | --tasks → env → desktop | 0.99 | Env намеренно фиксирует путь | Нет |
| Relative env | agent | От cwd, как --tasks | 0.95 | Для shell profile рекомендован absolute | Нет |
| Ошибочный env | agent | Fail, без fallback | 0.99 | Требует исправить/unset env | Нет |
| Android | agent | Только path override, без заявлений о runtime validation | 0.99 | Доступ к папке проверяется отдельно | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract | Source of truth | Изменение | Совместимость | Проверка |
| --- | --- | --- | --- | --- |
| Каталог | CLI args / env / desktop | Новая ступень env | Без env поведение прежнее | Матрица AC |
| Env | Окружение процесса | Чтение одной переменной | Нет persistence/migration | Дочерний процесс |
| Task files | Существующий storage pipeline | Нет изменений формата | Read/write rules прежние | Полный regression suite |

## 7. Бизнес-правила / Алгоритмы

Выбор источника завершается до открытия storage. Ошибка более приоритетного заданного пути не разрешает выбрать менее приоритетный. Desktop relative paths по-прежнему отсчитываются от конфига, env relative paths — от cwd.

## 8. Точки интеграции и триггеры

CliOptions.Parse вызывает production resolver только при отсутствии --tasks. Этот путь общий для всех команд. Help и некорректные аргументы не должны начать требовать доступный каталог.

## 9. Изменения модели данных / состояния

Новая переменная окружения, читаемая при запуске; persisted поля и task files не меняются. Env не редактируется программой.

## 10. Миграция / Rollout / Rollback

Миграции нет. Пользователь включает override установкой env; выключает `unset UNLIMOTION_TASKS` / `Remove-Item Env:UNLIMOTION_TASKS` или передаёт --tasks. Code rollback — revert изменения, без восстановления task files. Публикация/установка — отдельное поручение.

## 11. Тестирование и критерии приёмки

| AC | Automated test | Фактический evidence | Результат |
| --- | --- | --- | --- |
| AC-1: env выбран без desktop, чтение и запись одной тестовой задачи | Resolver + child status/create | Windows integration 41/41; Linux process smoke `linux-env-smoke-results.json` | PASS |
| AC-2: --tasks приоритетен, включая несуществующий/пустой аргумент | Unit + process, разные каталоги и контроль неизменности невыбранного | Windows integration/solver tests и Linux smoke | PASS |
| AC-3: null/empty/whitespace env сохраняют desktop fallback | Unit + HOME/XDG Linux process | Windows resolver 5/5; Linux resolver 5/5 и smoke | PASS |
| AC-4: env missing-directory / file-instead-of-directory не приводит к fallback | Process, desktop fixture с задачами остаётся нетронутым | Windows integration и Linux smoke: `invalidArguments`, exit 2, прежний контракт | PASS |
| AC-5: Unicode/spaces/relative cwd и справка/README корректны | Unit + process/help, документационный review | Windows integration и Linux smoke, README/--help diff review | PASS |

Обязательный набор: targeted resolver и CLI integration, обычная Release-сборка CLI, полный `Unlimotion.Test` на Windows (публичный config/storage-selection contract), targeted Linux regression + реальный запуск собранного CLI с новым env. UI tests не добавляются: desktop/Android UI state и flow не меняются. Полный UI-набор/Android/macOS runtime проверки не являются условием этого ограниченного изменения; не заявлять их выполненными.

Tests изолируют inherited env: helper дочернего процесса удаляет UNLIMOTION_TASKS по умолчанию и задаёт его только целевым тестам. Не менять глобальный env родительского runner, чтобы не создавать гонки. Не менять HOME/config пользователя.

Команды Windows, принятый TUnit runner:

```powershell
dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Release
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/TaskDirectoryResolverTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --maximum-parallel-tests 1 --output Detailed
git diff --check
```

Linux: отдельная локальная копия текущих исходников и SDK вне репозитория; обычная Release-сборка и дочерние CLI-процессы на временных папках. Минимальный test runner допустим для targeted тестов, но не называется полным suite.

Сначала новый failing regression (env задан, desktop отсутствует), затем реализация, targeted/build/full проверки. Логи сохранять локально, проверять progress; после таймаута не повторять без причины/изменения условий. После успешного обязательного набора не расширять проверки без нового риска.

## 12. Риски и edge cases

Главный риск — незаметное изменение хранилища для write-команд. Закрывается строгим приоритетом и отсутствием fallback при ошибке env. Пробелы внутри и по краям допустимого имени не обрезаются. Literal ~ не раскрывается CLI; использовать `$HOME` при export. Shared Android storage permissions не предоставляются этой функцией.

### Expected User Review Objections

| Возражение | Причина | Решение | Статус |
| --- | --- | --- | --- |
| --tasks должен побеждать env | Разовые команды | AC-2 | mitigated |
| Ошибка env не должна отправить запись в другой каталог | Целостность пользовательских данных | Fail без fallback, AC-4 | mitigated |
| Это точно работает в Linux? | Предыдущий запрос | Обязательный реальный Linux smoke нового поведения | mitigated |
| Значит Android проверен? | Мотивация Termux | Runtime и storage permissions явно вне утверждения о проверке | mitigated |

Rework checklist: исходный сценарий сохранён; все AC имеют tests; user-owned решений кроме exact approval нет; визуальные artifacts неприменимы; Android границы названы.

## 13. Открытые вопросы

Нет блокирующих вопросов дизайна. Требуется exact approval по quest-mode.

## 14. План реализации

1. Добавить failing tests выбора env и изоляцию process env.
2. Реализовать resolver приоритет без изменения explicit-argument поведения.
3. Обновить help/error hints и README.
4. Пройти Windows/Linux проверки и post-EXEC review, внести evidence в SPEC.

## 15. Зависимости

Текущие .NET 10, TUnit 1.44.0; новых production dependencies нет.

## 16. Оценка влияния

Все команды CLI без --tasks при установленной env. Desktop/Android приложения и конфиги не изменяются.

## 17. Документация

CLI README и --help: новый приоритет, фиксированный характер env-пути, absolute path recommendation, unset, PowerShell и Bash/Termux. Не обещать автоматическое переключение вместе с Android UI.

## 18. Альтернативы

Отдельный файл/мост от Android сложнее, требует UI и синхронизации активного пространства. Повторение --tasks не закрывает поручение. Env закрывает постоянный выбор пути без изменения приложения.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Статус | Evidence |
| --- | --- | --- |
| 1 | PASS | §1 outcome |
| 2 | PASS | §2 проверенные call sites |
| 3 | PASS | §3 одна проблема |
| 4 | PASS | §4 цели |
| 5 | PASS | §5 границы |
| 6 | PASS | §6.1 ownership |
| 7 | PASS | §8 общий parse path |
| 8 | PASS | §6.2/7 приоритет и правила |
| 9 | PASS | §6.2 error/recovery |
| 10 | PASS | §6.2 constant-cost чтение |
| 11 | PASS | §9 нет persisted state |
| 12 | PASS | §10 no-env compatibility |
| 13 | PASS | §10 unset/revert |
| 14 | PASS | §11 AC-1..5 |
| 15 | PASS | §11 tests, включая ошибки |
| 16 | PASS | §11 команды/stop |
| 17 | PASS | §14 этапы |
| 18 | PASS | §6.5/13 решения |
| 19 | PASS | §0 medium/expanded |
| 20 | PASS | §0/11 profiles, UI N/A |

Итог: ГОТОВО к запросу approval, код не реализован.

### SPEC Rubric Result

| Критерий | Балл | Основание |
| --- | ---: | --- |
| Цель/границы | 5 | §1/5 фиксируют env outcome |
| AS-IS | 5 | Parse/resolver/tests прочитаны |
| Дизайн | 5 | Empty, errors, relative, explicit заданы |
| Безопасность/откат | 5 | Без миграции, strict failure, unset |
| Тестируемость | 5 | Unit/process/Linux/full Windows |
| Автономность | 5 | Все решения заданы, остаётся phase gate |

30/30, готово после approval; это оценка SPEC, не результат реализации.

### Role-Based Review Result

| Role | Применимость | Проверка | Verdict |
| --- | --- | --- | --- |
| Business analyst | Да: выбор источника | Убирает повтор --tasks, Android bridge не нужен | PASS |
| UX/designer | Да: CLI copy | Help и примеры объясняют priority/unset | PASS |
| Tester | Да | Negative cases, process-env isolation, Linux evidence | PASS |
| Developer/architect | Да | Production env boundary, deterministic resolver seam | PASS |
| Delivery/operations/security | Да | Без fallback при ошибке, без установки/env persistence | PASS |

### Post-SPEC Review

- Scope/Evidence pass: прочитаны central template/owners, Program.cs call sites/help, TaskDirectoryResolver.cs, TaskDirectoryResolverTests.cs и process helper интеграционных тестов; baseline Linux evidence доступен в отчёте текущей сессии. git status перед SPEC чистый.
- Contract pass: original request покрыт AC-1; env отсутствует → прежний контракт; explicit path и ошибки не меняются. Non-Goals исключают Android bridge и публикацию.
- Adversarial pass: проверены counterexamples: explicit empty переходит на env; invalid env молча выбирает desktop; inherited env меняет тестовый dataset; relative env ошибочно привязан к desktop config; документация обещает Android runtime PASS. Все закрыты явными правилами/проверками §6/11/12.
- Findings: MEDIUM / contract — naive `Resolve(tasksPath)` мог бы изменить прежнюю семантику явного пустого аргумента; fixed в §6.2/8, отдельный process test AC-2. MEDIUM / tests — process-wide env создаёт гонки; fixed через injected values/child env §11.
- Fix and re-review: повторно сверены §6.2/6.4/8/11; сохранены точные semantics и план отрицательных тестов. Блокирующих находок нет.
- Role-Based pass: результаты в таблице выше.
- Depth checklist: scope drift отсутствует; AC/test mapping заполнен; runtime PASS не заявлен; docs/help включены; конфигурационный контракт открыт явно; hidden storage/UI changes исключены.
- Manual-review challenge: проверить, что write-команда использует тот же env resolver, а тест --tasks не проходит только из-за одинаковых fixtures. Включено в AC-1/2.
- Stop decision: PASS для SPEC. Needs human: только «Спеку подтверждаю». Residual: реальное Android окружение не проверено и не является обязательством этого изменения.

### Post-EXEC Review

- Статус / stop decision: PASS для локального изменения.
- Scope/Evidence pass: утверждённая SPEC, `git status --short`, diff пяти изменённых файлов, `git diff --check`, целевые Windows/Linux тесты и full Windows TUnit log. Изменений вне CLI, тестов, README и этой SPEC нет.
- Contract pass: выбор пути происходит до открытия хранилища и общий для read/write. `--tasks` сохраняет высший приоритет; env имеет приоритет над desktop; пустое значение возвращает desktop fallback. Ошибка заданного пути не вызывает fallback. Не изменены task format, desktop/Android UI, переменные окружения пользователя или установка CLI.
- Adversarial risk pass: первоначальный тест показал desktop dataset вместо env-каталога (RED). После fix проверены разные каталоги при записи, explicit empty, несуществующий каталог и файл вместо каталога, Unicode/пробелы и relative cwd, отсутствие изменения файлов на read. Child process helper удаляет унаследованную переменную для старых тестов.
- Role-Based pass: workflow — фиксированный env путь описан честно; UX/copy — README и --help называют приоритет; tester — AC-1..5 имеют автоматические проверки; developer — resolver сохраняет deterministic overload; operations — ошибочный env не направляет запись в desktop. Android runtime/permissions отдельно не проверены.
- Fix and re-review: первоначальные новые negative tests ошибочно ожидали exit 1 / `operationFailed`; текущий контракт CLI — exit 2 / `invalidArguments`. Исправлены только test expectations, интеграционный набор повторно прошёл 41/41. После этого чистая CLI Release-сборка, Linux проверка и полный Windows suite 1122/1122. Дальнейших code changes не было.
- Findings/disposition: MEDIUM / validation — ошибочные ожидания новых тестов, fixed; LOW / UI help — примеры команд ещё показывают `--tasks` как аргумент, но отдельная строка и README явно задают порядок выбора, accepted в существующей форме help; blocking findings нет.
- Evidence: локальные логи вне репозитория — `full-windows-tests.log` (1122 passed, 0 failed, 0 skipped, 20m 00s), `build-cli-env.log`, `resolver-tests-env.log`, `linux-env-smoke-results.json` (11/11). Windows CLI Release build: 0 errors, 0 warnings. `git diff --check`: clean; Git выводит только уведомления о будущей замене LF на CRLF.
- User-observable completion gate: `UNLIMOTION_TASKS` выбирает временную папку и работает для `status`/`create`; --tasks выбирает другую папку; Linux фактический процесс повторяет сценарий. AC-1..5 PASS.
- Manual-review challenge: может ли внешняя переменная изменить старые CLI-тесты? Нет: helper удаляет её для обычных дочерних процессов; специальные тесты явно задают значение. Может ли ошибочный путь перенаправить запись? Нет: процесс завершает работу до открытия storage.
- Residual: Android/Termux на устройстве не запускался; из этой проверки нельзя утверждать работоспособность конкретной установки или доступность выбранного Android каталога. Публикация/установка не проводились.

## Approval

Получена точная фраза «Спеку подтверждаю»; разрешённый EXEC завершён локально.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / 2026-09-25 | Пользователь предложил env; выбран --tasks → UNLIMOTION_TASKS → desktop | Текущие call sites и Linux baseline проверены; новый код не написан | Approval, затем EXEC | Предложение env, не exact approval | Эта SPEC |
| Post-SPEC / 2026-09-25 | Уточнены explicit-empty, invalid-env и test isolation | Full self-review PASS; Android claims ограничены | Ожидать «Спеку подтверждаю» | Не получено | Эта SPEC |
| EXEC / 2026-09-25 | Получена точная фраза «Спеку подтверждаю»; реализация разрешена в утверждённых границах | Реальный Linux baseline уже сохранён; новое поведение ещё не проверено | Failing regression, реализация, обязательные проверки | Спеку подтверждаю | Эта SPEC |
| EXEC / characterization | Новый `Status_UsesTasksEnvironmentWithoutExplicitPath` воспроизвёл проблему: вместо пустого env-каталога CLI прочитал desktop-задачи | Expected RED на Windows, без записи в пользовательские задачи | Внедрить resolver и выполнить targeted проверки | Ранее данное approval действует | CLI/test файлы |
| EXEC / targeted validation | Добавлен приоритет env и сохранён прежний контракт ошибок (`invalidArguments`, exit 2 для отсутствующего каталога) | Windows resolver 5/5, CLI integration 41/41; CLI Release build 0 warnings/errors; Linux resolver 5/5 и 11/11 process smoke. Linux logs сохранены вне репозитория | Дождаться полного Windows suite и выполнить post-EXEC review | Ранее данное approval действует | Resolver, Program, README, tests, эта SPEC |
| EXEC / завершение | Полный Windows suite подтвердил сохранение совместимости; post-EXEC review PASS | 1122/1122; Linux 5/5 + 11/11; CLI build clean; diff check clean; Android на устройстве не проверен | Локальная работа завершена; rollout по отдельному поручению | Approval использовано только для реализации | Код, тесты, README, эта SPEC, локальные test logs |
