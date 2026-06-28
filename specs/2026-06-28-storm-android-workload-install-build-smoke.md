# STORM Environment Admin: точечная установка Android workload и повтор build smoke

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
  - Не заявлять Android runtime release support без фактического runtime evidence.
  - Выполнить только точечную команду `dotnet workload install android` после подтверждения SPEC и explicit sandbox approval.
  - Не запускать `dotnet workload repair`, `dotnet workload install android ios wasm-tools` или Visual Studio Installer UI без новой SPEC/approval.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/ranking.md`
  - `specs/2026-06-27-storm-android-ios-workload-set-repair.md`
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Устранить оставшийся Android build smoke blocker для `ST-0015 / AC-0042 / SC-0015-002`: после partial workload-set repair iOS Debug build smoke прошёл, но Android Debug build падает `NETSDK1147` и требует workload `android` despite `dotnet workload list` showing Android installed.

Outcome contract:
- Success means: Android Debug build smoke проходит или получает более точную post-install классификацию.
- Итоговый output: Android workload install evidence, Android build smoke evidence, Post-EXEC в этой SPEC, STORM artifact/report sync only if evidence changes.
- Stop rules:
  - Остановиться, если `dotnet workload install android` требует Visual Studio Installer UI, admin prompt, unavailable feed, credentials или broad repair.
  - Остановиться, если install предлагает менять repo files or SDK/NuGet config.
  - Остановиться, если Android build после install доходит до product/project errors; предложить отдельную delivery SPEC.

## 2. Текущее состояние (AS-IS)

- Workload set state после предыдущей SPEC:
  - `dotnet workload list` -> workload version `10.0.301.1`.
  - `android 36.1.69/10.0.100` listed with source `SDK 10.0.300, VS ...`.
  - `ios 26.5.10284/10.0.100` listed and iOS Debug build smoke passed.
- Android build result:
  - `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug`
  - fails before compile with `NETSDK1147`.
  - required workload: `android`.
- Repo state:
  - existing STORM artifact-only changes are dirty and must be preserved.
  - code/tests/project files are unchanged.

## 3. Проблема

`dotnet workload list` claims Android is installed, but SDK `10.0.301` build resolution still does not see workload `android`. The next narrow environment step is a targeted Android workload install, not repo configuration change.

## 4. Цели дизайна

- Разделение ответственности: targeted Android workload install отдельно от product/project delivery.
- Тестируемость: install and build smoke evidence are recorded separately.
- Консистентность: STORM artifacts only change after factual evidence.
- Безопасность: no broad workload repair, no Visual Studio Installer automation, no repo config changes.
- Обратная совместимость: product behavior and tests remain unchanged.

## 5. Non-Goals

- Не менять source code, tests, test annotations, `.feature`, `.csproj`, manifests, NuGet/SDK config или workflows.
- Не запускать Android emulator.
- Не заявлять Android runtime UX parity or release support.
- Не запускать full-suite.
- Не трогать iOS beyond preserving already-passed evidence.
- Не трогать `CV-0007`.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `dotnet workload install android` -> targeted system workload repair for Android.
- `dotnet workload list` -> verify whether install changed state.
- `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug` -> Android build smoke.
- `docs/product/storm.json` and reports -> sync only factual evidence/status.
- This SPEC -> QUEST audit trail.

### 6.2 Детальный дизайн

Execution after approval:
1. Reconfirm dirty worktree scope and current workload list.
2. Run `dotnet workload install android` with escalation.
3. If command asks for interactive/admin/Visual Studio Installer UI or broad repair, stop and record operator blocker.
4. Re-run `dotnet workload list`.
5. Run Android Debug build smoke.
6. Classify:
   - `passed_build_smoke`
   - `environment_blocked`
   - `operator_required`
   - `requires_product_delivery_spec`
7. Sync STORM artifacts only if evidence/status changes.

Evidence rules:
- Android build smoke is not runtime/release support.
- Passing Android build does not imply emulator launch or mobile UX parity.
- Product/project errors after install require separate delivery SPEC.

Visual planning artifact: Не применимо, UI не меняется.

UI test video evidence: Не применимо, UI flow не меняется.

## 7. Бизнес-правила / Алгоритмы

| Result | Условие | Artifact claim |
| --- | --- | --- |
| `passed_build_smoke` | Android Debug build exits 0 | Android build smoke evidence only |
| `environment_blocked` | Workload/feed/cache still blocks before compile | Android environment gap remains |
| `operator_required` | Admin/UI/VS Installer is required | Stop and provide runbook |
| `requires_product_delivery_spec` | Build reaches repo code/project failure | Stop and create delivery SPEC |

## 8. Точки интеграции и триггеры

- `src/Unlimotion.Android/Unlimotion.Android.csproj`
- STORM trace: `ST-0015 -> AC-0042 -> SC-0015-002 -> TS-0024/TS-0026`
- No runtime trigger is introduced.

## 9. Изменения модели данных / состояния

- Product runtime state: Не применимо.
- Repository source state: не меняется, кроме artifact/report updates after evidence.
- Local machine state: may change system .NET Android workload packs/caches.
- Build outputs: generated `bin/obj`, not tracked.

## 10. Миграция / Rollout / Rollback

- Миграция user data: Не применимо.
- Rollout: local environment only.
- Rollback:
  - Artifact/report changes can be reverted by Git.
  - System workload changes roll back through .NET/Visual Studio tooling, not Git.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
1. Current dirty artifact-only scope is recorded.
2. Current workload state is recorded before install.
3. `dotnet workload install android` is attempted once after approval.
4. Android Debug build smoke is attempted after install, unless install is operator-blocked.
5. No production code, tests, test annotations, `.feature`, project config or workflows are changed.
6. If Android evidence changes, STORM artifacts/reports are synchronized without runtime overclaim.
7. `validate-artifacts.py`, `git diff --check`, and trailing whitespace scan pass for changed artifact files.

Какие tests добавить/изменить: не применимо, tests не меняются.

Commands after approval:

```powershell
git status --short --untracked-files=all
dotnet workload list
dotnet workload install android
dotnet workload list
dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" docs\product specs\2026-06-28-storm-android-workload-install-build-smoke.md
```

Stop rules:
- Do not run broad `dotnet workload repair`.
- Do not run Visual Studio Installer UI.
- Do not retry install more than once without new evidence.
- Do not edit repo files to bypass local workload state.

## 12. Риски и edge cases

- Install may require admin privileges, interactive UI or network/feed access.
- SDK/VS mixed workload state may remain inconsistent.
- Android SDK/JDK dependencies may become the next blocker after workload resolution.
- Passing build smoke still does not prove runtime support.

## 13. План выполнения

1. Confirm dirty scope and workload state.
2. Run targeted `dotnet workload install android`.
3. If operator/admin UI is required, stop.
4. Re-run workload list.
5. Run Android Debug build smoke.
6. Sync artifacts and this SPEC if evidence changes.
7. Run artifact validator and hygiene checks.
8. Report next step.

## 14. Открытые вопросы

Блокирующих вопросов нет для SPEC. Подтверждение SPEC означает согласие на exact command `dotnet workload install android`, но не на broader repair/install commands.

## 15. Соответствие профилю

- Профиль: `storm-product-development` + `delivery-task` + `.NET validation`.
- Выполненные требования профиля:
  - `/storm:cover` continuation without restart.
  - Existing artifacts preserved.
  - Product artifacts in Russian.
  - Code/tests/test annotations remain unchanged unless a later SPEC explicitly authorizes delivery changes.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-28-storm-android-workload-install-build-smoke.md` | Создать SPEC и Post-EXEC evidence | QUEST trace for targeted Android workload install |
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
| Android build smoke | `NETSDK1147`, required workload `android` | Target: pass or exact post-install blocker |
| iOS build smoke | passed | preserved |
| Source code/tests | unchanged | unchanged |
| Local machine state | Android workload listed but unresolved by build | targeted install attempted |

## 18. Альтернативы и компромиссы

- Вариант A: Visual Studio Installer manual repair.
  - Плюсы: aligns with VS/MSI-managed workloads.
  - Минусы: Codex cannot safely drive interactive installer.
- Вариант B: Targeted `dotnet workload install android`.
  - Плюсы: exact command tied to `NETSDK1147` output.
  - Минусы: may still conflict with VS/MSI state or require admin/network.
  - Выбран: narrowest CLI action after current evidence.
- Вариант C: Broad `dotnet workload repair`.
  - Плюсы: may fix mixed state.
  - Минусы: too broad for current approval.
  - Не выбран.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Exact command, evidence rules and stop rules defined. |
| C. Безопасность изменений | 11-13 | PASS | Broad repair/UI/project edits prohibited. |
| D. Проверяемость | 14-16 | PASS | Commands and AC are concrete. |
| E. Готовность к автономной реализации | 17-19 | PASS | EXEC path clear after approval phrase. |
| F. Соответствие профилю | 20 | PASS | STORM/QUEST route and Russian artifact rule reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to targeted Android workload install and build smoke. |
| 2. Понимание текущего состояния | 5 | Uses latest iOS pass and Android `NETSDK1147` evidence. |
| 3. Конкретность целевого дизайна | 5 | Exact command and classifications are explicit. |
| 4. Безопасность | 5 | Broad/system/UI actions are stopped. |
| 5. Тестируемость | 5 | Build smoke and artifact validation commands listed. |
| 6. Готовность к автономной реализации | 5 | Clear EXEC path after approval. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: current dirty artifact-only worktree, latest coverage report, workload list, process snapshot, previous workload repair SPEC.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: targets only Android `NETSDK1147` missing workload blocker.
  - Contract pass: no code/tests/test annotations/project config/workflow changes.
  - Adversarial risk pass: broad repair and Visual Studio Installer UI are explicitly prohibited.
  - Re-review after fixes: не требуется.
  - Stop decision: wait for `Спеку подтверждаю`.
- Evidence inspected:
  - `docs/product/reports/coverage.md`
  - `dotnet workload list`
  - `Get-Process dotnet,msiexec`
- Depth checklist:
  - Scope drift / unrelated changes: PASS.
  - Acceptance criteria: PASS.
  - Validation evidence: PASS.
  - Unsupported claims: PASS.
  - Regression / edge case: PASS.
  - Comments/docs/changelog: PASS.
  - Hidden contract change: PASS.
  - Manual-review challenge: reviewer should verify exact command remains `dotnet workload install android`.
- No-findings justification: SPEC uses the narrowest command matching the observed Android build error.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | environment | Targeted install can still require admin/network or conflict with VS/MSI-managed workloads. | Stop and report exact blocker if encountered. | accepted-risk |

- Fixed before continuing: no fixes required.
- Checks rerun: manual SPEC linter/rubric/review completed.
- Needs human: approval phrase.
- Residual risks / follow-ups: if Android build reaches product/project error, create separate delivery SPEC.

### Post-EXEC Review
- Статус: PASS; Android build-smoke objective выполнен, artifact validation and hygiene checks passed with one known BDD lint warning.
- Approval received: `Спеку подтверждаю`.
- Scope reviewed before EXEC: dirty artifact-only STORM reports/specs, workload list, current process snapshot.
- Executed command: `dotnet workload install android`.
  - Первый escalation attempt не был выполнен из-за approval review timeout.
  - Повтор exact command завершился exit 0: изменений workload не найдено, workload `android` уже установлен.
- Workload state after install: `dotnet workload list` unchanged, workload version `10.0.301.1`, Android/iOS/wasm-tools listed.
- Android build smoke:
  - Первый canonical build после install выполнил restore и вернул exit 1 без captured final compiler/project error; `NETSDK1147` больше не был подтвержден как final blocker на этом шаге.
  - `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug --no-restore -v minimal` прошел и produced `src\Unlimotion.Android\bin\Debug\net10.0-android\Unlimotion.Android.dll`; warnings include `NU1608`, `CA1416`, `XA0141`, `XA4301`.
  - Повтор canonical `dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug -v minimal` прошел за `00:00:08.66`, warnings 4, errors 0.
- Build server cleanup: `dotnet build-server shutdown` завершился для MSBuild/compiler servers.
- Repo impact: production code, tests, test annotations, `.feature`, `.csproj`, manifests, NuGet/SDK config and workflows не менялись.
- Classification: `passed_build_smoke`; runtime launch, emulator/device validation and release support не заявляются.
- Artifact validation:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 1 warning.
  - Known warning: duplicate shared Given step text across `SD-0009`, `SD-0013`, `SD-0022`; это intentional reuse, не блокер.
  - `git diff --check` -> passed.
  - Trailing whitespace scan over `docs\product` and current STORM specs -> no matches.
  - `Get-Process dotnet,msiexec` -> no lingering processes found.

## Approval

Получено: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего шага после Android workload blocker | 0.9 | Нет | Создать targeted install SPEC | Нет | Нет | Android build explicitly requires workload `android`; broad repair remains out of scope. | `docs/product/reports/coverage.md`, local workload state |
| SPEC | Подготовка targeted Android workload install SPEC | 0.88 | Approval | Остановиться до подтверждения | Да | Нет | `dotnet workload install android` mutates system workload state, so EXEC must be approval-gated. | `specs/2026-06-28-storm-android-workload-install-build-smoke.md` |
| EXEC | Подтверждение SPEC и targeted Android workload install | 0.9 | Нет | Проверить build smoke | Нет | Да | User approved SPEC; exact targeted command completed as no-op because Android workload was already installed. | local workload state |
| EXEC | Android Debug build smoke | 0.88 | Нет | Синхронизировать STORM artifacts | Нет | Нет | No-restore build and canonical rerun passed; first post-install build was ambiguous but no longer reproduced. | `src/Unlimotion.Android/Unlimotion.Android.csproj`, build output |
| EXEC | STORM artifact/report sync | 0.86 | Validation checks | Запустить artifact validator and diff hygiene | Нет | Нет | Build-smoke evidence changed from environment-blocked to passed_build_smoke without code/test changes. | `docs/product/storm.json`, `docs/product/reports/*`, this SPEC |
| EXEC | Artifact validation and hygiene checks | 0.9 | Нет | Завершить отчет | Нет | Нет | STORM validator passed with one known warning; diff and trailing whitespace checks passed; no lingering dotnet/msiexec process found. | `docs/product/storm.json`, `docs/product/reports/coverage.md`, this SPEC |
