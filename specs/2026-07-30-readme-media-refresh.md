# Обновление скриншотов README

## 0. Метаданные

- Тип (профиль): delivery-task; .NET Desktop Client + UI Automation Testing.
- Владелец: Kibnet.
- Масштаб: small.
- Целевое семейство / behavior baseline: GPT-5.6 family optimization baseline.
- Поверхность: Work / Codex desktop.
- Effective runtime: текущий Codex runtime; модель не влияет на контракт генерации изображений.
- Eval baseline / evidence: текущая ветка docs/readme-content-refresh на коммите 1b265a44; штатный script, TUnit UI suites, deterministic ReadmeDemo scenario, отчёты захвата и просмотр PNG/GIF.
- Целевой релиз / ветка: docs/readme-content-refresh.
- Ограничения:
  - на EXEC изменяются только media/readme/en, media/readme/ru и эта spec;
  - README.md, README.RU.md, исходный код, тесты, script и CI не меняются;
  - используются только штатные ReadmeDemo data и file names; реальные пользовательские данные не попадают в медиа;
  - временный output lives only under ignored artifacts/readme-media;
  - не изменяются размеры окна, порядок capture steps, подписи или ссылки в README.
  - approved fallback: перед capture обязательно проходит актуальный Headless UI suite; полный FlaUI suite не запускается как blocking gate, потому что две последовательные попытки были nondeterministically red despite an isolated task-card pass. Пользователь выбрал этот fallback 2026-08-24.
- Связанные ссылки:
  - scripts/update-readme-media.ps1;
  - tests/Unlimotion.ReadmeMedia/README.md;
  - tests/Unlimotion.ReadmeMedia/Program.cs;
  - tests/Unlimotion.UiTests.Headless;
  - tests/Unlimotion.UiTests.FlaUI;
  - README.md, README.RU.md;
  - previous README-content spec: specs/2026-07-24-readme-content-refresh.md.

## 1. Overview / Цель

Заменить устаревшие файлы изображений, на которые уже ссылаются корневые README, свежим output штатного UI automation harness.

Outcome contract:

- Success means:
  - по 10 PNG и 1 GIF для English и Russian созданы из текущего ReadmeDemo scenario и скопированы в media/readme/en и media/readme/ru;
  - Headless TUnit suite проходит перед capture; прежняя нестабильность полного FlaUI suite зафиксирована как residual risk, а не blocking precondition для этого media-only fallback;
  - новые изображения открываются, не пустые, соответствуют фиксированным именам и визуально показывают ожидаемые проекции;
  - README продолжает ссылаться на те же валидные пути без copy changes.
- Итоговый артефакт / output: 22 обновлённых committed media files, ignored capture reports under artifacts/readme-media and эта spec.
- Stop rules:
  - не заменять не прошедшие проверку изображениями вручную или изображениями с реальными данными;
  - не копировать output в media после падения тестов, capture error или неполного report;
  - остановиться и сообщить, если видимый desktop capture не может быть создан или inspection выявляет пустой, обрезанный либо неверно локализованный кадр.

## 2. Текущее состояние (AS-IS)

- Корневые README ссылаются на 11 медиафайлов на язык: tab-tour.gif и PNG для All Tasks, Last Created, Last Updated, Unlocked, In Progress, Completed, Archived, Last Opened, Roadmap и Settings.
- Текущие файлы датированы 2026-07-15, а README copy был обновлён на текущем main отдельным коммитом 1b265a44.
- tests/Unlimotion.ReadmeMedia использует ReadmeDemo and AppAutomation/FlaUI, захватывает desktop окно 1760x1060, создаёт report.json и умеет copy successful assets into media.
- scripts/update-readme-media.ps1 builds three relevant projects and launches generator with copy-to-media for en,ru; normal mode also invokes both UI suites, while the approved fallback uses SkipTests after an independent Headless pass.
- artifacts directory ignored; media/readme is committed user-facing output.

## 3. Проблема

После актуализации смысла README визуальные примеры остались от более раннего снимка UI и перестали быть доказательством текущего состояния продукта.

## 4. Цели дизайна

- Использовать единственный существующий путь генерации, а не ручную съёмку.
- Сохранить стабильные имена и структуру медиа, чтобы ссылки README не менялись.
- Получить UI-test, machine-readable report и ручной visual evidence для каждого языка.
- Не смешивать refresh visual assets с изменением UI design, behavior или README copy.
- Обеспечить простой rollback через revert media-only commit.

## 5. Non-Goals (чего НЕ делаем)

- Не меняем product code, UI layout, automation selectors, test code, script или source data.
- Не меняем README text, captions, headings or local links.
- Не добавляем изображения, видео, новые tab capture steps или alternate device viewports.
- Не публикуем, не создаём PR и не изменяем релизные артефакты в этой задаче.
- Не сохраняем отдельное before-video: это не UI feature/bugfix, а регенерация deterministic visual documentation.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- scripts/update-readme-media.ps1 -> единственный orchestration command: build, capture and copy; approved fallback invokes it with SkipTests after a separate serial Headless gate.
- tests/Unlimotion.ReadmeMedia -> фиксирует window size, ReadmeDemo scenario, ten capture steps per language, GIF and reports.
- tests/Unlimotion.UiTests.Headless -> required acceptance gate before fallback capture; tests/Unlimotion.UiTests.FlaUI -> recorded non-blocking residual risk for this approved fallback.
- media/readme/en and media/readme/ru -> only committed output.
- artifacts/readme-media/20260824-readme-refresh -> ignored inspection output and reports; не коммитится.

### 6.2 Детальный дизайн

1. Выполнить current serial Headless UI suite. Затем выполнить штатный script с SkipTests, output root artifacts/readme-media/20260824-readme-refresh and languages en,ru. Script still builds relevant projects and clears only the selected output/known generated targets before copying successful capture.
2. Для каждого языка требуются all-tasks.png, last-created.png, last-updated.png, unlocked.png, in-progress.png, completed.png, archived.png, last-opened.png, roadmap.png, settings.png and tab-tour.gif.
3. Проверить root and language report.json: scenario ReadmeDemo, both language reports, eleven assets per language and no warning/error that invalidates capture.
4. Проверить существование, non-zero size and decodability всех PNG/GIF; открыть representative All Tasks, Roadmap and Settings для EN/RU plus the GIF.
5. Сверить git diff: изменены только ожидаемые медиафайлы and spec; README paths still resolve.

Visual planning artifact: Не применимо к UI design — layout и flow не проектируются заново. Fallback visual contract: fixed ReadmeDemo at 1760x1060, ten named projections per language and tab-tour.gif in the order defined by Program.cs.

UI test video evidence: Не применимо. Задача не меняет UI behavior or feature flow; generated PNG and tab-tour.gif are the user-facing artefacts and visual evidence. The GIF plus inspected PNGs are the fallback evidence.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| English README reader | Opens any English screenshot or tab tour | Current deterministic English UI is visible at the existing path | en report, file checks, visual inspection | AC-01, AC-03 |
| Russian README reader | Opens any Russian screenshot or tab tour | Current deterministic Russian UI is visible at the existing path | ru report, file checks, visual inspection | AC-01, AC-03 |
| Repository maintainer | Regenerates media with documented command | Tests gate capture and only expected paths change | script output, TUnit results, git diff | AC-02, AC-04 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Existing committed media | Successful script run | Fresh output copied atomically per file to same media paths | Test/capture error stops EXEC; do not commit partial output | Script owns stale generated-file deletion |
| Ignored artifacts output | Script start | Output root is cleared and recreated | Existing timestamped root may be replaced | Only artifacts/readme-media/20260730-readme-refresh is selected |
| README link | Media replacement | Same relative link displays new file | Missing media is blocker | README text remains unchanged |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Capture path | agent | Existing update-readme-media.ps1 with both languages | 1.00 | Bypassing harness could introduce non-deterministic/manual assets | Нет |
| Artifact scope | agent | All 22 current referenced assets, no selective refresh | 1.00 | Partial refresh leaves EN/RU inconsistent | Нет |
| Test gate | user | Current serial Headless pass, then script build/capture with SkipTests; full FlaUI is an approved non-blocking residual risk | 1.00 | Full desktop UI coverage is weaker while FlaUI remains nondeterministically red | Нет |
| Inspection depth | agent | Decode all assets; visually inspect EN/RU All Tasks, Roadmap, Settings and GIF | 0.95 | A minor issue in another tab could evade manual viewing | Нет |
| Temporary evidence | agent | Ignored timestamped artifacts output; commit only media and spec | 1.00 | Untracked output must not be accidentally staged | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| README media paths | README.md and README.RU.md | File content refresh, same paths/names | No link migration | local-link check |
| Capture data | ReadmeDemo scenario | None | No persisted data change | report.json |
| UI behavior and selectors | application/UI test projects | None | No behavior contract change | serial UI tests |
| Temporary output | artifacts/readme-media | Fresh ignored inspection evidence | Recreated on each run | git status and .gitignore |

## 7. Бизнес-правила / Алгоритмы (если есть)

1. Only a successful project script may replace committed README media.
2. Every capture run includes both en and ru unless a separate user-approved scope says otherwise.
3. File names are the README contract; replacement preserves all eleven names per language.
4. A Headless-test failure, incomplete report, missing asset, decode failure or visual capture defect blocks commit. The full FlaUI suite is not rerun under the explicit approved fallback.
5. Temporary reports remain ignored; user-facing media only comes from deterministic ReadmeDemo.

## 8. Точки интеграции и триггеры

- User request triggers media refresh.
- serial Headless test and update-readme-media.ps1 with SkipTests trigger the approved fallback capture.
- Copy-to-media updates files consumed directly by root README links.
- No application runtime event, configuration or release workflow changes.

## 9. Изменения модели данных / состояния

Не применимо: generated static documentation assets replace prior assets; task storage and application state are unchanged.

## 10. Миграция / Rollout / Rollback

- Rollout: commit only the 22 generated assets and approved spec after all validation passes.
- Backward compatibility: filenames, directories and README links are unchanged.
- Rollback: revert the media-refresh commit; the previous committed images return.
- Cleanup: ignored artifacts/readme-media/20260730-readme-refresh may remain for local inspection and is never staged.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- AC-01: exactly 11 generated assets exist in each of media/readme/en and media/readme/ru with the existing filenames; every file is non-empty and decodable.
- AC-02: current serial Headless TUnit suite passes before capture; the script completes Debug builds and capture with SkipTests under the user-approved FlaUI fallback.
- AC-03: reports identify ReadmeDemo, both languages and eleven captured assets per language; visual inspection confirms current localized All Tasks, Roadmap, Settings and tab-tour output.
- AC-04: README.md and README.RU.md are unchanged; their local media links remain valid, and the final diff contains no code, scripts, CI or generated artifacts files.
- AC-05: the final worktree contains only committed media files and spec; ignored inspection artifacts are explicitly excluded from staging.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-01 | PowerShell asset-count, size and decode check | Inspect output directories | media paths and file metadata | All files checked automatically |
| AC-02 | serial Headless TUnit run; script build/capture with SkipTests | Record previous FlaUI diagnostics and user-approved exception | console output + spec decision ledger | Full FlaUI is nondeterministically red; user explicitly selected fallback |
| AC-03 | report.json structure/count check | View representative six PNG and two GIF files | reports + inspected images | Every type is decoded automatically |
| AC-04 | local Markdown link check; git diff allowlist | Read diff/stat | command output | README remains unchanged |
| AC-05 | git status and staged-file review | Confirm ignored artifact location | git output | No exception |

Expected execution command:

~~~powershell
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -- --maximum-parallel-tests 1 --output Detailed
pwsh -File scripts/update-readme-media.ps1 -SkipTests -OutputRoot artifacts/readme-media/20260824-readme-refresh -Languages en,ru
~~~

Stop rules: after a timeout, inspect current process/log/output before retrying; after Headless test or capture failure, do not copy/commit partial assets. Do not infer that prior isolated FlaUI success makes a full FlaUI run green.

## 12. Риски и edge cases

- Visible FlaUI capture can fail in the current desktop session. Mitigation: use the established script, retain reports/logs and stop rather than manually faking a replacement.
- A window may render incorrectly at current DPI. Mitigation: harness sets a fixed logical desktop viewport; inspect representative layout-sensitive tabs for both languages.
- Stale output may look successful. Mitigation: script clears chosen output root and overwrites media only after capture; verify reports, timestamps and diff.
- Large GIFs may create noisy diffs. Mitigation: accept only deterministic generator output; do not recompress or edit manually.
- Artifacts are ignored and can be confused with changes. Mitigation: final staged-file allowlist excludes artifacts.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Screenshots were not actually refreshed | Previous task intentionally skipped media | Timestamped script output, reports and committed media diff | mitigated |
| UI test failures were ignored | FlaUI full suite was nondeterministically red | Headless reruns; explicit user-approved FlaUI fallback and visual/report validation are recorded | accepted-risk |
| Russian screenshots still show English UI | Two localized README versions exist | Both language reports and visual inspection | mitigated |
| README text or code changed as a side effect | Scope was previously widened | strict media/spec allowlist and diff check | mitigated |

### Rework Prevention Checklist

- User-visible result named: yes, fresh EN/RU screenshots and tab tours at existing README paths.
- Every scenario has evidence: yes, report, test, file, link and visual checks map to ACs.
- Agent assumptions listed: yes, Decision Ledger.
- Likely objections addressed: yes, four rows above.
- Role-based review applicability recorded: yes, section 19.
- Acceptance criteria are verifiers: yes.
- EXEC proof path: yes, script plus reports, media inspection and diff allowlist.

## 13. План выполнения

1. Create this media-only spec and obtain explicit approval.
2. Preflight toolchain and confirm a clean branch at 1b265a44.
3. Run current serial Headless tests, then the documented script with SkipTests and both languages into timestamped ignored output.
4. Validate reports, all assets, Markdown links, TUnit output and diff allowlist; inspect representative localized views and GIF.
5. Perform post-EXEC review, commit only media/spec and report generated paths/results.

## 14. Открытые вопросы

Нет блокирующих вопросов.

## 15. Соответствие профилю

- Профиль: .NET Desktop Client + UI Automation Testing.
- Выполненные требования профиля: UI behavior, selectors and code are unchanged; the deterministic AppAutomation harness and current Headless UI suite run before capture, and screenshot/GIF output is inspected. Full FlaUI is an explicit user-approved residual risk due nondeterministic failures. New UI test coverage and before-video are not applicable because no feature or defect behavior changes.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| specs/2026-07-30-readme-media-refresh.md | New working spec | Isolate new media scope from completed copy refresh |
| media/readme/en/*.png, media/readme/en/tab-tour.gif | Regenerated English output | Current visual evidence for English README |
| media/readme/ru/*.png, media/readme/ru/tab-tour.gif | Regenerated Russian output | Current visual evidence for Russian README |

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Screenshot source | 2026-07-15 generated assets | Fresh current-branch ReadmeDemo capture |
| EN/RU coverage | Existing files may lag current README/UI | Both languages regenerated in one run |
| Validation | Prior copy-only validation did not inspect media | Current Headless test, documented FlaUI fallback, reports, decode/link/diff checks and visual inspection |
| README contract | Stable media paths | Same file names and paths with refreshed contents |

## 18. Альтернативы и компромиссы

- Вариант: снять окна вручную средствами ОС.
  - Плюсы: quick capture.
  - Минусы: non-deterministic state, possible real data and missing test gate.
- Вариант: обновить only selected PNG.
  - Плюсы: smaller binary diff.
  - Минусы: inconsistent visual age between languages/tabs.
- Выбранный вариант: existing full harness for all assets. It is the smallest reliable way to keep README visual evidence consistent without changing product behavior.

- Fallback amendment (user-approved): run the same harness with SkipTests after a fresh successful Headless pass.
  - Плюсы: current deterministic user-facing media can be refreshed despite a demonstrably flaky full FlaUI gate.
  - Минусы: no full green FlaUI proof accompanies this refresh.
  - Ограничение: approved only for this media-only change; it does not reclassify the FlaUI failures as passing.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Goal, AS-IS, one root problem, design goals and strict Non-Goals are explicit. |
| B. Качество дизайна | 6-10 | PASS | Existing script, harness, data source, outputs and rollback are assigned. |
| C. Безопасность изменений | 11-13 | PASS | Media-only allowlist, deterministic synthetic data and no runtime/config change. |
| D. Проверяемость | 14-16 | PASS | Five AC map to current Headless TUnit, reports, file/link/diff checks and visual inspection; the FlaUI exception is explicit. |
| E. Готовность к автономной реализации | 17-19 | PASS | No user-owned decision remains; exact command and stops are provided. |
| F. Соответствие профилю | 20 | PASS | Existing UI automation harness is run proportionally to a visual-asset refresh. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Exact asset count, filenames, allowlist and non-goals are named. |
| 2. Понимание текущего состояния | 5 | Script, Program capture list, existing asset dates and README consumers were inspected. |
| 3. Конкретность целевого дизайна | 5 | One deterministic capture path and output/report contract are specified. |
| 4. Безопасность (миграция, откат) | 5 | Synthetic data, ignored temporary output and commit/revert boundary are explicit. |
| 5. Тестируемость | 5 | Headless UI test, reports, decode, link, visual and diff checks are mapped; the approved FlaUI exception is bounded. |
| 6. Готовность к автономной реализации | 5 | No open design fork; same script is repository-standard. |

Итоговый балл: 30 / 30.
Зона: готово к автономному выполнению.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | not applicable | No domain rules or workflow behavior change | PASS | None |
| UX / designer | applicable | Do current visual artefacts truthfully show localized task projections without a new layout decision? | PASS | Define representative visual inspection. |
| Tester / validation | applicable | Does every asset refresh have a test/report/file/visual evidence path? | PASS | Map ACs to script and reports. |
| Developer / architect | applicable | Are existing capture contracts, file names and rollback boundaries preserved? | PASS | Keep generator unchanged. |
| Delivery / operations / security | applicable | Are generated files isolated, rollbackable and free of real user data? | PASS | Use ignored artifacts and ReadmeDemo only. |

### Post-SPEC Review

- Статус: PASS.
- Scope reviewed: this spec; README media file inventory; README consumers; update-readme-media.ps1; Program capture steps; README Media guide; TUnit project references; local .gitignore; current clean branch at 1b265a44.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: exact two language directories and 22 media outputs match generator and README links.
  - Contract pass: plan leaves code, selectors, text, filenames and paths unchanged; its amended validation contract is current Headless plus an explicitly authorized FlaUI fallback.
  - Adversarial risk pass: rejected manual capture, selective refresh, real data and accidental staging of artifacts; only the full FlaUI gate is excluded by an explicit user decision.
  - Role-Based pass: applicable UX, validation, engineering and operations review areas are recorded above.
  - Re-review after fixes / Fix and re-review: first draft corrected an inaccurate nine-PNG count to ten per language and added a staged-file allowlist.
  - Stop decision: PASS; no human product choice blocks EXEC.
- Evidence inspected: current 2026-07-15 media inventory; Program capture list, dimensions and language set; script build/test/copy contract; README media references; media generator guide; .gitignore; prior full/isolated FlaUI diagnostics; user fallback decision.
- Depth checklist:
  - Scope drift / unrelated changes: media/spec allowlist prevents code or README copy changes.
  - Acceptance criteria: five observable evidence-backed conditions.
  - User-observable scenarios / Decision ledger / Expected objections: populated.
  - Validation evidence: current Headless TUnit, prior FlaUI diagnostics, report, decode, link, diff and visual paths specified.
  - Unsupported claims: no compatibility or product claims added.
  - Regression / edge case: failed Headless or partial capture blocks copy/commit; full FlaUI flake is an approved residual risk.
  - Comments/docs/changelog: README prose and changelog unchanged; media guide already documents the standard workflow.
  - Hidden contract change: filenames and README paths remain fixed.
  - Manual-review challenge: inspect that EN/RU UI is not swapped, truncated or blank and that no ignored artifact is staged.
- No-findings justification: known harness and bounded output give one objectively best path; all residual visual risks have direct checks.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | visual evidence | Representative review cannot manually inspect every pixel of every frame | Decode all assets and inspect layout-sensitive views plus GIF | mitigated |
| LOW | generated artifacts | Ignored reports can be accidentally staged via broad Git commands | Stage exact media/spec paths only | mitigated |
| LOW | environment | Desktop capture depends on an active compatible Windows session | Stop with logs rather than fabricate assets | follow-up |
| LOW | validation | Full FlaUI gate is deliberately not rerun | Record the prior red result and user-approved fallback; do not claim full UI green | accepted-risk |

- Fixed before continuing: PNG count corrected to ten per language after source inspection.
- Checks rerun: capture list, README paths, script contract, media inventory and historical FlaUI evidence.
- Needs human: user selected option 2 on 2026-08-24; no further approval needed.
- Residual risks / follow-ups: full FlaUI stabilization remains separate work.

### Post-EXEC Review

- Статус: ASK-HUMAN.
- Scope reviewed: approved spec; standard and fallback capture attempts; isolated and full FlaUI diagnostics; fresh serial Headless TRX; git status and media diff; existing UI test source/selector evidence.
- Decision: fallback was user-approved on 2026-08-24 and Headless passed, but both capture attempts failed on the same FlaUI locator before media copy. This media-only EXEC stops; a generator/UI-automation stabilization task needs separate scope and approval.
- Review passes:
  - Scope/Evidence pass: the standard script stopped at its FlaUI gate; both approved `SkipTests` captures then stopped at the generator's required FlaUI locator. No files under media/readme changed.
  - Contract pass: the original test-before-copy rule was honored. The user then approved a narrowly scoped SkipTests fallback after a fresh Headless gate; UI tests/code remain out of scope.
  - Adversarial risk pass: an isolated originally failing test passed 1/1, but the fresh full suite still failed 5/12 with a different set, including a tooltip interaction. The clean retry failed after no matching UI-automation process was found, so concurrent-process contention is not supported as the sole cause.
  - Role-Based pass: UX/validation requires not publishing a visual artefact behind a failing desktop UI gate; engineering/operations requires an explicit decision before changing the test contract.
  - Re-review after fixes / Fix and re-review: no fix is allowed in this media-only scope.
  - Stop decision: both capture attempts failed and no partial media change exists. Stop without a code/test change; ask the user whether to open a separate stabilization spec or retain the current media.
- Evidence inspected:
  - standard script: build and Headless gate completed; FlaUI failed 6/12 after about 11.5 minutes;
  - isolated Task_card_can_be_closed_and_reopened_from_details_toggle: PASS 1/1 in 43 seconds;
  - fresh full FlaUI run: FAIL 5/12 in 5m08s; failures included the tooltip hover and missing current-task controls;
  - fresh serial Headless run: PASS 36/36; TRX: `artifacts/readme-media/20260824-readme-refresh-headless-test/Kibnet_DESKTOP-AUDO1TJ_2026-08-23_22_29_58.0390176.trx`;
  - fallback capture: all three Debug builds passed, then ReadmeMedia threw `ElementNotAvailableException` for `CurrentTaskTitleTextBox` before copying media; `git diff -- media/readme` remains empty;
  - process check after the failure: no remaining Unlimotion/AppAutomation/ReadmeMedia/UiTests process was found.
  - clean-session retry with `-SkipTests -NoBuild`: failed at the identical `CurrentTaskTitleTextBox` locator; `git status --short` still shows only this spec and `git diff -- media/readme` is empty.
  - MainControl XAML and test host confirm the missing AutomationIds are declared and DetailsAreOpen is forced true on launch;
  - git diff for media/readme remains empty.
- Depth checklist:
  - Scope drift / unrelated changes: no product/media file was written; only this spec is uncommitted.
  - Acceptance criteria: Headless part of AC-02 passed, but successful capture and the remaining asset/report criteria are still unmet.
  - User-observable scenarios / Acceptance-to-test matrix / Expected objections: the predicted UI-test objection materialized and is an explicit accepted risk after the user decision.
  - Validation evidence: fresh Headless 36/36 is green; red/inconsistent FlaUI evidence is retained as an explicit residual risk; both fallback captures are red before copy.
  - Unsupported claims: no claim of refreshed media is made.
  - Regression / edge case: potentially stateful/desktop-session FlaUI flake remains a separate stabilization follow-up.
  - Comments/docs/changelog: unchanged.
  - Hidden contract change: SkipTests is an explicit user-approved validation exception, recorded in this spec.
  - Manual-review challenge: publishing screenshots now would hide a failed UI gate and could make stale/broken UI look validated.
- No-findings justification: Не применимо; blocking evidence finding exists.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | validation | Full FlaUI suite is nondeterministically red: initial 6/12 and fresh 5/12 fail, while one isolated failing test passes | Do not claim full UI green; move root-cause work to a separate stabilization scope | accepted-risk |
| HIGH | capture evidence | ReadmeMedia itself cannot resolve `CurrentTaskTitleTextBox` even in a clean session | Do not alter screenshots manually; require a separately approved generator/UI-automation stabilization task | ask-human |
| LOW | scope | Build/test result files and ignored artifacts exist locally | Keep them untracked and stage only an approved future change set | mitigated |

- Fixed before final report: no code/test fix is in scope; validation contract amended with explicit user approval.
- Checks rerun: isolated test, fresh full FlaUI run, fresh serial Headless 36/36, two fallback captures, post-failure process/diff check and final no-media-diff check.
- Validation evidence: fresh Headless 36/36 is green; red/inconsistent FlaUI evidence is retained as an explicit residual risk; both capture attempts failed before copy.
- Unrelated changes: only this untracked spec.
- Needs human: choose whether to approve a new, separate generator/UI-automation stabilization spec or keep the existing screenshots. The current approved scope cannot deliver updated media.
- Residual risks / follow-ups: full FlaUI and generator-capture stabilization are separate work; images remain at their prior version.

## Approval

Ожидается фраза: Спеку подтверждаю

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Scope discovery | 1.00 | Нет | Inspect canonical generator and current media | Нет | User asked to update screenshots if they were not refreshed | Prior spec deliberately excluded media; new scope must be isolated | Script, generator guide, media inventory |
| SPEC | Capture contract audit | 0.99 | Desktop session availability only | Request approval for media-only EXEC | Да | Ожидается Спеку подтверждаю | Existing script has one deterministic path, test gate and stable filenames | Эта spec and source evidence |
| EXEC | Approval gate | 1.00 | Нет | Run toolchain preflight and standard capture script | Нет | User confirmed Спеку подтверждаю on 2026-07-31 | New scope now has explicit authorization; implementation remains media-only | Эта spec |
| EXEC | Standard capture attempt | 1.00 | Root cause of FlaUI missing elements | Inspect failure report and repository state before any retry | Нет | No new user decision requested | Headless/build steps completed, but FlaUI had 6 missing-card-control failures; script correctly stopped before media copy | Ignored build/test output; no committed media changed |
| EXEC | Isolated FlaUI characterization | 0.98 | Whether failure leaks only across full test sequence | Run full FlaUI suite once in a fresh process | Нет | No new user decision requested | The originally failing task-card test passed 1/1 in a new desktop session | FlaUI test report only |
| EXEC | Fresh full FlaUI verification | 0.98 | Whether a clean suite is green | Stop and request a scope decision | Да | Fresh full run failed 5/12; failure set changed and included tooltip hover | Evidence shows nondeterministic desktop UI test state, not a completed capture gate | FlaUI report; no media diff |
| EXEC | Fallback authorization | 1.00 | Нет | Rerun Headless, then capture with SkipTests | Нет | User selected option 2 on 2026-08-24 | This is a scoped exception to the full FlaUI gate, not a claim that its failures are harmless | Эта spec |
| EXEC | Headless fallback gate | 1.00 | Нет | Run the standard capture script with SkipTests | Нет | Нет | Fresh serial suite passed 36/36; TRX is retained under ignored artifacts | `artifacts/readme-media/20260824-readme-refresh-headless-test/` |
| EXEC | Fallback capture attempt | 1.00 | Whether concurrent desktop automation steals the FlaUI session | Verify no matching process, then allow one clean-session retry | Нет | Нет | Builds passed, but capture again could not resolve `CurrentTaskTitleTextBox`; no media file changed | Ignored output root; no media diff |
| EXEC | Clean-session capture retry | 1.00 | Нет | Stop media-only EXEC and request stabilization-scope decision | Да | Ожидается решение пользователя | The identical locator failed without other matching automation processes; a third retry would add no new evidence | Ignored output root; no media diff |
