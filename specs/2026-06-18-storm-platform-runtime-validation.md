# STORM Platform Runtime Validation: ST-0015

## 0. Метаданные
- Тип: `storm-product-development` + `delivery-task/QUEST`.
- Цель: продолжение после закрытия `CV-0004`; active `/storm:cover` gaps отсутствуют, следующий необязательный claim — runtime/release evidence для `ST-0015`.
- Целевая ветка: `storm-bootstrap`.
- Instruction stack:
  - `C:\Users\Kibnet\.codex\agents\AGENTS.md`
  - `instructions/governance/routing-matrix.md`
  - `instructions/core/model-behavior-baseline.md`
  - `instructions/core/quest-governance.md`
  - `instructions/core/quest-mode.md`
  - `instructions/core/testing-baseline.md`
  - `instructions/contexts/testing-dotnet.md`
  - `instructions/profiles/storm-product-development.md`
  - `instructions/governance/spec-linter.md`
  - `instructions/governance/spec-rubric.md`
  - `instructions/governance/review-loops.md`
  - локальный `AGENTS.override.md`
- Ограничения:
  - До подтверждения SPEC не менять code/tests/workflows/STORM artifacts.
  - Не запускать `/storm:full-cycle` и не пересоздавать существующие artifacts.
  - Не менять Android/browser/iOS runtime behavior, manifests, startup flow, package metadata или release workflows без отдельной SPEC.
  - Не чинить platform workloads/SDK/restore environment как часть этой SPEC; environment blockers фиксировать как evidence.
  - Не заявлять runtime release support без фактического build/runtime evidence.
  - UI behavior не меняется; local UI-testing override не требует UI tests для этой SPEC.

## 1. Overview / Цель
Проверить, можно ли поднять `ST-0015 / AC-0042` с project-contract support до более сильного runtime/release evidence для Android/browser/iOS, не меняя продуктовое поведение.

Outcome contract:
- Если локальная среда готова, выполнить non-mutating build/runtime smoke для доступных platform shells и синхронизировать evidence.
- Если среда не готова, зафиксировать точные blockers в SPEC/STORM reports без изменения code/tests.
- Не превращать optional validation blocker в product implementation task.

## 2. Текущее состояние
- `ST-0015` уже покрыт:
  - desktop/update evidence через `TS-0011`, `TS-0015`;
  - non-desktop project-contract evidence через `TS-0024`.
- `SC-0015-002` явно говорит: Android/browser/iOS имеют project-contract coverage без runtime release claim.
- Предыдущая SPEC `2026-06-17-storm-cover-platform-shell-policy.md` зафиксировала:
  - Android optional build smoke был заблокирован `NETSDK1147` / workload restore state;
  - Browser optional build smoke был заблокирован отсутствующим `project.assets.json` under `--no-restore`;
  - эти blockers не чинились в рамках coverage task.
- Текущий ranking говорит: active `/storm:cover` behavior gaps нет; следующий шаг может быть platform runtime validation или executable Gherkin step definitions.

## 3. Проблема
В artifacts есть осознанное ограничение: Android/browser/iOS не заявлены как runtime release-supported surfaces. Чтобы изменить этот claim, нужны фактические validation evidence, а не только `.csproj` contract tests.

## 4. Non-Goals
- Не реализуем новые platform features.
- Не меняем manifests, app startup, icons, package IDs, signing, workflows, release notes или deployment pipeline.
- Не добавляем emulator/simulator automation, если оно требует настройки окружения.
- Не добавляем executable Gherkin runner.
- Не трогаем `CV-0007` attachment decision.

## 5. Предлагаемый план EXEC после approval
1. Reconfirm clean worktree and current STORM state.
2. Проверить установленные .NET workloads и restore state без изменения файлов репозитория:
   - `dotnet workload list`
   - targeted `dotnet restore` only if needed and safe for local packages/cache.
3. Попробовать platform build smoke в порядке роста риска:
   - Browser: `dotnet build src/Unlimotion.Browser/Unlimotion.Browser.csproj -c Release`
   - Android: `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug`
   - iOS: only inspect/build if host/workload support exists; on Windows likely record as unsupported host/environment.
4. Если build smoke успешен, зафиксировать точное evidence:
   - build command;
   - target framework;
   - output path if produced;
   - whether artifact is only build smoke or also runtime smoke.
5. Если build smoke заблокирован, зафиксировать точную ошибку и классифицировать blocker:
   - missing workload;
   - restore/package source;
   - platform SDK;
   - host OS unsupported;
   - project issue.
6. Синхронизировать только artifacts, которые отражают evidence:
   - `docs/product/storm.json`
   - `features/storm/st-0015-platform-shells.feature`
   - `docs/product/reports/*`
   - this SPEC Post-EXEC review
7. Run validators/hygiene:
   - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
   - `git diff --check`
   - trailing-space scan over changed artifact files.

## 6. Acceptance Criteria
1. No code/tests/workflows are changed unless a new SPEC is approved.
2. Platform validation commands and blockers are recorded with exact evidence.
3. Runtime/release support заявляется только для платформ с успешным evidence.
4. Existing `TS-0024` project-contract coverage remains intact.
5. STORM artifacts preserve the distinction between project-contract support and runtime/release support.
6. Если environment блокирует validation, итоговый report рекомендует отдельную environment/setup task, а не изменение product behavior.

## 7. Риски
- Platform workloads may be missing locally.
- Android build may require SDK/Java/AAPT/restore state not present on this machine.
- iOS build on Windows is likely unsupported without remote Mac/signing.
- Browser build may require restore assets or WebAssembly workload.
- Overclaim risk: successful compile is not the same as runtime UX parity.

## 8. Файлы, которые можно менять после approval
| Файл | Разрешенное изменение |
| --- | --- |
| `docs/product/storm.json` | Только evidence/status/metrics для ST-0015 platform validation |
| `features/storm/st-0015-platform-shells.feature` | Только status/tags wording if evidence changes claim |
| `docs/product/reports/*` | Только synced report updates |
| `specs/2026-06-18-storm-platform-runtime-validation.md` | Post-EXEC review and action journal |

Запрещено без новой SPEC: `src/Unlimotion.Android/*`, `src/Unlimotion.Browser/*`, `src/Unlimotion.iOS/*`, `.github/workflows/*`, test files, production code.

## 9. SPEC Quality Gate
### SPEC Linter Result

| Блок | Статус | Комментарий |
| --- | --- | --- |
| Полнота | PASS | Цель, scope, non-goals, validation plan и stop rules описаны явно. |
| Безопасность | PASS | Code/test/workflow changes blocked until separate approval. |
| Проверяемость | PASS | Commands and evidence rules listed. |
| STORM compliance | PASS | Использует trace `ST-0015/AC-0042/SC-0015-002` и сохраняет project-contract caveat. |

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Ясность цели | 5 | SPEC только валидирует platform runtime/release evidence. |
| Границы | 5 | Явно запрещает behavior/workflow/test changes. |
| Тестируемость | 5 | Commands и blocker classification конкретны. |
| Риск overclaim | 5 | Разделяет build smoke, runtime smoke и release support claims. |
| Готовность EXEC | 5 | Approval phrase безопасно переводит задачу в validation-only EXEC. |

Итог: 25 / 25.

### Post-SPEC Review
- Статус: PASS
- Scope reviewed:
  - `docs/product/reports/ranking.md`
  - `docs/product/storm.json` ST-0015 references
  - `features/storm/st-0015-platform-shells.feature`
  - `specs/2026-06-17-storm-cover-platform-shell-policy.md`
  - platform project files for Android/browser/iOS
- Decision: можно запрашивать approval.
- Findings:
  - Блокирующих findings нет.
  - Residual risk: локальная среда может не поддержать runtime/build smoke; эта SPEC трактует это как validation evidence, а не как implementation failure.

### Post-EXEC Review
- Статус: PASS
- Approval: получено `Спеку подтверждаю`.
- Scope reviewed:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/traceability.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/reports/stories.md`
  - `features/storm/st-0015-platform-shells.feature`
- Evidence:
  - `dotnet workload list` прошел; installed workloads include `android`, `ios`, `maccatalyst`, `maui-windows`, `wasm-tools`.
  - `dotnet --info` прошел; SDK `10.0.301`, Workload version `10.0.300-manifests.6fc1bb7b`, Host `11.0.0-preview.4.26230.115`.
  - `dotnet build src/Unlimotion.Browser/Unlimotion.Browser.csproj -c Release` прошел с существующими warnings; output `src/Unlimotion.Browser/bin/Release/net10.0-browser/Unlimotion.Browser.dll`.
  - `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug` заблокирован `NETSDK1147`; suggested `dotnet workload restore` for `wasm-tools`.
  - `dotnet build src/Unlimotion.iOS/Unlimotion.iOS.csproj -c Debug` заблокирован `NETSDK1147`; suggested `dotnet workload restore` for `wasm-tools`.
- Files changed:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/bdd-sync.md`
  - `docs/product/reports/bdd-lint.md`
  - `docs/product/reports/traceability.md`
  - `docs/product/reports/ranking.md`
  - `docs/product/reports/stories.md`
  - `specs/2026-06-18-storm-platform-runtime-validation.md`
- Not changed:
  - production code
  - tests
  - test annotations
  - platform manifests/workflows
  - `features/storm/st-0015-platform-shells.feature`
- Validation:
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json` => OK, 0 errors, 0 warnings.
  - `git diff --check` => passed.
  - trailing-space scan over changed artifact files => no matches.
- Decision:
  - `ST-0015 / AC-0042 / SC-0015-002` получает Browser Release build smoke evidence.
  - Android/iOS остаются environment-blocked до отдельной setup task.
  - Runtime/release support claims не расширялись.

## Approval
Получено: `Спеку подтверждаю`.

## 10. Журнал действий агента
| Фаза | Действие | Статус | Затронутые файлы |
| --- | --- | --- | --- |
| SPEC | Создан validation-only plan для ST-0015 platform runtime evidence | done | `specs/2026-06-18-storm-platform-runtime-validation.md` |
| EXEC | Reconfirmed worktree and central STORM route | done | no file changes |
| EXEC | Собран workload/.NET environment evidence | done | no file changes |
| EXEC | Выполнен Browser Release build smoke | done | no tracked file changes |
| EXEC | Выполнены Android/iOS build smoke attempts; зафиксирован `NETSDK1147` blocker | done | no tracked file changes |
| EXEC | Синхронизированы STORM artifacts и reports без code/test changes | done | `docs/product/storm.json`, `docs/product/reports/*`, this SPEC |
| EXEC | Feature spec reviewed; оставлен без изменений, потому что product wording и no-runtime claim остаются корректными | done | no feature file changes |
| EXEC | Запущены STORM validator и hygiene checks | done | no file changes |
