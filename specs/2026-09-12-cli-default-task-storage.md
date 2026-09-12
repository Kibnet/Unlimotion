# CLI Unlimotion: каталог задач по активной desktop-настройке

## 0. Метаданные
- Тип (профиль): `delivery-task`; .NET desktop client + config/state behavior.
- Владелец: Codex; код и интеграция остаются у основного агента.
- Масштаб: medium; меняется публично наблюдаемое поведение CLI и чтение локальной конфигурации, но без миграции данных.
- Целевое семейство / behavior baseline: `GPT-6 Astra`; задача не меняет model/prompt behavior.
- Поверхность: Codex.
- Effective runtime: текущая Codex-сессия; на контракт CLI не влияет.
- Eval baseline / evidence: Не применимо — задача не про модель; evidence составляют TUnit-тесты и CLI contract checks.
- Целевой релиз / ветка: после approval создать отдельный worktree на свежем `origin/main` и ветку `feat/cli-default-task-storage`; текущий checkout `main` отстаёт от `origin/main` на 11 коммитов.
- Ограничения: до approval изменяется только этот файл. Не читать/не выводить `Login`, `Password`, URL, токены или Git-настройки из пользовательского `Settings.json`; не записывать настройки и не менять каталог задач для read-команд.
- Связанные ссылки: `src/Unlimotion.Cli/Program.cs`, `src/Unlimotion.Cli/README.md`, `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs`, `src/Unlimotion/Services/TaskSourceSettingsAdapter.cs`.

## 1. Overview / Цель
Убрать обязательность `--tasks` для CLI Unlimotion. Если флаг отсутствует, CLI должен определить каталог активного локального task space так же, как установленное desktop-приложение: по compatibility-проекции `TaskStorage` в стандартном `Settings.json`.

Outcome contract:

- Success means: `unlimotion-cli unlocked --format json` без `--tasks` работает с папкой задач, указанной в активной локальной desktop-настройке; явный `--tasks` неизменно имеет приоритет.
- Итоговый артефакт / output: обновлённые CLI, документация и regression tests в новом worktree.
- Stop rules: не реализовывать до точной фразы «Спеку подтверждаю»; при неясности источника настроек или необходимости поддерживать server source остановиться и запросить решение, а не обращаться к учётным данным/сети.

## 2. Текущее состояние (AS-IS)
- `Program.Main` после разбора аргументов немедленно отклоняет пустой `CliOptions.TasksPath` с `Missing required --tasks <path> option.`
- В release desktop-приложение хранит стандартный конфиг в `%USERPROFILE%\Documents\Unlimotion\Settings.json` (`Environment.SpecialFolder.Personal`, `Program.cs` desktop).
- Desktop поддерживает несколько task spaces. `TaskSourceSettingsAdapter.SyncLegacy(...)` поддерживает `TaskStorage` как compatibility-проекцию активного source; для file source это его `Path`, для server source — `IsServerMode=true`.
- CLI уже использует `FileTaskStorage` и предназначен для file task directory; у него нет зависимостей и полномочий для server storage.
- `UnlimotionCliIntegrationTests` запускает упакованный CLI в дочернем процессе и уже покрывает статусы, критерии, ошибки и сохранность файлов, но не выбор default directory из desktop settings.

## 3. Проблема
Агент или пользователь, работающий с установленным Unlimotion, вынужден вручную искать и повторять путь активного каталога задач в каждом CLI-вызове, хотя desktop уже хранит это значение в своей standard settings compatibility-проекции.

## 4. Цели дизайна
- Сохранить `--tasks` как явный и приоритетный override.
- Повторить именно active local task-space путь desktop-приложения через его `TaskStorage` compatibility projection, а не дублировать хрупкий парсер каталога `TaskSources`.
- Читать минимальный набор несекретных полей конфигурации.
- Сделать ошибки конфигурации различимыми, стабильными в JSON и безопасными для автоматизации.
- Добавить детерминированные regression tests без чтения реального пользовательского профиля во время тестов.

## 5. Non-Goals (чего НЕ делаем)
- Не добавляем новый CLI-флаг для пути к settings и не меняем формат существующих команд, кроме того что `--tasks` становится необязательным.
- Не поддерживаем active server task space, сетевую аутентификацию, URL, backup/Git sync или переключение task spaces.
- Не создаём, не исправляем, не мигрируем и не сохраняем `Settings.json`.
- Не меняем задачу пользователя, данные в task directory, формат task files или алгоритмы availability.
- Не меняем desktop UI, UI automation selectors или visual flow; visual planning artifact и UI video evidence не применимы.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `src/Unlimotion.Cli/TaskDirectoryResolver.cs` (новый) | Вычислить стандартный путь `Documents\Unlimotion\Settings.json`, прочитать только `TaskStorage.Path` и `TaskStorage.IsServerMode`, вернуть file-directory path либо typed resolution error. Тестируемый overload принимает путь settings явно. |
| `src/Unlimotion.Cli/Program.cs` | После parse и до `Directory.Exists` выбрать explicit `--tasks` либо resolver; сохранить общий pipeline `FileTaskStorage` для всех команд. |
| `src/Unlimotion.Cli/README.md` | Показать `--tasks` как optional override, описать default source и невозможность server source. |
| `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs` | Закрепить precedence `--tasks`, successful resolution и negative configuration cases. |

### 6.2 Детальный дизайн
1. После `CliOptions.Parse` и help path `Program.Main` вызывает единый resolver. Если `options.TasksPath` непустой, resolver возвращает его без попытки открыть desktop settings.
2. Если `--tasks` отсутствует, resolver получает `%USERPROFILE%\Documents\Unlimotion\Settings.json`, читает JSON read-only через `System.Text.Json` и извлекает только:
   - `TaskStorage.Path` — непустой строковый путь к file task directory;
   - `TaskStorage.IsServerMode` — JSON boolean или стандартная строка `True`/`False`; по умолчанию `false` только если поле отсутствует.
3. CLI использует `TaskStorage.Path` как authoritative compatibility-projection активного task space. Это согласуется с desktop, который при activate/switch сохраняет projection через `TaskSourceSettingsAdapter.SyncLegacy`; не требуется интерпретация `TaskSources` slots и их migration journal.
4. Если config отсутствует, JSON некорректен, `TaskStorage`/`Path` отсутствует или пуст, либо `IsServerMode=true`, resolver выдаёт `CliException` с exit code `1` и стабильным kind: соответственно `settingsNotFound`, `settingsInvalid`, `settingsPathMissing` или `settingsUnsupported`. Текст объясняет, что следует указать `--tasks <path>`; не включает содержимое settings.
5. После успешного resolution применяется прежняя проверка `Directory.Exists`; отсутствующий каталог остаётся `operationFailed`/exit `1`, но сообщение указывает resolved path. Затем все commands, включая write commands, используют тот же уже выбранный path и имеющийся directory lock/atomic storage behavior.
6. Public CLI usage и README показывают `[--tasks <path>]`. JSON error envelope сохраняет существующую форму `{ success: false, error: { kind, message } }`.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Default local source | Запускает `unlimotion-cli unlocked --format json` без `--tasks` | CLI читает `TaskStorage.Path` стандартного desktop settings и выводит результат для этой папки | automated resolver/CLI test with temporary settings fixture | AC-1 |
| Explicit override | Передаёт `--tasks D:\\OtherTasks` при отсутствующих/некорректных settings | CLI работает с `D:\\OtherTasks`, не читая settings | automated regression test | AC-2 |
| Unsupported desktop source | Активный source projected as `IsServerMode=true` | exit `1`, stable JSON kind `settingsUnsupported`, подсказка про `--tasks`; сеть и credentials не используются | automated negative test | AC-3 |
| Broken/missing settings | Settings отсутствует, повреждён или не содержит path | exit `1`, stable configuration error; task directory не открывается | automated negative tests | AC-4 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| `--tasks` present | Any CLI command | Use supplied path | Missing directory preserves existing error | Highest priority, no settings read |
| `--tasks` absent, local projection valid | Any CLI command | Read projected `TaskStorage.Path`, then use existing storage pipeline | Concurrent desktop config write may produce unreadable JSON | Return `settingsInvalid`; do not retry/mutate |
| `--tasks` absent, server projection | Any CLI command | Refuse before storage creation | No network fallback | CLI stays file-only |
| `--tasks` absent, config invalid/missing | Any CLI command | Refuse before storage creation | No default guessed path | User can supply explicit `--tasks` |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Source of active local path | agent | `TaskStorage.Path` compatibility projection, not direct `TaskSources` parsing | 0.93 | Projection may be stale only if desktop failed before its own persistence; direct slot parsing is less compatible and wider scope | Нет |
| Settings location | agent | `%USERPROFILE%\Documents\Unlimotion\Settings.json`, matching release desktop `Program.cs` | 0.98 | A portable/debug app with `--config` is intentionally out of scope; it can use `--tasks` | Нет |
| Empty/missing path fallback | agent | Fail safely; do not guess/create `Tasks` | 0.90 | A first-run user must give `--tasks`; avoids reading/creating an unintended directory | Нет |
| Server mode | agent | Refuse with typed error and explicit override hint | 0.99 | Server task source cannot be faithfully handled by `FileTaskStorage` | Нет |
| Test seam | agent | Test resolver with an explicit temporary settings-file path; keep public CLI surface free of test-only settings arguments | 0.90 | Requires a small testable internal seam or friend-access arrangement | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Desktop config location | release `Unlimotion.Desktop/Program.cs` | CLI derives the same standard `Settings.json` path only when `--tasks` absent | No config schema migration | Fixture and resolver tests |
| Active source projection | `TaskSourceSettingsAdapter.SyncLegacy` -> `TaskStorage` | CLI reads `Path` and `IsServerMode` only | Supports existing projection; ignores secrets and `TaskSources` internal slots | File/server fixture tests |
| CLI options | `CliOptions` and `PrintUsage` | `--tasks` optional but still allowed for every current command | Existing explicit invocations behave identically | Override integration test and help/README checks |
| Error protocol | `ErrorOutput` / `CliException` | Add typed configuration failures within existing envelope | No output-schema break | JSON negative tests |

## 7. Бизнес-правила / Алгоритмы
- `EffectiveTasksPath = explicit --tasks`, если значение задано и не состоит из whitespace.
- Иначе `EffectiveTasksPath = TaskStorage.Path` из standard settings только при `IsServerMode != true` и непустом path.
- Resolver не применяет relative path normalization, не создаёт директории и не изменяет settings; действующая проверка `Directory.Exists(EffectiveTasksPath)` определяет возможность продолжения.
- `--tasks` не зависит от settings даже когда они отсутствуют, повреждены или обозначают server source.
- Конфигурационные ошибки имеют exit code `1`; syntax/option errors остаются `2`.

## 8. Точки интеграции и триггеры
- Единственная интеграционная точка — `Program.Main` между parse/help и существующей проверкой каталога.
- Все read/write command branches получают уже разрешённый `CliOptions.TasksPath` (или локальную effective path variable); command-specific logic не дублирует resolver.

## 9. Изменения модели данных / состояния
- Новых persisted model fields, task-file properties и desktop setting keys нет.
- CLI читает существующие `TaskStorage.Path`/`IsServerMode` read-only.
- Resolver state ephemeral; credentials, URL и токены не десериализуются и не логируются.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется: explicit `--tasks` продолжает работать без изменений.
- Rollout: после merge/package update пользователи могут постепенно убрать `--tasks` в local file source workflows.
- Rollback: вернуть CLI package/commit к предыдущей версии; settings и task files не изменяются этой feature, поэтому data rollback отсутствует.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:

- AC-1: каждый существующий CLI command может работать без `--tasks`, когда standard settings содержит valid local `TaskStorage.Path`; selected directory передаётся в existing storage pipeline.
- AC-2: explicit `--tasks` имеет приоритет и не требует доступного/валидного `Settings.json`.
- AC-3: projected server source не провоцирует network/auth access и возвращает JSON envelope kind `settingsUnsupported`, exit `1`.
- AC-4: missing, malformed и pathless desktop settings возвращают стабильные configuration errors, exit `1`, до открытия task directory.
- AC-5: `--format json` сохраняет current error envelope; help и README правильно показывают optional `--tasks` и default behavior.
- AC-6: targeted CLI tests, affected Release build и полный `Unlimotion.Test` проходят; unrelated current worktree changes не смешиваются с delivery worktree.

Проверки после approval (до long full suite сначала preflight SDK/restore и состояние процесса; suites запускаются последовательно):

```powershell
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src/Unlimotion.Cli/Unlimotion.Cli.csproj -c Release
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
git diff --check
```

Stop rules: при timeout не повторять тот же runner blindly; сначала собрать progress/root-cause evidence. Green targeted test не заменяет full suite. UI test/video evidence не применимы: изменение не затрагивает UI, navigation, layout или UI-facing state.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | Resolver fixture: valid local settings resolves expected path; CLI process invokes a read command with resolved path | Inspect targeted TUnit output | TUnit console/TRX | N/A |
| AC-2 | CLI integration test: explicit temp directory succeeds while injected fixture settings is missing/invalid | Inspect no resolver attempt if observable | TUnit output | N/A |
| AC-3 | Resolver/CLI test with `IsServerMode=true`, asserts exit 1 and `settingsUnsupported` JSON | Confirm no credential/network code paths are referenced in diff | TUnit + diff review | N/A |
| AC-4 | Separate missing, invalid JSON and empty-path fixtures assert stable kinds/exit 1 | Inspect error messages do not include config contents | TUnit output | N/A |
| AC-5 | Help and JSON-envelope regression tests; README diff review | `dotnet ... --help` smoke after build | CLI stdout | N/A |
| AC-6 | Targeted class, CLI Release build, full `Unlimotion.Test` serial | `git diff --check`, post-EXEC review | commands above | N/A |

## 12. Риски и edge cases
- Desktop could be writing `Settings.json` concurrently; malformed/partial snapshot fails safely, with no retries or writes.
- Active source may be a server; treating URL as a file path would be unsafe, so resolution refuses it.
- The compatibility projection can be stale only outside normal desktop persistence; direct `TaskSources` parsing would duplicate migration/recovery behavior and increase drift risk, so it is intentionally excluded.
- The new default affects write commands too. Existing lock/atomic write behavior stays intact, but user should use explicit `--tasks` when targeting a non-active task space.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «CLI выбрал не тот task space» | В desktop несколько spaces | Use active source compatibility projection persisted by desktop, and retain explicit override | mitigated |
| «CLI прочитал пароль или пошёл в сеть» | Settings can hold credentials and server configuration | Parse only `Path`/`IsServerMode`; server source refuses before storage/network | mitigated |
| «После обновления автоматизация с --tasks сломалась» | Existing scripts use explicit path | Explicit option retains absolute priority and regression coverage | mitigated |
| «Первый запуск создал не ту папку» | Default folder heuristics are risky | Missing/pathless settings fail; no directory/config creation | mitigated |

### Rework Prevention Checklist
- Does the spec name what the user will see or operate? Да: CLI without `--tasks`, JSON errors and optional usage.
- Does every user-visible scenario have evidence? Да: section 6.3 and AC matrix.
- Did the agent list decisions it assumed? Да: Decision Ledger.
- Did the agent predict likely objections and mitigate them? Да: table above.
- Did role-based review run for the relevant task type? Да: section 19.
- Are acceptance criteria verifiers, not preparation steps? Да.
- Does EXEC have a path to prove the scenarios before final? Да: staged TUnit/build/full-suite plan.

## 13. План выполнения
1. После approval проверить fresh `origin/main`, создать отдельный worktree/branch и подтвердить clean ownership boundary.
2. Добавить testable read-only settings resolver, затем заменить mandatory-path guard единым effective-path flow.
3. Сначала добавить and run regression tests for default, override and configuration failures; confirm expected red before resolver implementation where practical.
4. Update CLI README/usage and run targeted tests, affected build, full test suite serially, then `git diff --check`.
5. Выполнить post-EXEC review; создать commit/push/PR только по отдельному прямому поручению пользователя.

## 14. Открытые вопросы
Нет. Контракт намеренно ограничен active local source compatibility projection; portable/debug configurations могут использовать existing `--tasks`.

## 15. Соответствие профилю
- Применимые документы: `creator-vibe-lens` (full skill не применим: задача factual/exact), `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `tool-execution-baseline`, `testing-baseline`, `testing-dotnet`, `session-insights-context`, `dotnet-desktop-client`, `spec-linter`, `spec-rubric`, `review-loops`; локальный `AGENTS.override.md` не добавляет UI test обязанностей, так как UI behavior не меняется.
- Выполненные требования профиля: staged automated tests, `dotnet build` и `dotnet test` запланированы; UI thread, selectors, visual artifacts не затрагиваются.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Cli/TaskDirectoryResolver.cs` | Новый read-only resolver settings -> local path/errors | Изолировать config contract и сделать его тестируемым |
| `src/Unlimotion.Cli/Program.cs` | Explicit-or-default path flow и typed configuration error propagation | Убрать mandatory `--tasks` |
| `src/Unlimotion.Cli/README.md` | Optional flag/default/error documentation | Не оставлять устаревший CLI contract |
| `src/Unlimotion.Test/TaskDirectoryResolverTests.cs` | Deterministic resolver fixtures | Защитить default, override и settings error contract без доступа к профилю пользователя |
| `specs/2026-09-12-cli-default-task-storage.md` | Эта SPEC/audit log | QUEST traceability |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| CLI without `--tasks` | Invalid arguments, exit 2 | Resolves active local desktop path or typed configuration failure, exit 1 |
| Explicit `--tasks` | Required and used | Optional, still selected first and used unchanged |
| Server active task space | No default behavior | Typed refusal; no network/auth fallback |
| Settings/task data | Not read | Settings read-only; task data behavior unchanged |

## 18. Альтернативы и компромиссы
- Вариант: parsing `TaskSources` catalog directly.
  - Плюсы: direct access to active-source descriptor.
  - Минусы: duplicates desktop migration/journal/recovery semantics and requires wider shared dependencies.
- Вариант: always guess `Documents\Unlimotion\Tasks`.
  - Плюсы: simple.
  - Минусы: ignores a configured/custom active path and may target wrong data.
- Выбранное решение: `TaskStorage` compatibility projection.
  - Плюсы: desktop already persists it for the active source; minimal JSON surface and no secret/server behavior.
  - Минусы: depends on normal desktop projection persistence.
  - Почему выбранное решение лучше в контексте этой задачи: it is the narrowest authoritative bridge between installed desktop settings and file-only CLI while preserving an explicit override.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Outcome, AS-IS, root cause, limits and non-goals are explicit. |
| B. Качество дизайна | 6-10 | PASS | One resolver, one integration point, typed failures and no-write contract are defined. |
| C. Безопасность изменений | 11-13 | PASS | Existing config only; no secret parsing, writes, migration or data rollback requirement. |
| D. Проверяемость | 14-16 | PASS | Six ACs map to negative and positive automated checks, staged commands and stop rules. |
| E. Готовность к автономной реализации | 17-19 | PASS | Worktree, ownership, decisions and no open questions are recorded. |
| F. Соответствие профилю | 20 | PASS | .NET/TUnit/build contract and UI non-applicability are explicit. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Observable default behavior and non-goals are concrete. |
| 2. Понимание текущего состояния | 5 | Mandatory guard, desktop config path and projection behavior were inspected. |
| 3. Конкретность целевого дизайна | 5 | Resolver inputs, precedence, error contract and integration point are fixed. |
| 4. Безопасность (миграция, откат) | 5 | Read-only minimal fields, server refusal and no-migration rollback are specified. |
| 5. Тестируемость | 5 | Positive, override and three error families are mapped to automated evidence. |
| 6. Готовность к автономной реализации | 5 | No user-owned decisions remain; plan and changed files are bounded. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does active desktop task-space selection map to the requested CLI workflow? | PASS | Use the desktop compatibility projection instead of arbitrary folder fallback. |
| UX / designer | not applicable | No UI, layout, visual state or copy surface changes. | Не применимо | None. |
| Tester / validation | applicable | Are precedence, settings failures and envelope stability covered? | PASS | AC matrix includes happy path and negative cases. |
| Developer / architect | applicable | Is the boundary compatible with existing desktop source management and file-only CLI? | PASS | Isolate resolver; do not parse catalog internals. |
| Delivery / operations / security | applicable | Are local config, secrets, write behavior and rollback bounded? | PASS | Read two nonsecret fields only; no writes/network; fresh worktree planned. |

### Post-SPEC Review
- Статус / stop decision: PASS; spec is ready for user approval, but EXEC is blocked by the QUEST gate.
- Scope/Evidence pass: inspected this spec; central `quest-governance`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`, testing/docs profiles; local `AGENTS.override.md`; current `Program.cs`, CLI README/project, existing CLI integration tests, release desktop `Program.cs`, `TaskSourceSettingsAdapter` and installed settings shape (field names only).
- Contract pass: `--tasks` precedence, file-only boundary, no config/task write, config errors and full test requirement agree with the inspected implementation and non-goals.
- Adversarial risk pass: considered stale/malformed concurrent settings, multiple spaces, server source, first-run no config, secret leakage, explicit override and accidental task-directory creation. Each has a concrete no-write/refuse/override outcome.
- Findings:

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Active task-space selection | Direct `TaskSources` parsing would duplicate migration/recovery behavior and risk wrong source selection. | Use `TaskStorage` compatibility projection only. | Fixed in section 6.2 |
| MEDIUM | First-run behavior | Guessing a default folder could create/read unintended data. | Fail pathless/missing settings and preserve explicit override. | Fixed in sections 6.2 and 7 |
| LOW | Test isolation | Process integration cannot safely overwrite the user's real Documents config. | Require testable resolver fixture seam; no test-only CLI option. | Fixed in Decision Ledger and test matrix |
- Fix and re-review: after incorporating those constraints, rechecked sections 6–12 and AC matrix; no remaining BLOCKER/HIGH/MEDIUM findings.
- No-findings justification: scope, contracts, negative scenarios, test evidence, worktree boundary, docs and no-secret invariant were all inspected; the remaining projection-staleness risk is documented and mitigated by `--tasks`.
- Manual-review challenge / остаточные риски / needs human: a desktop crash before it persists the active-source projection can leave the prior path. This is existing desktop state; CLI must not invent recovery. User can override with `--tasks`.

### Post-EXEC Review
- Статус / stop decision: PASS.
- Scope/Evidence pass: reviewed approved SPEC, `git status`, relevant diff, resolver, Program, README, new TUnit tests, targeted expected-red/green evidence, Release CLI build and read-only installed-settings smoke; full sequential `Unlimotion.Test` completed successfully.
- Contract pass: explicit `--tasks` remains priority; missing default resolves through standard desktop settings; local path, string/boolean server flag, server refusal, no credentials/network/settings writes and stable error envelope conform to AC.
- Adversarial risk pass: real installed settings used string `IsServerMode="False"`; initial smoke rejected it, so resolver and regression fixture were corrected to accept both JSON booleans and parseable True/False strings. Re-run targeted tests and smoke passed.
- Role-Based pass: workflow, tester, architecture and delivery/security roles PASS; UX remains not applicable because no UI behavior changed.
- Findings/fixes: one MEDIUM compatibility finding (string boolean) fixed; no BLOCKER/HIGH/MEDIUM findings remain. No unrelated changes in the new worktree.
- No-findings justification: every user-observable scenario has targeted or smoke evidence; full suite and Release build completed; only existing repository warnings and LF-to-CRLF notices remain.
- Residual risk: a stale desktop compatibility projection can select the prior active space after a desktop persistence failure; explicit `--tasks` remains the safe override.

## Approval
Ожидается фраза: «Спеку подтверждаю».

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Inspect current CLI/default desktop config contract | 0.96 | None material | Draft expanded SPEC | Нет | Нет | User requests a behavior change; current CLI requires `--tasks`, desktop persists active source projection in `TaskStorage`. | `Program.cs`, desktop `Program.cs`, adapter, tests, README |
| SPEC | Define safe default resolution and test plan | 0.93 | None material | Complete quality gates and request approval | Да | Yes: user asked to implement feature in a new worktree; approval phrase has not yet been supplied | Chose compatibility projection, explicit precedence and no-write failures to minimize drift/secrets risk. | This SPEC |
| SPEC | Post-SPEC review | 0.94 | Exact future `origin/main` SHA (must refresh after approval) | Wait for exact approval | Да | Pending | All pre-approval gates PASS; QUEST prohibits code/worktree changes before approval. | This SPEC |
| EXEC | Implement, validate and review default task-directory resolution | 0.97 | No material data missing | Report completed uncommitted worktree change | Нет | User approved SPEC | Real smoke exposed the desktop's string boolean representation; corrected within approved settings-format compatibility scope. | `TaskDirectoryResolver.cs`, `Program.cs`, `README.md`, `TaskDirectoryResolverTests.cs`, this SPEC |
