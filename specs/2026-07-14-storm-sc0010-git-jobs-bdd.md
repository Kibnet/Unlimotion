# SPEC: Исполняемый BDD-мост Git backup jobs (SC-0010-004)

## 0. Метаданные
- Тип (профиль): `storm-product-development`, small delivery-task, test-only executable BDD.
- Baseline: `ST-0010`, `AC-0031`, `GR-031`, `SC-0010-004`.
- Целевая ветка: `storm-bootstrap`.
- Ограничения: не менять production code, `.feature`, existing tests, annotations, UI, Git config, реальные repositories или данные пользователя.
- Runtime/eval: .NET 10/TUnit; direct fixture-local Git repositories, model/config migration не применимы.

## 1. Overview / Цель
Связать текст `SC-0010-004` с существующим доказательством автоматических pull/push jobs и сохранности local/remote tasks, чтобы закрыть последнюю executable gap `ST-0010` без изменения продуктового поведения.

Outcome contract:
- Success means: Gherkin исполняется через `SD-0147..SD-0150` и три independent checks проходят.
- Output: `TS-0063`, contract, step definitions, executable scenario test, STORM sync/lint/reports.
- Stop rule: отдельная QUEST SPEC обязательна, если обнаружится потребность менять production, tests, annotations или feature text.

## 2. AS-IS / 3. Проблема
`SC-0010-004` связан только с `TS-0009`, не имеет step definitions и не учитывается как executable. Existing tests уже подтверждают: jobs запускают pull/push при включенном backup и отсутствии конфликта; pull получает remote task; non-empty local/remote folders сохраняют обе задачи после Git merge.

## 4. Цели дизайна / 5. Non-Goals
- Reuse existing passing tests through disposable test-only contract; explicit result flags для jobs, remote pull и preservation.
- Не создавать новый Git job behavior, UI, migration, persistence или external integration.
- UI visual plan/video: Не применимо, UI behavior/layout не меняются.
- Full suite PASS: не заявляется из-за ранее зафиксированного timeout without summary.

## 6. TO-BE
### 6.1 Ответственность
- `GitBackupJobsContract.cs` выполняет `Jobs_RunWhenBackupIsEnabledAndNoConflictResolutionIsInProgress` как scheduler trigger proof и два `BackupViaGitServiceTests` как Git-state invariants: `PullExistingRepository_PullsRemoteChanges_WhenTaskFolderIsExistingRepository`, `ConnectRepository_MergesNonEmptyRemoteWithLocalFolderAfterConfirmation`; освобождает оба fixture instances в `finally`.
- `GitBackupJobsStepDefinitions.cs` сопоставляет exact feature steps с `SD-0147..SD-0150`.
- `StormGitBackupJobsExecutableSpecTests.cs` проверяет parser tags/rule/steps и запускает runner.
- `StormStepDefinition.cs` добавляет transient context; `storm.json` и six reports фиксируют `TS-0063`.

### 6.2 Evidence contract
`GitBackupJobsScenarioResult` хранит `JobsExecutePassed`, `RemotePullPassed`, `TaskPreservationPassed`; `AssertAsync` проверяет все три. Это исключает частично passing scenario.

### 6.3 User-observable scenario
| Trigger | Expected result | Evidence | AC |
| --- | --- | --- | --- |
| Включенный Git backup без active conflict | Pull/push jobs запускаются, remote task поступает, local/remote tasks не теряются | BDD 1/1 + direct 3/3 | AC-0031 |

### 6.4 State matrix
| State | Trigger | Result | Notes |
| --- | --- | --- | --- |
| Backup enabled, no conflict | Jobs execute | One pull and one push | Existing job test |
| Existing repository with remote change | Pull | Remote task is present locally | Existing Git fixture |
| Non-empty local and remote folders | Merge | Both task files remain | Existing Git fixture |

### 6.5 Decisions
| Decision | Chosen option | Needs user before EXEC |
| --- | --- | --- |
| Evidence scope | Scheduler trigger и non-loss Git-state invariant доказываются раздельно, без заявления, что connect merge является job | Нет |
| Existing tests/annotations | Call unchanged through new bridge | Нет |
| UI automation | Не применимо: no UI change | Нет |

### 6.6 Runtime/data contract
| Area | Source of truth | Change | Verification |
| --- | --- | --- | --- |
| Git data | Fixture-local repositories/config | Нет | Three direct TUnit tests |
| Product state | Existing `BackupViaGitService`/jobs | Нет | No production diff |

## 7. Правила / 8. Интеграции / 9. State
- Scenario passes only when all three flags pass; Gherkin remains separate from acceptance criteria.
- `StormFeatureParser` -> `StormScenarioRunner` -> new step definitions -> new contract -> existing tests.
- Только transient context, no persisted model/data changes.

## 10. Rollout / Rollback
Migration/rollout не применимы. Откат: удалить новые test-only files/artifact links; product data и external remotes не затронуты.

## 11. Testing and AC
1. New BDD test executes exact four `SC-0010-004` steps and `SD-0147..SD-0150`.
2. Three direct tests are called and asserted independently.
3. `ST-0010` becomes 4/4, aggregate executable ratio becomes 38/45.
4. Production code, `.feature`, existing tests and annotations are unchanged.

```powershell
dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-restore -v minimal /nr:false
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/StormGitBackupJobsExecutableSpecTests/*" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/GitBackupJobTests/Jobs_RunWhenBackupIsEnabledAndNoConflictResolutionIsInProgress" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/BackupViaGitServiceTests/PullExistingRepository_PullsRemoteChanges_WhenTaskFolderIsExistingRepository" --output Detailed
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build --treenode-filter "/*/*/BackupViaGitServiceTests/ConnectRepository_MergesNonEmptyRemoteWithLocalFolderAfterConfirmation" --output Detailed
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
```

## 12. Risks / objections
- Fixtures create only local temporary repositories; contract disposes both fixture types.
- Full suite remains unconfirmed; targeted result is the only PASS claim.
- Expected objection: connect merge is not a scheduled job. Mitigation: it proves explicit non-loss invariant while job test separately proves scheduling trigger.

## 13. Plan
Add bridge -> targeted tests/build -> STORM sync/lint/coverage -> post-EXEC review -> commit.

## 14. Open questions
Нет; active workflow auto-confirms after PASS review.

## 15. Profile / 16. Files / 17. Delta / 18. Alternative
- Profile: `storm-product-development`; Russian artifacts, no unapproved product changes, exact evidence.
- Files: this SPEC; three test-only bridge files; minimal context; `storm.json`; six reports.
- Delta: linked-only `SC-0010-004` -> `TS-0063` executable/passing; `ST-0010` 3/4 -> 4/4.
- Alternative: add UI tests or alter `.feature`; rejected because no UI/product change and existing direct evidence is sufficient.

## 19. Quality gate and review
### SPEC Linter / Rubric
| Block | Status | Comment |
| --- | --- | --- |
| Scope/design/safety/testability/autonomy/profile | PASS | Exact files, boundaries, evidence and stop rule are defined |

### Role-Based Review
| Role | Verdict | Required change |
| --- | --- | --- |
| Domain | PASS | Evidence split explicitly distinguishes job trigger from task-preservation invariant |
| UX | Не применимо | No UI change |
| Tester | PASS | BDD and three direct checks have exact single-test filters |
| Developer | PASS | Contract must dispose `GitBackupJobTests` and `BackupViaGitServiceTests` separately |
| Delivery/security | PASS | Tests create fixture-local bare remotes only; no secrets or external configuration |

### Post-SPEC Review
- Статус: PASS после исправления.
- Scope/Evidence: PASS. Contract: PASS after separating scheduler proof from Git-state invariant. Adversarial risk: PASS. Role-based: PASS.
- Finding fixed: the first draft could have implied that connect merge itself was a scheduled job; the design now states the two evidence layers explicitly.
- Decision: active workflow auto-approval permits EXEC.

### Post-EXEC Review
- Статус: Не выполнен до EXEC.

## Approval
Автоматическое подтверждение active workflow после PASS review; явная фраза: "Спеку подтверждаю".

## 20. Журнал действий
| Phase | Decision | Next action |
| --- | --- | --- |
| SPEC | Existing tests cover jobs, remote pull and non-loss separately | Review and auto-EXEC |
| SPEC review | Separate scheduling trigger from non-loss invariant | Execute test-only bridge |
