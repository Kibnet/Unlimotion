# STORM Coverage Analysis

Сгенерировано: 2026-07-14
Команда: `/storm:cover -> /storm:bdd-implement SC-0006-003`
Режим: `delivery-task test-only executable BDD implementation + artifact sync`; production code, `.feature` wording, automation IDs, project files, workflows и existing test annotations не менялись

## Область

Эта итерация выполняет approved SPEC для `ST-0006 / AC-0018 / SC-0006-003`: сценарий про доступность `Wanted` и `Importance` в UI, wanted presentation, route фильтрации `ShowWanted` и importance sort definitions теперь исполняется через repo-local step definitions `SD-0095..SD-0098` и новый TUnit/Avalonia.Headless evidence `TS-0050`. Existing evidence `TS-0005` и `TS-0013` сохранено.

## Сводка

| Метрика | Значение |
| --- | --- |
| Acceptance criteria всего | 44 |
| AC с тестовыми связями | 44 |
| Scenario -> Test links | 45/45 |
| Passing scenarios | 25 |
| Step definitions | 98 |
| Step-executable scenarios | 25/45 |
| ST-0006 executable coverage | 3/3 scenarios |
| Full suite gate | 581/581 on escalated run |

## Результат SC-0006-003 Executable Slice

| Item | Было | Стало | Evidence |
| --- | --- | --- | --- |
| `SC-0006-003.step_definitions` | `[]` | `SD-0095`, `SD-0096`, `SD-0097`, `SD-0098` | `StormTaskPlanningWantedImportanceExecutableSpecTests` исполняет шаги feature. |
| `SC-0006-003.linked_tests` | `TS-0005`, `TS-0013` | `TS-0005`, `TS-0013`, `TS-0050` | `TS-0050` связывает scenario с UI/headless wanted/importance contract. |
| `SC-0006-003.status` | `automated` | `passing` | Targeted BDD evidence проходит. |
| `ST-0006` | 2/3 step-executable | 3/3 step-executable | Wanted/importance scenario закрыт на executable layer. |
| `AC-0018.coverage_level` | `critical` | `full` | Existing evidence сохранено, добавлен executable BDD bridge. |

## Validation Evidence

| Проверка | Результат |
| --- | --- |
| `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false` | прошло с existing warnings, errors 0 |
| `StormTaskPlanningWantedImportanceExecutableSpecTests` | прошло 1/1 |
| Preserved wanted/importance UI gates | `MainControlWantedUiTests` 1/1, `TaskImportanceUiTests` 4/4, `CurrentTaskCard_DesktopLayout_ExposesSectionsAndKeyControls` 1/1 |
| Full `Unlimotion.Test` | прошло 581/581 on escalated run |
| `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` | OK: 0 errors, 10 warnings по intentional shared steps |
| UI video evidence | не применимо: UI behavior/layout не менялись; preserved wanted/importance UI/headless tests использованы как next-best evidence |

## Оставшиеся Gaps

1. Step definitions покрывают 25/45 scenarios; остальные scenarios пока rely on linked TUnit evidence.
2. `ST-0006` закрыта на 3/3 step-executable scenarios.
3. Следующий `/storm:cover` candidate нужно выбрать вне `ST-0006`.
4. `CV-0007` не является active cover gap после решения Вариант B.
