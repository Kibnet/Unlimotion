# Актуализация содержания корневых README

## 0. Метаданные

- Тип (профиль): delivery-task; `.NET Desktop Client`, docs-only.
- Владелец: Kibnet.
- Масштаб: medium.
- Целевое семейство / behavior baseline: GPT-5.6 family optimization baseline.
- Поверхность: Work / Codex desktop.
- Effective runtime: текущий Codex runtime; точный model ID и reasoning mode не влияют на документационный контракт.
- Eval baseline / evidence: `origin/main` на `e11cae9a086ddd4fd97105f00b67bedf05f92700`; корневые README, `App.axaml.cs`, Settings UI/ViewModel, CLI guide, release `1.27.0` и его assets, packaging workflows.
- Целевая ветка: `docs/readme-content-refresh`, созданная от актуального `origin/main`.
- Ограничения:
  - на EXEC изменяются только `README.md`, `README.RU.md` и эта spec;
  - новые продуктовые возможности, storage, credential storage, CI/release pipeline, signing, media и workflows не меняются;
  - ни один текст не заявляет поддержку ОС, подпись, notarization или совместимость без current evidence;
  - language parity означает одинаковый смысл и порядок крупных разделов, а не дословный перевод;
  - если факт не подтверждён кодом, workflow, release asset или UI/test evidence, он удаляется либо формулируется как ограничение.
- Связанные ссылки:
  - `README.md`, `README.RU.md`;
  - `src/Unlimotion/App.axaml.cs`;
  - `src/Unlimotion/Views/SettingsControl.axaml`;
  - `src/Unlimotion.ViewModel/SettingsViewModel.cs`;
  - `src/Unlimotion.Cli/README.md`;
  - `tests/Unlimotion.ReadmeMedia/README.md`;
  - GitHub release `1.27.0`.

## 1. Overview / Цель

Обновить именно содержание двух корневых README, чтобы пользователь видел актуальное описание Unlimotion, безопасную установку, реальные возможности и известные ограничения, а не внутреннюю программу hardening/release delivery.

Outcome contract:

- Success means:
  - `README.md` и `README.RU.md` содержат проверяемые, пользовательские утверждения о текущем `main` и latest release;
  - ложное утверждение о каталоге `Tasks` в рабочем каталоге удалено;
  - CLI, актуальный settings surface и current task model описаны в подходящей для README глубине;
  - EN/RU не расходятся по структуре, платформам, status contract и caveats.
- Итоговый артефакт / output: один docs-only diff с обновлёнными `README.md` и `README.RU.md`, проверенный структурно и по источникам фактов.
- Stop rules:
  - не исправлять продукт ради документации;
  - не включать в root README machine-oriented evidence markers, SHA, CI terminology и планы будущей работы;
  - остановиться и сообщить о расхождении, если release assets, код и workflow не дают единственного правдивого утверждения.

## 2. Текущее состояние (AS-IS)

- Оба README содержат два H1: второй используется как language switch; это нарушает простую структуру документа.
- Раздел установки ссылается на `/releases/latest` и перечисляет действительно опубликованные для `1.27.0` assets, но смешивает доступность файла, будущую support matrix и технические предположения.
- macOS уже корректно направляет к Apple `Open Anyway`, но текст нужно оставить компактным и не обещать signing/notarization.
- Инструкции исходной сборки соответствуют текущим entry scripts: запуск происходит из корня checkout через `bash ./run.linux.sh` или `bash ./run.macos.sh`.
- Status/availability section отражает current shared transition policy: пять статусов, start/completion guards, unarchive normalization и markdown markers.
- UI раздел утверждает, что интерфейс состоит из трёх частей, но третья часть оканчивается незавершённым предложением; описание не показывает значимые нынешние возможности settings.
- README утверждает, что пустой `TaskStorage.Path` создаёт `Tasks` в working directory. Это противоречит `App.ResolveDefaultTaskStoragePath()`: default — `LocalApplicationData/Unlimotion/Tasks`; явный путь остаётся настраиваемым.
- Корневой README не ссылается на уже документированный `Unlimotion.Cli`, хотя CLI предоставляет inspect/validate/status/write commands для task directories.
- Settings UI фактически включает appearance/language, fuzzy search, outline clipboard, update checks, local/server storage, Git backup and SSH settings; README описывает только два поля.
- Media генерируется через `scripts/update-readme-media.ps1`, однако пользователь запросил актуализацию содержания, а не смену media. Текущее изображение не меняется в этой spec.

## 3. Проблема

Корневые README дают пользователю частично устаревшую и неполную картину продукта: один storage claim ложен, текущие automation/settings возможности не отражены, а часть текста ориентирована на внутреннюю инженерную историю вместо пользовательской задачи.

## 4. Цели дизайна

- Сохранить README самостоятельной, короткой и полезной входной точкой проекта.
- Описывать только доступные в current `main` возможности и published release artifacts.
- Разделить пользовательскую информацию и внутреннее evidence: README содержит ясные caveats, но не CI internals.
- Дать равнозначный EN/RU опыт с естественной локализацией.
- Сохранить существующую media и не менять UI, API, storage или release behavior.

## 5. Non-Goals (чего НЕ делаем)

- Не переносим и не продолжаем Stage 3 distribution support contract из PR #280.
- Не меняем `run.*`, packaging workflows, manifests, GitHub branch protection, release assets или release process.
- Не исправляем default path в коде: current `main` уже использует platform local app data; задача только исправляет устаревший текст.
- Не меняем storage, server/Git credentials или security model.
- Не создаём новую документационную иерархию, `CONTRIBUTING*`, assertion inventory или documentation CI.
- Не регенерируем изображения/GIF и не меняем UI tests: это docs-copy change без UI behavior/layout change.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `README.md` -> актуальный английский product entry point.
- `README.RU.md` -> равнозначный русский product entry point.
- `src/Unlimotion.Cli/README.md` -> детальный CLI contract; root README даёт краткое назначение и ссылку.
- `App.axaml.cs`, Settings UI/ViewModel, workflows and release -> authoritative evidence only; не меняются.
- `tests/Unlimotion.ReadmeMedia` -> источник generation contract existing media; не запускается и не меняется без отдельной media-задачи.

### 6.2 Детальный дизайн

1. Заменить второй H1-language switch обычной ссылкой сразу под единственным H1.
2. Переписать краткое позиционирование и strengths на факты: task graph с несколькими родителями, dependency/blocking flow, planning/statuses, local-first task data, roadmap/filters и automation.
3. В Download:
   - оставить `/releases/latest` и реальные категории published artifacts;
   - отличать «available artifact» от подтверждённой широкой совместимости;
   - оставить безопасную macOS/Gatekeeper caveat, portable ZIP extraction и Android sideload caveat;
   - не добавлять source SHA, table of internal evidence levels, hidden HTML markers, raw support manifests или CI prose;
   - использовать exact current asset names лишь там, где имя не маскирует отсутствующую поддержку, и не выдавать Android x64 emulator artifact за универсальный mobile target.
4. Сохранить source-build commands, потому что current scripts всё ещё ожидают запуск из checkout root; не обещать запуск из произвольного каталога до отдельного merge entry-point fix.
5. Сжать и выровнять task-model section: пять статусов, relationship graph, blockers, planned start and completion criteria. Полную matrix оставить, только если после final claim review она остаётся точной и читаемой; иначе заменить короткими правилами и markers.
6. Заменить незавершённое «interface consists of 3 parts» точным описанием main navigation, task details and named projections; сохранить текущий список tabs и screenshots.
7. Исправить storage section: пустой local `TaskStorage.Path` использует platform local application data under `Unlimotion/Tasks`; пользователь может выбрать другой local directory, server storage или Git backup through Settings. Не добавлять несуществующий first-run workaround.
8. Добавить краткий раздел `CLI and automation` / `CLI и автоматизация` со ссылкой на `src/Unlimotion.Cli/README.md` и нейтральным описанием inspect/validate/controlled task operations.
9. Расширить settings overview до реально доступных категорий, но не публиковать claims о безопасном persistent secret storage.
10. Удалить или переписать каждое оставшееся устаревшее, unfinished или implementation-only утверждение; не менять media paths и captions без отдельной visual validation.

Visual planning artifact: Не применимо — изменение не меняет UI layout, flow, state или визуальный acceptance приложения; корректируется только Markdown copy.

UI test video evidence: Не применимо — UI behavior и automation selectors не меняются.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Выбор установки | Открывает root README | Видит latest release, реальные категории artifacts и честные platform caveats | release assets + README review | AC-01, AC-02 |
| Настройка хранилища | Читает Settings section | Не получает ложный совет про working-directory `Tasks`; понимает default local storage и configurable sources | `App.axaml.cs`, Settings UI/ViewModel | AC-03 |
| Автоматизация | Ищет CLI capability | Находит краткое назначение CLI и ссылку на authoritative guide | `src/Unlimotion.Cli/README.md` + link check | AC-04 |
| Выбор языка | Переходит между README | Получает одну и ту же структуру и смысл на EN/RU | heading/parity review | AC-05 |

### 6.4 State / Interaction Matrix

Не применимо: README-only change не меняет runtime state, UI interaction или persisted data.

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope | user | Только root README EN/RU, без distribution/storage/CI implementation | 1.00 | Scope drift повторится | Нет |
| README depth | agent | Product-oriented overview with current caveats; deep CLI contract stays in existing CLI README | 0.95 | В root README может остаться слишком много operational detail | Нет |
| Release wording | agent | «Available published artifact» instead of «officially supported platform», unless evidence proves support | 0.99 | Underclaiming is safer than false compatibility promise | Нет |
| Storage wording | agent | Document actual LocalApplicationData default, configurable path and sources; no new workaround | 0.99 | Platform-specific physical location may vary | Нет |
| Media | user | Не менять в этой задаче | 1.00 | Existing visual drift remains a separate task | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

Не применимо: реализация не меняет runtime, configuration или persisted data. Config/UI source files используются только как evidence для copy.

## 7. Бизнес-правила / Алгоритмы

1. Каждое public claim в README должно иметь authoritative source: current code/UI, current release asset, existing test or official platform documentation for an external caveat.
2. При конфликте README и source of truth исправляется README; продукт не меняется.
3. Нельзя превращать artifact presence в обещание compatibility/support.
4. EN/RU должны иметь одинаковые H1, language switch target, порядок ключевых sections и смысл caveats.
5. Корневой README не дублирует полный CLI reference и не включает internal CI metadata.

## 8. Точки интеграции и триггеры

- README links: language switch, `releases/latest`, Apple guidance, CLI guide and local media.
- Product claims are refreshed from `App.axaml.cs`, Settings UI/ViewModel, CLI README and release assets before editing.
- Никакие runtime methods, events or workflows не меняются.

## 9. Изменения модели данных / состояния

Не применимо: документационный diff не добавляет model fields, migration or state.

## 10. Миграция / Rollout / Rollback

- Rollout: обычный docs-only PR from `docs/readme-content-refresh`.
- Rollback: revert документационного commit; release assets и product data не затрагиваются.
- Backward compatibility: links to current README paths and media basenames are preserved.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC-01:** оба README содержат один H1, reciprocal language switch и working local media links.
- **AC-02:** install table/caveats match the current latest release assets and do not claim unverified platform compatibility, signing or notarization.
- **AC-03:** README no longer says an empty `TaskStorage.Path` uses the launch working directory; it truthfully describes the `LocalApplicationData/Unlimotion/Tasks` default and configurable storage.
- **AC-04:** README exposes current CLI automation through a valid link to the authoritative CLI README without duplicating its command contract.
- **AC-05:** task status, graph availability, UI projections, settings and shortcuts only describe existing current-main behavior; unfinished «three parts» copy is removed.
- **AC-06:** EN/RU retain equivalent semantic section order and caveats; no new code, workflow, media or configuration files are changed.

No application build or UI test is required for a copy-only change. The validation gate is structural plus claim-by-claim source review; current UI tests remain authoritative evidence and are not modified.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-01 | Markdown heading/link/media PowerShell checks | Inspect first screen of both Markdown files | command output | No repo documentation validator exists in scope |
| AC-02 | release asset-name comparison via GitHub CLI | Read rendered install copy | release JSON + diff | Compatibility is an evidence review, not a runtime test |
| AC-03 | source-to-copy assertion via `rg` | Read Settings section | `App.axaml.cs` lines 1846-1877 + diff | No runtime behavior changes |
| AC-04 | local link existence check | Read concise CLI paragraph | CLI README + diff | CLI binary remains unchanged |
| AC-05 | heading/parity token check | Claim-by-claim source review | diff + referenced source files | No UI behavior changes |
| AC-06 | `git diff --check`, changed-file allowlist | Review EN/RU side by side | git output | No code change |

Verification commands after EXEC:

```powershell
git diff --check
rg -n "^# " README.md README.RU.md
Test-Path README.md; Test-Path README.RU.md; Test-Path src/Unlimotion.Cli/README.md
gh release view --repo Kibnet/Unlimotion --json tagName,publishedAt,assets
git diff --name-only origin/main...HEAD
```

Stop rule: if a claimed asset or product capability cannot be independently confirmed, remove it or explicitly mark it as unavailable rather than inferring it.

## 12. Риски и edge cases

- A subsequent release can make exact asset names stale. Mitigation: anchor download at `/releases/latest` and phrase package categories conservatively.
- Root README can grow into a second manual. Mitigation: concise overview plus existing CLI guide; no new documentation tree in this scope.
- EN/RU can drift during rewriting. Mitigation: edit paired sections together and compare heading/caveat structure before finish.
- Media may be visually outdated. Mitigation: retain existing paths and label this as explicit non-goal, not evidence that screenshots were refreshed.
- Settings include secret-related fields. Mitigation: describe capabilities at category level; never imply secure persistent credential storage.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Мы снова ушли в CI и distribution» | Предыдущая roadmap expanded scope | Strict file allowlist and explicit Non-Goals | mitigated |
| «README всё ещё не описывает текущий продукт» | CLI/settings/default storage are absent or false now | Mandatory AC-03..05 and claim inventory | mitigated |
| «Текст стал слишком техническим» | Earlier branch inserted evidence metadata | Product copy; no SHA, CI states or HTML markers | mitigated |
| «Скриншоты устарели» | Media may lag UI | Explicitly deferred, no false claim of refreshed media | accepted-risk |

### Rework Prevention Checklist

- User-visible result named: yes, two accurate bilingual root README files.
- Every scenario has evidence: yes, AC-01..06 map to source/link/release checks.
- Agent assumptions listed: yes, Decision Ledger.
- Likely objections addressed: yes, table above.
- Role-based review applicability recorded: yes, section 19.
- Acceptance criteria are verifiers: yes.
- EXEC proof path: yes, source review, release query, structural checks and diff allowlist.

## 13. План выполнения

1. Freeze the former distribution branch; work only from fresh `origin/main` in this branch.
2. Complete source-backed audit of every root README section and record this scoped spec.
3. After `Спеку подтверждаю`, rewrite paired README sections together within the file allowlist.
4. Run structural/link/release/claim checks and inspect the complete diff for EN/RU parity and scope drift.
5. Run post-EXEC review; commit and propose a docs-only PR only after PASS.

## 14. Открытые вопросы

Нет блокирующих вопросов. Future media refresh and a broader installation guide are intentionally separate work.

## 15. Соответствие профилю

- Профиль: `.NET Desktop Client`.
- Выполненные требования профиля: application UI, navigation, selectors and runtime behavior are not changed; therefore UI tests and visual planning artifacts are not applicable. Claims about UI are verified against existing UI/ViewModel/test evidence.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-24-readme-content-refresh.md` | New scoped working spec | Replace over-broad roadmap for this delivery |
| `README.md` | English content refresh | Accurate current product/release entry point |
| `README.RU.md` | Russian content refresh | Equivalent Russian entry point |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Scope | README refresh coupled to distribution hardening roadmap | README-only docs delivery from fresh main |
| Language switch | Second H1 | One H1 and normal reciprocal link |
| Storage default | Working-directory `Tasks` claim | Actual platform local application-data default |
| Automation | Root README omits CLI | Concise CLI entry with authoritative link |
| Settings | Two legacy fields | Current categories without security overclaim |
| Installation | Internal validation wording mixed into user copy | Available artifacts plus concise, honest caveats |

## 18. Альтернативы и компромиссы

- Вариант: продолжить old distribution roadmap before editing README.
  - Плюсы: more release evidence eventually exists.
  - Минусы: повторяет scope drift and delays correction of known false text.
- Вариант: update only the one false storage sentence.
  - Плюсы: minimal diff.
  - Минусы: leaves CLI, settings, unfinished UI copy and bilingual content drift unresolved.
- Выбранный вариант: a medium docs-only refresh. Он исправляет весь user-facing content drift without making product/release changes.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, problem, design goals and strict Non-Goals are explicit. |
| B. Качество дизайна | 6-10 | PASS | File ownership, content rules, evidence sources and rollback are defined. |
| C. Безопасность изменений | 11-13 | PASS | Docs-only allowlist; no product, secret, config or release mutation. |
| D. Проверяемость | 14-16 | PASS | Six AC map to source/release/link/diff checks. |
| E. Готовность к автономной реализации | 17-19 | PASS | No blocking user choice; exact execution boundary and stop rules exist. |
| F. Соответствие профилю | 20 | PASS | .NET desktop profile is applied proportionally to docs-only scope. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | README-only allowlist and Non-Goals are explicit. |
| 2. Понимание текущего состояния | 5 | Claims were checked against main, release, UI/settings and CLI sources. |
| 3. Конкретность целевого дизайна | 5 | Section-level content rules and forbidden internal copy are specified. |
| 4. Безопасность (миграция, откат) | 5 | No data/runtime changes; normal docs revert is defined. |
| 5. Тестируемость | 5 | Each AC has a source, release, structural or diff check. |
| 6. Готовность к автономной реализации | 5 | No unresolved product decision remains. |

Итоговый балл: 30 / 30.
Зона: готово к автономному выполнению.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does README explain actual task/status/storage workflows without inventing rules? | PASS | Status and storage source-of-truth are named. |
| UX / designer | applicable | Is the root README an understandable first entry rather than an internal evidence report? | PASS | Copy rules remove internal CI metadata and preserve media. |
| Tester / validation | applicable | Does every content change have an evidence/check path? | PASS | AC matrix and commands are present. |
| Developer / architect | applicable | Does documentation respect actual code/CLI/settings boundaries? | PASS | No code change and authoritative source mapping are explicit. |
| Delivery / operations / security | applicable | Are release, signing and secret claims bounded conservatively? | PASS | No release/config mutation; underclaim rule is explicit. |

### Post-SPEC Review

- Статус: PASS.
- Scope reviewed: this spec; `README.md`, `README.RU.md`; `App.axaml.cs`; Settings UI/ViewModel; CLI README; `global.json`; current GitHub release `1.27.0`; branch `docs/readme-content-refresh` at `e11cae9`.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: only README content is planned; former distribution branch is excluded.
  - Contract pass: every proposed statement has a named source; no product change is hidden in docs work.
  - Adversarial risk pass: rejected hard-coded support claims, false working-directory storage claim, internal evidence markers and unmerged entry-script promises.
  - Role-Based pass: all five relevant roles are recorded above.
  - Re-review after fixes / Fix and re-review: initial draft was strengthened with explicit current local-app-data evidence, source-root script limitation, media non-goal and CLI boundary.
  - Stop decision: PASS; no human choice blocks EXEC.
- Evidence inspected: current README pair; `App.ResolveDefaultTaskStoragePath`; Settings XAML/ViewModel; CLI README; `global.json`; latest release assets; existing run scripts and workflows.
- Depth checklist:
  - Scope drift / unrelated changes: prevented by a three-file allowlist.
  - Acceptance criteria: six observable documentation outcomes, all mapped.
  - User-observable scenarios / Decision ledger / Expected objections: populated.
  - Validation evidence: source/release/link/diff checks named; UI tests not needed for copy-only change.
  - Unsupported claims: prohibited by explicit source-of-truth rule.
  - Regression / edge case: future release asset drift handled with `/releases/latest` and conservative wording.
  - Comments/docs/changelog: only README pair; no changelog required for documentation correction.
  - Hidden contract change: none; product behavior is excluded.
  - Manual-review challenge: verify no text accidentally promises support, secure secret storage or arbitrary-directory script launch.
- No-findings justification: scope, evidence and validation are sufficient for a medium copy-only delivery.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | media | Existing screenshots may not match every current UI detail | Keep them unchanged and route refresh to separate media task | follow-up |
| LOW | release drift | Future assets can change after this audit | Link to latest release and recheck exact names before PR | mitigated |
| LOW | scope | Former broad roadmap can be mistaken for this task | New branch/spec and allowlist isolate this delivery | mitigated |

- Fixed before continuing: scope was narrowed from distribution roadmap to root README only.
- Checks rerun: source/release/CLI/settings evidence collected after fresh branch creation.
- Needs human: exact phrase `Спеку подтверждаю` to begin EXEC.
- Residual risks / follow-ups: media refresh and full documentation IA are intentionally separate.

### Post-EXEC Review

- Статус: PASS.
- Проверенная область: только `README.md`, `README.RU.md` и эта spec. Runtime-код, release pipeline, media и конфигурация не изменялись.
- Evidence refresh:
  - current release remained `1.27.0` with Windows, Linux, macOS and Android artifact categories referenced in the README;
  - `App.ResolveDefaultTaskStoragePath()` confirms the local-application-data `Unlimotion/Tasks` default;
  - Settings XAML/ViewModel confirms the documented appearance, clipboard, update, local/server storage and Git-backup categories;
  - the existing CLI README confirms the linked inspect, validate and controlled-write capabilities.
- Checks:
  - `git diff --check`: PASS;
  - Markdown parser check: exactly one rendered H1 in each README; EN/RU headings have matching levels and order;
  - local Markdown link check: PASS;
  - media check: 11 image paths per README, all present;
  - obsolete working-directory `Tasks` claim: absent;
  - changed-file review: only the two root README files and this spec are in scope.
- Findings:
  - No HIGH or MEDIUM findings.
  - LOW residual risk: a later GitHub release can change its assets. The README links to `releases/latest` and describes categories rather than pinning a version; recheck this table immediately before publishing the PR.
- Regression / user-observable result: both entry points now describe the same current product surface in their respective languages, including the actual default storage location, settings categories and CLI; no UI behavior or release behavior changed.
- Decision: ready to commit as a docs-only change.

## Approval

Ожидается фраза: `Спеку подтверждаю`

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Scope reset | 1.00 | Нет | Создать clean branch from fresh main | Нет | Пользователь разрешил «Приступай» | Distribution PR остаётся нетронутым; README delivery изолирован | Git branch `docs/readme-content-refresh` |
| SPEC | Claim-by-claim evidence audit | 0.98 | Visual freshness media intentionally not evaluated | Request approval for docs-only EXEC | Да | Ожидается `Спеку подтверждаю` | Current README, source, settings, CLI and release show exact copy drift and non-blocking scope | Эта spec, README sources and release evidence |
| EXEC | README content refresh | 0.99 | Future release assets can drift after audit | Run structural and source-backed checks | Нет | User confirmed this spec | Rewrote EN/RU together, kept media, removed internal delivery prose and corrected public facts | `README.md`, `README.RU.md` |
| EXEC | Post-EXEC review | 0.99 | Markdown rendering in every external viewer is not evaluated | Ready for docs-only commit | Нет | Not required | H1, heading parity, local links, media paths, diff scope and storage evidence all pass | Эта spec and final README diff |
