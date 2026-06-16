# STORM BDD Lint

Сгенерировано: 2026-06-16
Команда: `/storm:bdd-lint`
Режим: delivery-task

## Результат

Статус: passed_with_warnings

| Проверка | Результат |
|---|---|
| У каждого сценария есть `@scenario:<id>` | OK |
| У linked story сценариев есть `@story:<id>` | OK |
| У сценариев есть need/constraint basis | OK |
| Passing/automated scenarios имеют linked tests или step definitions | OK |
| Acceptance criteria сохранены отдельно от Gherkin | OK |
| Production code changes прошли через approved QUEST gate; test annotations не изменялись | OK |

## Warnings

1. `step_definitions` пока пусты: BDD layer связан с TUnit tests напрямую.
2. `SC-0014-001` и `SC-0014-002` остаются draft без test links.
3. `SC-0011-002` покрыт contract/security/live SignalR tests; ServiceStack task API live smoke остаётся blocker из-за ServiceStack free-quota operation registration даже в minimal test-only AppHost.

## Сценарии По Статусам

| Scenario | Story | AC | Status | Coverage role | Tests |
|---|---|---|---|---|---|
| SC-0001-001 | ST-0001 | AC-0001 | automated | happy_path | TS-0001<br>TS-0004 |
| SC-0001-002 | ST-0001 | AC-0002 | automated | happy_path | TS-0001<br>TS-0014 |
| SC-0001-003 | ST-0001 | AC-0003 | automated | happy_path | TS-0004 |
| SC-0002-001 | ST-0002 | AC-0004 | automated | business_rule | TS-0003<br>TS-0005 |
| SC-0002-002 | ST-0002 | AC-0005 | automated | negative_path | TS-0003<br>TS-0005 |
| SC-0002-003 | ST-0002 | AC-0006 | automated | regression | TS-0003<br>TS-0014 |
| SC-0003-001 | ST-0003 | AC-0007 | automated | happy_path | TS-0002<br>TS-0003<br>TS-0005 |
| SC-0003-002 | ST-0003 | AC-0008 | automated | happy_path | TS-0002<br>TS-0014 |
| SC-0003-003 | ST-0003 | AC-0009 | automated | happy_path | TS-0002<br>TS-0003 |
| SC-0004-001 | ST-0004 | AC-0010 | automated | happy_path | TS-0001<br>TS-0004<br>TS-0011 |
| SC-0004-002 | ST-0004 | AC-0011 | automated | happy_path | TS-0001<br>TS-0004<br>TS-0011<br>TS-0016 |
| SC-0004-003 | ST-0004 | AC-0012 | automated | happy_path | TS-0004<br>TS-0011 |
| SC-0005-001 | ST-0005 | AC-0013 | automated | business_rule | TS-0001<br>TS-0004<br>TS-0006 |
| SC-0005-002 | ST-0005 | AC-0014 | automated | business_rule | TS-0006<br>TS-0013 |
| SC-0005-003 | ST-0005 | AC-0015 | automated | business_rule | TS-0006 |
| SC-0006-001 | ST-0006 | AC-0016 | automated | happy_path | TS-0005<br>TS-0013 |
| SC-0006-002 | ST-0006 | AC-0017 | automated | happy_path | TS-0013 |
| SC-0006-003 | ST-0006 | AC-0018 | automated | constraint_check | TS-0005<br>TS-0013 |
| SC-0007-001 | ST-0007 | AC-0019 | automated | happy_path | TS-0005 |
| SC-0007-002 | ST-0007 | AC-0020 | automated | happy_path | TS-0005<br>TS-0008 |
| SC-0007-003 | ST-0007 | AC-0021 | automated | business_rule | TS-0003<br>TS-0005 |
| SC-0008-001 | ST-0008 | AC-0022 | automated | happy_path | TS-0007 |
| SC-0008-002 | ST-0008 | AC-0023 | automated | happy_path | TS-0007 |
| SC-0008-003 | ST-0008 | AC-0024 | automated | happy_path | TS-0006<br>TS-0007 |
| SC-0009-001 | ST-0009 | AC-0025 | automated | regression | TS-0014 |
| SC-0009-002 | ST-0009 | AC-0026 | automated | regression | TS-0003<br>TS-0014 |
| SC-0009-003 | ST-0009 | AC-0027 | automated | regression | TS-0014 |
| SC-0010-001 | ST-0010 | AC-0028 | automated | constraint_check | TS-0008<br>TS-0009 |
| SC-0010-002 | ST-0010 | AC-0029 | automated | constraint_check | TS-0008<br>TS-0009 |
| SC-0010-003 | ST-0010 | AC-0030 | automated | constraint_check | TS-0008<br>TS-0009 |
| SC-0010-004 | ST-0010 | AC-0031 | automated | happy_path | TS-0009 |
| SC-0011-001 | ST-0011 | AC-0032 | passing | happy_path | TS-0017 |
| SC-0011-002 | ST-0011 | AC-0033 | passing | happy_path | TS-0017<br>TS-0018<br>TS-0019 |
| SC-0012-001 | ST-0012 | AC-0034 | automated | happy_path | TS-0008<br>TS-0012 |
| SC-0012-002 | ST-0012 | AC-0035 | automated | constraint_check | TS-0008<br>TS-0009 |
| SC-0012-003 | ST-0012 | AC-0036 | automated | constraint_check | TS-0008<br>TS-0015 |
| SC-0013-001 | ST-0013 | AC-0037 | automated | happy_path | TS-0001<br>TS-0004<br>TS-0010 |
| SC-0013-002 | ST-0013 | AC-0038 | automated | happy_path | TS-0001<br>TS-0004<br>TS-0010 |
| SC-0014-001 | ST-0014 | AC-0039 | draft | happy_path | нет |
| SC-0014-002 | ST-0014 | AC-0040 | draft | business_rule | нет |
| SC-0015-001 | ST-0015 | AC-0041 | automated | compatibility | TS-0011<br>TS-0015 |
| SC-0015-002 | ST-0015 | AC-0042 | automated | constraint_check | TS-0015 |
| SC-0015-003 | ST-0015 | AC-0043 | automated | constraint_check | TS-0011<br>TS-0015 |
