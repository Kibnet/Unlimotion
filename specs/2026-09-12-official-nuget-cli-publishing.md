# Официальная публикация Unlimotion CLI как .NET tool

## 0. Метаданные
- Тип (профиль): delivery-task; `testing-dotnet`, `dotnet-desktop-client`, `github-delivery-policy`, `tool-execution-baseline`, `session-insights-context`.
- Владелец: Kibnet / Unlimotion.
- Масштаб: medium, expanded SPEC: меняются package/release contracts и планируется внешняя публикация.
- Целевое семейство / behavior baseline: не применимо — задача не меняет model/prompt behavior.
- Поверхность: Codex; конечная поверхность — NuGet.org и стандартный .NET SDK.
- Effective runtime: локальный .NET SDK из `global.json`; точную версию записать в EXEC evidence перед pack.
- Eval baseline / evidence: не применимо — нет model-eval; evidence составят package inspection, clean tool install/smoke, CI и read-back NuGet.
- Целевой релиз / ветка: сначала PR из `feat/cli-default-task-storage` в `main`; затем отдельный release `v1.30.1` от проверенного `main` и NuGet `Unlimotion.Cli` `1.30.1`.
- Ограничения: не публиковать токены или секреты; не выпускать из неслитой ветки; NuGet package immutable; GitHub release/tag и NuGet push выполняются только после local/CI evidence и при наличии authorizations.
- Связанные ссылки: [текущая реализация default task directory](2026-09-12-cli-default-task-storage.md); latest release `v1.30.0` от `3aa24c8f96928f16f5e319be9e098c0037adf08d`; public NuGet flat-container `unlimotion.cli` на 2026-09-12 ответил HTTP 404.

## 1. Overview / Цель
Сделать Unlimotion CLI официальным публичным .NET tool: пользователь устанавливает релиз из стандартного NuGet.org источника командой

```powershell
dotnet tool install --global Unlimotion.Cli
```

и запускает `unlimotion-cli` без `--add-source`, `--tool-path` и локальной сборки.

Outcome contract:
- Success means: опубликованный публичный пакет `Unlimotion.Cli` версии `1.30.1` доступен из NuGet.org, является .NET tool, содержит исходный README/license metadata и устанавливается/запускается в чистом временном tool-path.
- Итоговый артефакт / output: source changes, GitHub Actions workflow с защищённым NuGet secret, PR/merge, SemVer GitHub release `v1.30.1`, NuGet package и verified read-back.
- Stop rules: не выполнять `dotnet nuget push`, не создавать tag/release и не утверждать публикацию, если source SHA не совпадает с проверенным `main`, пакет не проходит local install/smoke, CI не green, имя/версия заняты, либо отсутствует требуемое GitHub/NuGet authorization.

## 2. Текущее состояние (AS-IS)
- `src/Unlimotion.Cli/Unlimotion.Cli.csproj` уже содержит `PackAsTool=true`, `ToolCommandName=unlimotion-cli`, `PackageId=Unlimotion.Cli`, README и `License.txt`; версия локального пакета зафиксирована как `0.3.0`.
- `src/Unlimotion.Cli/README.md` документирует только local-feed установку с `--tool-path` и `--add-source`.
- В `.github/workflows/` нет workflow, который pack/push-ит `Unlimotion.Cli` в NuGet.org. Существующие packaging workflows запускаются после опубликованного GitHub release и обслуживают desktop-артефакты.
- Публичный NuGet endpoint `https://api.nuget.org/v3-flatcontainer/unlimotion.cli/index.json` вернул 404, поэтому пакет с таким id пока не опубликован; это не доказывает резервирование имени, его повторно проверить непосредственно перед push.
- `origin/main` и release `v1.30.0` указывают на `3aa24c8f96928f16f5e319be9e098c0037adf08d`. Feature worktree содержит один локальный commit `a4268ae3` поверх него; выпускать его напрямую нельзя.
- Текущий GitHub token имеет `repo`, но не заявляет scope `workflow`; это может блокировать push нового workflow и должно быть проверено до delivery, не маскируясь кодовой правкой.

## 3. Проблема
Пакет технически собирается локально, но не имеет публичного и воспроизводимого канала доставки. Пользователь не может установить CLI стандартной командой `dotnet tool install --global Unlimotion.Cli`.

## 4. Цели дизайна
- Привязать версию CLI к SemVer release Unlimotion, а не переиспользовать локальную `0.3.0` после публичной публикации.
- Собирать и публиковать только проверенный код `main`, воспроизводимо и без секретов в репозитории.
- Сделать нормальную установку основной в README, сохранив local-package путь для разработчиков.
- Дать проверяемое доказательство реальной установки и запуска public package.
- Сохранить существующие команду `unlimotion-cli`, JSON/text contract и явный `--tasks` override.

## 5. Non-Goals (чего НЕ делаем)
- Не меняем команды, task-graph semantics, desktop UI, storage schema или сетевые/server-mode границы CLI.
- Не публикуем npm/winget/chocolatey/Scoop пакеты, GitHub Packages либо private NuGet feed.
- Не добавляем NuGet API key в файлы, командные строки, логи или git history; не создаём/не изменяем account ownership NuGet.org автоматически.
- Не делаем desktop binary-release частью этой задачи и не заявляем, что NuGet tool является self-contained desktop installer.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `src/Unlimotion.Cli/Unlimotion.Cli.csproj` | Публичные NuGet metadata: package version from release build, project/repository URLs, license/readme, tool contract. |
| `src/Unlimotion.Cli/README.md` | Официальная global-install команда и совместимость с `unlimotion-cli`; local-feed instructions остаются developer fallback. |
| `.github/workflows/nuget-cli.yml` | Проверка release tag/version, restore/build/test/pack, package inspection and clean installation smoke; authenticated push only after all gates. |
| `src/Unlimotion.Test/*` | Existing CLI regression suite, including `TaskDirectoryResolverTests`; no new runtime behavior is required solely for publication. |
| GitHub repository secret `NUGET_API_KEY` | Protected user-owned credential used only by the publication step. |
| NuGet.org | Immutable public package hosting and post-push availability. |

### 6.2 Детальный дизайн
1. The CLI project receives public package metadata (`PackageProjectUrl`, `RepositoryUrl`, `RepositoryType`, `PackageReleaseNotes`) and no secret-bearing value. Its default checked-in `Version` becomes `1.30.1`, the first public CLI release matching the planned release tag.
2. Add a dedicated `nuget-cli.yml` trigger for published GitHub release `vMAJOR.MINOR.PATCH`. It checks out that tag, strips exactly one leading `v`, requires numeric SemVer and requires it to equal the project package version. This prevents publishing an arbitrary `main` checkout or silently publishing `0.3.0` under an unrelated product tag.
3. Before push, the workflow restores/builds the CLI and executes the relevant TUnit suite, packs `Unlimotion.Cli` to an isolated artifact directory, validates the `.nupkg` content/readme/license/tool metadata, then installs it into an isolated `--tool-path` from that directory and runs `unlimotion-cli --help` plus a read-only fixture smoke.
4. The final workflow step runs `dotnet nuget push` only with GitHub secret `NUGET_API_KEY`, source `https://api.nuget.org/v3/index.json`, and `--skip-duplicate`. `--skip-duplicate` makes retry after a confirmed same-version push non-fatal but does not treat a skipped package as a new publication; the read-back determines the actual state.
5. The README presents the global NuGet command first, with an explicit note that it installs executable `unlimotion-cli`, requires a compatible .NET runtime/SDK according to the package TFM, and that `--tasks` remains an override. Local packing remains as a contributor workflow.
6. Delivery order: PR from the feature branch -> CI green -> merge into `main` -> create `v1.30.1` GitHub release from exact merged SHA -> workflow publishes -> query NuGet metadata/download -> clean global-equivalent temporary install smoke. The repository may create desktop assets from the release independently; their outcome is reported separately.

No UI-facing behavior changes: visual planning artifact and UI video evidence are not applicable.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| First-time install | `dotnet tool install --global Unlimotion.Cli` from default NuGet.org source | SDK resolves public package and installs `unlimotion-cli` without custom source | NuGet read-back + clean temporary `--tool-path` install equivalent | AC-1, AC-5 |
| Run installed tool | `unlimotion-cli --help` then `status --format json` with desktop settings | Command is available and preserves CLI contract/default settings behavior | package-install smoke and existing resolver test | AC-2, AC-3 |
| Release retry | workflow is retried after NuGet accepted the version | It does not overwrite a package or expose secret; duplicate is reported honestly | workflow log + NuGet version read-back | AC-4 |
| Invalid delivery state | tag/version, tests, authorization or pack smoke fails | No NuGet push occurs | ordered workflow gates/log | AC-6 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Feature branch only | PR merged and CI green | exact commit becomes `main` candidate | CI red/pending -> no tag/release | Never publish from feature ref. |
| Published GitHub release | `v1.30.1` workflow starts | version/source validation, pack and smoke precede push | malformed/tag-version mismatch -> hard failure before secret step | Tag is public delivery evidence. |
| Pack validated | NuGet secret available | one push attempt to public source | secret absent/invalid -> failure, no retry with pasted credential | Secret is never logged. |
| Version already exists | workflow retry | `--skip-duplicate`, then read-back | different content cannot replace immutable package | Human reviews package SHA/content evidence. |
| Public package indexed | clean install | tool is installed and help/smoke succeeds | indexing delay -> bounded polling; no false PASS until success | Public API is source of truth. |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Public feed | agent | NuGet.org, because standard `dotnet tool install` uses it and current package id is absent there | 0.98 | Package id might be unavailable/reserved when publishing | Нет |
| Command/package identity | agent | `Unlimotion.Cli` package and `unlimotion-cli` command, preserving existing project metadata | 0.99 | Naming collision or user expects another command | Нет |
| Release/version scheme | agent | First public tool `1.30.1`, matched to release tag `v1.30.1` | 0.86 | User may prefer independent CLI SemVer | Нет; current desktop release train and public absence make alignment the least ambiguous option |
| Publication credential | user / NuGet owner | GitHub Actions secret `NUGET_API_KEY`, restricted to `Unlimotion.Cli` where NuGet supports scoped keys | 0.95 | Missing or overbroad credential blocks/raises blast radius | Нет для implementation; yes as runtime prerequisite for actual public push |
| Trigger | agent | `release.published`, only after tag/version checks | 0.91 | Release can trigger desktop packaging alongside CLI workflow | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| .NET tool identity | CLI `.csproj` | Preserve package id and command; version becomes `1.30.1` | Existing local install users may pin `0.3.0`; public users get first stable version | inspect `.nupkg` and install it |
| Public package feed | NuGet.org | First public immutable `Unlimotion.Cli/1.30.1` | No data migration; uninstall/reinstall changes client tool only | flat-container/version read-back |
| Release authority | GitHub release tag | workflow trusts only release tag that matches project version | Tag must point at merged `main` candidate | `git ls-remote`, `gh release view`, workflow checkout SHA |
| Authentication | GitHub Actions secret | `NUGET_API_KEY` only in push environment | Configure separately; no repository file stores it | workflow secret availability and masked log |

## 7. Бизнес-правила / Алгоритмы
- A package is publishable iff all of: release is published and not draft/prerelease; tag matches `^v\d+\.\d+\.\d+$`; normalized tag equals project version; checked-out SHA is the tag target and is an ancestor/equal of `origin/main`; targeted/full mandatory test gates, pack inspection and local-install smoke pass; package id/version are not conflicting on NuGet; secret is available.
- A failed prerequisite stops before `dotnet nuget push`.
- NuGet `--skip-duplicate` is recovery behavior only. Public read-back decides whether version is available, not the command exit code alone.

## 8. Точки интеграции и триггеры
- GitHub `release.published` event invokes `.github/workflows/nuget-cli.yml`.
- `Unlimotion.Cli.csproj` is the version and pack metadata source; workflow compares rather than overrides it.
- README updates mirror the actual package id/command.

## 9. Изменения модели данных / состояния
Не применимо: task data, settings and storage contracts do not change. Изменяется только externally hosted immutable build artifact and CI configuration.

## 10. Миграция / Rollout / Rollback
- Rollout: publish a first stable tool only after merge/CI/release gates; then verify public install before announcing it.
- Compatibility: `unlimotion-cli` name, CLI arguments and package README remain compatible. Existing local packs are not deleted.
- Rollback: NuGet versions are immutable and cannot be overwritten. If a bad published version is found, unlist it in NuGet.org, publish a new corrective version from a new verified tag, and document the safe version. Do not delete or mutate a package as a substitute for a corrective release.

## 11. Тестирование и критерии приёмки

- AC-1: `Unlimotion.Cli` is a public package on NuGet.org; `dotnet tool install --global Unlimotion.Cli --version 1.30.1` is valid without `--add-source`.
- AC-2: package declares `DotnetTool` metadata and exposes `unlimotion-cli`.
- AC-3: installed tool keeps existing help and default-task-directory behavior; resolver regression suite remains green.
- AC-4: publish workflow contains version/source/gate ordering, uses masked `NUGET_API_KEY`, no secret literals, and retry does not claim a duplicate as a new upload.
- AC-5: package includes README and `License.txt`, with actual official-install documentation.
- AC-6: before public push: required local/CI checks pass; after push: NuGet version/read-back and clean install smoke succeed. Any failed condition stops completion.

Commands/evidence, in order:

```powershell
dotnet --info
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/TaskDirectoryResolverTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release
dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --maximum-parallel-tests 1 --output Detailed
dotnet pack src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release -o <isolated-pack-dir>
dotnet tool install --tool-path <isolated-tool-dir> --add-source <isolated-pack-dir> Unlimotion.Cli --version 1.30.1
<isolated-tool-dir>\unlimotion-cli --help
gh pr checks <pr-number> --watch
gh release view v1.30.1 --repo Kibnet/Unlimotion
Invoke-WebRequest https://api.nuget.org/v3-flatcontainer/unlimotion.cli/index.json
dotnet tool install --tool-path <clean-tool-dir> Unlimotion.Cli --version 1.30.1
<clean-tool-dir>\unlimotion-cli --help
```

Long full TUnit runs remain serial (`--maximum-parallel-tests 1`) and need recorded progress/evidence. Do not retry an identical timeout; inspect the report/log and change the diagnostic scope. A public install must wait through normal NuGet indexing but has a bounded polling deadline and reports a delay rather than a false success.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 public install | clean `--tool-path` install from NuGet | NuGet flat-container and Gallery read-back | workflow log + public endpoint | Not applicable after actual publication |
| AC-2 tool metadata | package content/tool install | inspect `.nuspec` and executable | `.nupkg` inspection log | Not applicable |
| AC-3 behavior unchanged | `TaskDirectoryResolverTests` + full TUnit | installed `--help` and status fixture smoke | TUnit report + tool output | Not applicable |
| AC-4 safe publish gate | workflow YAML static contract checks where practical | review ordered steps and masked secret reference | workflow diff/CI run | No separate app runtime test needed |
| AC-5 package docs | package content check | rendered/read README review | `.nupkg` file listing | Not applicable |
| AC-6 release evidence | CI and pack/install smoke | exact SHA/tag/release/NuGet read-backs | CI URL, release URL, NuGet URL | Must remain incomplete if an external gate is unavailable |

## 12. Риски и edge cases
- NuGet package id/selected version is unavailable at execution: stop, record response, choose a new version/name only through a follow-up decision.
- Current GitHub auth lacks `workflow` scope: do not bypass it; user refreshes authorization or uses a repository-integrated credential before workflow push.
- NuGet API key is absent/invalid: workflow fails closed before public package upload.
- Package indexed slowly: poll public endpoint with a bounded timeout and do not publish an announcement first.
- A later GitHub release with unchanged package version would fail tag/version check, forcing intentional version maintenance instead of duplicate publishing.
- `--skip-duplicate` can conceal a stale retry: compare public package version and local package SHA/content during delivery read-back.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Нужна обычная команда, а не локальный источник» | This is the primary outcome | README and public smoke use default NuGet.org source; local source is demoted to developer fallback | mitigated |
| «Не публикуй секрет и не делай скрытый push» | NuGet publication needs credentials | Secret only in GitHub Actions; ordered workflow, explicit public read-back and no CLI arg/token logging | mitigated |
| «Не выпускай незамёрженный feature worktree» | Current code is one commit ahead of main | PR/CI/merge/release SHA gates are required before tag/push | mitigated |
| «Версия tool должна быть понятной» | Existing `0.3.0` is local-only while desktop release is `1.30.0` | First public version aligns to `v1.30.1`; README/release evidence shows it | mitigated |

### Rework Prevention Checklist
- User-visible install/run scenarios are named and each has evidence.
- Public package/release/credential choices are captured in the decision ledger.
- ACs are verifiable outcomes, not preparation tasks.
- Role-based review includes delivery/security; UI evidence is explicitly not applicable.
- EXEC has an end-to-end proof path and stops safely if external authorization is unavailable.

## 13. План выполнения
1. On approval, update CLI package metadata/version and official-install README; add a release-gated NuGet workflow with no secrets in source.
2. Add/adjust narrow automation checks for packaging/workflow contracts if repository patterns permit; otherwise make pack/install smoke a required CI workflow gate.
3. Run expected-red (new package/workflow contract where applicable), targeted tests, CLI build, serial full TUnit, local pack/content inspect/install smoke, and `git diff --check`.
4. Complete post-EXEC review, commit, push feature branch, open PR and wait for required green CI.
5. Merge only after review; verify `main` SHA and create public GitHub release `v1.30.1` from it.
6. Ensure the `NUGET_API_KEY` secret/appropriate GitHub permission exists, observe workflow result, perform NuGet/read-back/clean-install verification, then report actual publication status and any desktop-package jobs separately.

## 14. Открытые вопросы
Нет блокирующих design-вопросов. External runtime prerequisites remain: repository ability to push a workflow (current token reports no `workflow` scope) and a NuGet.org owner-provided scoped `NUGET_API_KEY`. Their absence blocks publication, not the approved source implementation.

## 15. Соответствие профилю
- Профиль: `delivery-task` + `testing-dotnet` + `dotnet-desktop-client` + `github-delivery-policy` + `tool-execution-baseline` + `session-insights-context`.
- Выполненные требования профиля: expanded SPEC due to CI/release/public contract; observable delivery scenarios and AC evidence; TUnit/full-test plan; source/tag/CI/secret/rollback gates; no UI change therefore local UI override is not applicable.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Cli/Unlimotion.Cli.csproj` | version `1.30.1` and public NuGet metadata | Stable public package identity and provenance |
| `src/Unlimotion.Cli/README.md` | Official global installation and developer fallback | Users can use the standard command |
| `.github/workflows/nuget-cli.yml` | New gated build/test/pack/install/publish flow | Reproducible no-secret-in-source publication |
| `src/Unlimotion.Test/*` | Only if a narrowly scoped package/workflow contract test follows repository practice | Guard observable release contract |
| `specs/2026-09-12-official-nuget-cli-publishing.md` | Plan, evidence and journal | QUEST governance |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Installation | local pack + custom `--add-source` and `--tool-path` | public default-source `dotnet tool install --global Unlimotion.Cli` |
| Package version | local-only `0.3.0` | first public version `1.30.1`, paired with `v1.30.1` |
| Publication | manual local artifact only | gated GitHub release workflow + NuGet read-back |
| Credentials | no release credential model | protected GitHub secret only in final push step |

## 18. Альтернативы и компромиссы
- Publish `0.3.0` directly: smaller diff, but the public CLI version diverges immediately from the active product release and makes tag-to-package evidence unclear.
- Manual local `dotnet nuget push`: faster for one version, but puts publication/audit/secret handling on a workstation and is less reproducible.
- Publish on every `main` push: minimizes manual steps but can leak unreviewed/untagged versions and lacks a stable release identity.
- Chosen: release-gated public package `1.30.1`; it preserves standard installation, ties immutable package to a GitHub delivery artifact, and keeps secrets in the CI boundary.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, root problem, goals, explicit non-goals cover public delivery outcome. |
| B. Качество дизайна | 6-10 | PASS | Responsibilities, trigger/order, gates/errors, performance/indexing and alternatives recorded. |
| C. Безопасность изменений | 11-13 | PASS | No task-data change; immutable-package migration/rollback and secret boundary are explicit. |
| D. Проверяемость | 14-16 | PASS | ACs map to package, workflow, CI, public read-back and clean install; stop rules specified. |
| E. Готовность к автономной реализации | 17-19 | PASS | Ordered plan, decision ledger and no unresolved design decision; external prerequisites are recorded. |
| F. Соответствие профилю | 20 | PASS | .NET, delivery, testing and GitHub release contracts applied. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Standard command and prohibited scope are explicit. |
| 2. Понимание текущего состояния | 5 | `.csproj`, README, workflow inventory, main/tag and live NuGet 404 were inspected. |
| 3. Конкретность целевого дизайна | 5 | Version/tag, gate order, secret boundary, retry/read-back are specified. |
| 4. Безопасность (миграция, откат) | 5 | Immutable NuGet rollback and secret/authorization failure handling are concrete. |
| 5. Тестируемость | 5 | Every AC has automation/log/public evidence and failure stop rules. |
| 6. Готовность к автономной реализации | 5 | No design decision remains; credentials are objective external prerequisites. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does official NuGet installation meet the stated operational goal? | PASS | Standard global command is outcome and evidence. |
| UX / designer | not applicable | Is there a UI visual/interaction surface? | PASS | CLI/package text only; README command is reviewed as output contract. |
| Tester / validation | applicable | Are public/negative/retry states testable? | PASS | Added clean install, package inspection, CI and public API evidence. |
| Developer / architect | applicable | Are version, source provenance and tool contracts coherent? | PASS | One package version matches one verified release tag. |
| Delivery / operations / security | applicable | Are publication, secrets, rollback and authorization safe? | PASS | Release gating, secret isolation, immutability and current token limitation captured. |

### Post-SPEC Review
- Статус / stop decision: PASS — можно запрашивать подтверждение.
- Scope reviewed: this SPEC; central stack listed in section 15; current feature worktree `feat/cli-default-task-storage`; planned files in section 16; no unresolved design question.
- Review passes:
  - Scope/Evidence: inspected CLI project/README, workflow inventory, `origin/main`, `v1.30.0`, GitHub auth and NuGet flat-container response.
  - Contract: standard install, command name, package/release version and no-secret boundary are explicit.
  - Adversarial risk: version collision, pre-merge publishing, missing workflow scope/key, slow indexing and duplicate retry each fail closed.
  - Role-Based: all applicable delivery roles PASS; UX explicitly not applicable.
  - Fix and re-review: initial draft was reviewed for feature-branch release risk and changed to require merged-main/tag provenance; matrix and stop rules rechecked.
  - Stop decision: no BLOCKER/HIGH/MEDIUM finding remains in the source design; actual external credentials are runtime gates.
- Evidence inspected: `Unlimotion.Cli.csproj`, CLI README, `.github/workflows` inventory, `git ls-remote`, `gh auth status`, `gh release view v1.30.0`, NuGet HTTP 404.
- Depth checklist: scope/unrelated changes — bounded; acceptance/scenarios/ledger/objections — populated; validation evidence — layered; unsupported claims — public absence is time-stamped; regression/edge cases — enumerated; docs/changelog — README required, release notes reviewed at delivery; hidden contract — package/version/tag contract explicit; manual challenge — verify actual NuGet binary contents and tag SHA after publication.
- No-findings justification: SPEC avoids treating a local package or successful `push` exit code as publication proof and contains an external read-back.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | delivery provenance | Initial thought of direct feature-branch release was unsafe | Require PR/CI/merge and exact-tag source gate | fixed |
| LOW | evidence | NuGet HTTP 404 does not reserve package name | Recheck immediately before push | fixed |

- Fixed before continuing: provenance gate and availability caveat integrated above.
- Checks rerun: SPEC linter/rubric and review content self-check after amendment.
- Needs human: only if external authorization/secret is absent at publication time.
- Residual risks / follow-ups: desktop packaging jobs triggered by the GitHub release are outside tool publication proof and will be reported independently.

### Post-EXEC Review
- Статус: PASS для source implementation и local validation; GitHub/NuGet delivery остаётся следующим внешним этапом.
- Scope reviewed: approved SPEC; CLI metadata/README; new `nuget-cli.yml`; current status/diff; package/install evidence; CI-style TUnit evidence. Unrelated changes отсутствуют.
- Review passes:
  - Scope/Evidence pass: changed files match section 16; no task/domain/UI behavior or secret was added.
  - Contract pass: package `Unlimotion.Cli` and executable `unlimotion-cli` preserved; version is exactly `1.30.1`, paired with planned `v1.30.1`; README uses default NuGet install command.
  - Adversarial risk pass: workflow refuses malformed/mismatched tag, tag outside `origin/main`, missing secret, missing pack entries and unindexed public package; `--skip-duplicate` is followed by read-back.
  - Role-Based pass: business workflow, test, architecture and delivery/security contracts verified; UX remains not applicable because no UI surface changed.
  - Fix and re-review: corrected `git fetch origin main` to explicit remote-tracking ref update before source ancestry check; YAML parse and diff/whitespace checks repeated.
  - Stop decision: source changes may be committed and sent to PR; do not create release/NuGet package until merged `main`, green CI and credential gates.
- Evidence inspected:
  - expected red: pre-change pack produced `Unlimotion.Cli.0.3.0.nupkg`, not required `1.30.1`;
  - YAML parse: PyYAML 6.0.3 parsed `nuget-cli.yml` contract;
  - targeted `TaskDirectoryResolverTests`: PASS 2/2;
  - Release build + pack: `Unlimotion.Cli.1.30.1.nupkg`, 660212 bytes; manifest is `Unlimotion.Cli` `1.30.1` with `DotnetTool`, README/license/runtime/tool files;
  - clean temporary local install: PASS, `unlimotion-cli.exe --help` shows optional `--tasks` contract;
  - repository CI runner main restore/build/test: PASS, TRX 970 total / 970 passed / 0 failed / telemetry complete, duration 20:57.
- Depth checklist: scope drift/unrelated changes — none; AC/scenarios/matrix — all source-side entries evidenced; validation — layered; unsupported claims — package is not yet public; regression/edge cases — gate ordering and failure paths inspected; docs — official and local install paths coherent; hidden contract — tag/version equality explicit; manual challenge — inspect actual public package/read-back after NuGet indexing.
- No-findings justification: source and workflow use no secret literals, retain CLI command/behavior, produce a verified .NET tool, and stop before external publication when provenance or credentials fail.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | delivery provenance | `git fetch origin main` could leave stale `origin/main` ref | Fetch into `refs/remotes/origin/main` explicitly | fixed |
| LOW | test evidence | direct TUnit HTML retained prior targeted report | Repeat through repository CI runner with fresh TRX/metadata | fixed |

- Fixed before continuing: remote-tracking fetch and CI-runner evidence path.
- Checks rerun: YAML parse; targeted test/build/pack/install; full CI runner; `git diff --check` and whitespace scan.
- Validation evidence: listed above; pre-existing compiler warnings remain unrelated and did not fail build/test.


- Unrelated changes: none.
- Needs human: external delivery is blocked until a repository owner both configures a scoped `NUGET_API_KEY` GitHub Actions secret and grants/refreshes GitHub authentication with `workflow` permission. The checked secret list contains only Android signing secrets; the current token has `gist`, `read:org`, `repo`, but no `workflow`.
- Residual risks / follow-ups: local commits `a4268ae3` and `7e3038ab` are not pushed; public NuGet availability, PR/CI status and release-triggered workflow remain unverified.
- Delivery stop decision: NEEDS-HUMAN — do not push the workflow, create `v1.30.1`, or claim a public package before the two prerequisites above are confirmed.
## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Standard public CLI install | 0.94 | NuGet owner secret and effective workflow permission are runtime prerequisites | Request SPEC approval, then implement and validate source changes | Да, approval gate | Нет | Expanded SPEC is required because public package/release/CI contracts and irreversible publication are in scope | `specs/2026-09-12-official-nuget-cli-publishing.md` |
| EXEC | Public tool package and release workflow | 0.96 | GitHub workflow push scope and protected NuGet secret are still external gates | Commit and deliver source change through PR | Нет | Пользователь подтвердил SPEC | `1.30.1` package/install contract, targeted tests and CI-style full suite validated locally | `.csproj`, CLI README, `nuget-cli.yml`, this SPEC |
| EXEC | External publication preflight | 0.99 | `NUGET_API_KEY` and GitHub `workflow` permission are absent | Wait for owner to configure both prerequisites, then push/PR/release/read-back | Да, external credential/authorization | Да, reported exact blocker | Commit `7e3038ab` is local only; package was not published and no release/tag was created | this SPEC |
