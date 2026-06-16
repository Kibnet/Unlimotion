# /storm:cover CV-0005: platform shell policy и contract coverage для ST-0015

## 0. Метаданные
- Тип (профиль): delivery-task / QUEST, `storm-product-development` + `testing-dotnet` + `.NET desktop/client platform contracts`.
- Владелец: Codex, под подтверждение пользователя.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: текущая рабочая ветка `Unlimotion`.
- Ограничения:
  - До подтверждения этой SPEC разрешено менять только этот файл.
  - После подтверждения не менять пользовательское UI-поведение, runtime startup flow и platform manifests без отдельной SPEC.
  - Не менять test annotations.
  - Не заявлять Android/browser/iOS как production-ready runtime surfaces без build/runtime evidence.
  - Продуктовые артефакты и отчёты писать на русском; technical identifiers оставлять как есть.
- Связанные ссылки:
  - `docs/product/storm.json`
  - `docs/product/reports/coverage.md`
  - `docs/product/reports/ranking.md`
  - `features/storm/st-0015-platform-shells.feature`
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`
  - `src/Unlimotion.Browser/Unlimotion.Browser.csproj`
  - `src/Unlimotion.iOS/Unlimotion.iOS.csproj`
  - `src/Unlimotion.Test/Unlimotion.Test.csproj`

Если секция не применима, это указано явно внутри соответствующего раздела.

## 1. Overview / Цель
Закрыть следующий ranked `/storm:cover` gap `CV-0005` для `ST-0015/AC-0042`: зафиксировать conservative platform support policy и добавить deterministic contract coverage для non-desktop platform shell projects.

Outcome contract:
- Success means:
  - `AC-0042` больше не остаётся `partial` из-за отсутствия policy/evidence.
  - Android, browser и iOS shell projects проверены как platform project contracts: target framework, shared UI reference, Avalonia platform package, expected startup builder/service hooks and Android native libgit2 assets.
  - `storm.json`, `features/storm/st-0015-platform-shells.feature` и STORM reports синхронизированы с фактическим evidence.
  - Статус не завышает runtime maturity: desktop остаётся release-supported, non-desktop shells получают policy `project-contract supported / runtime maturity needs explicit release evidence`.
- Итоговый артефакт / output:
  - Новый TUnit test suite для platform shell project contracts.
  - Обновлённые STORM artifacts/reports с `Scenario -> Test` link.
  - Проверки build/test/validator.
- Stop rules:
  - Если для закрытия `AC-0042` требуется менять Android/browser/iOS runtime behavior, manifests, startup flow, packaging workflow или install/update behavior, остановиться и предложить отдельную SPEC.
  - Если platform workload отсутствует и реальный Android/browser/iOS build не запускается локально, не чинить окружение в этой задаче; использовать deterministic project contract tests как primary evidence и явно записать build blocker.
  - Если tests показывают фактический broken project contract, исправлять только минимальный contract drift, уже описанный в этой SPEC; более крупные platform changes вынести отдельно.

## 2. Текущее состояние (AS-IS)
- `ST-0015` в `docs/product/storm.json` имеет статус `partial`.
- `AC-0042`: "Android, browser и iOS projects существуют и подключают общую UI-модель, но зрелость требует продуктового подтверждения."
- `coverage.md` помечает `CV-0005` как `proposed` с prerequisite: "Определить, какие non-desktop shells считаются release-supported."
- `ranking.md` ставит `CV-0005` следующим ranked coverage item без прямого product implementation.
- `SC-0015-002` уже существует в `features/storm/st-0015-platform-shells.feature`, но текст сценария слишком общий и связан с `TS-0015`, который в основном покрывает desktop/headless startup/update behavior.
- Platform projects:
  - `src/Unlimotion.Android/Unlimotion.Android.csproj` таргетит `net10.0-android`, ссылается на `..\Unlimotion\Unlimotion.csproj`, подключает `Avalonia.Android`, `Xamarin.AndroidX.Core.SplashScreen`, `LibGit2Sharp.NativeBinaries` и Android native libraries для `android-arm64`/`android-x64`.
  - `src/Unlimotion.Browser/Unlimotion.Browser.csproj` таргетит `net10.0-browser`, ссылается на shared `Unlimotion` и подключает `Avalonia.Browser`.
  - `src/Unlimotion.iOS/Unlimotion.iOS.csproj` таргетит `net10.0-ios`, ссылается на shared `Unlimotion` и подключает `Avalonia.iOS`.
- Startup code:
  - Browser `Program.BuildAvaloniaApp()` конфигурирует `TaskStorageFactory`, `AppModelMapping`, `Dialogs`, `NotificationManagerWrapper`, `MainControl.DialogsInstance` и возвращает `AppBuilder.Configure<App>()`.
  - Android `MainActivity` конфигурирует app services, storage path, update service, folder picker and Git safe directory/cert bundle.
  - iOS `AppDelegate` использует `AvaloniaAppDelegate<App>` и `UseReactiveUI(App.ConfigureReactiveUIBuilder)`.
- Скрытые зависимости:
  - Локальная машина может не иметь Android/iOS/browser workloads.
  - Полный test suite может быть нестабилен на unrelated Avalonia.Headless teardown, что уже фиксировалось в прежних SPEC.
  - Валидация platform contracts через XML `.csproj` не доказывает runtime UX, install/update behavior или store readiness.

## 3. Проблема
`AC-0042` смешивает два разных утверждения: project existence/shared UI contract и release/runtime maturity. Из-за этого `/storm:cover` не может честно закрыть gap: существующие project references есть, но продуктовая policy по уровню поддержки Android/browser/iOS не оформлена и не покрыта отдельными deterministic tests.

## 4. Цели дизайна
- Разделение ответственности: project contract tests проверяют структуру platform shells; STORM artifacts фиксируют продуктовую policy; runtime/platform release work остаётся за отдельными SPEC.
- Повторное использование: тесты используют стандартный XML parser и repo-relative paths без запуска real Android/browser/iOS runtime.
- Тестируемость: каждая platform surface получает конкретный observable contract.
- Консистентность: `storm.json`, `.feature` и reports должны говорить одно и то же о maturity.
- Обратная совместимость: production/runtime code не меняется, существующие shell projects сохраняются.

## 5. Non-Goals (чего НЕ делаем)
- Не реализуем новый Android/browser/iOS startup behavior.
- Не меняем `.csproj` platform projects, если tests подтверждают текущие contracts.
- Не меняем Android manifest, permissions, native assets packaging, browser app bundle или iOS delegate.
- Не добавляем Gherkin runner или executable step definitions.
- Не меняем test annotations.
- Не заявляем full runtime support для non-desktop shells без фактического build/runtime evidence.
- Не закрываем `CV-0004` Telegram Git timers и `CV-0007` attachment workflow.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` -> deterministic TUnit checks для Android/browser/iOS `.csproj` и platform startup contract files.
- `docs/product/storm.json` -> canonical status, policy, tests, scenario links and coverage metrics.
- `features/storm/st-0015-platform-shells.feature` -> уточнённый `SC-0015-002` как declarative business behavior example, без low-level procedural mechanics.
- `docs/product/reports/coverage.md`, `bdd-sync.md`, `bdd-lint.md`, `traceability.md`, `ranking.md`, `stories.md` -> derived STORM reports после sync.

### 6.2 Детальный дизайн
- Потоки данных:
  - Test reads repo files using paths relative to test assembly / repository root.
  - Test parses `.csproj` через `System.Xml.Linq.XDocument`.
  - Test asserts required properties/items without invoking platform SDKs.
- Контракты / API:
  - Android project contract:
    - `TargetFramework = net10.0-android`
    - `RuntimeIdentifiers` include `android-arm64` and `android-x64`
    - `ProjectReference` points to `..\Unlimotion\Unlimotion.csproj`
    - `PackageReference` includes `Avalonia.Android`, `Xamarin.AndroidX.Core.SplashScreen`, `LibGit2Sharp.NativeBinaries`
    - `LibGit2Sharp.NativeBinaries` has `GeneratePathProperty=true`
    - `AndroidNativeLibrary` includes required `libcrypto`, `libssl`, `libssh2` paths for both Android RIDs
    - `MainActivity.cs` contains `AvaloniaMainActivity` and app service configuration hooks.
  - Browser project contract:
    - `TargetFramework = net10.0-browser`
    - `ProjectReference` points to `..\Unlimotion\Unlimotion.csproj`
    - `PackageReference` includes `Avalonia.Browser`
    - `Program.cs` exposes `BuildAvaloniaApp`, calls `UseReactiveUI(App.ConfigureReactiveUIBuilder)` and `StartBrowserAppAsync`.
  - iOS project contract:
    - `TargetFramework = net10.0-ios`
    - `ProjectReference` points to `..\Unlimotion\Unlimotion.csproj`
    - `PackageReference` includes `Avalonia.iOS`
    - `AppDelegate.cs` derives from `AvaloniaAppDelegate<App>` and calls `UseReactiveUI(App.ConfigureReactiveUIBuilder)`.
- Output contract / evidence rules:
  - New test id proposed: `TS-0024` with name `PlatformShellProjectContractTests`.
  - `SC-0015-002` should link to `TS-0024`.
  - `AC-0042` coverage can move from `partial` to `critical` or `full` only for the conservative policy statement, not for runtime release maturity.
  - If platform builds are not run or blocked, artifact text must explicitly say `project-contract coverage`, not `runtime verified`.
- Visual planning artifact для UI-facing изменений: `Не применимо`, because planned changes do not modify UI layout, visual state, navigation flow or user-facing copy.
- UI test video evidence для UI automation задач: `Не применимо`, no UI automation behavior changes; validation is static project contract plus optional build smoke.
- Границы сохранения поведения:
  - No production code changes unless a minimal project contract drift is found and is already covered by the listed contracts.
  - No behavior/output text changes.
- Обработка ошибок:
  - Missing file -> test failure with path in assertion message.
  - XML parse failure -> test failure, because project contract is unreadable.
  - Platform workload build failure -> report as environment/build blocker unless failure is an obvious repo contract issue within scope.
- Производительность:
  - XML/file contract tests are fast and deterministic.

## 7. Бизнес-правила / Алгоритмы
Platform support policy for this SPEC:

| Surface | Policy status | What can be claimed after this task | What cannot be claimed |
| --- | --- | --- | --- |
| Desktop | release-supported | Desktop shell build/update/startup remains primary supported release surface. | Не применимо. |
| Android | project-contract supported | Android project exists, references shared UI and carries required native Git assets. | Store readiness, install/update success, runtime UX parity. |
| Browser | project-contract supported | Browser project exists, references shared UI and builds Avalonia browser app entrypoint contract. | Hosted production deployment or browser runtime parity. |
| iOS | project-contract supported | iOS project exists, references shared UI and uses Avalonia iOS delegate contract. | iOS release, signing, App Store readiness, runtime parity. |

Rule for coverage wording:
- `AC-0042` is closed only if its text is interpreted as "non-desktop shells are present and intentionally tracked as project-contract supported surfaces".
- Any stronger release claim must stay out of this SPEC.

## 8. Точки интеграции и триггеры
- Tests run through `src/Unlimotion.Test/Unlimotion.Test.csproj`.
- `/storm:bdd-sync` updates `storm.json` and reports after tests pass.
- `/storm:bdd-lint` verifies tags, scenario status, linked tests and behavior coverage.
- No runtime integration trigger is introduced.

## 9. Изменения модели данных / состояния
- Persisted product state changes:
  - `docs/product/storm.json`: update STORM artifact data, links and coverage metrics.
  - `features/storm/st-0015-platform-shells.feature`: refine `SC-0015-002`.
  - `docs/product/reports/*`: refresh reports derived from `storm.json`.
- Application runtime data: no changes.
- Database/storage: no changes.

## 10. Миграция / Rollout / Rollback
- Миграция: не требуется, runtime data не меняется.
- Rollout: merge test/artifact update after validation.
- Rollback: revert this SPEC's changes to test file and product artifacts; no user data migration.
- Обратная совместимость: project files and behavior unchanged unless a narrow contract drift fix is necessary and approved by this SPEC.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  1. `PlatformShellProjectContractTests` verifies Android/browser/iOS shared UI project contracts.
  2. Android test verifies required `LibGit2Sharp.NativeBinaries` and Android native library assets for `android-arm64` and `android-x64`.
  3. Browser/iOS tests verify platform startup hooks reference shared `App`/ReactiveUI builder contract.
  4. `SC-0015-002`, `AC-0042`, `ST-0015`, and `TS-0024` are linked in `storm.json`.
  5. STORM reports state the conservative policy and do not claim runtime parity/release support for non-desktop shells.
  6. `validate-artifacts.py` returns `0 errors`, `0 warnings`.
  7. No test annotations, UI behavior or production runtime code are changed unless an in-scope project contract drift forces a minimal fix.
- Какие тесты добавить/изменить:
  - Add `src/Unlimotion.Test/PlatformShellProjectContractTests.cs`.
  - Do not modify existing tests unless compile integration requires a narrow helper adjustment.
- Characterization tests / contract checks:
  - `AndroidProject_IncludesSharedUiReferenceAndNativeGitAssets`
  - `BrowserProject_UsesSharedUiAndBrowserAppStartupContract`
  - `IosProject_UsesSharedUiAndAvaloniaDelegateContract`
- Visual acceptance для UI-facing изменений: `Не применимо`, UI не меняется.
- UI video evidence для UI-facing фич/багфиксов: `Не применимо`, no UI flow or visual behavior change.
- Базовые замеры до/после для performance tradeoff: `Не применимо`.
- Команды для проверки:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-restore`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/PlatformShellProjectContractTests/*" --output Detailed`
  - `dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release --no-build --no-restore -- --treenode-filter "/*/*/SingleViewStartupUiTests/*" --output Detailed`
  - `python C:\Users\Kibnet\.codex\agents\scripts\storm\validate-artifacts.py docs\product\storm.json`
  - `git diff --check`
  - Optional, if workloads are available:
    - `dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Debug --no-restore`
    - `dotnet build src/Unlimotion.Browser/Unlimotion.Browser.csproj -c Release --no-restore`
- Stop rules для test/retrieval/tool/validation loops:
  - Do not use VSTest `--filter`; this repo uses TUnit/MTP `--treenode-filter`.
  - If full test suite is required but fails on unrelated known Avalonia.Headless teardown, rerun failed tests isolated and report residual risk.
  - If optional platform builds fail due missing workload/signing/SDK, do not repair environment inside this task.

## 12. Риски и edge cases
- Risk: XML project contract tests may pass while real platform builds fail.
  - Mitigation: artifacts explicitly claim project-contract coverage only; optional builds attempted when environment supports them.
- Risk: AC coverage is over-promoted.
  - Mitigation: update AC wording/notes to conservative policy and keep runtime maturity limitation visible.
- Risk: adding tests under `Unlimotion.Test` accidentally requires platform SDK packages.
  - Mitigation: tests use file/XML inspection only.
- Risk: Browser startup contract text check is brittle.
  - Mitigation: assert a small set of stable startup symbols, not exact formatting.
- Risk: future maintainers confuse project-contract supported with release-supported.
  - Mitigation: add explicit policy in STORM artifacts and reports.

## 13. План выполнения
1. Add `PlatformShellProjectContractTests` with XML helper and three focused TUnit tests.
2. Run test project build and targeted `PlatformShellProjectContractTests`.
3. Run targeted `SingleViewStartupUiTests` as regression around shared single-view startup evidence already linked to `ST-0015`.
4. Update `docs/product/storm.json`:
   - Add `TS-0024`.
   - Link `TS-0024` to `ST-0015`, `AC-0042`, `SC-0015-002`.
   - Update `AC-0042` coverage note/level according to conservative policy.
   - Update behavior coverage metrics and coverage backlog status for `CV-0005`.
5. Update `features/storm/st-0015-platform-shells.feature` so `SC-0015-002` describes project-contract supported non-desktop shells and links `@test:TS-0024`.
6. Refresh `coverage.md`, `bdd-sync.md`, `bdd-lint.md`, `traceability.md`, `ranking.md`, `stories.md`.
7. Run STORM validator and whitespace checks.
8. Perform post-EXEC review and update this SPEC journal/result.

## 14. Открытые вопросы
Блокирующих вопросов нет: эта SPEC выбирает conservative policy внутри approval contract. Если пользователь подтверждает SPEC, он подтверждает, что Android/browser/iOS в текущем `/storm:cover` закрываются как project-contract supported surfaces, а не как runtime release-supported platforms.

## 15. Соответствие профилю
- Профиль: `storm-product-development`, route `delivery-task` because tests and product artifacts will change after approval.
- Выполненные требования профиля:
  - Central stack used: `AGENTS.md` -> `routing-matrix.md` -> `storm-product-development`.
  - `QUEST` gate used before test/code/artifact mutations outside this SPEC.
  - BDD layer stays between AC and tests; Gherkin does not replace acceptance criteria.
  - Scenario/test/code trace will be updated after implementation.
  - Product artifacts remain in Russian.
  - Coverage is requirements/behavior coverage, not line coverage.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Test/PlatformShellProjectContractTests.cs` | Добавить TUnit project contract tests | Закрыть `CV-0005/AC-0042` deterministic evidence |
| `docs/product/storm.json` | Добавить `TS-0024`, обновить `AC-0042`, `SC-0015-002`, metrics/reports source data | Синхронизировать STORM traceability |
| `features/storm/st-0015-platform-shells.feature` | Уточнить `SC-0015-002`, tags and test link | Согласовать Gherkin scenario с conservative policy |
| `docs/product/reports/coverage.md` | Обновить coverage report | Показать закрытие `CV-0005` и remaining gaps |
| `docs/product/reports/bdd-sync.md` | Обновить sync report | Зафиксировать Scenario -> Test link |
| `docs/product/reports/bdd-lint.md` | Обновить lint report | Подтвердить BDD quality state |
| `docs/product/reports/traceability.md` | Обновить traceability | Показать `ST-0015 -> AC-0042 -> SC-0015-002 -> TS-0024 -> code/files` |
| `docs/product/reports/ranking.md` | Обновить ranked backlog status | Перевести `CV-0005` из proposed в covered/closed-by-policy-contract |
| `docs/product/reports/stories.md` | Обновить story summary | Зафиксировать новый статус `ST-0015/AC-0042` |
| `specs/2026-06-17-storm-cover-platform-shell-policy.md` | Обновлять журнал и review | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `CV-0005` | `proposed`, ждёт platform support policy | Covered by conservative platform project contract policy and `TS-0024` |
| `AC-0042` | `partial`, linked only to broad `TS-0015` | Linked to focused `TS-0024`, maturity caveat explicit |
| `SC-0015-002` | Generic scenario, automated link to broad startup tests | Specific project-contract scenario with `@test:TS-0024` |
| Product claim | Non-desktop shells exist, maturity unclear | Desktop release-supported; Android/browser/iOS project-contract supported only |

## 18. Альтернативы и компромиссы
- Вариант: требовать реальные Android/browser/iOS builds before closing `CV-0005`.
  - Плюсы: сильнее confidence.
  - Минусы: зависит от local workloads, signing, SDK and machine setup; может превратить coverage task в environment setup.
  - Почему не выбран: `AC-0042` сформулирован вокруг project existence/shared UI model and maturity confirmation; deterministic project contracts are the right minimum evidence.
- Вариант: оставить `CV-0005` открытым до product owner runtime decision.
  - Плюсы: минимальный риск завышения claims.
  - Минусы: `/storm:cover` не двигается, хотя conservative policy can be encoded now.
  - Почему не выбран: approval of this SPEC can serve as explicit conservative policy decision.
- Вариант: сразу реализовать platform runtime smoke tests.
  - Плюсы: лучше executable confidence.
  - Минусы: требует platform workloads, browser harness/emulator/simulator and likely new infrastructure.
  - Почему не выбран: это отдельная `/storm:bdd-implement` или platform validation SPEC.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн-цели и Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, contracts, policy, data impact and rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Есть AC, stop rules, phased plan and no runtime/UI behavior scope. |
| D. Проверяемость | 14-16 | PASS | Проверочные команды и таблица файлов заданы; TUnit filter корректный. |
| E. Готовность к автономной реализации | 17-19 | PASS | Было/стало, alternatives and review result included. |
| F. Соответствие профилю | 20 | PASS | STORM + QUEST route and Russian artifact rule reflected. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope limited to `CV-0005/AC-0042`, explicit Non-Goals. |
| 2. Понимание текущего состояния | 5 | AS-IS names actual projects, reports, scenario and missing policy. |
| 3. Конкретность целевого дизайна | 5 | Test contracts and artifact sync steps are concrete. |
| 4. Безопасность (миграция, откат) | 5 | No data/runtime migration; rollback is revert of test/artifacts. |
| 5. Тестируемость | 5 | Targeted tests, build, validator and optional build smoke specified. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; approval encodes conservative policy choice. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-17-storm-cover-platform-shell-policy.md`, central stack (`AGENTS.md`, `routing-matrix.md`, `quest-governance.md`, `quest-mode.md`, `storm-product-development.md`, `testing-baseline.md`, `testing-dotnet.md`), local `AGENTS.override.md`, `docs/product/storm.json`, `coverage.md`, `ranking.md`, `features/storm/st-0015-platform-shells.feature`, platform `.csproj` files and startup files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: inspected actual STORM gap, ranked backlog, ST-0015/AC-0042 scenario/test links and platform project contracts.
  - Contract pass: SPEC respects QUEST SPEC-only gate, no test annotation changes, no UI behavior changes, STORM route is delivery-task after approval.
  - Adversarial risk pass: checked overclaim risk, missing workloads, brittle text checks, runtime maturity ambiguity and UI testing override applicability.
  - Re-review after fixes / Fix and re-review: no blocking fixes required after review; conservative policy wording already prevents release-support overclaim.
  - Stop decision: PASS.
- Evidence inspected:
  - `docs/product/reports/coverage.md` lines for `CV-0005`, `AC-0042`, open question and recommended next step.
  - `docs/product/reports/ranking.md` ranked backlog.
  - `src/Unlimotion.Android/Unlimotion.Android.csproj`, `src/Unlimotion.Browser/Unlimotion.Browser.csproj`, `src/Unlimotion.iOS/Unlimotion.iOS.csproj`.
  - `src/Unlimotion.Browser/Program.cs`, `src/Unlimotion.Android/MainActivity.cs`, `src/Unlimotion.iOS/AppDelegate.cs`, `src/Unlimotion.iOS/Main.cs`.
- Depth checklist:
  - Scope drift / unrelated changes: PASS, planned changes limited to tests and STORM artifacts for `CV-0005`.
  - Acceptance criteria: PASS, measurable contract checks and artifact sync AC defined.
  - Validation evidence: PASS, commands include targeted TUnit, build, STORM validator and diff check.
  - Unsupported claims: PASS, runtime release support explicitly not claimed.
  - Regression / edge case: PASS, missing workloads and unrelated full-suite instability handled as stop/report rules.
  - Comments/docs/changelog: PASS, no source comments/changelog planned.
  - Hidden contract change: PASS, no runtime behavior or UI changes planned.
  - Manual-review challenge: reviewer should verify that tests do not silently assert runtime support, that `AC-0042` is not over-promoted beyond project-contract coverage, and that optional platform build blockers are reported rather than fixed by scope creep.
- No-findings justification: SPEC isolates the policy decision, uses deterministic evidence, preserves current runtime behavior, and makes remaining platform maturity limits explicit.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Optional platform builds may be unavailable locally because workloads/signing/SDKs are machine-dependent. | Treat contract tests as primary evidence and record optional build blocker if it occurs. | accepted-risk |

- Fixed before continuing: no fixes required.
- Checks rerun: manual SPEC linter/rubric/review completed in this section.
- Needs human: approval phrase only.
- Residual risks / follow-ups: true Android/browser/iOS runtime smoke coverage remains future `/storm:bdd-implement` or platform validation work.

### Post-EXEC Review
- Статус: Не выполнен до EXEC
- Scope reviewed: Не применимо до подтверждения.
- Decision: Не применимо до EXEC.
- Review passes:
  - Scope/Evidence pass: Не применимо до EXEC.
  - Contract pass: Не применимо до EXEC.
  - Adversarial risk pass: Не применимо до EXEC.
  - Re-review after fixes / Fix and re-review: Не применимо до EXEC.
  - Stop decision: Не применимо до EXEC.
- Evidence inspected: Не применимо до EXEC.
- Depth checklist:
  - Scope drift / unrelated changes: Не применимо до EXEC.
  - Acceptance criteria: Не применимо до EXEC.
  - Validation evidence: Не применимо до EXEC.
  - Unsupported claims: Не применимо до EXEC.
  - Regression / edge case: Не применимо до EXEC.
  - Comments/docs/changelog: Не применимо до EXEC.
  - Hidden contract change: Не применимо до EXEC.
  - Manual-review challenge: Не применимо до EXEC.
- No-findings justification: Не применимо до EXEC.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | pre-exec | EXEC ещё не выполнялся. | Дождаться подтверждения SPEC. | follow-up |

- Fixed before final report: Не применимо.
- Checks rerun: Не применимо.
- Validation evidence: Не применимо.
- Unrelated changes: Не применимо.
- Needs human: требуется фраза `Спеку подтверждаю`.
- Residual risks / follow-ups: runtime platform smoke coverage remains separate future work.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Выбор следующего `/storm:cover` gap | 0.88 | Подтверждение conservative platform policy | Подготовить SPEC и запросить подтверждение | Да | Да, нужно подтверждение пользователя | `CV-0005` является следующим ranked gap без runtime implementation, но требует policy; SPEC фиксирует project-contract supported policy | `docs/product/storm.json`, `docs/product/reports/coverage.md`, `docs/product/reports/ranking.md` |
| SPEC | Создание SPEC и quality gate | 0.90 | Подтверждение SPEC | Остановиться до фразы `Спеку подтверждаю` | Да | Да, ожидается решение пользователя | QUEST gate запрещает менять tests/code/artifacts до подтверждения; SPEC задаёт точный scope, tests, BDD sync and stop rules | `specs/2026-06-17-storm-cover-platform-shell-policy.md` |
