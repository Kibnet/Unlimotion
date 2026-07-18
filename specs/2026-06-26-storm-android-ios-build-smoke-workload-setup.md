# STORM Environment Setup: Android/iOS build smoke для ST-0015

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST SPEC / STORM validation follow-up.
- Владелец: Codex + product owner approval gate.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка `storm-bootstrap`.
- Instruction stack: central `AGENTS.md` -> `routing-matrix.md` -> `model-behavior-baseline`, `quest-governance`, `quest-mode`, `testing-baseline`, `testing-dotnet`, `dotnet-desktop-client`, `storm-product-development`; local `AGENTS.override.md` applied after central stack.
- Ограничения:
  - До подтверждения SPEC менять только этот файл.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие STORM artifacts.
  - Не менять production code, tests, test annotations, platform manifests, package metadata, workflows или release pipeline в этой SPEC.
  - Не заявлять Android/iOS runtime release support без фактического build evidence.
  - Environment changes через `dotnet workload restore` / restore/build commands выполнять только после подтверждения SPEC и с явным sandbox approval, если потребуется.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/reports/bdd-sync.md`
  - `specs/2026-06-17-storm-cover-platform-shell-policy.md`
  - `specs/2026-06-18-storm-platform-runtime-validation.md`
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`
  - `src/Unlimotion.iOS/Unlimotion.iOS.csproj`

Если секция не применима, явно указано `Не применимо` и причина.

## 1. Overview / Цель

Вернуть Android/iOS build smoke evidence для `ST-0015 / AC-0042 / SC-0015-002`, где текущая STORM-трасса честно фиксирует только project-contract coverage и Browser Release build smoke, а Android/iOS build smoke заблокированы `NETSDK1147` / workload restore state.

Outcome contract:
- Success means: Android и/или iOS build smoke либо проходят с точным evidence, либо получают свежую классификацию environment blocker с точной командой, ошибкой и следующим action.
- Итоговый output: environment/setup report в этой SPEC, синхронизированные STORM artifacts/reports только если evidence изменился.
- Stop rules:
  - Остановиться, если требуется менять `.csproj`, Android/iOS source, manifests, signing, SDK paths, workflows или release pipeline.
  - Остановиться, если `dotnet workload restore` требует admin/system install, interactive installer, Visual Studio component changes или неавторизованный network/system mutation.
  - Остановиться, если build failure после restore указывает на product/project defect, а не environment/setup state; предложить отдельную delivery SPEC.

## 2. Текущее состояние (AS-IS)

- `CV-0005` закрыт как conservative platform policy:
  - `TS-0024` покрывает Android/browser/iOS project contracts;
  - `TS-0026` исполняет `SC-0015-002` через `SD-0001..SD-0004`;
  - Browser Release build smoke был получен 2026-06-18.
- Android/iOS runtime release support не заявляется.
- Текущие reports после full-suite stabilization говорят:
  - active `/storm:cover` behavior gaps нет;
  - full `Unlimotion.Test` проходит 563/563 вне sandbox;
  - следующий environment/product gap: Android/iOS build smoke по `NETSDK1147`.
- Предыдущая platform validation SPEC зафиксировала:
  - `dotnet workload list` показывал installed workloads including `android`, `ios`, `maccatalyst`, `maui-windows`, `wasm-tools`;
  - `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug` был заблокирован `NETSDK1147`, suggested `dotnet workload restore` for `wasm-tools`;
  - `dotnet build src/Unlimotion.iOS/Unlimotion.iOS.csproj -c Debug` был заблокирован `NETSDK1147`, suggested `dotnet workload restore` for `wasm-tools`.
- Проекты:
  - Android target: `net10.0-android`, RIDs `android-arm64;android-x64`, native Git assets.
  - iOS target: `net10.0-ios`, Avalonia iOS delegate.

## 3. Проблема

STORM artifacts не могут усилить `ST-0015 / AC-0042` beyond project-contract support для Android/iOS, пока локальная среда не дает хотя бы build smoke evidence или точную свежую классификацию setup blocker. Старый `NETSDK1147` evidence уже достаточно повторялся в прошлых задачах, но после full-suite gate restoration следующий процессный шаг — проверить, можно ли исправить именно workload/restore state без изменения продукта.

## 4. Цели дизайна

- Разделение ответственности: environment/setup команды отдельно от product/project changes.
- Тестируемость: каждая build smoke попытка фиксирует точную команду, host, SDK/workload state, outcome и artifacts.
- Консистентность: STORM artifacts обновляются только по фактическому evidence.
- Безопасность: `dotnet workload restore` не должен превращаться в silent system mutation без approval.
- Обратная совместимость: runtime behavior и project contracts остаются неизменными.

## 5. Non-Goals

- Не менять Android/iOS production code.
- Не менять `.csproj`, manifests, package IDs, signing, entitlements, app icons или native asset declarations.
- Не добавлять tests или test annotations.
- Не запускать emulator/simulator и не заявлять runtime UX parity.
- Не чинить Browser build/runtime; Browser build smoke уже имеет evidence.
- Не трогать `CV-0007` attachment decision.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `dotnet --info`, `dotnet workload list` -> snapshot SDK/workload state.
- `dotnet workload restore ...` -> attempt to repair workload restore state for Android/iOS project graph after approval.
- Android/iOS build smoke commands -> validate whether setup is now enough for compile/package smoke.
- `docs/product/storm.json` and reports -> record only changed evidence/status.
- This SPEC -> canonical QUEST audit trail for environment/setup actions.

### 6.2 Детальный дизайн

- Поток:
  1. Reconfirm clean worktree.
  2. Capture SDK/workload state.
  3. Run workload restore against the narrowest project targets first.
  4. Attempt build smoke for Android and iOS.
  5. Classify each result as `passed_build_smoke`, `environment_blocked`, `host_blocked`, or `requires_product_delivery_spec`.
  6. Sync STORM artifacts only when classification differs from current artifacts or when fresh evidence should replace stale evidence.
- Evidence rules:
  - Build smoke evidence is not runtime release support.
  - iOS on Windows may be host-blocked even after workload restore; this is not product failure.
  - `NETSDK1147` after workload restore remains environment blocker unless logs point to repo configuration.
- Visual planning artifact: Не применимо, UI не меняется.
- UI test video evidence: Не применимо, UI flow не меняется.
- Ошибки:
  - Network/feed failure -> environment blocker; do not edit package sources without separate approval.
  - Admin/interactive installer required -> stop and ask.
  - Product/project configuration failure -> stop and propose delivery SPEC.
- Производительность: commands may be slow because workload restore/build can download packages; no performance product claim is made.

## 7. Бизнес-правила / Алгоритмы

Platform evidence classification:

| Result | Условие | Artifact claim |
| --- | --- | --- |
| `passed_build_smoke` | Project build exits 0 and produces expected build output | Build smoke evidence only |
| `environment_blocked` | Missing workload/feed/cache/SDK/restore state blocks before project compile | Environment/setup gap remains |
| `host_blocked` | Host OS cannot build target without required external host/signing/simulator | Host limitation, not product failure |
| `requires_product_delivery_spec` | Build reaches repo code/project issue requiring file changes | Stop and create separate SPEC |

## 8. Точки интеграции и триггеры

- `src/Unlimotion.Android/Unlimotion.Android.csproj`
- `src/Unlimotion.iOS/Unlimotion.iOS.csproj`
- STORM trace: `ST-0015 -> AC-0042 -> SC-0015-002 -> TS-0024/TS-0026`
- No runtime trigger is introduced.

## 9. Изменения модели данных / состояния

- Product runtime state: Не применимо.
- Repository source state: не меняется, кроме possible artifact/report updates.
- Local machine state: workload restore may update local .NET workload packs/cache outside repository after approval.
- Build outputs under `bin/obj`: generated and not tracked.

## 10. Миграция / Rollout / Rollback

- Миграция: Не применимо для user data.
- Rollout: evidence-only; no product release artifact is published.
- Rollback:
  - Artifact/report changes can be reverted.
  - Local workload/cache changes are machine environment changes and are not reverted by Git.
- Safety: if environment change is too broad or requires elevated install, stop before mutation.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:
1. Clean worktree is confirmed before environment setup.
2. SDK/workload state is recorded.
3. Android build smoke is attempted after restore or classified with exact blocker.
4. iOS build smoke is attempted only if host/workload state makes it meaningful; otherwise classified as host/environment blocker.
5. No production code, tests, test annotations, workflows or platform manifests are changed.
6. If Android/iOS evidence changes, `storm.json` and reports are synchronized without overclaiming runtime support.
7. `validate-artifacts.py`, `git diff --check`, and trailing whitespace scan pass for changed tracked files.

Какие tests добавить/изменить: не применимо, tests не меняются.

Commands after approval:

```powershell
git status --short
dotnet --info
dotnet workload list
dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj
dotnet workload restore src\Unlimotion.iOS\Unlimotion.iOS.csproj
dotnet build src\Unlimotion.Android\Unlimotion.Android.csproj -c Debug
dotnet build src\Unlimotion.iOS\Unlimotion.iOS.csproj -c Debug
python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json
git diff --check
rg -n "[ \t]+$" docs\product specs\2026-06-26-storm-android-ios-build-smoke-workload-setup.md
```

Stop rules for validation loops:
- Do not run `dotnet workload install` unless `dotnet workload restore` explicitly cannot proceed and the user approves a narrower follow-up.
- Do not edit NuGet config, SDK manifests, package sources, `.csproj` or platform source files in this SPEC.
- Do not retry heavy restore/build loops more than twice without new evidence.

## 12. Риски и edge cases

- `dotnet workload restore` may require network or admin privileges.
- iOS build may be unsupported from Windows without Apple tooling or remote Mac.
- Android build may require Android SDK/JDK components beyond .NET workloads.
- Restore can update local caches and produce untracked `obj/bin` outputs.
- A successful build smoke still does not prove runtime UX parity.

## 13. План выполнения

1. Confirm clean worktree and current HEAD.
2. Record `.NET` and workload state.
3. Run narrow workload restore for Android and iOS projects.
4. Run Android Debug build smoke.
5. Run iOS Debug build smoke only as meaningful for current host; classify host blocker if needed.
6. Update `storm.json`, coverage/ranking/bdd-sync/bdd-lint/traceability/stories reports only if evidence changes.
7. Update this SPEC with Post-EXEC evidence.
8. Run artifact validator and hygiene checks.
9. Stop and report whether next step is product delivery SPEC, environment install action, or return to scenario-level BDD coverage.

## 14. Открытые вопросы

Блокирующих вопросов нет до EXEC. Подтверждение SPEC означает согласие попробовать `dotnet workload restore` / build smoke как environment setup действие, но не согласие менять source code или workflows.

## 15. Соответствие профилю

- Профиль: `storm-product-development` + `delivery-task` + `.NET validation`.
- Route: `/storm:cover` continuation; environment/setup validation after full-suite gate restored.
- QUEST gate required because commands may mutate local environment and artifacts.
- Gherkin/AC links preserved; Gherkin does not replace acceptance criteria.
- Product artifacts remain Russian; technical identifiers unchanged.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-06-26-storm-android-ios-build-smoke-workload-setup.md` | Создать SPEC и Post-EXEC evidence | QUEST trace for environment/setup |
| `docs/product/storm.json` | Evidence/status sync только если changed | Canonical STORM state |
| `docs/product/reports/coverage.md` | Evidence/gaps sync только если changed | `/storm:cover` report |
| `docs/product/reports/ranking.md` | Next-step sync только если changed | Ranking follow-up |
| `docs/product/reports/bdd-sync.md` | Gap/evidence sync только если changed | BDD sync report |
| `docs/product/reports/bdd-lint.md` | Warning/evidence sync only if changed | BDD lint report |
| `docs/product/reports/traceability.md` | Trace gap sync only if changed | Traceability report |
| `docs/product/reports/stories.md` | Story gap sync only if changed | Story report |

Запрещено без новой SPEC: `src/**`, `tests/**`, `.github/**`, platform manifests, package/source configuration.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Android build smoke | `NETSDK1147` / workload restore state blocker | Target: fresh pass or exact fresh blocker |
| iOS build smoke | `NETSDK1147` / workload restore state blocker | Target: fresh pass, host blocker, or exact fresh blocker |
| ST-0015 claim | Project-contract support + Browser build smoke | Target: add Android/iOS build smoke only if evidence supports it |
| Source code/tests | unchanged | unchanged |

## 18. Альтернативы и компромиссы

- Вариант A: Не трогать environment и перейти к следующему BDD scenario.
  - Плюсы: меньше риска локальных environment mutations.
  - Минусы: Android/iOS build smoke gap останется stale и не будет свежей классификации.
- Вариант B: Выполнить narrow workload restore/build smoke.
  - Плюсы: может закрыть Android/iOS build smoke или обновить blocker точным evidence.
  - Минусы: может требовать network/admin/system dependencies.
  - Выбран: это прямой следующий gap после восстановления full-suite gate.
- Вариант C: Сразу менять `.csproj`/SDK configuration.
  - Плюсы: может починить build, если причина в repo config.
  - Минусы: это product/project delivery scope, не environment setup.
  - Не выбран: сначала нужен fresh evidence.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, goals and non-goals explicit. |
| B. Качество дизайна | 6-10 | PASS | Environment/setup boundary and evidence classification described. |
| C. Безопасность изменений | 11-13 | PASS | Stop rules block code/tests/workflows/project changes. |
| D. Проверяемость | 14-16 | PASS | Commands and acceptance criteria are concrete. |
| E. Готовность к автономной реализации | 17-19 | PASS | Plan and file scope concrete; no blocking questions. |
| F. Соответствие профилю | 20 | PASS | STORM + QUEST route and Russian artifact rule reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to Android/iOS build smoke environment setup. |
| 2. Понимание текущего состояния | 5 | Uses previous platform validation evidence and current reports. |
| 3. Конкретность целевого дизайна | 5 | Commands, classifications and stop rules are explicit. |
| 4. Безопасность | 5 | Source changes prohibited; environment mutation approval-gated. |
| 5. Тестируемость | 5 | Validation commands and artifact sync checks listed. |
| 6. Готовность к автономной реализации | 5 | EXEC path clear after approval phrase. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: current clean worktree, HEAD `5fcb1a2`, current STORM reports, previous platform validation SPEC, platform project files and central STORM/QUEST route.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: SPEC addresses only the current `NETSDK1147` Android/iOS environment gap after full-suite restoration.
  - Contract pass: no code/tests/test annotations/workflow changes allowed.
  - Adversarial risk pass: admin/network/system mutation, iOS host limitation and overclaim risks are explicit.
  - Re-review after fixes: не требуется.
  - Stop decision: wait for `Спеку подтверждаю`.
- Evidence inspected:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `specs/2026-06-18-storm-platform-runtime-validation.md`
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`
  - `src/Unlimotion.iOS/Unlimotion.iOS.csproj`
- Depth checklist:
  - Scope drift / unrelated changes: PASS.
  - Acceptance criteria: PASS.
  - Validation evidence: PASS.
  - Unsupported claims: PASS, build smoke is separated from runtime release support.
  - Regression / edge case: PASS.
  - Comments/docs/changelog: PASS, no code comments/changelog planned.
  - Hidden contract change: PASS, source changes prohibited.
  - Manual-review challenge: reviewer should check that workload restore approval is explicit and that artifacts do not claim runtime support from build-only evidence.
- No-findings justification: SPEC isolates environment/setup from product delivery and preserves current STORM trace.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | environment | `dotnet workload restore` may require network/admin/system changes outside repository. | Execute only after SPEC approval and sandbox approval; stop if install becomes broad or interactive. | accepted-risk |

- Fixed before continuing: no fixes required.
- Checks rerun: manual SPEC linter/rubric/review completed.
- Needs human: approval phrase.
- Residual risks / follow-ups: if build reaches product/project errors, create separate delivery SPEC.

### Post-EXEC Review
- Статус: PASS как environment-blocked validation; product/source changes не выполнялись.
- Approval: `Спеку подтверждаю` получено, EXEC запущен в рамках approved environment/setup scope.
- Scope observed:
  - `dotnet --info` / `dotnet workload list` snapshot captured.
  - `dotnet workload restore src\Unlimotion.Android\Unlimotion.Android.csproj` attempted and stopped after MSI workload-set install cancellation/blocker.
  - iOS workload restore not retried after Android restore blocker to avoid repeating system install path without new approval.
  - Android/iOS build smoke attempted and both stopped at `NETSDK1147` before project compile.
- Classification:
  - Android: `environment_blocked` by workload restore state / workload-set install cancellation and `NETSDK1147`.
  - iOS: `environment_blocked` by same `NETSDK1147` before host/runtime validation.
  - Product/project failure: not reached.
- Changed files:
  - `specs/2026-06-26-storm-android-ios-build-smoke-workload-setup.md`
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
- Residual risk: local .NET workload-set state is inconsistent enough that installed `wasm-tools` is listed, but build still requires workload restore; resolving this likely needs admin/interactive Visual Studio/.NET workload-set repair outside repository.
- Validation:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` -> OK: 0 errors, 1 warning по intentional shared Given step text.
  - `git diff --check` -> passed.
  - `rg -n "[ \t]+$" docs\product specs\2026-06-26-storm-android-ios-build-smoke-workload-setup.md` -> no matches (`rg` exit 1).
- Decision: do not change repo config/tests/code in this SPEC; next action is environment/admin workload recovery or a separate delivery SPEC.

## Approval

Получено: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего шага после full-suite stabilization | 0.9 | Нет | Создать SPEC | Нет | Нет | Full-suite gate восстановлен; текущий оставшийся environment/product gap — Android/iOS build smoke по `NETSDK1147`. | `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md` |
| SPEC | Подготовка environment/setup SPEC | 0.88 | Approval | Остановиться до подтверждения | Да | Нет | Workload restore/build smoke может менять local environment, поэтому EXEC должен быть approval-gated. | `specs/2026-06-26-storm-android-ios-build-smoke-workload-setup.md` |
| EXEC | Approval received | 0.95 | Нет | Capture environment state | Нет | Да: user wrote `спеку подтверждаю` | SPEC moved to EXEC; environment/setup commands allowed, source changes still prohibited. | `specs/2026-06-26-storm-android-ios-build-smoke-workload-setup.md` |
| EXEC | SDK/workload snapshot | 0.9 | Нет | Attempt narrow workload restore | Нет | Нет | SDK `10.0.301`; workloads listed as installed, but workload sets absent. | local environment evidence |
| EXEC | Android workload restore | 0.85 | Admin/interactive MSI install cannot be completed in repo scope | Stop further workload install/restore escalation | Да для future admin install | Нет после blocker | Restore attempted workload set `10.0.301.1` via MSI, then was canceled/blocked and rolled back; stop rule applies. | local environment evidence |
| EXEC | Android/iOS build smoke | 0.9 | Не хватает repaired workload set / wasm-tools state | Classify environment blocker and sync artifacts | Нет | Нет | Both Debug builds fail `NETSDK1147` before project compile, so product/project defect is not reached. | `docs/product/storm.json`, reports |
| EXEC | Artifact sync | 0.9 | Final validator results | Run STORM validator and hygiene checks | Нет | Нет | STORM artifacts updated to record fresh environment blocker without source/test changes. | `docs/product/storm.json`, `docs/product/reports/*.md` |
| EXEC | Artifact validation | 0.94 | Нет | Report result | Нет | Нет | STORM validator OK with known shared-Given warning; diff/hygiene checks clean. | `docs/product/storm.json`, `docs/product/reports/*.md`, this SPEC |
