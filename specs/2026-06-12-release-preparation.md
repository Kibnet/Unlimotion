# Подготовка релизных материалов 1.26.0

## 0. Метаданные
- Тип (профиль): `delivery-task`; профиль `dotnet-desktop-client`; contexts `testing-dotnet`, `visual-feedback`; governance `github-delivery-policy`
- Владелец: Codex
- Масштаб: medium
- Целевая модель: gpt-5.5
- Целевой релиз / ветка: `main` at `d0f2e1d`; proposed release `1.26.0`
- Ограничения:
  - Фаза `SPEC`: до подтверждения менять только этот файл.
  - Переход в EXEC только после фразы `Спеку подтверждаю`.
  - Не публиковать GitHub Release, не создавать tag и не push-ить изменения без отдельного запроса.
  - Не включать секреты, реальные пользовательские данные, уведомления или сторонние окна в скриншоты/видео.
  - Для UI-facing documentation/media refresh использовать существующие UI/media harnesses и запускать релевантные UI checks.
- Связанные ссылки:
  - `README.md`
  - `README.RU.md`
  - `media/readme/en/*`
  - `media/readme/ru/*`
  - `scripts/update-readme-media.ps1`
  - `tests/Unlimotion.ReadmeMedia/Program.cs`
  - `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`
  - `specs/2026-06-09-task-status-model.md`
  - `specs/2026-06-08-emoji-filter-text-search.md`

Если секция не применима, явно указано `Не применимо`.

## 1. Overview / Цель
Подготовить release-ready набор материалов для следующего релиза Unlimotion после `1.25.1`: обновить README-документацию и скриншоты, составить GitHub release notes, Telegram release post, сценарий демонстрационного видео и собрать итоговый MP4.

Outcome contract:
- Success means:
  - Локальный `main` синхронизирован с GitHub `origin/main` и релизная дельта считается от `1.25.1` до `d0f2e1d`.
  - README на английском и русском отражают новые статусы, вкладку `In Progress` / `Выполняется`, searchable emoji filter и важные пользовательские изменения релиза.
  - README screenshots/GIF пересобраны из deterministic demo workspace и соответствуют свежему UI.
  - Если README содержит вкладку `In Progress`, media harness генерирует `in-progress.png` для `en` и `ru`, либо в отчете явно зафиксирован обоснованный отказ.
  - GitHub release notes готовы в Markdown и сгруппированы по `Highlights`, `Added`, `Changed`, `Fixed`, `Validation`, `Known Issues`.
  - Telegram post готов на русском, короче и маркетинговее GitHub notes, без неподтвержденных обещаний.
  - Video script описывает кадры, текстовые титры и порядок демонстрации.
  - Итоговый MP4 сохранен, проверен `ffprobe`, визуально проверен хотя бы по ключевым кадрам.
- Итоговый артефакт / output:
  - Изменения в `README.md`, `README.RU.md`, `media/readme/en/*`, `media/readme/ru/*` и при необходимости в `tests/Unlimotion.ReadmeMedia/*` / `tests/Unlimotion.UiTests.Authoring/*`.
  - Release artifacts under `artifacts/release/1.26.0/`: `github-release-notes.md`, `telegram-post.md`, `video-script.md`, raw capture/edit assets, final MP4.
- Stop rules:
  - Остановиться до EXEC, пока пользователь не подтвердит spec фразой `Спеку подтверждаю`.
  - Остановиться и запросить решение, если нужно выбрать между release base `HEAD` и другим commit/tag.
  - Не завершать EXEC, если media generator не прошел и нет next-best fallback evidence.
  - Не выдавать видео как готовое, пока файл не существует, не имеет ненулевой размер и не проверен по duration/dimensions.

## 2. Текущее состояние (AS-IS)
- Локальный `main` подтянут fast-forward с GitHub: `HEAD -> main, origin/main` = `d0f2e1d`.
- Последний локальный stable tag: `1.25.1` (`deaea65`, 2026-05-14).
- Release delta `1.25.1..HEAD` включает merge PR:
  - `#244` inline task title editor polish.
  - `#245` UI runner/test stability.
  - `#246` Apple Silicon macOS packaging and macOS emoji font crash fix.
  - `#247` Roadmap search no rebuild.
  - `#248` All Tasks search selection restore.
  - `#249` Android back gesture/task card fixes.
  - `#250/#241/#251` task card redesign and dense responsive polish.
  - `#252` main tabs overflow layout.
  - `#253` compact filter toolbar redesign.
  - `#254` SSH key storage path setting and known_hosts routing.
  - `#255` mobile conflict resolver visibility.
  - `#256` headless UI suite stabilization.
  - `#258` searchable emoji filter dropdown.
  - `#257` task status model with lifecycle statuses, status picker, in-progress tab, migration and tests.
- `README.md` and `README.RU.md` already describe the new five-status model and `In Progress` section after the pull.
- Current committed `media/readme/en/*` and `media/readme/ru/*` are still dated 2026-04-27 and therefore predate the recent UI redesign/status changes.
- `tests/Unlimotion.ReadmeMedia/Program.cs` currently captures `all-tasks`, `last-created`, `last-updated`, `unlocked`, `completed`, `archived`, `last-opened`, `roadmap`, `settings`, plus `tab-tour.gif`; it does not capture `in-progress.png`.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` currently lacks `InProgressTabItem` / `InProgressTree` page object bindings even though `MainControl.axaml` has those automation ids.
- `scripts/update-readme-media.ps1` is the existing README media workflow: builds UI test/media projects, runs Headless and FlaUI UI tests sequentially, regenerates screenshots/GIF and copies them into `media/readme/*`.
- `ffmpeg.exe` and `ffprobe.exe` are available on PATH; ImageMagick `magick` is not available, and Windows `convert.exe` must not be treated as ImageMagick.

## 3. Проблема
Релизная дельта содержит крупные пользовательские изменения, но публичные материалы не готовы как единый release package: README screenshots устарели, `In Progress` не имеет generated screenshot, release notes / Telegram post / video script / MP4 еще не созданы.

## 4. Цели дизайна
- Разделение ответственности:
  - README описывает устойчивые пользовательские возможности.
  - `Unlimotion.ReadmeMedia` отвечает за воспроизводимые screenshots/GIF.
  - Release artifacts in `artifacts/release/1.26.0/` отвечают за GitHub/Telegram/video delivery copy и не становятся каноническим кодом приложения.
- Повторное использование:
  - Использовать `scripts/update-readme-media.ps1` вместо ручного скриншотинга README.
  - Использовать synthetic `ReadmeDemo` workspace для скриншотов и видео.
- Тестируемость:
  - Проверять media refresh через сборку и UI tests, которые запускает `scripts/update-readme-media.ps1`.
  - Проверять MP4 через `ffprobe` и keyframe inspection.
- Консистентность:
  - Сохранять пары English/Russian README и media file names.
  - Держать release notes фактически привязанными к `git log 1.25.1..HEAD`, README и specs.
- Обратная совместимость:
  - Не менять пользовательскую модель, storage, ViewModel API или release workflows в рамках этой задачи, кроме media harness extension для документации.

## 5. Non-Goals (чего НЕ делаем)
- Не публикуем GitHub Release.
- Не создаем и не пушим tag `1.26.0`.
- Не собираем release binaries/packages.
- Не меняем продуктовый UI/UX за пределами automation/media support, нужного для README screenshots.
- Не добавляем новые фичи и не исправляем unrelated bugs.
- Не меняем release workflow YAML.
- Не записываем видео с голосом или системным аудио; тихая фоновая музыка без вокала допустима по пользовательскому решению `13Б`.
- Не используем реальные пользовательские задачи или приватные репозитории для демонстрации.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `README.md` -> English documentation updates, release-relevant feature descriptions and `in-progress.png` reference if generated.
- `README.RU.md` -> Russian documentation updates aligned with English README.
- `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` -> add page bindings for `InProgressTabItem` and `InProgressTree` if needed by media generator.
- `tests/Unlimotion.ReadmeMedia/Program.cs` -> add `in-progress` capture step and generated file name support through existing step list.
- `media/readme/en/*` and `media/readme/ru/*` -> regenerated PNG/GIF assets copied by `scripts/update-readme-media.ps1`.
- `artifacts/release/1.26.0/github-release-notes.md` -> GitHub release body draft.
- `artifacts/release/1.26.0/telegram-post.md` -> Telegram post draft in Russian.
- `artifacts/release/1.26.0/video-script.md` -> storyboard/script with frame order, captions and source assets.
- `artifacts/release/1.26.0/video/*` -> raw captures, extracted keyframes, final MP4.

### 6.2 Детальный дизайн
- Release baseline:
  - Source range: `1.25.1..HEAD`.
  - Version proposal: `1.26.0`, because release includes new user-facing features, not only fixes.
  - Existing repo tags do not use mandatory `v`; workflows accept numeric SemVer and `v`-prefixed tags, but local convention is numeric.
- Documentation:
  - Keep conceptual status model section already pulled from `main`.
  - Add/verify user-facing descriptions for:
    - lifecycle statuses and status picker;
    - `In Progress` tab;
    - compact task card and creation/action menus;
    - searchable emoji filter dropdown;
    - compact filter toolbar / tabs overflow if useful in interface docs;
    - SSH key storage path setting only if Settings section needs a concise note.
  - Add `in-progress.png` reference under `In Progress` / `Выполняется` when media generation supports it.
  - Keep README EN/RU aligned by section order and media names.
- Screenshots/GIF:
  - Extend page object and `ReadmeMedia` only as needed to include `InProgress`.
  - Run `scripts/update-readme-media.ps1`.
  - Inspect representative outputs with `view_image`: at minimum `en/all-tasks.png`, `en/in-progress.png`, `en/roadmap.png`, `ru/in-progress.png`, `ru/settings.png`.
  - Keep generated `report.json` under `artifacts/readme-media/latest/` as evidence; only PNG/GIF under `media/readme/*` are committed candidates.
- GitHub release notes:
  - Draft in Markdown, grouped:
    - `Highlights`
    - `Added`
    - `Changed`
    - `Fixed`
    - `Validation`
    - `Known Issues`
  - Include migration note for new status model and old active tasks migrating to `Not ready` / `Не готово`.
  - Include platform note for Apple Silicon and Android/OpenSSL/versioning where relevant.
  - Avoid claims not backed by commits/specs/tests.
- Telegram post:
  - Russian, concise, suitable for channel.
  - Lead with practical user value: statuses, current work tab, searchable filters, denser task card, mobile/responsive fixes.
  - Include one migration caveat about statuses and task storage Git rollback.
  - Avoid long technical validation details.
- Video planning artifact / storyboard:

```text
0-8s      Title and all-tasks context: one Russian demo task becomes the release storyline.
8-33s     Status picker: show all statuses, then move the task through `Не готово` -> `Подготовлено` -> `Выполняется`.
33-47s    `Выполняется` tab: current-work queue with the same task.
47-62s    Task card: description, completion criteria, plan and relations.
62-90s    Searchable emoji filters: search, select, list changes, reset, second search.
90-107s   Roadmap: graph context plus filters without losing the selected work context.
107-123s  Narrow screen: overflow menu for hidden tabs.
123-133s  Settings/backup: show only safe settings details, no personal paths or tokens.
133-145s  Completion and archive: move the same task to `Завершено`, then `Архив`, then show migration note.
```

- Video implementation:
  - Preferred path: record deterministic app window or deterministic media/GIF segments using `ffmpeg`, window capture and existing `ReadmeDemo` workspace.
  - Fallback path if live window capture is unstable: compose MP4 from regenerated README screenshots, `tab-tour.gif`, UX-review screenshots and text overlays using `ffmpeg`.
  - Use no voiceover; add quiet background music without vocals if a safe local/generated track is available.
  - Target duration: 2:10-2:25.
  - Keep raw captures and generated stills under `artifacts/release/1.26.0/video/raw/`.
  - Final output path: `artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4`.
- Output contract / evidence rules:
  - Every release text must cite internal evidence source in comments or footer metadata only if useful for review; final copy itself should be user-facing.
  - Video script must map each scene to a source asset or recording step.
  - Final report must include validation commands and paths to generated artifacts.
- UI test video evidence:
  - Не применимо as automated UI test run video, because existing UI test harness does not emit video.
  - Fallback evidence: `scripts/update-readme-media.ps1` UI test run + final MP4 + keyframe inspection.
- Обработка ошибок:
  - If `scripts/update-readme-media.ps1` fails due environment/single-instance/window capture, inspect logs and retry once after verifying no stale Unlimotion process masks the build.
  - If `InProgress` capture requires nontrivial test host changes beyond page object/capture step, stop and report finding rather than changing app behavior.
  - If `ffmpeg` window capture fails, use screenshot/GIF montage fallback and document the fallback in release artifacts.
- Производительность:
  - Media generation and video composition are offline release-prep tasks; no runtime performance impact expected.

## 7. Бизнес-правила / Алгоритмы (если есть)
- Release notes classification:
  - `feat(...)` and new user workflow merge PRs -> `Added` / `Highlights`.
  - `fix(...)` UI and platform changes -> `Fixed`.
  - dense redesign / filter toolbar / status model docs -> `Changed`.
  - test-only changes -> `Validation` unless they are user-visible stability improvements.
- Version proposal:
  - `1.26.0` because the delta contains new features: task status model, `In Progress` tab, searchable emoji filters, Apple Silicon packaging.
- Migration note:
  - Old active tasks (`IsCompleted=false`) migrate to `Not ready`; `Prepared` is an explicit user decision.
  - Rollback is expected through Git history of the task storage directory.

## 8. Точки интеграции и триггеры
- README media trigger: `scripts/update-readme-media.ps1`.
- Release evidence trigger: `git log --oneline 1.25.1..HEAD`, `git diff --stat 1.25.1..HEAD`, relevant specs and README.
- Video validation trigger: `ffprobe` plus keyframe extraction/screenshot inspection.
- No app runtime trigger is changed.

## 9. Изменения модели данных / состояния
- Не применимо: this task does not change application data model or persisted task state.
- Release notes will describe model changes already present in `HEAD`, especially migration behavior from the status model feature.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - Run docs/media update locally after spec approval.
  - Produce release artifacts under ignored `artifacts/release/1.26.0/`.
  - Optionally commit tracked docs/media changes only if user later asks for commit/push.
- Backward compatibility:
  - README/media changes do not alter app compatibility.
  - Media harness extension is build/test code only.
- Rollback:
  - Revert tracked README/media/harness changes.
  - Delete generated `artifacts/release/1.26.0/` if needed.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `git status --short --branch` shows `main...origin/main` before EXEC edits.
  - README EN/RU are aligned and do not reference missing media files.
  - `media/readme/en/in-progress.png` and `media/readme/ru/in-progress.png` exist if README references them.
  - All regenerated README PNG/GIF files exist with nonzero sizes.
  - `scripts/update-readme-media.ps1` completes, or failure is explained with logs and a next-best validation path.
  - Release notes mention the major release items and migration caveat.
  - Telegram post is Russian, concise and channel-ready.
  - Video script has scene-by-scene storyboard and source assets.
  - Final MP4 exists with nonzero size and `ffprobe` reports duration and dimensions.
  - Keyframes/screenshots from final MP4 visually match the storyboard.
- Какие тесты добавить/изменить:
  - No product behavior tests planned.
  - Update automation page object and media generator if needed for `InProgress`.
  - Existing Headless/FlaUI UI tests are run by `scripts/update-readme-media.ps1`.
- Characterization tests / contract checks:
  - `scripts/update-readme-media.ps1` acts as the README media contract.
  - `git diff --check` validates whitespace in tracked changes.
- Visual acceptance:
  - README screenshots show fresh UI with status icons/picker, dense task card, compact filters and no obvious clipping.
  - `In Progress` screenshot shows the tab content and started/elapsed metadata.
  - Video follows the storyboard and avoids unrelated desktop content.
- UI video evidence:
  - Final MP4 is the release demo artifact, not an automated UI test video.
  - If live capture is not reliable, fallback montage must be explicitly reported.
- Commands for verification:
  - `git status --short --branch`
  - `scripts/update-readme-media.ps1`
  - `dotnet build tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj -c Debug /p:UseSharedCompilation=false`
  - `git diff --check`
  - `ffprobe -v error -show_entries format=duration -show_entries stream=width,height -of default=noprint_wrappers=1 artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4`
  - keyframe extraction/inspection command to be finalized during EXEC based on the selected video path.
- Stop rules для test/retrieval/tool/validation loops:
  - Stop searching release history once `git log 1.25.1..HEAD`, merge PR list, specs and README explain all user-facing items.
  - Stop retrying media generation after one environment-focused retry unless a concrete fix is obvious and within spec.
  - Stop video iteration when storyboard coverage, nonzero MP4, `ffprobe` and keyframe inspection are all satisfied.

## 12. Риски и edge cases
- `ReadmeMedia` may fail on stale single-instance app window; mitigate by relying on script/test evidence and checking running windows/processes before retry.
- `InProgress` page object may require minor test authoring change; keep it scoped to automation selectors only.
- Screenshot/GIF diffs can be large; inspect before final report and avoid unrelated media changes.
- Video capture may include private desktop content if window capture falls back incorrectly; prefer deterministic app/window/screenshot montage.
- Release notes can overstate unvalidated behavior; tie every claim to commit/spec/README evidence.
- `artifacts/` is gitignored; release artifact paths are local deliverables, not committed source files.

## 13. План выполнения
1. Confirm EXEC via `Спеку подтверждаю`.
2. Re-check `git status --short --branch`, current `HEAD`, `git log 1.25.1..HEAD`.
3. Patch page object and `ReadmeMedia` to include `InProgress` capture if still missing.
4. Update README EN/RU text and add `in-progress.png` references if capture is added.
5. Run `scripts/update-readme-media.ps1`; if it fails, inspect logs and perform one targeted retry or fallback report.
6. Inspect generated screenshots/GIF key outputs.
7. Draft GitHub release notes, Telegram post and video script under `artifacts/release/1.26.0/`.
8. Record/compose the release demo MP4 under `artifacts/release/1.26.0/`.
9. Validate with `ffprobe`, keyframe inspection, `git diff --check` and relevant build/media commands.
10. Run post-EXEC review and report changed files, artifacts, validation and residual risks.

## 14. Открытые вопросы
- Нет блокирующих.
- Assumption for EXEC: release base is current `main` at `d0f2e1d`, proposed version `1.26.0`, no GitHub Release publication/tagging unless explicitly requested later.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client`
- Выполненные требования профиля:
  - Product UI behavior is not changed.
  - Stable automation ids are preserved; only missing page-object bindings may be added.
  - Relevant UI/media harness checks are planned.
  - `dotnet build`/UI checks are represented by `scripts/update-readme-media.ps1` and fallback commands.
- Context `visual-feedback`:
  - Window/screenshot/video artifacts are timestamped or release-scoped under `artifacts/release/1.26.0/`.
  - MP4 is checked by file existence, dimensions/duration and keyframes.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `README.md` | Align release documentation and `In Progress` screenshot reference | Public English docs |
| `README.RU.md` | Align release documentation and `Выполняется` screenshot reference | Public Russian docs |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Add `InProgressTabItem` / `InProgressTree` page bindings if needed | Deterministic media capture |
| `tests/Unlimotion.ReadmeMedia/Program.cs` | Add `in-progress.png` capture step if needed | README screenshot coverage |
| `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs` | Suppress one-time migration notice only in synthetic README demo config | Clean deterministic README screenshots |
| `media/readme/en/*` | Regenerated screenshots/GIF, possibly new `in-progress.png` | Fresh README media |
| `media/readme/ru/*` | Regenerated screenshots/GIF, possibly new `in-progress.png` | Fresh README media |
| `artifacts/release/1.26.0/github-release-notes.md` | New local release notes draft | GitHub Release copy |
| `artifacts/release/1.26.0/telegram-post.md` | New local Telegram post draft | Channel copy |
| `artifacts/release/1.26.0/video-script.md` | New local video scenario | Video production plan |
| `artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4` | New local final video | Release demo |
| `specs/2026-06-12-release-preparation.md` | Planning, quality gate and execution journal | QUEST governance |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Release baseline | Local `main` was behind GitHub | `main` is synced to `origin/main` at `d0f2e1d` |
| README status docs | Updated upstream but screenshots old | Docs and screenshots aligned with 1.26.0 UI |
| In Progress media | README section without generated screenshot | `in-progress.png` in EN/RU README media, unless blocked with explicit fallback |
| GitHub notes | Not prepared | Curated Markdown release body |
| Telegram post | Not prepared | Russian channel-ready post |
| Video | No script/MP4 | Storyboard plus final MP4 |

## 18. Альтернативы и компромиссы
- Вариант: keep release notes only in final chat.
  - Плюсы: no files outside tracked docs.
  - Минусы: hard to reuse for GitHub/Telegram/video workflow.
  - Почему не выбран: user asked to prepare multiple artifacts; local files are easier to review and reuse.
- Вариант: add tracked `docs/releases/1.26.0.md`.
  - Плюсы: release notes become repository history.
  - Минусы: no existing repo convention; may create a docs structure just for one release.
  - Почему не выбран: use ignored `artifacts/release/1.26.0/` for delivery copy unless user asks to preserve release notes in repo.
- Вариант: record live interactive video only.
  - Плюсы: closer to actual app usage.
  - Минусы: brittle window capture, possible single-instance/stale-window issues.
  - Почему выбран гибрид: try deterministic window/media flow first, keep screenshot/GIF montage fallback to guarantee deliverable.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals указаны. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, artifacts, media/video workflow, errors and rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Данные приложения не меняются; rollback и privacy constraints зафиксированы. |
| D. Проверяемость | 14-16 | PASS | Acceptance, media/UI checks, `ffprobe`, keyframe and diff checks заданы. |
| E. Готовность к автономной реализации | 17-19 | PASS | План EXEC и artifact paths конкретны; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | `dotnet-desktop-client`, visual-feedback and UI media checks отражены. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Release-prep output and Non-Goals are explicit. |
| 2. Понимание текущего состояния | 5 | Current tag, HEAD, README/media state and harness gaps are documented. |
| 3. Конкретность целевого дизайна | 5 | File responsibilities, artifact paths, release structure and video storyboard are concrete. |
| 4. Безопасность (миграция, откат) | 5 | No app data changes; rollback is revert/delete artifacts; privacy constraints included. |
| 5. Тестируемость | 5 | Media workflow, UI checks, `git diff --check`, `ffprobe` and visual inspection are defined. |
| 6. Готовность к автономной реализации | 5 | No blocking questions; ordered EXEC plan and stop rules are present. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-06-12-release-preparation.md`; instruction stack `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`, `visual-feedback`, `dotnet-desktop-client`, `github-delivery-policy`, local `AGENTS.override.md`; selected profile `dotnet-desktop-client`; open questions; planned changed files.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: checked current `main`, `HEAD=d0f2e1d`, latest tag `1.25.1`, release git log/stat, README EN/RU, media timestamps, `ReadmeMedia` capture steps, page object bindings, `ffmpeg/ffprobe` availability.
  - Contract pass: spec respects SPEC-only mutation rule, excludes release publication/tagging, includes visual artifacts, UI/media validation and privacy constraints.
  - Adversarial risk pass: identified stale README media, missing `InProgress` capture, artifact path being ignored by Git, live video capture brittleness and unsupported release claims.
  - Re-review after fixes / Fix and re-review: initial plan adjusted after `git pull` changed release baseline from `origin/main` with PR #258 only to `HEAD=d0f2e1d` including PR #257; checks reflected in AS-IS and plan.
  - Stop decision: PASS; no blocking findings remain before asking approval.
- Evidence inspected:
  - `git status --short --branch`
  - `git log --oneline --decorate -n 20`
  - `git log --merges 1.25.1..HEAD`
  - `git diff --stat 1.25.1..HEAD`
  - `README.md`
  - `README.RU.md`
  - `tests/Unlimotion.ReadmeMedia/README.md`
  - `tests/Unlimotion.ReadmeMedia/Program.cs`
  - `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`
  - `scripts/update-readme-media.ps1`
  - `media/readme/en/*`, `media/readme/ru/*` timestamps
- Depth checklist:
  - Scope drift / unrelated changes: release publication/tagging and binaries excluded.
  - Acceptance criteria: file, media, notes, post, script, MP4 and validation criteria covered.
  - Validation evidence: concrete commands and visual checks listed.
  - Unsupported claims: release claims must be tied to git/spec/README evidence during EXEC.
  - Regression / edge case: `InProgress` capture and stale single-instance app risks covered.
  - Comments/docs/changelog: README alignment and generated media explicitly in scope; no CHANGELOG exists in repo convention.
  - Hidden contract change: no app behavior/storage change planned.
  - Manual-review challenge: reviewer would likely ask whether `In Progress` screenshot is missing; spec makes it an acceptance criterion.
- No-findings justification: Spec includes the current release baseline, concrete artifact paths, validation commands and fallback strategy; no `BLOCKER`/`HIGH` finding remains.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | evidence | Initial release baseline was stale before user-requested pull. | Update spec to use `HEAD=d0f2e1d` and include PR #257. | fixed |
| LOW | artifacts | Release text/video artifacts under `artifacts/` are ignored by Git. | Explicitly document they are local deliverables, not committed source files. | fixed |

- Fixed before continuing: release baseline and artifact persistence notes.
- Checks rerun: manual SPEC linter/rubric and post-SPEC review after baseline update.
- Needs human: approval phrase `Спеку подтверждаю`.
- Residual risks / follow-ups: live video capture may require montage fallback; this is acceptable if reported with evidence.

### Post-EXEC Review
- Статус: PASS
- Scope reviewed: README EN/RU copy, regenerated README media, `ReadmeMedia`/AppAutomation test harness support, Russian release text artifacts, final MP4, validation commands and current git diff.
- Decision: можно завершать EXEC; публикация GitHub Release/tag/push не выполнялась и остается вне текущего scope.
- Review passes:
  - Scope/Evidence pass: PASS; release baseline remains `1.25.1..d0f2e1d`, changed files match docs/media/harness scope plus local ignored release artifacts.
  - Contract pass: PASS; product UI/storage behavior was not changed, only documentation, generated media and automation support were updated.
  - Adversarial risk pass: PASS; hidden-tab capture failure, migration toast in screenshots and video privacy risk were addressed.
  - Re-review after fixes / Fix and re-review: PASS; reran full `scripts/update-readme-media.ps1` after every capture-impacting fix and reinspected key images/video frames.
  - Stop decision: PASS; all acceptance criteria with local deliverables are satisfied.
- Evidence inspected:
  - `git status --short --branch`
  - `git diff --stat`
  - `git diff --check`
  - `pwsh -File scripts\update-readme-media.ps1`
  - `artifacts/readme-media/latest/report.json`
  - `README.md`, `README.RU.md`
  - `media/readme/en/all-tasks.png`, `media/readme/en/in-progress.png`, `media/readme/en/roadmap.png`
  - `media/readme/ru/in-progress.png`, `media/readme/ru/settings.png`
  - `ffprobe -v error -show_entries format=duration,size -show_entries stream=width,height,r_frame_rate,codec_name -of default=noprint_wrappers=1 artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4`
  - extracted live video keyframes `artifacts/release/1.26.0/video/live/final-keyframes/*.png`
- Depth checklist:
  - Scope drift / unrelated changes: PASS; no app runtime behavior changes beyond test/media harness and synthetic demo config.
  - Acceptance criteria: PASS; README refs exist, EN/RU `in-progress.png` exist, release notes/post/script/MP4 exist with nonzero size.
  - Validation evidence: PASS; Headless `31/31`, FlaUI `9/9`, media reports warnings `[]`, live-captured MP4 `1920x1080` at `30 fps`, duration `139.900000`, no audio stream.
  - Unsupported claims: PASS; release notes are grounded in `git log --merges 1.25.1..HEAD`, specs, README and generated media.
  - Regression / edge case: PASS; main tab overflow in capture is handled by maximizing desktop capture and overflow fallback; migration toast suppressed only for synthetic README demo config.
  - Comments/docs/changelog: PASS; no repo changelog convention found for this release, so local release artifacts were placed under ignored `artifacts/release/1.26.0/`.
  - Hidden contract change: PASS; task status migration behavior remains documented and unchanged.
  - Manual-review challenge: PASS; likely reviewer asks whether `In Progress` media exists and whether UI tests ran; both are covered.
- No-findings justification: Final artifacts are present, validations passed, and remaining warnings are Git line-ending warnings without `git diff --check` errors.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| LOW | persistence | Release text/video artifacts under `artifacts/` are local ignored deliverables. | Report exact paths in final response; commit only tracked README/media/harness/spec changes if requested later. | accepted |
| LOW | git hygiene | `git diff --check` emits CRLF conversion warnings for existing Windows line-ending behavior. | No code action; report as warnings, not validation failures. | accepted |

- Fixed before final report: added `InProgress` capture, made media capture overflow-aware, maximized FlaUI capture/test windows, suppressed one-time migration notice only in synthetic README demo config, reworked release notes/post/video to Russian, made Russian demo tasks and subtitles Russian, regenerated media, then replaced the synthetic montage with a real live capture of the running Unlimotion desktop window.
- Checks rerun: `scripts/update-readme-media.ps1`, README media reference check, live recorder build/run, `ffprobe`, keyframe extraction/inspection, `git diff --check`.
- Validation evidence: Headless `31/31`, FlaUI `9/9`, media reports no warnings, README refs OK (`22` refs), live raw MP4 nonzero (`3245297` bytes), final MP4 nonzero (`4632752` bytes), duration `139.900000`, video `1920x1080` at `30 fps`, no audio stream.
- Unrelated changes: none observed beyond approved scope.
- Needs human: none for this EXEC; publishing/tagging/pushing still requires a separate explicit request.
- Residual risks / follow-ups: final GitHub Release copy may still need last-minute binary/package links once actual release assets are built.

## Approval
Получена фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Instruction/memory discovery | 0.9 | Нет | Inspect repo release delta | Нет | Нет | Подключены central stack, local override, screenshot/video skills and Unlimotion memory. | `C:\Users\Kibnet\.codex\agents\AGENTS.md`, `AGENTS.override.md`, memory lookup |
| SPEC | Release baseline discovery | 0.8 | Нужен свежий local main | Pull latest main if user requests | Да | Да: пользователь попросил подтянуть последний main | Было найдено, что local `main` behind GitHub; пользователь явно запросил pull. | git refs/status/log |
| SPEC | Git sync | 0.95 | Нет | Recompute release delta | Нет | Нет | `git pull --ff-only origin main` completed, `main` synced to `d0f2e1d`. | `.git`, working tree fast-forward |
| SPEC | Release artifact planning | 0.9 | Только approval | Ask for SPEC approval | Да | Нет | Spec now covers docs, screenshots, GitHub notes, Telegram post, script, MP4 and validation. | `specs/2026-06-12-release-preparation.md` |
| EXEC | Approval received | 0.95 | Нет | Update README media harness and docs | Нет | Да: пользователь написал `Спеку подтверждаю` | SPEC gate passed; EXEC can modify files in approved scope. | `specs/2026-06-12-release-preparation.md` |
| EXEC | README media harness update | 0.9 | Media generation result | Run `scripts/update-readme-media.ps1` | Нет | Нет | Added `InProgress` selectors/capture and README references without changing product behavior. | `README.md`, `README.RU.md`, `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`, `tests/Unlimotion.ReadmeMedia/Program.cs` |
| EXEC | Media workflow triage | 0.85 | Rerun result | Rerun `scripts/update-readme-media.ps1` | Нет | Нет | First media workflow run passed Headless but FlaUI direct tab checks hit `Roadmap`/`Settings` hidden by main tab overflow; maximized FlaUI test window and added explicit `InProgressTree` assertion. | `tests/Unlimotion.UiTests.Authoring/Tests/MainWindowScenariosBase.cs`, `tests/Unlimotion.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs` |
| EXEC | README capture overflow fallback | 0.85 | Final media generation result | Rerun `scripts/update-readme-media.ps1` | Нет | Нет | UI suites passed (`Headless 31/31`, `FlaUI 9/9`), then `ReadmeMedia` hit hidden `SettingsTabItem`; added maximized capture viewport and overflow click fallback for desktop capture. | `tests/Unlimotion.ReadmeMedia/Program.cs` |
| EXEC | README media regeneration | 0.95 | Нет | Draft release notes and posts | Нет | Нет | Final `scripts/update-readme-media.ps1` completed: builds succeeded, Headless `31/31`, FlaUI `9/9`, EN/RU screenshots and GIFs regenerated without report warnings; demo config suppresses only one-time migration toast. | `media/readme/en/*`, `media/readme/ru/*`, `artifacts/readme-media/latest/*`, `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs` |
| EXEC | Release copy drafting | 0.9 | Video validation result | Compose demo MP4 | Нет | Нет | Created GitHub release notes, Telegram post and storyboard from `1.25.1..d0f2e1d`, README, specs and generated media. | `artifacts/release/1.26.0/github-release-notes.md`, `artifacts/release/1.26.0/telegram-post.md`, `artifacts/release/1.26.0/video-script.md` |
| EXEC | Demo video composition | 0.95 | Final diff/check result | Run final validation and post-EXEC review | Нет | Нет | Composed deterministic screenshot montage MP4; final `ffprobe` reports H.264 1920x1080 30 fps 64s and extracted keyframes were visually inspected. | `artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4`, `artifacts/release/1.26.0/video/raw/*`, `artifacts/release/1.26.0/video/keyframes/*`, `artifacts/release/1.26.0/video-script.md` |
| EXEC | Russian release copy revision | 0.95 | Нет | Re-render Russian video | Нет | Да: пользователь попросил GitHub notes, Telegram post and video fully in Russian | GitHub notes were rewritten in Russian style; Telegram post was made Russian with feature-by-feature user value and no Latin words except product name. | `artifacts/release/1.26.0/github-release-notes.md`, `artifacts/release/1.26.0/telegram-post.md` |
| EXEC | Russian dynamic video revision | 0.95 | Нет | Final validation | Нет | Нет | Russian demo task strings, current task id, captions and storyboard were revised; media workflow reran with Headless `31/31` and FlaUI `9/9`; final MP4 is H.264 1920x1080 30 fps 64s with eight inspected keyframes. | `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAutomationScenarioData.cs`, `tests/Unlimotion.AppAutomation.TestHost/UnlimotionAppLaunchHost.cs`, `tests/Unlimotion.UiTests.Headless/Tests/ReadmeDemoHeadlessTests.cs`, `media/readme/ru/*`, `artifacts/release/1.26.0/video-script.md`, `artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4` |
| EXEC | Detailed video approval | 0.95 | Нет | Record and compose updated demo video | Нет | Да: пользователь утвердил варианты `1А,2А,3А,4А,5В,6А,7А,8В,9А,10А,11А,12А,13Б,14Б` | Updated storyboard to 2:10-2:25, no voiceover, quiet background music, one-task storyline, full status lifecycle, extended emoji filter demo, roadmap filters, mandatory narrow-screen scene and conditional safe settings scene; per `14Б`, proceed to recording without another approval round. | `artifacts/release/1.26.0/video-script.md`, `specs/2026-06-12-release-preparation.md` |
| EXEC | Detailed video render | 0.9 | Нет | Final diff/check review | Нет | Нет | Rebuilt release demo as a 145s Russian dynamic montage from current README media using deterministic PowerShell frame rendering, cursor movement, zooms, status/filter overlays and quiet generated background music; `ffprobe` confirmed H.264 1920x1080 30 fps plus AAC audio, and 11 keyframes were inspected. | `artifacts/release/1.26.0/video/render-release-demo.ps1`, `artifacts/release/1.26.0/video/frames-v2/*`, `artifacts/release/1.26.0/video/keyframes-v2/*`, `artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4`, `specs/2026-06-12-release-preparation.md` |
| EXEC | Live video recapture | 0.95 | Нет | Report final live artifact | Нет | Да: пользователь потребовал обязательный live capture вместо нарисованного интерфейса | Replaced the montage output with a real `gdigrab` capture of the running Russian demo window, preserved raw live capture, rebuilt final MP4 without audio and without taskbar, then inspected status/search/roadmap/narrow/archive keyframes. | `artifacts/release/1.26.0/live-recorder/Program.cs`, `artifacts/release/1.26.0/video/live/live-raw.mp4`, `artifacts/release/1.26.0/video/live/final-keyframes/*.png`, `artifacts/release/1.26.0/unlimotion-1.26.0-demo.mp4`, `specs/2026-06-12-release-preparation.md` |
