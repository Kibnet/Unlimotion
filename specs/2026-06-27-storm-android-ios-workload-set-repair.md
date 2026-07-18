# STORM Environment Admin: восстановление .NET workload set для Android/iOS build smoke

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / STORM environment-admin follow-up.
- Владелец: Codex + product owner approval gate + local machine admin/operator.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`.
- Instruction stack: central `AGENTS.md` -> `routing-matrix.md` -> `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `storm-product-development`; local `AGENTS.override.md` applied after central stack.
- Ограничения:
  - До подтверждения SPEC менять только этот файл.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие STORM artifacts.
  - Не менять production code, tests, test annotations, `.feature` wording, platform manifests, package metadata, workflows, NuGet config, SDK config или release pipeline.
  - Не заявлять Android/iOS runtime release support без фактического build/runtime evidence.
  - Любые команды, которые меняют system .NET / Visual Studio / workload installation outside repo, выполнять только после подтверждения SPEC и explicit sandbox approval.
  - Если требуется интерактивный Visual Studio Installer или Windows admin consent, остановиться и выдать operator runbook вместо обхода через repo changes.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/ranking.md`
  - `specs/2026-06-26-storm-android-ios-build-smoke-workload-setup.md`
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`
  - `src/Unlimotion.iOS/Unlimotion.iOS.csproj`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Восстановить локальный `.NET` workload set / `wasm-tools` state, который блокирует Android/iOS build smoke для `ST-0015 / AC-0042 / SC-0015-002`, и затем повторить Android/iOS build smoke без изменения репозитория.

Outcome contract:
- Success means: Android и/или iOS build smoke проходят, либо получают более точную host/environment классификацию после восстановленного workload set.
- Итоговый output: admin/environment repair evidence, build smoke evidence, Post-EXEC в этой SPEC, синхронизированные STORM artifacts/reports только если evidence изменился.
- Stop rules:
  - Остановиться, если repair требует интерактивный Visual Studio Installer / Windows admin UI, который Codex не может корректно завершить.
  - Остановиться, если repair предлагает менять repo files вместо local workload state.
  - Остановиться, если build после repair доходит до product/project errors; предложить отдельную delivery SPEC.
  - Остановиться, если network/feed/auth failure требует менять package sources или credentials.

## 2. Текущее состояние (AS-IS)

- `CV-0005 / AC-0042 / ST-0015` закрыт на уровне conservative platform policy:
  - `TS-0024` покрывает Android/browser/iOS project contracts.
  - `TS-0026` исполняет `SC-0015-002` через `SD-0001..SD-0004`.
  - Browser Release build smoke прошёл 2026-06-18.
- Android/iOS runtime release support не заявляется.
- Последняя approved environment/setup SPEC зафиксировала:
  - SDK `10.0.301`, workload version `10.0.300-manifests.6fc1bb7b`.
  - `dotnet workload list` показывает installed workloads including `android`, `ios`, `maccatalyst`, `maui-windows`, `wasm-tools`.
  - workload sets не установлены.
  - `dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj` попытался установить workload set `10.0.301.1` через `microsoft.net.workloads.10.0.300.msi.x64`, затем был отменён/заблокирован и откатился.
  - Android/iOS Debug builds падают `NETSDK1147` до compile stage с требованием workload restore for `wasm-tools`.
- Текущий worktree уже содержит незакоммиченный artifact-only sync предыдущей SPEC; эта SPEC не должна его пересоздавать или откатывать.

## 3. Проблема

Локальная .NET workload state противоречива: workloads listed as installed, но SDK workload set отсутствует, и build всё равно требует `wasm-tools` restore. Пока этот system-level blocker не восстановлен, STORM trace не может получить Android/iOS build smoke evidence.

## 4. Цели дизайна

- Разделение ответственности: system workload repair отдельно от repo/project changes.
- Тестируемость: каждый admin/environment step фиксируется точной командой и outcome.
- Консистентность: STORM artifacts обновляются только после фактического repair/build evidence.
- Безопасность: не менять repo configuration для обхода локальной установки.
- Обратная совместимость: product behavior, tests и BDD links остаются неизменными.

## 5. Non-Goals

- Не менять source code, tests, test annotations, `.feature` files, `.csproj`, manifests, signing, NuGet config, SDK config или workflows.
- Не запускать emulator/simulator и не заявлять runtime UX parity.
- Не публиковать release artifacts.
- Не чинить Browser build/runtime.
- Не трогать `CV-0007`.
- Не выполнять silent system repair без explicit approval и evidence.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- Codex:
  - собирает текущий SDK/workload snapshot;
  - выполняет только approved CLI repair/build commands;
  - останавливается на interactive/admin UI;
  - обновляет STORM artifacts только по фактическому evidence.
- Operator/user:
  - подтверждает SPEC;
  - при необходимости выполняет Visual Studio Installer / admin consent вручную.
- Repo artifacts:
  - фиксируют evidence и next step;
  - не меняют product/test behavior.

### 6.2 Детальный дизайн

Execution ladder after approval:
1. Reconfirm worktree status and preserve existing artifact-only changes.
2. Capture environment:
   - `dotnet --info`
   - `dotnet workload list`
   - `dotnet workload --version`
3. Try narrow non-destructive repair diagnostics first:
   - `dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj --verbosity normal`
   - If it again attempts MSI/admin install and is canceled/blocked, stop and produce operator runbook.
4. If restore succeeds, repeat:
   - `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug`
   - `dotnet build src\Unlimotion.iOS\Unlimotion.iOS.csproj -c Debug`
5. If CLI says a specific workload install is required, do not run `dotnet workload install` until this SPEC is explicitly confirmed and the command is reviewed in the approval prompt.
6. If build reaches repo/product errors, stop and create separate delivery SPEC.

Evidence rules:
- Build smoke is compile/package smoke only, not runtime release support.
- iOS Windows host blocker after workload repair is acceptable and must be classified separately from product failure.
- `NETSDK1147` after a failed MSI repair remains environment blocker.
- No STORM metric should claim new behavior coverage unless actual build smoke passes.

Visual planning artifact: Не применимо, UI не меняется.

UI test video evidence: Не применимо, UI flow не меняется.

Обработка ошибок:
- `MSI install canceled/blocked`: stop, record operator runbook.
- Network/feed error: stop, record environment blocker.
- Admin prompt required: stop, ask user to complete outside Codex or approve exact elevated command.
- Product compile error: stop, propose delivery SPEC.

Производительность: workload repair/build can be slow and may download packages; no product performance claim.

## 7. Бизнес-правила / Алгоритмы

| Result | Условие | Artifact claim |
| --- | --- | --- |
| `workload_repaired` | Restore/install completes and build proceeds past `NETSDK1147` | Environment blocker reduced; build smoke result determines claim |
| `operator_required` | Visual Studio Installer/admin UI/MSI consent required | Stop; provide runbook |
| `environment_blocked` | Feed/cache/workload state still blocks before compile | Gap remains |
| `host_blocked` | iOS/target host requirements block after workloads are repaired | Host limitation, not product failure |
| `requires_product_delivery_spec` | Repo code/project failure reached | Stop and create separate SPEC |

## 8. Точки интеграции и триггеры

- `src/Unlimotion.Android/Unlimotion.Android.csproj`
- `src/Unlimotion.iOS/Unlimotion.iOS.csproj`
- STORM trace: `ST-0015 -> AC-0042 -> SC-0015-002 -> TS-0024/TS-0026`
- No runtime trigger is introduced.

## 9. Изменения модели данных / состояния

- Product runtime state: Не применимо.
- Repository source state: не меняется, кроме artifact/report updates after evidence.
- Local machine state: may change system .NET workloads, workload sets, packs, SDK caches, Visual Studio workload components.
- Build outputs: generated `bin/obj`, not tracked.

## 10. Миграция / Rollout / Rollback

- Миграция user data: Не применимо.
- Rollout: local environment only; no product release.
- Rollback:
  - Artifact/report changes can be reverted by Git.
  - System workload changes are outside Git; rollback is via .NET/Visual Studio Installer.
- Safety: stop before any broad/interactive repair that cannot be audited as a CLI command.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
1. Current dirty worktree is recorded; existing artifact-only changes are preserved.
2. SDK/workload state is recorded before repair.
3. Repair attempt is either completed or stopped with exact admin/operator blocker.
4. Android build smoke is retried only after meaningful repair success, or classified with exact blocker.
5. iOS build smoke is retried only after meaningful repair success, or classified as host/environment blocker.
6. No production code, tests, test annotations, `.feature` wording, project config or workflows are changed.
7. If evidence changes, `storm.json` and reports are synchronized without runtime release overclaim.
8. `validate-artifacts.py`, `git diff --check`, and trailing whitespace scan pass for changed artifact files.

Какие tests добавить/изменить: не применимо, tests не меняются.

Commands after approval:

```powershell
git status --short --untracked-files=all
dotnet --info
dotnet workload list
dotnet workload --version
dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj --verbosity normal
dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug
dotnet build src\Unlimotion.iOS\Unlimotion.iOS.csproj -c Debug
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" docs\product specs\2026-06-27-storm-android-ios-workload-set-repair.md
```

Commands requiring separate explicit review during EXEC:

```powershell
dotnet workload install wasm-tools
dotnet workload install android ios wasm-tools
dotnet workload repair
```

Stop rules for loops:
- Do not retry workload restore/install more than once without new evidence.
- Do not edit repo files to bypass local workload state.
- Do not run Visual Studio Installer UI from Codex.
- Do not run elevated install commands without explicit sandbox approval and exact command review.

## 12. Риски и edge cases

- Repair may require admin privileges or interactive Visual Studio Installer.
- Network/feed access may fail or require credentials.
- Installed workloads may be from Visual Studio/MSI and not fully manageable by `dotnet workload` CLI.
- iOS may remain host-blocked on Windows even after workload repair.
- Android may require Android SDK/JDK components beyond .NET workloads.
- A passing build smoke still does not prove runtime UX parity.

## 13. План выполнения

1. Confirm status and preserve current dirty artifact-only scope.
2. Capture `.NET` SDK/workload state.
3. Attempt narrow workload restore with verbosity.
4. If restore requires interactive/admin system install, stop and produce operator runbook.
5. If restore succeeds, run Android Debug build smoke.
6. If meaningful, run iOS Debug build smoke and classify host/environment outcome.
7. Update `storm.json`, coverage/ranking/bdd-sync/bdd-lint/traceability/stories only if evidence changes.
8. Update this SPEC with Post-EXEC evidence.
9. Run artifact validator and hygiene checks.
10. Report whether next step is manual admin repair, product delivery SPEC, or return to BDD scenario coverage.

## 14. Открытые вопросы

Блокирующих вопросов нет для SPEC. Подтверждение SPEC означает согласие на narrow CLI restore/build diagnostics, но не автоматическое согласие на interactive Visual Studio Installer or broad `dotnet workload install/repair`.

## 15. Соответствие профилю

- Профиль: `storm-product-development` + `delivery-task` + `.NET validation`.
- Выполненные требования профиля:
  - `/storm:cover` continuation without `/storm:full-cycle` restart.
  - Existing artifacts preserved.
  - Product artifacts in Russian.
  - Acceptance criteria and Gherkin links preserved.
  - Code/tests/test annotations remain unchanged unless a later SPEC explicitly authorizes delivery changes.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-27-storm-android-ios-workload-set-repair.md` | Создать SPEC и Post-EXEC evidence | QUEST trace for environment/admin workload repair |
| `docs/product/storm.json` | Evidence/status sync only if changed | Canonical STORM state |
| `docs/product/reports/coverage.md` | Evidence/gaps sync only if changed | `/storm:cover` report |
| `docs/product/reports/ranking.md` | Next-step sync only if changed | Ranking follow-up |
| `docs/product/reports/bdd-sync.md` | Gap/evidence sync only if changed | BDD sync report |
| `docs/product/reports/bdd-lint.md` | Warning/evidence sync only if changed | BDD lint report |
| `docs/product/reports/traceability.md` | Trace gap sync only if changed | Traceability report |
| `docs/product/reports/stories.md` | Story gap sync only if changed | Story report |

Запрещено без новой SPEC: `src/**`, `tests/**`, `.github/**`, platform manifests, package/source configuration.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Android build smoke | `NETSDK1147` before compile after failed workload restore | Target: pass, or exact post-repair blocker |
| iOS build smoke | `NETSDK1147` before compile after failed workload restore | Target: pass, host blocker, or exact post-repair blocker |
| ST-0015 claim | Project-contract support + Browser build smoke | Add Android/iOS build smoke only if evidence supports it |
| Source code/tests | unchanged | unchanged |
| Local machine state | workload set inconsistent | repaired or explicitly operator-blocked |

## 18. Альтернативы и компромиссы

- Вариант A: Не трогать system workload state и перейти к следующему BDD scenario.
  - Плюсы: минимальный риск local environment mutation.
  - Минусы: Android/iOS build smoke gap остаётся environment-blocked.
- Вариант B: Narrow CLI restore/build diagnostics with stop on admin/interactive UI.
  - Плюсы: может закрыть blocker или дать точный operator runbook.
  - Минусы: может требовать elevated/system changes.
  - Выбран: это прямой следующий шаг после свежей `NETSDK1147` классификации.
- Вариант C: Сразу выполнить broad `dotnet workload install/repair`.
  - Плюсы: может быстрее восстановить workloads.
  - Минусы: system mutation шире, может конфликтовать с VS/MSI-managed workloads.
  - Не выбран без дополнительного explicit approval внутри EXEC.
- Вариант D: Менять repo config.
  - Плюсы: может обойти local SDK issue.
  - Минусы: смешивает environment blocker с product delivery.
  - Не выбран.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | CLI repair ladder, evidence rules and stop rules defined. |
| C. Безопасность изменений | 11-13 | PASS | Broad install/repair and interactive admin UI are explicitly gated. |
| D. Проверяемость | 14-16 | PASS | Commands, acceptance criteria and artifact sync conditions are concrete. |
| E. Готовность к автономной реализации | 17-19 | PASS | EXEC path clear after approval phrase; admin blocker handling explicit. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST route and Russian artifact rule reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to local .NET workload repair and build smoke evidence. |
| 2. Понимание текущего состояния | 5 | Uses previous SPEC evidence and current reports. |
| 3. Конкретность целевого дизайна | 5 | Repair ladder, commands and classification table are explicit. |
| 4. Безопасность | 5 | Stops on interactive/admin/system mutation without approval. |
| 5. Тестируемость | 5 | Validation commands and artifact hygiene checks listed. |
| 6. Готовность к автономной реализации | 5 | Clear EXEC steps after approval, with stop conditions. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: current dirty artifact-only worktree, previous workload setup SPEC, current coverage/bdd-sync/ranking reports, platform project paths, central STORM/QUEST route.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: SPEC addresses only the current Android/iOS `NETSDK1147` environment blocker.
  - Contract pass: no code/tests/test annotations/project config/workflow changes allowed.
  - Adversarial risk pass: broad workload install, interactive Visual Studio Installer, admin consent and network/feed failure are explicit stop points.
  - Re-review after fixes: не требуется.
  - Stop decision: wait for `Спеку подтверждаю`.
- Evidence inspected:
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/ranking.md`
  - `specs/2026-06-26-storm-android-ios-build-smoke-workload-setup.md`
- Depth checklist:
  - Scope drift / unrelated changes: PASS; existing dirty artifact-only sync is preserved and not overwritten.
  - Acceptance criteria: PASS.
  - Validation evidence: PASS.
  - Unsupported claims: PASS; build smoke separated from runtime release support.
  - Regression / edge case: PASS.
  - Comments/docs/changelog: PASS; only SPEC planned before approval.
  - Hidden contract change: PASS.
  - Manual-review challenge: reviewer should check exact CLI commands before allowing `dotnet workload install/repair`.
- No-findings justification: SPEC contains a narrow repair ladder and explicitly stops before unaudited system mutation.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | environment | Repair may require Visual Studio Installer/admin UI that Codex should not drive. | Stop and provide operator runbook if encountered. | accepted-risk |

- Fixed before continuing: no fixes required.
- Checks rerun: manual SPEC linter/rubric/review completed.
- Needs human: approval phrase.
- Residual risks / follow-ups: if repaired builds reach product errors, create separate delivery SPEC.

### Post-EXEC Review
- Статус: PASS with residual environment blocker.
- Approval: `Спеку подтверждаю` получено, EXEC выполнен в approved environment-admin scope.
- Scope observed:
  - Dirty artifact-only worktree preserved; no code/test/project/workflow files changed.
  - SDK/workload snapshot captured before repair.
  - `dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj --verbosity normal` launched with escalation and timed out after 184 seconds without captured stdout.
  - Subsequent `dotnet workload list` shows workload version `10.0.301.1`, so repair partially changed workload state.
  - Android Debug build smoke attempted and failed fast with `NETSDK1147`, required workload `android`.
  - iOS Debug build smoke attempted and passed in 00:00:29.82 with existing warnings.
  - `dotnet build-server shutdown` completed; lingering dotnet/msiexec environment processes were observed and not forcibly killed.
- Classification:
  - workload repair: `partial_workload_repair` / operator-risk because restore timed out but workload set changed.
  - iOS: `passed_build_smoke`; build smoke only, not runtime support.
  - Android: `environment_blocked` by `NETSDK1147` requiring workload `android` despite workload list showing android installed.
  - Product/project failure: not reached.
- Changed files:
  - `specs/2026-06-27-storm-android-ios-workload-set-repair.md`
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/traceability.md`
  - `docs/product/reports/stories.md`
- Not changed:
  - production code
  - tests
  - test annotations
  - `.feature` wording
  - Android/iOS project files, manifests, signing, workflows, package/source configuration
- Residual risk: Android workload state remains inconsistent after partial repair; resolving it likely requires Visual Studio Installer or an explicitly approved targeted `dotnet workload install android` command outside repo behavior changes.
- Validation:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 1 warning по intentional shared Given step text.
  - `git diff --check` -> passed.
  - `rg -n "[ \t]+$" docs\product specs\2026-06-26-storm-android-ios-build-smoke-workload-setup.md specs\2026-06-27-storm-android-ios-workload-set-repair.md` -> no matches (`rg` exit 1).
- Decision: iOS build-smoke evidence is recorded; Android remains environment blocker; do not change repo config/tests/code in this SPEC.

## Approval

Получено: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего шага после Android/iOS environment blocker | 0.9 | Нет | Создать environment-admin SPEC | Нет | Нет | Current reports name workload set repair as next step; repo changes are prohibited. | `docs/product/reports/coverage.md`, `docs/product/reports/bdd-sync.md`, `docs/product/reports/ranking.md` |
| SPEC | Подготовка workload repair SPEC | 0.88 | Approval | Остановиться до подтверждения | Да | Нет | Workload repair may mutate system .NET/VS state, so EXEC must be approval-gated. | `specs/2026-06-27-storm-android-ios-workload-set-repair.md` |
| EXEC | Approval received | 0.95 | Нет | Capture environment state | Нет | Да: user wrote `спеку подтверждаю` | SPEC moved to EXEC; narrow restore/build diagnostics allowed. | `specs/2026-06-27-storm-android-ios-workload-set-repair.md` |
| EXEC | SDK/workload snapshot | 0.9 | Нет | Run narrow Android workload restore | Нет | Нет | SDK `10.0.301`, workload sets absent before repair; workloads installed via VS/MSI. | local environment evidence |
| EXEC | Android workload restore | 0.72 | Restore final exit status lost due timeout; lingering processes observed | Run build smoke based on changed workload list | Нет | Нет | Command timed out after 184s, but workload list changed to `10.0.301.1`; no broad install command was run. | local environment evidence |
| EXEC | Android build smoke | 0.9 | Repaired Android workload state | Sync Android blocker | Нет | Нет | Build fails `NETSDK1147` before compile and requires workload `android`; product code not reached. | `docs/product/storm.json`, reports |
| EXEC | iOS build smoke | 0.95 | Нет | Sync iOS build evidence | Нет | Нет | iOS Debug build passed and produced `Unlimotion.iOS.dll`; this is build smoke only. | `docs/product/storm.json`, reports |
| EXEC | Artifact sync | 0.9 | Final validator results | Run STORM validator and hygiene checks | Нет | Нет | Artifacts updated with iOS passing evidence and Android residual environment blocker. | `docs/product/storm.json`, `docs/product/reports/*.md` |
| EXEC | Artifact validation | 0.94 | Нет | Report result | Нет | Нет | STORM validator OK with known shared-Given warning; diff/hygiene checks clean. | `docs/product/storm.json`, reports, this SPEC |
