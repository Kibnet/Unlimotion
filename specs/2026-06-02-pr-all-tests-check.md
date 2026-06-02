# PR all-tests check

## 0. Метаданные
- Тип (профиль): delivery-task; `dotnet-desktop-client`, context `testing-dotnet`
- Владелец: Codex
- Масштаб: small
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `fix/roadmap-search-no-rebuild`
- Ограничения: central `QUEST` SPEC-first gate; локальный `AGENTS.override.md`; PR delivery policy; TUnit/Microsoft.Testing.Platform runner из `global.json`
- Связанные ссылки: PR #247; existing spec `specs/2026-06-02-roadmap-search-no-rebuild.md`

Если секция не применима, указано `Не применимо` с причиной.

## 1. Overview / Цель
Добавить в GitHub PR checks отдельный обязательный по workflow результат, который прогоняет полный набор CI-safe тестовых проектов репозитория.

Outcome contract:
- Success means: на `pull_request` в `main` запускается отдельный check с полным последовательным прогоном CI-safe runnable TUnit test projects.
- Итоговый артефакт / output: новый GitHub Actions workflow для тестов, обновленный PR, validation evidence локально и/или из GitHub Actions.
- Stop rules: остановиться после создания workflow, локальной YAML/diff проверки, push в PR и проверки, что новый GitHub check стартует; если full CI test run падает из-за environment-only ограничения, зафиксировать точную причину и next-best evidence.

## 2. Текущее состояние (AS-IS)
- `.github/workflows/android-packaging.yml` запускается на PR, но выполняет Android packaging build, а не `dotnet test`.
- `.github/workflows/codeql-analysis.yml` запускает CodeQL analysis без build/test.
- `.github/workflows/static.yml` не запускается на PR.
- Release packaging workflows не покрывают PR test gate.
- `global.json` задает `Microsoft.Testing.Platform`.
- Обычные CI-safe runnable test projects:
  - `src/Unlimotion.Test/Unlimotion.Test.csproj` использует `TUnit`.
  - `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj` имеет `IsTestProject=true` и `TUnit`.
- Desktop automation project:
  - `tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj` имеет `IsTestProject=true`, `TUnit` и Windows target, но требует реального desktop automation session; для обязательного PR gate на GitHub-hosted runner это high-risk/flaky без отдельной стабилизации.
- Вспомогательные/необычные проекты не являются PR all-tests target:
  - `tests/Unlimotion.UiTests.Authoring` содержит shared authoring code, `IsTestProject=false`.
  - `tests/Unlimotion.AppAutomation.TestHost` является test host/helper.
  - `tests/Unlimotion.ReadmeMedia` является генератором README media, `IsTestProject=false`.
  - `tests/Unlimotion.Performance` является performance/tooling project, не обычный PR test suite.

## 3. Проблема
Текущий PR может пройти checks без полного прогона тестов, поэтому regression UI/unit tests не являются обязательным GitHub evidence перед merge.

## 4. Цели дизайна
- Разделение ответственности: test workflow отдельно от packaging и CodeQL.
- Повторное использование: использовать существующие `.csproj` и TUnit runner, не добавлять новый test harness.
- Тестируемость: последовательный `dotnet test` для всех CI-safe runnable test projects.
- Консистентность: setup .NET через `global.json`, как Android workflow.
- Обратная совместимость: не менять код приложения, тесты или release workflows.

## 5. Non-Goals
- Не менять branch protection / required status checks в настройках GitHub, потому что это не хранится в PR diff.
- Не включать media generation, performance tooling или FlaUI desktop automation в обычный обязательный PR test gate.
- Не доказывать пригодность GitHub-hosted Windows runner для FlaUI в рамках этой правки.
- Не менять существующий Android packaging workflow.
- Не переписывать тесты и не исправлять flaky tests в рамках этой спеки, кроме объективно нужной CI invocation правки.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `.github/workflows/tests.yml` -> отдельный PR/push/manual workflow `Unlimotion Tests`.
- `All tests` job -> Windows runner, restore и последовательный `dotnet test` для двух CI-safe runnable TUnit projects.
- FlaUI остается вне обязательного PR gate; при необходимости его можно добавить отдельным non-blocking/manual workflow после отдельной стабилизации.

### 6.2 Детальный дизайн
- Trigger: `pull_request` и `push` в `main`, плюс `workflow_dispatch`.
- Runner: `windows-latest`, чтобы сохранить совместимость с Windows-sensitive tests в `Unlimotion.Test` и desktop stack.
- Checkout: `actions/checkout@v4`, `submodules: recursive` для консистентности с repo checkout requirements.
- .NET: `actions/setup-dotnet@v4` с `global-json-file: global.json`.
- Cache: NuGet cache по `global.json`, `src/Directory.Packages.props`, `src/nuget.config`, `src/**/*.csproj`, `tests/**/*.csproj`.
- Execution:
  - restore выбранных test projects;
  - последовательно выполнить:
    - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --maximum-parallel-tests 1 --output Detailed`
    - `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -- --maximum-parallel-tests 1 --output Detailed`
  - job-level `timeout-minutes: 30`, чтобы UI-heavy tests не зависали бесконечно.
- Visual planning artifact: Не применимо, потому что изменение касается CI workflow и не меняет UI layout/state/flow.
- UI test video evidence: Не применимо как `до/после` UI artifact; workflow сам выполняет UI tests, evidence будет Actions log и локальная команда проверки, если окружение позволит.
- Обработка ошибок: любой non-zero exit code должен валить job и PR check.
- Производительность: последовательный запуск снижает риск shared UI state conflicts; `concurrency` отменяет устаревшие runs одного PR/ref.

## 7. Бизнес-правила / Алгоритмы
- "All tests" для обязательного PR gate означает все CI-safe runnable TUnit test projects, а не helper/tooling/performance/media/FlaUI desktop automation projects.
- Если в будущем появится новый test project, workflow должен быть обновлен в том же PR, где он добавлен.

## 8. Точки интеграции и триггеры
- GitHub Actions запускает workflow на `pull_request` к `main`, `push` в `main`, `workflow_dispatch`.
- PR #247 получает новый check после push ветки.

## 9. Изменения модели данных / состояния
Не применимо: persisted/runtime state не меняется.

## 10. Миграция / Rollout / Rollback
- Rollout: commit в текущую PR ветку.
- Rollback: удалить `.github/workflows/tests.yml` или revert commit.
- Совместимость: существующие workflow остаются без изменений.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- В репозитории есть отдельный workflow, запускающийся на PR.
- Workflow явно прогоняет `src/Unlimotion.Test` и `Unlimotion.UiTests.Headless`.
- Workflow не включает FlaUI в обязательный PR gate без отдельного стабилизированного runner/evidence.
- Workflow использует TUnit/MTP аргументы без VSTest `--filter`.
- PR #247 показывает новый GitHub check после push.
- Existing roadmap targeted UI test остается зеленым или уже подтвержден предыдущим commit evidence; эта правка не меняет UI behavior.

Какие тесты добавить/изменить:
- Новые product tests не нужны: меняется CI orchestration, не приложение.
- Добавляется workflow-level test execution.

Команды для проверки:
- `git diff --check`
- `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -- --maximum-parallel-tests 1 --output Detailed`
- `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -- --maximum-parallel-tests 1 --output Detailed`
- `gh pr checks 247 --watch` или equivalent GitHub Actions inspection after push.

Stop rules для validation:
- Если локальные full tests не завершаются из-за времени/окружения, указать точный failed command и проверить через GitHub Actions after push.
- Если позже потребуется FlaUI в CI, сначала завести отдельный non-blocking/manual check и собрать evidence стабильности.

## 12. Риски и edge cases
- FlaUI на GitHub-hosted Windows может быть environment-sensitive, поэтому исключен из обязательного PR gate.
- Полный набор UI tests может быть долгим.
- Branch protection может не сделать новый check required автоматически; это repository setting вне PR diff.
- Excluding `ReadmeMedia` может выглядеть как неполный "all"; mitigated by explicit `IsTestProject=false`/tooling classification.

## 13. План выполнения
1. Добавить `.github/workflows/tests.yml` с обязательным прогоном `Unlimotion.Test` и `Unlimotion.UiTests.Headless`.
2. Выполнить `git diff --check`.
3. Запустить доступные локальные test commands последовательно или зафиксировать объективный blocker.
4. Выполнить post-EXEC review.
5. Commit, push, обновить PR body validation.
6. Проверить новый GitHub check в PR.

## 14. Открытые вопросы
Нет блокирующих вопросов. Branch protection required-check selection может потребовать отдельной настройки вне PR.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`, context `testing-dotnet`
- Выполненные требования профиля:
  - Используется `dotnet test`.
  - Учитывается TUnit/Microsoft.Testing.Platform.
  - UI behavior не меняется, поэтому новые UI assertions не требуются; workflow прогоняет существующие UI tests.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `.github/workflows/tests.yml` | Новый PR/push/manual workflow с полным тестовым прогоном | Сделать test evidence частью PR checks |
| `specs/2026-06-02-pr-all-tests-check.md` | Рабочая спецификация и audit trail | Соблюсти QUEST gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| PR checks | Android build + CodeQL, без отдельного full tests check | Отдельный `Unlimotion Tests / All tests` check |
| Test execution | Локально/ручной запуск, не гарантирован в PR | GitHub Actions запускает CI-safe runnable TUnit test projects |
| FlaUI coverage | Не часть PR checks | Остается вне mandatory PR gate до отдельной стабилизации |

## 18. Альтернативы и компромиссы
- Вариант: добавить test step в `android-packaging.yml`.
  - Плюсы: меньше файлов.
  - Минусы: смешивает packaging и tests, Linux runner не подходит для FlaUI.
- Вариант: matrix по test projects.
  - Плюсы: быстрее.
  - Минусы: выше риск shared UI state/resource conflicts, сложнее читать failure flow.
- Вариант: включить FlaUI в обязательный PR gate.
  - Плюсы: шире UI automation coverage.
  - Минусы: desktop automation на GitHub-hosted runner может быть flaky и блокировать merge не из-за продуктовой регрессии.
- Выбранное решение лучше: отдельный Windows workflow явно отражает PR test gate и последовательно запускает стабильный unit/headless набор; FlaUI остается для отдельного optional workflow после стабилизации.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals заданы |
| B. Качество дизайна | 6-10 | PASS | Workflow boundaries, runner, commands, FlaUI exclusion, rollback и state impact описаны |
| C. Безопасность изменений | 11-13 | PASS | Риски CI/FlaUI/branch protection зафиксированы, rollback прост |
| D. Проверяемость | 14-16 | PASS | Есть acceptance criteria, commands и planned changed files |
| E. Готовность к автономной реализации | 17-19 | PASS | План и tradeoffs есть, блокирующих вопросов нет |
| F. Соответствие профилю | 20 | PASS | Учтен .NET/TUnit runner и desktop/UI test context |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Цель ограничена CI PR test check |
| 2. Понимание текущего состояния | 5 | Перечислены текущие workflows, test projects и FlaUI CI риск |
| 3. Конкретность целевого дизайна | 5 | Задан файл, runner, triggers и команды |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback прост, app behavior не меняется |
| 5. Тестируемость | 5 | Есть local и GitHub validation commands |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-02-pr-all-tests-check.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `github-delivery-policy`; selected profile `dotnet-desktop-client`; open questions none; planned changed files `.github/workflows/tests.yml`, this spec.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: просмотрены current workflow list, `android-packaging.yml`, `codeql-analysis.yml`, `static.yml`, test csproj files, `global.json`, `update-readme-media.ps1`.
  - Contract pass: spec добавляет PR test gate без изменения app behavior, исключает FlaUI из mandatory gate по CI-stability risk и не включает branch protection settings вне PR diff.
  - Adversarial risk pass: проверены counterexamples: FlaUI needs interactive desktop automation session, helper projects with TUnit.Core are not runnable tests, media generator is not normal PR test suite, branch protection cannot be guaranteed by repo diff.
  - Re-review after fixes / Fix and re-review: после вопроса пользователя FlaUI удален из обязательного PR gate; acceptance, commands, risks and alternatives пересмотрены.
  - Stop decision: PASS with explicit residual branch protection note.
- Evidence inspected: `.github/workflows/android-packaging.yml`, `.github/workflows/codeql-analysis.yml`, `.github/workflows/static.yml`, `src/Unlimotion.Test/Unlimotion.Test.csproj`, `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`, `tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj`, `tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj`, `global.json`, `scripts/update-readme-media.ps1`.
- Depth checklist:
  - Scope drift / unrelated changes: scope limited to CI workflow and spec.
  - Acceptance criteria: check file, test project list, PR check evidence.
  - Validation evidence: local diff/test commands and GitHub Actions inspection planned.
  - Unsupported claims: branch protection not claimed.
  - Regression / edge case: CI/FlaUI/environment risks documented.
  - Comments/docs/changelog: no comments/docs changes needed beyond spec.
  - Hidden contract change: CI cost increases, app behavior unchanged.
  - Manual-review challenge: likely question is whether `FlaUI`/`ReadmeMedia`/`Performance` are excluded; spec explains why.
- No-findings justification: workflow design is narrow, reversible and based on inspected repo evidence plus user feedback on FlaUI.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | operations | New check may not be branch-protection required until selected in GitHub settings | Report as outside PR diff after implementation | accepted-risk |
| LOW | coverage | FlaUI is excluded from mandatory PR gate, so visible desktop automation is not merge-blocking | Keep unit/headless mandatory now; consider optional/manual FlaUI workflow later | accepted-risk |

- Fixed before continuing: FlaUI removed from mandatory PR gate design after user challenge.
- Checks rerun: SPEC linter/rubric updated in this file.
- Needs human: yes, `Спеку подтверждаю`.
- Residual risks / follow-ups: branch protection required-check selection outside PR diff.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: approved spec, `git status --short`, `git diff --stat`, relevant diff for `.github/workflows/tests.yml`, local restore/test evidence, docs/changelog impact.
- Decision: можно commit/push; GitHub Actions evidence проверить после push и отразить в PR body.
- Review passes:
  - Scope/Evidence pass: проверены два changed files, local restore, `Unlimotion.Test` full run, targeted rerun одного failed test, headless project run, whitespace check.
  - Contract pass: workflow запускается на PR/push/manual, гоняет `src/Unlimotion.Test` и `tests/Unlimotion.UiTests.Headless`, не включает FlaUI, использует TUnit/MTP args.
  - Adversarial risk pass: проверены risks: missing local NuGet feed dir, infinite UI test hang, flaky full-suite baseline, branch protection outside PR diff, accidental FlaUI inclusion.
  - Re-review after fixes / Fix and re-review: добавлен `timeout-minutes: 30` после review risk по зависанию; diff и spec пересмотрены.
  - Stop decision: PASS с residual risk, что локальный full `Unlimotion.Test` сейчас flaky/failing и новый PR check может стать красным, что соответствует цели выявлять broken baseline.
- Evidence inspected: `.github/workflows/tests.yml`, `specs/2026-06-02-pr-all-tests-check.md`, local command outputs.
- Depth checklist:
  - Scope drift / unrelated changes: only workflow and spec files changed.
  - Acceptance criteria: repo workflow exists; includes two CI-safe projects; excludes FlaUI; GitHub PR check pending after push.
  - Validation evidence: restore passed; headless project passed 25/25; `Unlimotion.Test` full failed twice with different single-test failures after 421/422 passed; targeted rerun of one failed test passed 1/1; `git diff --check` passed.
  - Unsupported claims: no claim that branch protection is updated or local full suite is green.
  - Regression / edge case: CI now exposes existing full-suite instability instead of hiding it.
  - Comments/docs/changelog: no changelog needed; spec updated.
  - Hidden contract change: PR CI cost increases and may block on current suite failures; this is intended by the request to run all tests.
  - Manual-review challenge: likely finding is that "all tests" excludes FlaUI; spec documents why FlaUI is outside mandatory gate.
- No-findings justification: implementation matches approved spec; residual risks are explicitly reported.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Local `Unlimotion.Test` full run failed twice on different tests after 421/422 passed, while a targeted rerun of one failure passed | Push strict workflow and inspect clean GitHub runner; report if PR check turns red | accepted-risk |
| LOW | operations | Branch protection may not automatically require the new check | Report as repository setting outside PR diff | accepted-risk |

- Fixed before final report: added job timeout.
- Checks rerun: `git diff --check`; local restore for both test projects; `dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj ...` passed.
- Validation evidence: restore passed; headless 25/25 passed; local `Unlimotion.Test` full runs failed 421/422 twice on different tests; targeted failed-test rerun passed 1/1.
- Unrelated changes: none observed in `git status --short`.
- Needs human: none before push.
- Residual risks / follow-ups: inspect GitHub check after push; repository settings may need selecting the new check as required.

## Approval
Получено подтверждение пользователя: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Context gathering | 0.87 | Нет | Создать спецификацию | Нет | Нет | Найдены текущие PR workflows и runnable TUnit test projects | `.github/workflows/*`, test `.csproj`, `global.json` |
| SPEC | Spec creation and review | 0.91 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, ожидается решение человека | Требуется SPEC-gate перед изменением CI infrastructure | `specs/2026-06-02-pr-all-tests-check.md` |
| SPEC | User feedback on FlaUI | 0.93 | Подтверждение пользователя | Запросить `Спеку подтверждаю` | Да | Да, пользователь оспорил FlaUI в mandatory gate | FlaUI исключен из обязательного PR check как CI-flaky/high-risk без отдельной стабилизации | `specs/2026-06-02-pr-all-tests-check.md` |
| EXEC | Workflow implementation | 0.88 | Локальная и GitHub validation pending | Запустить проверки | Нет | Да, пользователь подтвердил spec | Добавлен отдельный PR workflow для CI-safe TUnit/headless tests без FlaUI | `.github/workflows/tests.yml`, `specs/2026-06-02-pr-all-tests-check.md` |
| EXEC | Local validation | 0.78 | GitHub clean runner result pending | Выполнить post-EXEC review и push | Нет | Нет | Restore прошел; headless project passed 25/25; full `Unlimotion.Test` выявил existing flaky failures 421/422 на разных тестах в двух прогонах | local `dotnet restore`, `dotnet test`, `.github/workflows/tests.yml` |
