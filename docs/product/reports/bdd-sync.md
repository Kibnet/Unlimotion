# STORM BDD Sync

Сгенерировано: 2026-06-16
Команда: `/storm:bdd-sync`
Режим: delivery-task после утверждения SPEC; production code/tests изменялись в рамках approved SPEC, test annotations не менялись

## Scope

Синхронизация обновляет STORM artifacts после добавления `TS-0019` и follow-up попытки ServiceStack API smoke для `ST-0011`. Acceptance criteria сохранены отдельно от Gherkin; Gherkin-сценарий `SC-0011-002` остаётся связанным с TUnit contract/security/live SignalR evidence, а ServiceStack API live smoke зафиксирован как blocker без добавления нового passing test.

## Синхронизированные Связи

| Связь | Значение |
|---|---:|
| Feature files | 15 |
| Gherkin Rules | 43 |
| Gherkin Scenarios | 43 |
| Active stories со сценариями | 15/15 |
| AC с rules | 43/43 |
| AC со scenarios | 43/43 |
| Scenario -> Test links | 41/43 |
| Constraints со scenario verification | 8/8 |

## Scenario Статусы

| Status | Количество | Основание |
|---|---:|---|
| automated | 39 | Сценарии связаны с существующими тестами, но не запускались в этом EXEC. |
| passing | 2 | `SC-0011-001` связан с `TS-0017`; `SC-0011-002` связан с `TS-0017`, `TS-0018` и `TS-0019`; retained targeted live SignalR run passed 1/1. |
| draft | 2 | Нет test links; нужна отдельная delivery-task. |
| manual | 0 | Не назначалось: нет свежего manual evidence. |
| failing | 0 | Не назначалось: targeted run зелёный. |

## Draft Scenarios Без Automated Evidence

| Scenario | Story | AC | Status | Coverage role | Tests |
|---|---|---|---|---|---|
| SC-0014-001 | ST-0014 | AC-0039 | draft | happy_path | нет |
| SC-0014-002 | ST-0014 | AC-0040 | draft | business_rule | нет |

## Известные Gaps

1. `SC-0011-002` имеет passing contract/security/live SignalR evidence; ServiceStack task API live smoke остаётся blocker, потому что minimal test-only AppHost с `TaskService` assembly падает на ServiceStack free-quota operation registration до endpoint assertions.
2. `SC-0014-001` и `SC-0014-002` остаются draft для Telegram bot.
