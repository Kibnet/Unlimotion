# STORM BDD Sync

Сгенерировано: 2026-06-16
Команда: `/storm:bdd-sync`
Режим: delivery-task; test annotations не менялись

## Scope

Синхронизация связывает `SC-0014-001` с новым `TS-0022` и переводит сценарий в `passing`. Acceptance criteria сохранены отдельно от Gherkin; callback-сценарий `SC-0014-002` остаётся draft для `CV-0004`.

## Синхронизированные Связи

| Связь | Значение |
| --- | --- |
| Feature files | 16 |
| Gherkin Rules | 44 |
| Gherkin Scenarios | 44 |
| Active stories со сценариями | 16/16 |
| AC с rules | 44/44 |
| AC со scenarios | 44/44 |
| Scenario -> Test links | 43/44 |
| Constraints со scenario verification | 8/8 |

## Scenario Статусы

| Status | Количество | Основание |
| --- | --- | --- |
| passing | 4 | Есть свежий targeted evidence текущего или недавнего EXEC. |
| automated | 39 | Связаны с существующими тестами, но эти тесты не запускались в текущем EXEC. |
| draft | 1 | Нет automated test links; нужна отдельная delivery-task или product decision. |
| manual | 0 | Свежий manual evidence не назначался. |
| failing | 0 | Свежих failing evidence нет. |

## Новые/Обновлённые Сценарии

| Scenario | Story | AC | Status | Coverage role | Tests |
| --- | --- | --- | --- | --- | --- |
| SC-0014-001 | ST-0014 | AC-0039 | passing | happy_path | TS-0022 |

## Draft Scenarios Без Automated Evidence

| Scenario | Story | AC | Status | Coverage role | Tests |
| --- | --- | --- | --- | --- | --- |
| SC-0014-002 | ST-0014 | AC-0040 | draft | business_rule | нет |

## Известные Gaps

1. Step definitions отсутствуют: отдельный BDD implementation task нужен перед executable Gherkin.
2. SC-0014-002 остаётся draft без automated test links.
3. CV-0004 callbacks/status/relation/Git timer coverage не входит в текущий CV-0003 delivery.
