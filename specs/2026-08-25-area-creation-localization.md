# Локализация ошибки пустого имени области

## 0. Метаданные

- Тип (профиль): `delivery-task`; .NET Desktop Client + UI Automation Testing.
- Владелец: Codex.
- Масштаб: small.
- Целевое семейство / behavior baseline: GPT-5.6 guidance; на поведение приложения не влияет.
- Поверхность: Work / Codex.
- Effective runtime: предоставленный средой Codex runtime; версию и tier не требуется фиксировать для UI-копирайта.
- Eval baseline / evidence: не применимо — задача не меняет model/prompt workflow.
- Целевой релиз / ветка: `feat/daily-feed`, существующий draft PR #285.
- Ограничения: код меняется только после фразы «Спеку подтверждаю»; сохраняются текущие AutomationId и UI-layout; локальные `chat-artifacts/`, `output/` и `.codex-remote-attachments/` не коммитятся.
- Связанные ссылки: пользовательский скриншот в чате (local-only evidence); `specs/2026-08-24-daily-feed-mode.md`.

## 1. Overview / Цель

В русской локализации ошибка пустого имени при создании, переименовании или сохранении области должна быть показана по-русски.

Outcome contract:

- Success means: при активном языке `ru` пользователь видит «Название области не может быть пустым.», а при `en` сохраняется «An area name cannot be empty.»
- Итоговый артефакт / output: локализованная пользовательская ошибка и UI-регрессия.
- Stop rules: не трогать другие английские исключения, если они не принадлежат этому UI-потоку; не менять макет или lifecycle ошибки без отдельного запроса.

## 2. Текущее состояние (AS-IS)

- `AreaManagementCreateRootButton` вызывает `AreaManagementViewModel.CreateRootCommand`.
- `CreateRootFromDraftAsync` передаёт имя в `CreateAsync`; `RequireName` выбрасывает `InvalidDataException` с жёстко заданным английским текстом.
- `ExecuteSafelyAsync` кладёт `exception.Message` в `ErrorMessage`; `AreaManagement.axaml` показывает её через `AreaManagementErrorText`.
- Тот же `RequireName` используется для создания дочерней области, переименования и сохранения локального черновика области.
- В этом ViewModel уже применяется `L10n.Get(...)`; парные ресурсы находятся в `Strings.resx` и `Strings.ru.resx`, а `LocalizationService` использует английский fallback.
- Похожая английская проверка есть в `MarkdownMutationService`, но она не лежит на показанном UI-пути и остаётся вне scope.

## 3. Проблема

Пользовательская ошибка области хранится внутри ViewModel как английский литерал, поэтому она игнорирует выбранный язык интерфейса.

## 4. Цели дизайна

- Использовать единый механизм локализации ViewModel, а не переводить исключение в XAML.
- Сохранить одну валидационную точку для всех операций с именем области.
- Проверить видимый текст через реальный headless UI-поток и стабильные AutomationId.
- Не создавать зависимость `Unlimotion.Notes` от `Unlimotion.ViewModel`.

## 5. Non-Goals (чего НЕ делаем)

- Не выполняем массовую локализацию доменных/инфраструктурных исключений.
- Не меняем `MarkdownMutationService` и его contract.
- Не меняем layout, кнопку, фокус, доступность или AutomationId управления областями.
- Не меняем отдельно наблюдаемое поведение, при котором старая ошибка остаётся видимой, пока пользователь не отправит следующую операцию: это другой UX-вопрос.
- Не создаём/не переводим PR в ready, не коммитим и не пушим без отдельной просьбы.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент/файл | Ответственность |
| --- | --- |
| `AreaManagementViewModel.cs` | Запрашивает локализованный текст для пользовательской валидации имени области. |
| `Strings.resx` | Английское fallback-значение нового ключа. |
| `Strings.ru.resx` | Русское значение нового ключа. |
| `FeedAuxiliaryUiTests.cs` | Проверяет действие кнопки, error binding и текст в русской/английской локали. |

### 6.2 Детальный дизайн

1. Добавить ключ `AreaNameRequired` в оба resource-файла:
   - EN: `An area name cannot be empty.`
   - RU: `Название области не может быть пустым.`
2. Заменить literal в `AreaManagementViewModel.RequireName` на `L10n.Get("AreaNameRequired")`. Метод остаётся единственной проверкой пустого имени; его callers не меняются.
3. Добавить parameterized Avalonia.Headless test рядом с существующими `AreaManagement` тестами. Для `ru` и `en` тест временно устанавливает `LocalizationService.Current`, открывает настоящий `AreaManagement`, оставляет `AreaManagementNewNameTextBox` пустым, нажимает `AreaManagementCreateRootButton`, ожидает terminal error state и проверяет видимый `AreaManagementErrorText` плюс отсутствие созданной области. Глобальную локализацию и culture восстанавливает в `finally`.
4. Visual planning artifact (copy-only wireframe):

```text
[Название области: пусто] [Создать корневую]
                 ↓
AreaManagementErrorText: «Название области не может быть пустым.»
```

Геометрия, цвет, положение и доступность панели не меняются; меняется только локализованный текст.

5. UI video evidence: `Не применимо` без расширения scope. В репозитории нет automated video scenario для `AreaManagement`; имеющиеся recorder wrappers привязаны к другим flows и не должны переиспользоваться как ложное evidence. Обязательный fallback: выполнить exact Headless command из §11; затем, при доступной интерактивной desktop-сессии, вручную показать ту же ошибку и сохранить inspected after-screenshot в `chat-artifacts/area-error-localization/after-ru.png` (local-only, вне коммита). Если desktop-сессии нет, явно зафиксировать эту объективную причину и оставить targeted Headless assertion как next-best evidence.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Русская ошибка | Интерфейс на русском; оставить имя пустым и нажать «Создать корневую» | Видна русская ошибка, область не создаётся | Headless UI assertion; после-скриншот при доступном desktop runtime | AC-1, AC-2 |
| Английский fallback | Интерфейс на английском; выполнить то же действие | Видна исходная английская ошибка, область не создаётся | Parameterized Headless UI assertion | AC-3 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Панель областей, пустое имя, `ru` | Click Create root | `HasError=true`, русский текст в `AreaManagementErrorText` | Никакой area не сохранена | Главное исправление |
| Панель областей, пустое имя, `en` | Click Create root | `HasError=true`, английский fallback | Никакой area не сохранена | Совместимость |
| Корректное имя | Existing create flow | Без изменения | Не входит в новый тестовый assertion кроме отсутствия mutation в negative path | Existing coverage сохраняется |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Текст RU | agent | «Название области не может быть пустым.» | 0.99 | Практически отсутствует: прямой перевод screenshot error | Нет |
| Scope source | agent | Только `AreaManagementViewModel.RequireName` | 0.98 | Случайно затронуть доменный слой и создать неверную dependency | Нет |
| UX clearing старой ошибки | agent | Не менять | 0.95 | Отдельная ожидаемая UX-правка останется | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

Не применимо: нет persist-формата, конфигурации, API, миграции или изменения хранилища.

## 7. Бизнес-правила / Алгоритмы (если есть)

- Пустая или whitespace-only строка продолжает быть недопустимым именем.
- Текст ошибки выбирается из текущей UI-локали через `L10n.Get`.
- При отсутствии конкретного ресурса применяется уже существующий английский fallback `LocalizationService`.

## 8. Точки интеграции и триггеры

- `CreateRootCommand`, `CreateChildCommand`, сохранение/переименование области используют `RequireName` без дублирования проверки.
- `ExecuteSafelyAsync` продолжает переносить user-facing текст в `ErrorMessage`.
- XAML binding `AreaManagementErrorText` и его AutomationId не меняются.

## 9. Изменения модели данных / состояния

Нет. Добавляется только два локализованных string resource; `ErrorMessage`, модель области и файлы vault не меняются.

## 10. Миграция / Rollout / Rollback

- Миграция не нужна: изменения применяются при следующем вызове валидатора.
- Rollback: вернуть ссылку на resource key к прежнему literal и удалить ресурсный ключ одним обратным коммитом.
- Existing vault, area IDs и каталог остаются совместимыми.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC-1: В русской локали попытка создать корневую область без имени показывает ровно «Название области не может быть пустым.» через `AreaManagementErrorText`.
- AC-2: Negative action не создаёт/не изменяет область.
- AC-3: В английской локали тот же сценарий показывает «An area name cannot be empty.»
- AC-4: Не меняются `AreaManagement.axaml` и конкретные селекторы затронутого пути: `AreaManagementNewNameTextBox`, `AreaManagementCreateRootButton`, `AreaManagementErrorText`.
- AC-5: Новые ключи присутствуют в fallback и русском resource-файле; existing resource-key parity test остаётся green.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | Новый parameterized test в `FeedAuxiliaryUiTests` | Проверить после-скриншот при доступном desktop runtime | `chat-artifacts/area-error-localization/after-ru.png` (local-only) | — |
| AC-2 | Тот же test проверяет пустой catalog | — | Test output | — |
| AC-3 | Второй аргумент parameterized test | — | Test output | — |
| AC-4 | Новый test использует три текущих AutomationId; existing `AreaManagement_CrudCycleArchiveAndCompactLayout_PreserveCatalog` сохраняет compact-layout contract | Diff review `AreaManagement.axaml` | Test output + `git diff --check` | — |
| AC-5 | Existing `RussianResources_HaveSameKeysAsFallbackResources` | Resource diff review | Test output | — |

### Planned validation

Новый regression-тест называется `AreaManagement_EmptyNameErrorUsesCurrentUiLanguage`.

1. Add the reproducing UI test, then run the command below before the production change. The `ru` argument is expected to fail against the current English literal; this is the TDD red evidence.

```powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/FeedAuxiliaryUiTests/*AreaManagement_EmptyNameErrorUsesCurrentUiLanguage*" --maximum-parallel-tests 1 --no-ansi --no-progress
```

2. After the production change, rerun the same command; both `ru` and `en` arguments must pass.

3. Verify resource-key parity:

```powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --treenode-filter "/*/*/LocalizationSettingsTests/*RussianResources_HaveSameKeysAsFallbackResources*" --maximum-parallel-tests 1 --no-ansi --no-progress
```

4. Build the affected test project:

```powershell
dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -nr:false -m:1
```

5. Run the repository's full serial core TUnit gate (expected historical duration: 30–60 minutes); save its output and do not repeat unchanged after a timeout:

```powershell
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -- --maximum-parallel-tests 1 --no-ansi --no-progress --timeout 45m
```

6. Run `git diff --check`, inspect the exact `AreaManagement.axaml` diff, and carry out the desktop screenshot fallback described in §6.2 when its prerequisite is available.

Stop rules: не повторять полный runner после timeout без нового diagnostics evidence; testhost/lock/SDK failures классифицировать отдельно от product defect.

## 12. Риски и edge cases

- Глобальный `LocalizationService.Current` в тесте может влиять на параллельные тесты. Mitigation: existing non-parallel Headless class, snapshot и `finally` restoration.
- Ошибка может быть закэширована во ViewModel после переключения языка. Не меняем этот existing lifecycle; test задаёт язык до action.
- Отдельный `MarkdownMutationService` literal может всплыть в другом потоке. Он исключён, потому что нет воспроизведения от пользователя и отсутствует безопасная dependency direction для UI-localization.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Ошибка по-прежнему на английском в другом месте» | В codebase есть иной literal | Трассирован именно screenshot path; иной path не меняется без reproduce | mitigated |
| «Почему меняете только текст, а не логику?» | Screenshot содержит старую ошибку после ввода | Scope явно не меняет lifecycle ошибки; имя и mutation behavior сохраняются | mitigated |
| «Проверьте не только ViewModel» | Пользователь видит XAML surface | Test нажимает реальную кнопку и читает `AreaManagementErrorText` | mitigated |

### Rework Prevention Checklist

- User-visible action and output are named: да.
- Every user-visible scenario has evidence: да.
- Assumptions are recorded in Decision Ledger: да.
- Likely objections are predicted and scoped: да.
- Role-based review is present: да.
- Acceptance criteria are verifiable outcomes: да.
- EXEC has a proof path: да.

## 13. План выполнения

1. Добавить red UI regression test for current Russian error.
2. Добавить resource key in both `.resx` files and replace only the ViewModel literal.
3. Запустить targeted UI/localization tests and affected build.
4. Capture/inspect after-state evidence when desktop runner is available, then run broader required validation.
5. Выполнить post-EXEC review; delivery actions only on explicit request.

## 14. Открытые вопросы

Нет. Выбранный перевод однозначно соответствует reported user-visible error.

## 15. Соответствие профилю

- Профиль: .NET Desktop Client + UI Automation Testing.
- Выполненные требования профиля: UI behavior covered by automated headless interaction; existing stable selectors reused; no UI-thread blocking planned; visual copy-only plan and evidence fallback recorded.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.ViewModel/Feed/AreaManagementViewModel.cs` | Replace user-facing literal with `L10n.Get` | Respect active localization |
| `src/Unlimotion.ViewModel/Resources/Strings.resx` | Add EN `AreaNameRequired` | Fallback/localization source |
| `src/Unlimotion.ViewModel/Resources/Strings.ru.resx` | Add RU `AreaNameRequired` | Russian visible copy |
| `src/Unlimotion.Test/FeedAuxiliaryUiTests.cs` | Add actual AreaManagement error-flow test | Prevent UI regression |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| RU create area with blank name | `An area name cannot be empty.` | `Название области не может быть пустым.` |
| EN create area with blank name | `An area name cannot be empty.` | Не меняется |
| Area mutation / storage | No mutation | Не меняется |

## 18. Альтернативы и компромиссы

- Вариант: перевести literal непосредственно на русский.
  - Плюсы: минимальный diff.
  - Минусы: ломает английскую локализацию и обходит resource convention.
- Вариант: перехватывать и переводить исключение в XAML/`ExecuteSafelyAsync`.
  - Плюсы: не меняет валидатор.
  - Минусы: хрупко зависит от exception text и не покрывает direct user operations consistently.
- Выбранный вариант: resource key at the single shared validator.
  - Почему лучше: сохраняет shared validation and language fallback with the narrowest behavior change.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, дизайн и non-goals конкретны. |
| B. Качество дизайна | 6-10 | PASS | Описана единая точка валидации и UI путь. |
| C. Безопасность изменений | 11-13 | PASS | Нет model/storage migration; rollback задан. |
| D. Проверяемость | 14-16 | PASS | AC связаны с headless UI и resource parity tests. |
| E. Готовность к автономной реализации | 17-19 | PASS | Нет открытых user-owned решений. |
| F. Соответствие профилю | 20 | PASS | UI automation and visual fallback explicitly covered. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Точно ограничено одним error flow. |
| 2. Понимание текущего состояния | 5 | Traced command → validator → binding. |
| 3. Конкретность целевого дизайна | 5 | Key names, values and test path are explicit. |
| 4. Безопасность (миграция, откат) | 5 | No data impact; one-commit rollback documented. |
| 5. Тестируемость | 5 | Visible UI assertion plus localization parity are specified. |
| 6. Готовность к автономной реализации | 5 | No unresolved product choices. |

Итоговый балл: 30 / 30.
Зона: готово к автономному выполнению.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does blank-name rejection stay unchanged? | PASS | No mutation behavior change. |
| UX / designer | applicable | Is visible Russian copy clear and layout-stable? | PASS | Copy-only wireframe added. |
| Tester / validation | applicable | Does UI test prove actual user surface? | PASS | Click-and-binding scenario specified. |
| Developer / architect | applicable | Does fix preserve dependency direction and one validator? | PASS | Excludes Notes literal. |
| Delivery / operations / security | not applicable | No config/deploy/secret/runtime access change. | PASS | No changes required. |

### Post-SPEC Review

- Статус: PASS after fix and re-review.
- Scope reviewed: this spec; screenshot evidence; `AreaManagementViewModel`, `AreaManagement.axaml`, localization resources/service, `FeedAuxiliaryUiTests`; central core/testing/UI profiles.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: traced exact UI command, exception and binding; no implementation files changed.
  - Contract pass: active-culture resource lookup covers all area-name callers without changing validation rules.
  - Adversarial risk pass: reviewed duplicate Notes literal and stale-error lifecycle; both are explicitly outside scope.
  - Role-Based pass: completed above.
  - Re-review after fixes: reviewer found missing exact commands, an over-broad selector claim and a non-specific visual fallback; §6.2, §11 and AC-4 were corrected and rechecked.
  - Stop decision: PASS; no user-owned choice remains.
- Evidence inspected: attached screenshot; source trace; resources; existing UI test harness; two-pass read-only reviewer feedback from a writable shared environment (not an independent sandbox review).
- Depth checklist:
  - Scope drift / unrelated changes: excluded.
  - Acceptance criteria: all mapped.
  - User-observable scenarios / Decision ledger / Expected objections: completed.
  - Validation evidence: exact red/pass, parity, build and serial-full commands plus documented screenshot fallback.
  - Unsupported claims: no claim of video capability.
  - Regression / edge case: RU/EN and zero-mutation covered.
  - Comments/docs/changelog: no documentation/changelog update required.
  - Hidden contract change: no data, API or selector change.
  - Manual-review challenge: could expose the same English text after a different operation; shared `RequireName` makes the planned fix cover each of its user-facing callers.
- No-findings justification: after the three reviewer corrections, scope and proof path are concrete; the only non-scope English literal is in a lower Notes layer and does not reproduce the reported surface.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | validation/evidence | Initial draft named test classes but omitted reproducible runner commands. | Added exact red/pass, parity, build and full serial commands. | fixed |
| LOW | UI artifact/fallback | Initial fallback implied a possible FlaUI capture with no matching scenario. | Declared video not applicable without scope expansion; specified Headless + desktop-screenshot fallback. | fixed |
| LOW | acceptance | Initial AC-4 claimed every layout/selector stayed intact without evidence. | Narrowed to untouched XAML and three affected stable IDs. | fixed |
| LOW | UX follow-up | Existing error can remain visible while a new name is typed. | Keep out of this translation-only change; create follow-up only if requested. | follow-up |
| BLOCKER/HIGH | scope/design/acceptance/risk/evidence/profile | Нет находок. | — | — |

- Fixed before continuing: all actionable post-SPEC review findings.
- Checks rerun: manual spec-linter/rubric, trace review, exact-command and evidence-contract re-review (PASS).
- Needs human: explicit SPEC approval.
- Residual risks / follow-ups: stale error lifecycle remains intentionally unchanged.

### Post-EXEC Review

- Статус: PASS after remediation and re-review.
- Реализация: `AreaManagementViewModel.RequireName` получает текст через `L10n.Get("AreaNameRequired")`; добавлены RU и fallback EN ресурсы.
- Покрытие: новый headless UI test нажимает настоящий `AreaManagementCreateRootButton`, проверяет видимый `AreaManagementErrorText` и отсутствие созданной области для RU и EN.
- Review finding: MEDIUM — новый тест менял process-wide culture, но возвращал его неполно. Исправлено через snapshot/restore current и default UI cultures; повторное review не выявило BLOCKER/HIGH/MEDIUM/LOW.
- Validation:
  - `AreaManagement_EmptyNameErrorUsesCurrentUiLanguage`: 2/2 passed.
  - `RussianResources_HaveSameKeysAsFallbackResources`: 1/1 passed.
  - full serial `Unlimotion.Test`: 1249/1249 passed, 0 skipped, 17m 34s.
- Ограничение evidence: исходное приложение держало default Desktop output lock, поэтому tests/build использовали отдельный `bin-codex-area-localization`; desktop screenshot новой сборки не снимался. Headless UI flow непосредственно проверяет пользовательскую поверхность.
- Временные `obj-codex-area-localization` от первой неудачной изоляции остались untracked: рекурсивное удаление заблокировано защитой среды. Они не являются изменениями продукта и не должны попадать в commit.

## Approval

Получено: «Спеку подтверждаю».

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Diagnose screenshot error path | 0.99 | Нет | Prepare approval-ready spec | Да | Предстоит запрос approval | Literal in shared user-facing validator bypasses resources | Screenshot, source/resource/test inspection |
| SPEC | Define narrow localized fix | 0.98 | Нет | Wait for «Спеку подтверждаю» | Да | Предстоит | Resource key + shared validator preserves EN fallback and avoids Notes dependency | This spec |
| SPEC | Post-SPEC review and rework | 0.99 | Нет | Wait for «Спеку подтверждаю» | Да | Предстоит | Added reproducible TUnit commands, scoped selector AC and honest visual-evidence fallback | This spec |
| SPEC | Re-review after spec rework | 0.99 | Нет | Wait for «Спеку подтверждаю» | Да | Предстоит | Read-only reviewer returned PASS after all actionable findings were fixed | This spec |
| EXEC | Approval | 1.00 | Нет | Add reproducing UI test, then run it before the production fix | Нет | Пользователь написал exact «Спеку подтверждаю» | Approval permits only the scoped localization fix and validation, not delivery actions | This spec |
| EXEC | TUnit CLI preflight | 1.00 | Нет | Rerun red test with supported serial option | Нет | Нет | TUnit 1.44 rejects `--parallelism-strategy`; `--maximum-parallel-tests 1` remains the supported serial control | This spec |
| EXEC | Implement localized validation | 1.00 | Нет | Run focused UI and resource tests | Нет | Нет | Reused the existing localization API at the one shared area-name validator; no storage or validation rule changed | `AreaManagementViewModel`, two resource files, UI test |
| EXEC | Focused validation | 1.00 | Нет | Run serial full suite and post-EXEC review | Нет | Нет | Headless UI test passed for RU and EN; resource-key parity test passed | `Unlimotion.Test` isolated bin output |
| EXEC | Post-EXEC review remediation | 1.00 | Нет | Rerun focused tests and serial full suite | Нет | Нет | Review found incomplete restoration of process-wide culture in the new test; added exact culture snapshot/restore and re-review passed | `FeedAuxiliaryUiTests` |
| EXEC | Final validation | 1.00 | Нет | Report completed scoped change | Нет | Нет | Focused UI 2/2, resource parity 1/1, full serial suite 1249/1249 passed; no commit or push requested | This spec and test reports |
| EXEC | Delivery checkpoint | 1.00 | Нет | Commit the scoped implementation and spec | Нет | Пользователь явно попросил закоммитить текущее состояние; push не запрошен | Commit only the reviewed localization change and its spec; keep local evidence and build artifacts untracked | This spec and five scoped files |
