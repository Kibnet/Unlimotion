# CLI: версия пакета из GitHub Release

## 0. Метаданные
- Тип (профиль): delivery-task; .NET backend/API + GitHub delivery; не UI-facing.
- Владелец: Kibnet.
- Масштаб: medium / expanded — меняются public NuGet package identity и release workflow.
- Целевое семейство / behavior baseline: не применимо, модельный runtime не затрагивается.
- Поверхность: GitHub Actions release workflow и NuGet package `Unlimotion.Cli`.
- Effective runtime: GitHub Actions `windows-latest`, .NET SDK из `global.json`; version resolution выполняется в `pwsh` workflow step.
- Eval baseline / evidence: AS-IS inspection `Unlimotion.Cli.csproj`, `nuget-cli.yml`, Windows/Linux/macOS/Android packaging workflows и `NuGetCliWorkflowContractTests`.
- Целевой релиз / ветка: текущий PR #302, `feat/cli-approved-task-application`; release/tag/publish не входят в эту задачу.
- Ограничения: только stable GitHub release tag `vMAJOR.MINOR.PATCH`; никаких secret, NuGet publish или GitHub Release side effects.
- Связанные ссылки: PR #302; `.github/workflows/nuget-cli.yml`; `src/Unlimotion.Cli/Unlimotion.Cli.csproj`.

## 1. Overview / Цель
Убрать ручное дублирование версии CLI между `.csproj` и GitHub release tag. NuGet package должен получать нормализованную версию только из опубликованного stable release tag, как desktop artifacts получают её из release workflow.

Outcome contract:
- Success means: опубликованный `v1.32.0` release приводит к build, pack, nuspec и install smoke именно версии `1.32.0`, без `<Version>` в CLI project file.
- Итоговый артефакт / output: обновлённый `nuget-cli.yml`, contract test и краткая CLI release-документация.
- Stop rules: не запускать release/publish; invalid/draft/prerelease tag продолжает fail-closed; при невозможности доказать package version не завершать EXEC как PASS.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.Cli/Unlimotion.Cli.csproj` содержит `<Version>1.31.0</Version>`.
- `.github/workflows/nuget-cli.yml` принимает только `vMAJOR.MINOR.PATCH`, извлекает numeric version, но затем читает XML project и отказывает при несовпадении tag и `<Version>`.
- workflow build/pack не передаёт `PackageVersion`; ожидаемое имя `.nupkg` и nuspec уже проверяются against release-derived output.
- Windows, Linux, macOS и Android workflows нормализуют release tag и передают version в `dotnet publish/build` через MSBuild property.
- `NuGetCliWorkflowContractTests` сейчас проверяет только безопасную передачу release tag в PowerShell environment.

## 3. Проблема
CLI — единственный release artifact, для которого требуется вручную менять project version до публикации. Это создаёт лишний release-blocker и риск расхождения исходников с уже авторитетным GitHub release tag.

## 4. Цели дизайна
- Единственный release source of truth — stable GitHub release tag.
- NuGet package identity и smoke install получают одну numeric SemVer из tag.
- Local pack остаётся возможным, но требует явно заданный `-p:PackageVersion`, а не скрытый production fallback.
- Invalid/release-not-on-main/protected-secret checks сохраняются.
- Contract проверяется автоматическим тестом без настоящей публикации.

## 5. Non-Goals
- Не менять release tag format, release workflow trigger, NuGet secret, package ID, tool command, package metadata или desktop/Android version algorithms.
- Не публиковать NuGet package, GitHub Release, tag и не менять установленный CLI.
- Не вводить общий repository-wide version file или versioning tool.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `Unlimotion.Cli.csproj` -> package metadata, без release version.
- `nuget-cli.yml` -> validates and normalizes release tag; passes `PackageVersion` into build and pack.
- `NuGetCliWorkflowContractTests` -> guards source-of-truth and property propagation.
- `src/Unlimotion.Cli/README.md` -> documents explicit local package version requirement.

### 6.2 Детальный дизайн
1. Remove `<Version>` from the CLI project file.
2. Keep existing release tag validation and `origin/main` ancestry check.
3. Replace XML project-version equality check with a guard that the project has no `<Version>` release source.
4. Pass `-p:PackageVersion=${{ steps.release.outputs.version }}` to both `dotnet build` and `dotnet pack --no-build`; pack output, nuspec assertion and tool install continue using `steps.release.outputs.version`.
5. Document a local pack example with explicit `-p:PackageVersion=<numeric-semver>`.

No visual planning artifact or UI video evidence: this change has no desktop/mobile UI flow, selector, layout or UI-facing state.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Stable release package | Publish GitHub release `v1.32.0` from main | NuGet workflow packages and installs `Unlimotion.Cli` `1.32.0` | workflow contract + local pack metadata check | AC-1, AC-2 |
| Invalid tag | Publish malformed/zero tag | Workflow fails before packing/publishing | existing release validation + contract test | AC-3 |
| Local package | Run local pack | Caller supplies a numeric `PackageVersion`; no hidden production version exists | README example + manual pack check | AC-4 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Release tag `vX.Y.Z` | workflow published event | normalize to `X.Y.Z`, build/pack with `PackageVersion` | non-SemVer/`0.0.0` fails before pack | existing fail-closed format gate remains |
| CLI project has no version | local pack | caller passes explicit property | absent property produces SDK default only for local developer experimentation; never release workflow output | documented, not a publish path |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | ---: | ---: | --- | --- |
| Release source | agent | GitHub release tag only | 0.98 | duplicate source remains if XML retained | Нет |
| MSBuild property | agent | `PackageVersion`, passed to build and pack | 0.95 | build/pack drift if only pack receives it | Нет |
| Local behavior | agent | explicit `PackageVersion` documented | 0.91 | local default package may be misleading if omitted | Нет |
| Release/publish | user | excluded | 1.00 | unintended external publication | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| NuGet CLI version | `<Version>` + release tag equality | numeric release tag only | existing stable tags still map to same numeric version | workflow contract test + local nupkg nuspec |
| Release validity | `nuget-cli.yml` regex and ancestry check | unchanged | no data migration | targeted contract tests |
| Local pack | implicit project version | explicit `PackageVersion` command | developer-facing documentation change | local pack and README review |

## 7. Бизнес-правила / Алгоритмы
- Accept only `vMAJOR.MINOR.PATCH`, numeric version greater than `0.0.0`.
- `releaseVersion = tag.TrimStart('v')` only after regex validation.
- `PackageVersion == releaseVersion` for both build and pack in the release workflow.
- Every release tag must still resolve to a commit reachable from `origin/main`.

## 8. Точки интеграции и триггеры
- GitHub `release.published` triggers `.github/workflows/nuget-cli.yml`.
- `dotnet build` receives `PackageVersion` to align generated assembly/package metadata.
- `dotnet pack --no-build` repeats the same property to align nuspec/output filename.

## 9. Изменения модели данных / состояния
Нет persisted data, task model, CLI command or API changes. Меняется only CI build-time package metadata source.

## 10. Миграция / Rollout / Rollback
- Migration: no data migration; next stable release uses its tag as package version.
- Rollout: merge and normal PR CI only; GitHub release remains a separate user authorization.
- Rollback: revert the commit restores the project `<Version>` and prior equality guard. Do not republish/overwrite an existing NuGet version.

## 11. Тестирование и критерии приёмки
- AC-1: workflow no longer parses/comparers a CLI `<Version>`; it validates tag and passes release output as `PackageVersion` to build and pack.
- AC-2: packing with `PackageVersion=1.32.0` produces `Unlimotion.Cli.1.32.0.nupkg` with nuspec metadata version `1.32.0` and local tool install works.
- AC-3: malformed/zero tags and non-main tag reachability remain fail-closed.
- AC-4: README describes explicit local `PackageVersion`; no stale claim says project file owns release version.
- AC-5: no UI behavior changes; existing UI test coverage remains unaffected and no UI test is required.

Commands:
```powershell
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/NuGetCliWorkflowContractTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -p:PackageVersion=1.32.0
dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release --no-build -p:PackageVersion=1.32.0 -o <temp-output>
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --maximum-parallel-tests 1 --output Normal
```
Stop rules: do not simulate a release with secrets; stop on package/nuspec version mismatch, contract test failure, or unrelated test failure until classified.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | updated `NuGetCliWorkflowContractTests` | inspect workflow commands | test report | — |
| AC-2 | package metadata assertion where feasible | inspect nupkg/nuspec and local tool `--help` | temporary local package path/report | — |
| AC-3 | workflow contract assertions | inspect retained guards | test report | actual GitHub release excluded |
| AC-4 | README regression text check where proportional | review command | diff | — |
| AC-5 | Не применимо | no UI files/flows changed | diff | no UI contract changes |

## 12. Риски и edge cases
- `PackageVersion` passed only to pack can make assembly metadata drift; mitigate by passing it to build and pack.
- An accidental local pack without property may get SDK default; document explicit local command and keep release workflow authoritative.
- Invalid tag may accidentally reach pack; retain validation before build.
- Existing published versions cannot be republished; no release action is part of this change.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Локальная упаковка получила 1.0.0» | project version disappears | explicit `PackageVersion` command in README and test procedure | mitigated |
| «Версия DLL и nuspec различаются» | build and pack are separate invocations | same property passed to both commands; inspect nupkg | mitigated |
| «Сломан release protection» | XML equality guard is removed | retain tag regex, zero check, main ancestry and package metadata validation | mitigated |

### Rework Prevention Checklist
- User-visible package version and local pack command named: yes.
- Every scenario has evidence: yes.
- Agent decisions recorded: yes.
- Likely objections mitigated: yes.
- Relevant roles reviewed: yes.
- AC are verifiers: yes.
- EXEC validation path exists: yes.

## 13. План выполнения
1. Add a failing contract test for the release-derived `PackageVersion` flow.
2. Remove `<Version>` and update workflow build/pack plus its guard.
3. Add local pack documentation.
4. Run targeted contract test, package smoke/metadata check, CLI build and full TUnit suite.
5. Perform post-EXEC review; do not publish release/package.

## 14. Открытые вопросы
Нет блокирующих вопросов. The chosen source mirrors existing release artifact workflows and preserves all fail-closed gates.

## 15. Соответствие профилю
- Профиль: `.NET backend/API`, delivery workflow, `testing-dotnet`; `ui-automation-testing` не применим — no UI behavior.
- Выполненные требования профиля: public package contract, release source, local validation, rollback and no-external-side-effect boundary specified.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Cli/Unlimotion.Cli.csproj` | remove `<Version>` | remove duplicate source |
| `.github/workflows/nuget-cli.yml` | tag-only version flow; property propagation | derive package identity from release |
| `src/Unlimotion.Test/NuGetCliWorkflowContractTests.cs` | source/property contract checks | prevent regression |
| `src/Unlimotion.Cli/README.md` | local pack version guidance | explicit developer workflow |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Release source | tag plus hard-coded project version | tag only |
| Workflow guard | tag equals XML `<Version>` | tag validity/main ancestry and property propagation |
| Pack version | implicit project value | explicit release-derived `PackageVersion` |
| Local packaging | static project version | explicit developer-selected `PackageVersion` |

## 18. Альтернативы и компромиссы
- Keep `<Version>` and auto-update it before every release: rejected; preserves duplicate source and mutable release preparation.
- Introduce repository-wide version file/tool: rejected; broader migration without a need for desktop artifacts already deriving from tags.
- Selected: derive only NuGet CLI package version in its release workflow; smallest change matching existing packaging pattern.

## 19. Результат quality gate и review
### SPEC Linter Result
| № | Блок | Статус | Комментарий |
| ---: | --- | --- | --- |
| 1 | A | PASS | success is a tag-derived NuGet package version |
| 2 | A | PASS | project, workflow, tests and peer artifact flows inspected |
| 3 | A | PASS | duplicate release source identified |
| 4 | A | PASS | single source, compatibility and testability goals stated |
| 5 | A | PASS | release/publish and broad version migration excluded |
| 6 | B | PASS | project/workflow/test/docs responsibilities assigned |
| 7 | B | PASS | release event, build and pack integrations listed |
| 8 | B | PASS | tag normalization, propagation and ancestry invariants defined |
| 9 | B | PASS | invalid tag and omitted local property behavior specified |
| 10 | B | PASS | no runtime-performance impact; CI steps remain bounded |
| 11 | C | PASS | build-time package metadata only; no persisted data |
| 12 | C | PASS | existing stable tags map to same numeric SemVer |
| 13 | C | PASS | revert path and no-republish boundary defined |
| 14 | D | PASS | AC-1..AC-5 are observable and measurable |
| 15 | D | PASS | matrix covers workflow contract, nupkg and negative cases |
| 16 | D | PASS | concrete commands and stop rules supplied |
| 17 | E | PASS | ordered implementation and validation plan supplied |
| 18 | E | PASS | decision ledger complete; no user-owned blocker |
| 19 | E | PASS | expanded form selected for public package/release contract |
| 20 | F | PASS | .NET/delivery/test profile is applied; UI is explicitly not applicable |

Итог: ГОТОВО.

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | one release-version outcome and explicit exclusions |
| 2. Понимание текущего состояния | 5 | project/workflow/test and other artifact patterns inspected |
| 3. Конкретность целевого дизайна | 5 | exact property propagation and guard replacement specified |
| 4. Безопасность (миграция, откат) | 5 | fail-closed rules, no-publish boundary and revert plan |
| 5. Тестируемость | 5 | contract, nupkg and full-suite evidence mapped |
| 6. Готовность к автономной реализации | 5 | no user-owned decision remains |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does a released CLI package have one understandable version source? | PASS | no |
| UX / designer | not applicable | No UI or visual artifact changes | PASS | no |
| Tester / validation | applicable | Are release, invalid-tag and local-pack evidence covered? | PASS | nupkg metadata and full suite required |
| Developer / architect | applicable | Does tag-only version avoid build/pack drift? | PASS | same property must reach both commands |
| Delivery / operations / security | applicable | Do release gates/secrets stay fail-closed and unchanged? | PASS | no publish in EXEC |

### Post-SPEC Review
- Статус / stop decision: `PASS`; можно запрашивать exact approval.
- Scope/Evidence pass: read `AGENTS.md`, routing/QUEST/tool execution/versioning/spec/review owners, local override, canonical template, current PR #302 state, CLI project, NuGet workflow, other package workflows, contract test and memory pointers. Effective profiles: delivery-task, .NET backend/API, testing-dotnet; no UI profile.
- Contract pass: source is only release tag; project metadata remains except version; release tag validation, main ancestry, package identity and install smoke stay in scope; release publication excluded.
- Adversarial risk pass: considered absent local property, property only at pack, malformed tag, zero version, release not on main, stale documentation, duplicate source and accidental external side effect. The design addresses each without changing desktop workflows.
- Role-Based pass: all applicable roles are PASS above; UX is not applicable because no UI state/layout/automation changes.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | local developer workflow | SDK default version is possible if local pack omits property | document explicit `PackageVersion` command; release workflow always supplies it | planned |

- Fix and re-review: local-pack command, property propagation to build and pack, and metadata evidence were added to design; reviewed again.
- Depth checklist: scope drift none; AC/matrix complete; no unsupported current release claim; negative tag and assembly/nuspec drift included; docs/test/workflow impact enumerated; no hidden UI/data/API change.
- No-findings justification: the remaining LOW is an intentional local-only choice, not a release-path failure.
- Manual-review challenge / residual risk: a real release event is intentionally not fired; local package metadata plus workflow contract are next-best pre-release evidence.
- Needs human: exact QUEST approval only.

### Post-EXEC Review
- Статус / stop decision: `PASS`; локальная реализация и её доказательства завершены. Commit, push, PR update, GitHub Release, NuGet publication и изменение установленного CLI не выполнялись.
- Scope/Evidence pass: reviewed approved SPEC, `git status --short`, relevant project/workflow/test/README diffs, local package smoke and TUnit report. Changed behavior is limited to package-version source; no task model, CLI command or UI state changed.
- Contract pass: `<Version>` is absent from the CLI project; the release workflow retains stable-tag/zero-version/main-ancestry gates and passes `PackageVersion=${{ steps.release.outputs.version }}` to both build and pack. The nupkg output and nuspec both reported `1.32.0`; a temporary tool-path install returned `--help` successfully.
- Adversarial risk pass: checked for a stale XML comparison, property propagation to only one build phase, nuspec/output mismatch, local global-tool mutation, accidental release/publish, UI drift and unrelated task changes. None remains in changed scope.
- Role-Based pass: business workflow, tester, developer/architect and delivery/operations/security are PASS; UX remains not applicable because no UI flow or visual state changed.
- Fix and re-review: the new contract test first failed on the project `<Version>`, then passed after XML removal and workflow property propagation. Re-read workflow and local pack evidence after the fix.
- Validation:
  - `dotnet test src\\Unlimotion.Test\\Unlimotion.Test.csproj -c Release --no-build -- --treenode-filter "/*/*/NuGetCliWorkflowContractTests/*" --maximum-parallel-tests 1 --output Detailed` — PASS, 2/2.
  - `dotnet pack src\\Unlimotion.Cli\\Unlimotion.Cli.csproj -c Release -p:PackageVersion=1.32.0 -o <temp-feed>` + nuspec inspection + temporary `dotnet tool install --tool-path` and `unlimotion-cli --help` — PASS; package/nuspec/tool version `1.32.0`.
  - `dotnet test src\\Unlimotion.Test\\Unlimotion.Test.csproj -c Release -- --maximum-parallel-tests 1 --output Normal` — PASS, 1063/1063, 12m 49s.
  - `git diff --check` — pending final diff check below.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | live release event | A real GitHub release/NuGet publication was deliberately not run | Preserve this as a separate authorized delivery step; local package smoke is next-best evidence | accepted scope boundary |

- Depth checklist: no scope drift/unrelated product change; AC-1..AC-5 mapped to contract/package/full-suite evidence; no unsupported release claim; negative validation remains in workflow; README updated; no hidden UI/API/data contract change.
- Manual-review challenge / residual risk: GitHub-hosted release event and secret-backed NuGet publish have not been executed, so CI/release delivery must be checked after separately authorized PR update/merge/release.
- Needs human: none for the completed local change; explicit authorization remains required for commit/push/PR update and release/publication.

## Approval
Ожидается фраза: `Спеку подтверждаю`.

Подтверждение разрешает только реализацию и тестирование в границах этой SPEC. Оно не разрешает commit, push, PR update, merge, GitHub Release, NuGet publication, deploy или изменение установленного CLI.

## 20. Журнал действий агента
| Фаза | Тип намерения/сценария | Уверенность | Каких данных не хватает | Следующее действие | Нужна ли передача решения человеку | Фактическое обращение/решение | Короткое объяснение | Артефакты |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | AS-IS version-source audit | 0.98 | Actual release execution intentionally excluded | Compare CLI project, NuGet workflow and other packaging flows | Нет | Не требовалось | Found hard-coded CLI version plus workflow equality guard; other artifacts inject normalized release tag | csproj, workflows, contract test |
| SPEC | Tag-only package version design | 0.95 | No user-owned decision remains | Request exact approval | Да | Ожидается `Спеку подтверждаю` | `PackageVersion` in build and pack gives one release source with no broad migration | this SPEC |
| EXEC | Tag-derived CLI package version | 0.99 | Live release event intentionally excluded | Add red contract test, remove project version, propagate property, run package/full validation | Нет | User provided `Спеку подтверждаю`; local implementation and validation completed | The release tag is now the only production version source; local packaging is explicit and isolated | csproj, workflow, test, README, 1063/1063 TUnit |
