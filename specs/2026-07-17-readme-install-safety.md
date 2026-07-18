# Срочная актуализация установки и download guidance в README

## 0. Метаданные
- Тип (профиль): delivery-task; `.NET Desktop Client`, documentation-only child package
- Владелец: Kibnet
- Масштаб: medium
- Целевое семейство / behavior baseline: GPT-5.6 family optimization baseline
- Поверхность: Work / Codex desktop
- Effective runtime: текущий Codex runtime; точный model ID/reasoning mode не влияет на документационный контракт
- Eval baseline / evidence:
  - утверждённая master roadmap `specs/2026-07-17-readme-reliability-roadmap.md`;
  - freshness gate 2026-07-17: `origin/main` = HEAD = `5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd`;
  - latest release API/CLI evidence: tag `1.27.0`, Windows/Linux/macOS/Android assets;
  - текущие `README.md`, `README.RU.md`, `global.json`, `run.windows.cmd`, `run.linux.sh`, `run.macos.sh` и packaging workflows.
- Целевой релиз / ветка:
  - planned branch: `docs/readme-install-safety`;
  - starting Git state: detached HEAD на exact tag `1.27.0`; этот commit случайно совпадает с текущим `origin/main`, но tag/detached state не используется как branch base;
  - base: актуальный `origin/main` после повторного fetch непосредственно перед созданием ветки;
  - dependent PR: нет; stage 1 является первым delivery package программы;
  - rebase/full-validation gate: перед final delivery повторно fetch; если `origin/main` ушёл вперёд, rebase branch на актуальный base и повторить весь S1 validation set и post-EXEC review;
  - planned PR title: `docs(readme): correct installation and download guidance`;
  - этот child package не публикует release.
- Ограничения:
  - до фразы пользователя `Спеку подтверждаю` разрешено менять только этот файл;
  - root README не должен hardcode current release version;
  - нельзя заявлять официальную Debian compatibility до package smoke evidence из следующего delivery package;
  - нельзя утверждать Windows/macOS byte-level signing/notarization state без native artifact evidence; current workflows не публикуют такую verification evidence;
  - нельзя менять scripts, workflows, production code, UI, media и detailed task-model content;
  - Markdown backlog удаляется, но GitHub Issues/Projects не создаются и не изменяются этим package;
  - EN/RU должны изменяться атомарно и передавать одинаковый смысл.
- Связанные ссылки:
  - `specs/2026-07-17-readme-reliability-roadmap.md`
  - `README.md`
  - `README.RU.md`
  - `global.json`
  - `run.windows.cmd`
  - `run.linux.sh`
  - `run.macos.sh`
  - `.github/workflows/windows-packaging.yml`
  - `.github/workflows/deb_packaging.yml`
  - `.github/workflows/osx-packaging.yml`
  - `.github/workflows/android-packaging.yml`

## 1. Overview / Цель
Немедленно убрать из публичных README опасные, неверные и устаревшие инструкции установки, добавить прямой download path и фактическую release-матрицу, не ожидая последующих status, packaging, signing и full documentation packages.

Outcome contract:
- Success means:
  - пользователь находит `/releases/latest` и выбирает существующий asset по платформе/архитектуре;
  - README честно сообщает, что project release process пока не публикует verified Windows/macOS signing/notarization evidence; возможные предупреждения ОС описаны отдельно как platform behavior, а не как следствие отсутствия опубликованного evidence;
  - Linux AppImage и Android APK видимы, а `.deb` не выдаётся за проверенную current Debian support;
  - source-build steps совпадают с `global.json` и существующими run scripts;
  - отсутствуют `chmod -R 755`, blanket «no extra steps», Fork promotion, `main.zip` как stable source, встроенный stale backlog и историческое обещание rollback миграции через Git history;
  - EN/RU передают один install contract без hardcoded current version.
- Итоговый артефакт / output:
  - обновлённые `README.md` и `README.RU.md`;
  - обновлённые журналы child spec и master roadmap;
  - validation evidence по local/external links, release assets, forbidden fragments, EN/RU parity и Git diff.
- Stop rules:
  - не изменять product behavior или packaging ради удобства текста;
  - не рекомендовать artifact, которого нет в latest release inventory;
  - не повышать `.deb`, server, CLI или platform support level без evidence;
  - не завершать при расхождении EN/RU platform rows/caveats;
  - transient external failure повторить; если `/releases/latest` или official guidance реально недоступны, не утверждать AC и сообщить residual issue;
  - не начинать stage 2 в этом EXEC.

## 2. Текущее состояние (AS-IS)
- `README.md:12-28` и `README.RU.md:12-28` используют неструктурированные «Case/Вариант 1/2».
- Текст говорит «перейти по ссылке с релизами», но actual Releases hyperlink отсутствует.
- Перечислены только Windows, Debian и macOS; latest release также содержит Linux AppImage и Android arm64/x64 APK.
- macOS guidance предлагает `sudo chmod -R 755 /Applications/Unlimotion.app`, хотя это не решает Gatekeeper signing/notarization.
- Windows/macOS packaging workflows не выполняют подтверждённое desktop signing/notarization.
- Blanket claim «дополнительные действия для Windows/Debian не требуются» неподтверждён.
- `.deb` существует, но current package metadata/smoke evidence не доказывают официальную поддержку Debian 12/13.
- Android project declares `SupportedOSPlatformVersion=23` и arm64/x64 RIDs; это minimum API declaration, а не verified device compatibility matrix. Android updater отдельно запрашивает per-source install permission только на API 26+.
- Source path называет `main.zip` «latest source», смешивая development snapshot со stable release.
- Source steps продвигают сторонний Git client Fork.
- Unix run scripts имеют Git mode `100644`, без shebang, поэтому безопасная текущая инструкция — `bash ./run.*.sh` из repo root.
- `global.json` требует .NET SDK `10.0.100` с `latestFeature`; release binaries self-contained, SDK нужен только source build.
- Root backlog содержит уже реализованные search, watcher, Android и server mode; EN/RU checkbox state расходится.
- Абзац `README.md:83` / `README.RU.md:83` описывает одноразовую историческую миграцию старой status model и обещает rollback через Git history каталога задач; это не current user guidance и не доказанный recovery contract.

Freshness evidence:
- HEAD и `origin/main`: `5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd`.
- Latest release endpoint: `https://github.com/Kibnet/Unlimotion/releases/latest` -> tag `1.27.0` на момент SPEC.
- User-facing assets, использованные для contract:
  - `Unlimotion-win-Setup.exe`;
  - `Unlimotion-win-Portable.zip`;
  - `Unlimotion.AppImage`;
  - `Unlimotion-1.27.0.deb` как preview alternative, но имя в README описывается pattern без версии;
  - `Unlimotion-osx-Setup.pkg`, `Unlimotion-osx-Portable.zip` для x64;
  - `Unlimotion-osx-arm64-Setup.pkg`, `Unlimotion-osx-arm64-Portable.zip` для arm64;
  - `Unlimotion-1.27.0-android-arm64.apk`, `Unlimotion-1.27.0-android-x64.apk`, описываемые version-neutral patterns.

## 3. Проблема
Пользователь не получает из README безопасного и проверяемого пути установки: отсутствует download link, platform list неполон, macOS workaround неверен, Debian support переоценён, а source-build instructions смешивают stable release и development snapshot.

## 4. Цели дизайна
- Сделать download path заметным и version-neutral.
- Разделить release installation и source build.
- Разделить installer, portable, generic published и preview artifacts без превращения выбора файла в обещание platform support.
- Писать caveats прямо рядом с затронутой платформой.
- Сохранять одинаковую структуру и смысл EN/RU.
- Минимизировать diff: не перестраивать остальные README sections раньше full documentation package.
- Оставить platform hardening следующему delivery package, не скрывая current limitations.

## 5. Non-Goals (чего НЕ делаем)
- Не создаём `docs/installation*.md`; это stage 7 master roadmap.
- Не исправляем H1/language switch, strengths, current task model, tabs, settings, hotkeys, deletion, drag-and-drop, emoji и будущую storage migration documentation; из status guide удаляется только исторический одноразовый migration/rollback paragraph, явно принадлежащий master AC-12.
- Не меняем `run.*` file mode/shebang/CWD behavior.
- Не меняем packaging workflows, release tags, asset names или updater behavior.
- Не внедряем signing/notarization и не создаём secrets.
- Не обещаем магазинную установку Android; документируется sideload APK.
- Не объявляем `.deb` officially supported на Debian 12/13.
- Не создаём GitHub Issues/Project из удаляемого backlog.
- Не обновляем README media.
- Не запускаем UI tests: UI behavior/layout/state не меняются.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `README.md` -> английская install/source guidance, удаление historical migration paragraph и EN backlog.
- `README.RU.md` -> семантически эквивалентная русская guidance, удаление historical migration paragraph и RU backlog.
- Child spec -> decisions, acceptance, evidence и action log.
- Master roadmap -> stage-1 progress и post-EXEC summary после завершения child EXEC.
- Latest GitHub release + local workflows/scripts -> authoritative evidence, но не изменяются.

### 6.2 Детальный дизайн

В обоих README заменить текущий launch block на два раздела одинаковой структуры:

1. `Download and install / Скачать и установить`.
2. Прямая ссылка `[GitHub Releases](https://github.com/Kibnet/Unlimotion/releases/latest)`.
3. Вводный текст называет таблицу available/published builds, а не supported platforms: наличие asset подтверждает публикацию, но не является гарантией совместимости со всеми версиями соответствующей ОС.
4. Platform table с одинаковыми canonical row keys в обеих локалях:

| Available build | Asset | Role / caveat |
| --- | --- | --- |
| Windows x64 | `Unlimotion-win-Setup.exe`; `Unlimotion-win-Portable.zip` | installer и portable alternative; project release process не публикует verified Authenticode evidence; SmartScreen может отдельно показать warning |
| Linux x64 (AppImage) | `Unlimotion.AppImage` | generic published Linux option; compatibility ещё не подтверждена distro smoke matrix |
| Linux x64 (.deb) | `Unlimotion-<version>.deb` | preview alternative; official current Debian compatibility пока не подтверждена |
| macOS x64 | `Unlimotion-osx-Setup.pkg`; `Unlimotion-osx-Portable.zip` | Intel; verified Developer ID/notarization evidence не публикуется |
| macOS arm64 | `Unlimotion-osx-arm64-Setup.pkg`; `Unlimotion-osx-arm64-Portable.zip` | Apple Silicon; verified Developer ID/notarization evidence не публикуется |
| Android arm64 | `Unlimotion-<version>-android-arm64.apk` | sideload; project declares minimum API 23, без universal device-support promise |
| Android x64 | `Unlimotion-<version>-android-x64.apk` | sideload; primarily x86_64 devices/emulators |

Platform instructions:
- Windows: сообщить, что project release process не публикует verified Authenticode evidence и Microsoft Defender SmartScreen может показать warning; не обещать отсутствие warning и не давать blanket bypass command. Для portable ZIP явно сказать распаковать архив перед запуском included app.
- Linux AppImage:

```bash
chmod +x Unlimotion.AppImage
./Unlimotion.AppImage
```

- Linux `.deb`: указать наличие asset и preview-status без команды, обещающей successful dependency resolution на неподтверждённой matrix.
- macOS: удалить `chmod -R 755`; сообщить, что verified Developer ID/notarization evidence не публикуется, дать official Apple guidance link `https://support.apple.com/en-us/102445` для `Privacy & Security -> Open Anyway` только при доверии downloaded artifact, а для portable ZIP явно потребовать распаковку.
- Android: сообщить sideload и выбор APK по ABI; manifest-declared minimum — Android 6.0 / API 23, но это не заменяет device smoke matrix. ОС может потребовать разрешить unknown-app installation; точная модель разрешения зависит от Android version/device.
- Updates: desktop in-app updater доступен только когда Velopack сообщает, что build установлен и managed; desktop portable/source runs не должны полагаться на этот путь. Android использует отдельный flow скачивания подходящего APK с последующим системным подтверждением установки.

Source build section:
- явный заголовок `Build and run from source / Сборка и запуск из исходников`;
- уточнить, что clone `main` получает development snapshot, а stable users должны выбирать release assets/source archive на release page;
- prerequisites: Git, `.NET 10 SDK` по `global.json` со ссылкой `https://dotnet.microsoft.com/en-us/download/dotnet/10.0` и network access к NuGet для первого restore;
- commands из repository root:

```powershell
git clone https://github.com/Kibnet/Unlimotion.git
Set-Location Unlimotion
.\run.windows.cmd
```

```bash
git clone https://github.com/Kibnet/Unlimotion.git
cd Unlimotion
bash ./run.linux.sh
# or on macOS:
bash ./run.macos.sh
```

- не упоминать Fork и `main.zip` как рекомендуемый stable path.

Backlog:
- удалить заголовок и весь checklist из обоих README;
- не добавлять replacement backlog link в этом package, поскольку canonical GitHub Project/Issues destination ещё не выбран master roadmap.

Historical migration detail:
- удалить только парный абзац после status-marker table (`README.md:83` / `README.RU.md:83` в baseline), который описывает старую `IsCompleted=false` migration и rollback через Git history;
- не менять соседние marker/status tables, status-picker paragraph и остальную task-model semantics — это scope stage 2/7.

EN/RU parity contract:
- одинаковый порядок platform rows;
- одинаковые asset patterns и availability/validation levels;
- одинаковые signing/update/source caveats;
- естественный перевод, без машинной кальки технических имён.

Visual planning artifact: `Не применимо` — меняется Markdown copy без UI layout/flow/state. Реальный GitHub Markdown render проверяется как artifact-facing acceptance, но UI screenshot/video не требуется.

UI test video evidence: `Не применимо` — production UI и automation flow не меняются. Next-best evidence: GitHub Markdown render, link/asset checks и diff review.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Найти загрузку | Пользователь открывает README | Видит `/releases/latest` до подробного conceptual guide | EN/RU render + URL check | S1-AC-01 |
| Выбрать Windows asset | Пользователь использует Windows x64 | Видит installer и portable alternative плюс SmartScreen/signing-evidence caveat | asset inventory + render | S1-AC-02, S1-AC-03 |
| Выбрать Linux asset | Пользователь использует Linux x64 | Видит AppImage как generic published option и `.deb` как неподтверждённый preview | asset inventory + copy review | S1-AC-02, S1-AC-04 |
| Открыть macOS package | Пользователь использует Intel/Apple Silicon Mac | Выбирает правильную architecture и official Gatekeeper guidance без `chmod -R 755` | asset inventory + forbidden scan | S1-AC-02, S1-AC-03 |
| Установить Android APK | Пользователь использует Android | Выбирает APK по ABI и понимает sideload nature | asset inventory + render | S1-AC-02 |
| Запустить исходники | Contributor клонирует repository | Выполняет command, совпадающий с existing script/global.json contract | command/source inspection | S1-AC-05 |
| Посмотреть планы | Пользователь доходит до конца README | Не видит stale Markdown backlog | forbidden scan | S1-AC-06 |
| Читать текущую модель статусов | Пользователь читает status guide | Не получает устаревшую одноразовую migration story или неподтверждённое rollback promise | scoped deletion + forbidden scan | S1-AC-10 |
| Сменить язык | Пользователь открывает EN и RU | Получает одинаковую matrix и caveats | parity check | S1-AC-07 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Latest release доступен | Click `/releases/latest` | Redirect на current release | HTTP/API check фиксирует tag/assets | Version не hardcode в README |
| Desktop installer | Выбор Windows/macOS package | Показано отсутствие published verified signing/notarization evidence и возможный OS warning | Нет claim о точном signature state bytes без native verification | macOS с official guidance |
| Linux user | Выбор platform row | AppImage generic published; `.deb` preview | Нет promise distro/Debian compatibility | Stage 3 проверит support matrix |
| Android user | Выбор ABI | Получает отдельные arm64/x64 APK rows | Не обещать store install или universal device support | APK filenames version-neutral в prose |
| Source contributor | Clone main | Получает development snapshot | Stable user перенаправлен в release page | Commands run from repo root |
| External URL transient failure | Validation | Retry; AC не подтверждается по отсутствующему evidence | Не удалять correct URL после одного timeout | Report exact failure |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Primary download URL | agent | `/releases/latest`, без version hardcode | 1.00 | Redirect может быть broken external state | Нет |
| Linux artifact ordering | agent | AppImage как generic published option; `.deb` как preview | 0.95 | AppImage тоже ещё не имеет formal launch smoke | Нет; ни один artifact не называется supported/recommended до stage 3 |
| `.deb` wording | agent | Available preview, current Debian support unverified | 0.99 | Может выглядеть слишком осторожно | Нет |
| Windows/macOS signing | agent | Нет published verified signature/notarization evidence; возможны OS warnings | 0.99 | Точный signature state опубликованных bytes не доказан на Windows host для всех platforms | Нет; не выводить byte-level state только из workflow |
| macOS workaround | agent | Удалить chmod; official Apple Open Anyway guidance | 1.00 | Пользователь всё равно должен оценить trust | Нет |
| Android distribution | agent | Sideload APK by ABI | 0.95 | Device-specific install policy различается | Нет |
| In-app updates | agent | Desktop: только Velopack-managed install; Android: отдельный matching-APK flow | 0.95 | Platform package behavior может измениться позже | Нет; перепроверить оба services перед EXEC |
| Source acquisition | agent | Git clone main как development path; stable via release page | 1.00 | Новичкам нужен Git | Нет |
| Unix scripts | agent | Документировать `bash ./...`, не менять scripts | 1.00 | Не исправляет executable-bit debt | Нет; stage 3 owns fix |
| Backlog | user + agent | Удалить полностью без replacement link | 0.99 | Планы менее видимы из README | Нет; master roadmap выбрала Issues/Projects later |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Release URL/assets | GitHub latest release + packaging workflows | Docs reflect current asset classes without version hardcode | Future version requires no text edit unless asset contract changes | `gh release view`, HTTP redirect |
| .NET source prerequisite | `global.json` | Direct .NET 10 SDK link and source-only scope | No runtime migration | file inspection + link check |
| Run commands | `run.*` | Explicit repo-root commands and `bash` for Unix | No script change | command/source comparison |
| Update caveat | Desktop `VelopackApplicationUpdateService` + Android `AndroidApplicationUpdateService` | Platform-specific managed-install vs matching-APK wording | Copy-only | source inspection |
| Platform availability/support | Current evidence | Published-build rows отделены от support guarantees; `.deb` явно preview | Stage 3 may later upgrade wording | asset/workflow audit |
| Android install/update | Android csproj + `AndroidApplicationUpdateService` | Declared API 23, ABI choice и version-dependent sideload permission wording | Не обещать device-wide compatibility | source inspection + copy review |
| Backlog | Stale README checklists | Removed | Git history preserves old text | forbidden scan |
| Historical status migration | One-time legacy migration paragraph | Removed from both root READMEs | Current status guidance around it stays unchanged | scoped diff + forbidden scan |

### 6.7 Temporary Stage 1 release-class contract

До появления versioned canonical manifest в stage 3 S1-AC-01 использует временный contract, сформированный из current release/workflows. Он проверяет **весь** published asset set, а не только файлы из README. Для tag `$tag` validator нормализует `$version = $tag.TrimStart('v')` и требует:

| Class | Required patterns / names | Expected count | README role |
| --- | --- | ---: | --- |
| Windows user-facing | `Unlimotion-win-Setup.exe`, `Unlimotion-win-Portable.zip`, `Unlimotion-$version-win-x64-portable.zip` | 3 | installer + portable; versioned alternative classified, но не рекламируется отдельно |
| Linux user-facing | `Unlimotion.AppImage`, `Unlimotion-$version.deb` | 2 | generic published + preview |
| macOS x64 user-facing | `Unlimotion-osx-Setup.pkg`, `Unlimotion-osx-Portable.zip`, `Unlimotion-$version-osx-x64.pkg` | 3 | installer + portable; versioned alternative classified |
| macOS arm64 user-facing | `Unlimotion-osx-arm64-Setup.pkg`, `Unlimotion-osx-arm64-Portable.zip`, `Unlimotion-$version-osx-arm64.pkg` | 3 | installer + portable; versioned alternative classified |
| Android user-facing | `Unlimotion-$version-android-arm64.apk`, `Unlimotion-$version-android-x64.apk` | 2 | ABI-specific sideload |
| Update index/feed | `RELEASES`, `releases.win.json`, `releases.linux.json`, `releases.osx.json`, `releases.osx-arm64.json` | 5 | internal; не предлагать для manual install |
| Update packages | `Unlimotion-$version-full.nupkg`, `Unlimotion-$version-linux-full.nupkg`, `Unlimotion-$version-osx-full.nupkg`, `Unlimotion-$version-osx-arm64-full.nupkg` | 4 | internal; не предлагать для manual install |

Total expected count: 22 для current contract. Каждый asset обязан совпасть ровно с одним class entry; unknown, missing, duplicate-name или multi-class match блокирует S1-AC-01. JSON evidence сохраняет actual full list, classification каждого имени, expected/actual cardinality и verdict. Этот contract временный и будет заменён canonical manifest stage 3; любое изменение naming до stage 3 требует обновить child evidence/README либо остановить stage 1, но не игнорировать drift.

## 7. Бизнес-правила / Алгоритмы
1. Download URL version-neutral: `/releases/latest`.
2. Asset name patterns may be documented, current release number may not.
3. Published asset existence does not equal official support.
4. `.deb` remains preview until Debian smoke evidence exists.
5. README не утверждает byte-level signature state: он сообщает отсутствие published verified Windows/macOS signing/notarization evidence и возможность OS warning.
6. Permission change is not presented as a signing/Gatekeeper solution.
7. Android is described as sideload APK distribution, not app-store delivery.
8. SDK is required only to build/run source, not self-contained release binaries.
9. `main` clone is development source, not the latest stable source.
10. Unix scripts are invoked through `bash` until stage 3 changes their file contract.
11. EN/RU platform ordering, availability/validation level, platform-specific update semantics and caveats must match.
12. Root Markdown backlog is removed rather than manually repaired.
13. Historical one-time status migration/rollback detail is removed, while the surrounding current status contract is unchanged.

## 8. Точки интеграции и триггеры
- Latest release asset contract -> platform table rows and patterns.
- Windows/macOS signing workflow change in later stage -> trigger docs update removing caveats only after verified evidence.
- Debian smoke outcome in later stage -> trigger `.deb` support wording update.
- Run script hardening in later stage -> trigger source command simplification.
- Asset rename/architecture change -> trigger both README platform tables.
- Future full docs package -> move detailed install copy to `docs/installation*.md` and shorten root block.

## 9. Изменения модели данных / состояния
Не применимо: package меняет только Markdown и spec journals; runtime data, settings, tasks и release assets не изменяются.

## 10. Миграция / Rollout / Rollback
- Data migration: не применимо.
- Rollout: один docs PR с EN/RU changes и specs.
- Rollback: обычный Git revert README/spec diff; внешние binaries/releases не изменяются.
- Compatibility: existing anchors внутри launch/backlog не считаются public API; root language links/media paths сохраняются.
- Backlog recoverability: удалённый checklist остаётся в Git history; его не переносят автоматически.

## 11. Тестирование и критерии приёмки

### Stage 1 Acceptance Criteria
- **S1-AC-01:** Оба README содержат единственную primary download link `https://github.com/Kibnet/Unlimotion/releases/latest`, которая успешно разрешается в current release; полный published asset set без пропусков/unknown/duplicates сопоставлен с temporary Stage 1 release-class contract из 6.7 и сохранён в release-check JSON.
- **S1-AC-02:** EN/RU tables буквально содержат в одинаковом порядке canonical keys `Windows x64`, `Linux x64 (AppImage)`, `Linux x64 (.deb)`, `macOS x64`, `macOS arm64`, `Android arm64`, `Android x64`; соответствующие asset patterns существуют в release inventory, заголовок/вводный текст не называет наличие asset официальной поддержкой, portable rows требуют распаковки, а Android guidance фиксирует declared API 23 без universal compatibility claim.
- **S1-AC-03:** Удалены macOS `chmod -R 755` и blanket installation claims; обе локали говорят об отсутствии published verified Windows/macOS signing/notarization evidence и возможных OS warnings, не утверждают точный byte-level signature state и ссылаются на official Apple guidance.
- **S1-AC-04:** `.deb` описан как available preview без официального Debian support claim; AppImage получает executable/run commands.
- **S1-AC-05:** Source section использует direct .NET 10 SDK link, `git clone`, repo-root commands и `bash ./run.*.sh`; Fork и `main.zip` удалены.
- **S1-AC-06:** `Backlog of features` / `Бэклог возможностей` и их checklist полностью удалены.
- **S1-AC-07:** EN/RU имеют одинаковый порядок platform rows, asset classes, availability/validation levels, desktop-versus-Android update semantics и source semantics.
- **S1-AC-08:** README не содержит hardcoded current release version; проверка получает tag динамически из latest release, а не содержит статичную версию в validator.
- **S1-AC-09:** Все оставшиеся relative Markdown links/media resolve; изменённый Markdown не имеет broken fences/tables, проходит GitHub GFM parse и фактическую desktop/narrow viewport inspection.
- **S1-AC-10:** Парный historical paragraph про `IsCompleted=false` migration и rollback через Git history удалён; соседние status marker tables и current status-picker guidance не изменены.
- **S1-AC-11:** Diff не меняет production code, workflows, scripts, media или README sections вне launch/source block, точечного historical-migration paragraph и backlog deletion.
- **S1-AC-12:** Branch создан от актуального `origin/main`, а не detached tag; dependent PR отсутствует и это зафиксировано; перед delivery выполнены fetch/rebase-if-needed и полный повтор S1 validation/post-EXEC review.

### Mapping to master roadmap

| Stage 1 criterion | Master criterion | Contribution |
| --- | --- | --- |
| S1-AC-01, S1-AC-02, S1-AC-08 | Master AC-01 | Version-neutral latest-release entry point, README asset rows и full published-set classification по temporary expected-class contract; stage 3 заменит его permanent canonical manifest |
| S1-AC-02, S1-AC-03, S1-AC-04, S1-AC-05, S1-AC-07, S1-AC-09 | Master AC-16 (частично) | За один переход выбрать опубликованный asset и понять install caveats в обеих локалях |
| S1-AC-03, S1-AC-05, S1-AC-06, S1-AC-10 | Master AC-12 | Удаление опасного/исторического root README content |
| S1-AC-11, S1-AC-12 | Master AC-18 | Fresh base, branch/dependency/rebase state и auditable child scope |

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| S1-AC-01, S1-AC-02, S1-AC-08 | `gh release view` asset inventory + HTTP redirect + dynamic tag scan | Inspect resolved release | release-check JSON + command log | Network required; retry transient failures |
| S1-AC-03, S1-AC-04, S1-AC-05, S1-AC-06, S1-AC-10 | `rg` forbidden/required marker checks | Copy review EN/RU | terminal log + diff | Semantic quality also manual |
| S1-AC-07 | PowerShell semantic token/order parity check | Side-by-side copy review | parity output | Translation quality cannot be fully automated |
| S1-AC-09 | PowerShell relative-link/media validator + balanced fence/table checks + GFM API render | Browser inspection at desktop and narrow viewport | rendered HTML/screenshots or inspection log | API HTML alone does not prove viewport usability |
| S1-AC-11 | `git status`, `git diff --name-only`, scoped diff review | Review unchanged surrounding sections | diff evidence | — |
| S1-AC-12 | fetch + branch/base/merge-base checks; rebase when needed | Re-review freshness/dependency state | Git command log + journal | Re-run full set after any rebase |

### Reproducible parity contract

Parity validator использует одинаковый ordered key set для EN/RU. Для каждого key он требует ровно одно совпадение локализованных tokens; asset/URL/command tokens совпадают буквально. Output `artifacts/documentation-validation/parity-check.json` содержит ordered keys, matched excerpts и verdict без secrets.

| Semantic ID | EN required tokens/concept | RU required tokens/concept |
| --- | --- | --- |
| `download.latest` | exact `/releases/latest` URL | тот же exact URL |
| `availability.not-support` | `published builds` + `does not guarantee compatibility` | `опубликованные сборки` + `не гарантирует совместимость` |
| `build.windows-x64` | literal row key + Windows asset names | те же literal key/assets |
| `build.linux-appimage` | literal row key + `Unlimotion.AppImage` + compatibility not smoke-tested | те же literal key/asset + совместимость не проверена smoke matrix |
| `build.linux-deb` | literal row key + `preview` + Debian compatibility not verified | те же literal key + `предварительная` + совместимость Debian не подтверждена |
| `build.macos-x64` | literal row key + x64 Setup/Portable assets | те же literal key/assets |
| `build.macos-arm64` | literal row key + arm64 Setup/Portable assets | те же literal key/assets |
| `build.android-arm64` | literal row key + arm64 APK pattern | те же literal key/pattern |
| `build.android-x64` | literal row key + x64 APK pattern | те же literal key/pattern |
| `caveat.windows-signing` | `verified Authenticode evidence` + `SmartScreen` | `подтверждение Authenticode` + `SmartScreen` |
| `caveat.macos-signing` | `Developer ID` + `notarization` + `Open Anyway` | те же identifiers + локализованное объяснение доверия |
| `instruction.portable-extract` | `extract` for Windows and macOS portable ZIP | `распаковать` для Windows и macOS portable ZIP |
| `instruction.appimage` | exact `chmod +x Unlimotion.AppImage` then `./Unlimotion.AppImage` | те же команды и порядок |
| `instruction.android-minimum` | `Android 6.0` + `API 23` + not a universal device guarantee | те же version/API + не гарантия для всех устройств |
| `instruction.android-permission` | unknown-app/source permission varies by Android/device | разрешение внешнего источника зависит от версии/устройства |
| `update.desktop` | installed/managed desktop builds; portable/source must not rely on updater | установленная/managed desktop build; portable/source не полагаются на updater |
| `update.android` | matching APK + system installation confirmation | подходящий APK + системное подтверждение установки |
| `source.prerequisites` | Git + exact .NET 10 URL + NuGet first restore | те же identifiers/URL + локализованное объяснение |
| `source.stability` | `main` + development snapshot; stable via Releases | `main` + snapshot разработки; стабильная версия через Releases |
| `source.commands` | exact repository URL and three run commands in Windows/Linux/macOS order | те же URL/commands/order |

Row-order assertion отдельно требует sequence: `Windows x64` -> `Linux x64 (AppImage)` -> `Linux x64 (.deb)` -> `macOS x64` -> `macOS arm64` -> `Android arm64` -> `Android x64`. Manual bilingual review проверяет естественность текста; автоматическая parity проверяет contract, но не объявляет качество перевода доказанным.

### Protected status-section comparison

Для S1-AC-10 validator читает baseline через `git show origin/main:README.md` / `README.RU.md`, нормализует line endings и извлекает целые sections:
- EN: от `### Task states` до, но не включая, `### Tasks links`;
- RU: от `### Состояния задачи` до, но не включая, `### Связи задач`.

Из baseline section разрешено удалить ровно один exact historical paragraph соответствующей локали (baseline line 83) и один связанный пустой separator. Полученный expected section сравнивается byte-for-byte после newline normalization с current section. Ноль/два совпадения historical paragraph, любое другое отличие marker table, diagram, guards или status-picker paragraph и любой hunk вне baseline allowlist EN `12-28,83-84,212-242` / RU `12-28,83-84,209-239` завершают validation ошибкой. Backlog ranges включают только принадлежащий удаляемому финальному разделу preceding separator, чтобы файл не заканчивался blank line. Evidence сохраняет baseline/expected/actual SHA-256 и diff при mismatch.

Planned validation commands:

```powershell
git status --short
git diff --check
git diff --name-only
git branch --show-current
git rev-parse origin/main
git merge-base --is-ancestor origin/main HEAD

rg -n "chmod -R 755|git-fork\.com|refs/heads/main\.zip|Backlog of features|Бэклог возможностей|No additional steps|Дополнительных действий|IsCompleted=false|Git history каталога задач|Git history of the task storage" README.md README.RU.md

$release = gh release view --repo Kibnet/Unlimotion --json tagName,url,targetCommitish,assets | ConvertFrom-Json
$readmes = (Get-Content -Raw README.md), (Get-Content -Raw README.RU.md)
if ($readmes -match [regex]::Escape($release.tagName)) {
    throw "Current release tag is hardcoded in a README: $($release.tagName)"
}
```

Additional checks implemented as read-only PowerShell during EXEC:
- extract Markdown local links and verify each target exists;
- verify balanced code fences;
- verify EN/RU platform semantic tokens and ordering;
- verify exactly one `/releases/latest` link per README;
- call external URLs with bounded retry or use GitHub/official APIs;
- render Markdown through GitHub API to verify GFM parsing;
- после commit/push открыть **actual GitHub branch render**: root branch page для `README.md` и branch blob page для `README.RU.md`; проверить обе локали в браузере на desktop (~1280 px) и narrow (~390 px), сохранить ignored screenshots/inspection log и проверить CTA visibility, table overflow/legibility, code blocks и caveat placement. Raw API HTML либо unstyled local HTML не закрывает viewport acceptance.
- сохранить ignored evidence в `artifacts/documentation-validation/`: `release-check.json` (время, resolved latest URL, tag, target SHA, полный asset list/classification/verdict), `parity-check.json`, GFM `README*.html`, actual GitHub viewport screenshots/inspection log и protected-section hashes/diff; каталог уже исключён через `.gitignore` и не расширяет committed file scope.

Test applicability:
- Unit/domain/UI tests: `Не применимо`, потому что runtime/UI behavior, layout, state и automation selectors не изменяются.
- Build: `Не применимо`, потому что меняются только Markdown/spec files и run commands сверяются с source без modification.
- Next-best evidence: release API, source/workflow inspection, deterministic Markdown/link/parity checks и rendered output.

Stop rules:
- forbidden fragment найден -> исправить до review;
- platform token/order расходится EN/RU -> исправить обе версии;
- asset отсутствует -> удалить/понизить claim, не изменять release;
- external URL после retry недоступен -> AC остаётся неподтверждённым;
- diff выходит за разрешённые файлы/sections -> откатить unrelated edit;
- Actual GitHub branch render недоступен после bounded retry -> сохранить deterministic/GFM evidence, но S1-AC-09 не считать выполненным: stage 1 остаётся incomplete до восстановления доступа. Исключение возможно только через отдельное amendment и повторное approval master roadmap; текущая child spec deferral не разрешает.

## 12. Риски и edge cases
- Latest assets могут измениться после SPEC: перед EXEC повторить freshness gate.
- Temporary stage-1 validator нормализует optional `v` prefix, но current packaging workflows неединообразно используют raw tag и normalized version в filenames; stage 3 canonical manifest обязан разделить эти значения и проверить future `vMAJOR.MINOR.PATCH` naming до первого такого release.
- Asset filenames содержат version: prose использует stable patterns/classes, не literal current version.
- `.deb` может работать на части систем, но cautious wording сохраняется до matrix evidence.
- AppImage тоже пока не имеет formal support smoke: текст называет его generic published Linux option, но не рекомендует как verified/supported и не обещает distro-wide compatibility.
- Apple/Microsoft/Google UI wording может измениться: ссылаться на official guidance и избегать подробного OS-screen walkthrough.
- Portable updater semantics могут измениться: непосредственно перед edit перепроверить update service.
- Backlog removal может восприниматься как потеря планов: Git history сохраняет содержание, canonical Issues/Projects будет отдельным решением.
- EN/RU могут формально совпасть, но различаться по тону: нужен human bilingual review.
- GitHub Markdown table на узком viewport может быть тяжёлой: render review проверяет читаемость, но full IA redesign отложен.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Почему Linux рекомендует AppImage, если есть `.deb`?» | `.deb` выглядит нативнее | Честно указать `.deb` как preview до Debian 12/13 smoke package | mitigated |
| «Зачем писать про неподтверждённую подпись — это отпугнёт пользователей» | Caveat снижает доверие | Не скрывать возможный OS warning и не утверждать точный byte-level state без native evidence; позже обновить после signing verification | mitigated |
| «Почему удалён backlog без ссылки?» | Пользователь может искать roadmap | Stale/contradictory checklist опаснее; canonical tracker выбирается в full docs package | mitigated |
| «Почему не исправить scripts сразу?» | Проблема очевидна | Script behavior относится к distribution package с собственными tests/rollback | mitigated |
| «README всё ещё длинный» | Stage 1 не делает IA rewrite | Full restructuring уже запланирована stage 7 | accepted-risk |

### Rework Prevention Checklist
- [x] Пользовательские download/source scenarios перечислены.
- [x] Каждое visible claim имеет release/source/workflow evidence.
- [x] Decision Ledger фиксирует support-level choices.
- [x] EN/RU parity имеет automated и manual checks.
- [x] Scope не расширен на scripts/workflows/UI.
- [x] UI/video requirements явно помечены `Не применимо` с причиной.
- [x] Expected objections закрыты или приняты как scoped risk.

## 13. План выполнения
1. Повторить freshness gate: fetch `origin/main`, latest release asset inventory и updater/source contracts.
2. Создать branch `docs/readme-install-safety` от актуального `origin/main`, сохранив утверждённые specs.
3. Сначала переписать EN launch/source block по целевому contract.
4. Синхронно переписать RU block естественным переводом.
5. Удалить парный historical status-migration paragraph, не меняя соседний current status contract.
6. Удалить оба backlog sections целиком.
7. Выполнить deterministic forbidden/link/parity/scope checks и temporary full asset-set classification.
8. Выполнить external release/official-link checks и GitHub GFM parse.
9. Зафиксировать docs/spec diff Conventional Commit, push branch и открыть PR с validation/risk evidence.
10. Проверить actual GitHub branch render EN/RU на desktop и narrow viewport; при visual finding исправить, повторно commit/push и перепроверить.
11. Провести full post-EXEC review; исправить findings и повторить затронутые проверки.
12. Обновить child/master journals и подготовить delivery report.

## 14. Открытые вопросы
Блокирующих вопросов нет.

Child EXEC обязан повторно проверить перед edit:
- latest release и asset inventory не изменились;
- `VelopackApplicationUpdateService` всё ещё ограничивает updater installed builds;
- official Apple guidance URL доступен;
- `origin/main` не ушёл от SPEC baseline; при drift обновить child spec AS-IS/decisions до branch/edit.

## 15. Соответствие профилю
- Профиль: `.NET Desktop Client`.
  - Source/run/update claims сверяются с current desktop projects/services.
  - Production UI flow и selectors не меняются.
  - Build/UI tests неприменимы к docs-only diff; причина и next-best evidence зафиксированы.
- Overlay `UI Automation Testing`: не применим, потому что нет UI behavior/layout/state change.
- Context `testing-dotnet`: test-runner knowledge используется для scope; .NET tests не запускаются без code behavior change.
- Delivery/security review применим к signing/notarization-evidence и platform-support wording.
- Local `AGENTS.override.md` UI-test MUST не срабатывает: изменение не затрагивает UI behavior, visual flow или UI-facing state.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-17-readme-install-safety.md` | Child spec, journal, review evidence | QUEST contract этапа 1 |
| `README.md` | Install/source block + historical migration paragraph + backlog deletion | Исправить публичную EN guidance |
| `README.RU.md` | Семантически эквивалентный RU block + historical migration paragraph + backlog deletion | Исправить публичную RU guidance |
| `specs/2026-07-17-readme-reliability-roadmap.md` | Stage-1 progress/post-EXEC journal | Связать child delivery с master roadmap |

Запрещённые changed files: production source, tests, workflows, scripts, `media/readme`, release assets.

## 17. Таблица соответствий (было -> стало)

| Область | Было | Стало |
| --- | --- | --- |
| Download | Упоминание несуществующей ссылки | Явный `/releases/latest` |
| Platforms | Windows/Debian/macOS | Windows, Linux AppImage/preview `.deb`, macOS x64/arm64, Android |
| Windows | «Дополнительных действий не требуется» | Отсутствие published verified Authenticode evidence + возможный SmartScreen warning |
| macOS | `chmod -R 755` как решение | Отсутствие published verified Developer ID/notarization evidence + official Apple guidance |
| Debian | Безусловная установка | `.deb` available preview до smoke evidence |
| Android | Не упомянут | ABI-aware sideload guidance |
| Source | `main.zip` как latest + Fork promotion | Git clone main как development path; stable через Releases |
| Unix run | Просто имя `.sh` | `bash ./run.*.sh` из repo root |
| Historical migration | Одноразовая status migration + неподтверждённый rollback через Git history | Парный абзац удалён без изменения current status contract |
| Backlog | Stale divergent checklist | Удалён; future canonical tracker вне scope |

## 18. Альтернативы и компромиссы
- Вариант: дождаться full docs/package/signing stages.
  - Плюсы: один будущий rewrite.
  - Минусы: опасная `chmod` guidance и ложные claims остаются публичными.
- Вариант: исправить scripts/workflows вместе с README.
  - Плюсы: меньше caveats.
  - Минусы: существенно другой risk/test scope и нарушение child boundaries.
- Вариант: удалить platform instructions полностью.
  - Плюсы: меньше быстро устаревающего текста.
  - Минусы: пользователь не понимает, какой asset выбрать и почему возникают warnings.
- Выбранный вариант: небольшой truthful install/source patch сейчас, hardening и IA отдельно.
  - Почему лучше: быстрее устраняет вредную guidance и сохраняет проверяемый узкий diff.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, goals и Non-Goals определены |
| B. Качество дизайна | 6-10 | PASS | Copy contract, platform matrix, triggers и error handling определены |
| C. Безопасность изменений | 11-13 | PASS | Runtime/data не меняются; signing-evidence/support caveats и rollback зафиксированы |
| D. Проверяемость | 14-16 | PASS | 12 S1-AC связаны с release/link/parity/render/historical-cleanup/scope/freshness evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | Exact files, copy structure, commands и stop rules заданы |
| F. Соответствие профилю | 20 | PASS | Docs-only applicability и UI-test exclusion обоснованы |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Срочная truthful install/source guidance; unrelated roadmap stages исключены |
| 2. Понимание текущего состояния | 5 | Current README, release assets, scripts/global.json и workflows проверены |
| 3. Конкретность целевого дизайна | 5 | Platform rows, caveats, commands и deletion scope определены |
| 4. Безопасность (миграция, откат) | 5 | Docs-only diff; Git rollback и no-external-side-effect contract явные |
| 5. Тестируемость | 5 | Deterministic checks + external evidence + render review |
| 6. Готовность к автономной реализации | 5 | Нет blocking questions; child scope изолирован |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению после approval

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Support levels и user choices описаны без product overclaim? | PASS | Нет |
| UX / designer | applicable | Новый пользователь быстро находит asset и caveats читаемы? | PASS | Проверить GitHub table render |
| Tester / validation | applicable | Каждый claim имеет deterministic/external evidence? | PASS | Реализовать planned parity/link checks в EXEC |
| Developer / architect | applicable | Source commands совпадают с current code/scripts и scope изолирован? | PASS | Нет |
| Delivery / operations / security | applicable | Signing/support/update wording не скрывает operational risk? | PASS | Повторить freshness gate перед edit |

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: current child spec; approved master roadmap; README baseline allowlists EN `12-28,83-84,212-242` / RU `12-28,83-84,209-239`; all 22 current release assets; `global.json`; desktop/Android updater services; run scripts; Android project metadata; packaging contracts; branch/base/render/delivery gates
- Decision: можно запрашивать подтверждение child spec
- Review passes:
  - Release/evidence audit: PASS; current set классифицирован 22/22, missing/unknown/duplicate = 0.
  - Tester/validation review: PASS после включения historical migration allowlist, dynamic tag, exact parity и protected-section checks.
  - Delivery/operations/security review: PASS после устранения support/signing/update overclaims.
  - UX/test-governance review: PASS после S1 namespace/master mapping, actual GitHub viewport gate и запрета child-level deferral.
  - Re-review after fixes / Fix and re-review: выполнен по финальному draft; три независимых reviewer verdict = PASS.
  - Stop decision: child spec готова к отдельному approval; EXEC до approval запрещён.
- Evidence inspected:
  - `README.md:12-28,83-84,212-242` и `README.RU.md:12-28,83-84,209-239`;
  - current `gh release view`: tag `1.27.0`, target SHA `5aebebcb34eabe35fcdb7a47ff76ffdc2a7e16dd`, 22 assets;
  - temporary expected-class matching: 13 user-facing + 9 updater-internal = 22;
  - `global.json`, `run.*` contents/modes, desktop/Android updater services, Android `SupportedOSPlatformVersion=23`;
  - current detached tag/HEAD/origin-main freshness и master AC-01/12/16/18 stage mapping.
- Depth checklist:
  - Scope drift / unrelated changes: только два README, child/master specs.
  - Acceptance criteria: 12 S1-AC покрывают install/source/backlog/historical-migration cleanup/parity/render/scope/freshness.
  - User-observable scenarios / Decision ledger / Expected objections: заполнены.
  - Validation evidence: full asset contract, dynamic tag, semantic IDs, protected-section comparison, GFM parse и actual GitHub 1280/390 viewport определены.
  - Unsupported claims: available не назван supported/recommended; signing описан как отсутствие verified published evidence, а не byte-level fact; desktop/Android update semantics разделены.
  - Regression / edge case: versioned filenames, all 22 asset classes, architecture, API 23 vs device matrix, network/render failure, protected neighbor text и rebase drift учтены.
  - Comments/docs/changelog: changelog не требуется для correction-only docs patch; spec сохраняет audit trail.
  - Hidden contract change: отсутствует; support workflow не меняется.
  - Manual-review challenge: actual GitHub narrow viewport и естественность RU copy остаются обязательными EXEC checks, а не предположениями SPEC.
- No-findings justification: после исправления всех findings финальные delivery, validation и UX/test re-reviews дали PASS; live temporary asset contract совпал 22/22.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | scope | Historical migration detail из master AC-12 отсутствовала в child allowlist | Добавить точечное удаление и byte-level neighbor protection | fixed |
| HIGH | support | `recommended` могло означать неподтверждённую platform support | Перейти на published/available wording и validation levels | fixed |
| MEDIUM | signing | Workflow inference выдавал byte-level unsigned state за факт | Документировать отсутствие published verified evidence и возможный OS warning | fixed |
| MEDIUM | updates | Desktop managed-update caveat не учитывал Android APK updater | Разделить desktop и Android semantics | fixed |
| MEDIUM | install UX | Android API/permission и portable extraction были неполны | Добавить declared API 23 caveat, version-dependent permission и extraction steps | fixed |
| HIGH | master mapping | Master AC-01 требовал full asset-set comparison до permanent manifest stage 3 | Добавить version-controlled temporary exact 22-asset class contract | fixed |
| MEDIUM | test governance | Child AC numbering конфликтовал с master meanings | Ввести `S1-AC-*` и mapping к master AC-01/12/16/18 | fixed |
| MEDIUM | validation | Static version grep, vague parity и neighbor checks были невоспроизводимы | Dynamic tag, semantic IDs/parity JSON, protected-section comparison | fixed |
| MEDIUM | rendering | Raw GFM HTML не доказывал GitHub viewport usability | Требовать actual pushed-branch render at 1280/390 | fixed |
| MEDIUM | governance | Child-level render deferral конфликтовал с `Allowed deferral = Нет` | Оставлять stage incomplete; исключение только через master amendment/reapproval | fixed |
| LOW | evidence | Live release/official links могут измениться между SPEC и EXEC | Повторить freshness/external checks перед edit и после rebase | follow-up |

- Fixed before continuing: все HIGH/MEDIUM findings исправлены; LOW live-drift risk превращён в обязательный EXEC gate
- Checks rerun:
  - canonical template completeness;
  - SPEC linter A-F;
  - SPEC rubric;
  - Pre-Approval Rework Prevention Gate;
  - independent release, validation, delivery и UX/test re-reviews;
  - exact current asset-class reconciliation 22/22.
- Needs human: формальное approval child spec фразой `Спеку подтверждаю`
- Residual risks / follow-ups:
  - `.deb` и AppImage support остаются pending stage 3 smoke evidence;
  - signing caveats остаются до stage 9;
  - root README останется длинным до stage 7.

### Post-EXEC Review
- Статус: PASS для stage-1 content/local validation; local branch rebased после merged lifecycle prerequisite, force-with-lease push и новый GitHub rerun PR #274 ещё pending
- Scope reviewed: `README.md`, `README.RU.md`, эта child spec, master roadmap, diff относительно `origin/main`, релиз `1.27.0` и все 22 asset, локальные deterministic/external/GFM reports, фактический GitHub render ветки `docs/readme-install-safety`, commit/push и PR #274
- Decision: stage 1 выполнен в согласованном docs-only scope; install/source guidance можно передавать в review, а следующий package начинается только через отдельный child SPEC gate
- Review passes:
  - Scope/Evidence pass: PASS; изменены только два README и две утверждённые spec, runtime/workflows/scripts/media не затронуты.
  - Release-contract pass: PASS; 22/22 asset классифицированы, missing/unknown/multiple/duplicate = 0, все 10 рекламируемых filename-pattern существуют в latest release.
  - Copy/parity pass: PASS после fixes; 20/20 structural parity checks, одинаковые 7 platform rows, одинаковые safety/update/source contracts.
  - Diff/protected-section pass: PASS; ровно 6 разрешённых README hunks, status/concept sections сохранили ожидаемые SHA-256.
  - Link/Markdown/GFM pass: PASS; локальные ссылки, 4 внешних URL, fenced blocks и GitHub Markdown API render проверены.
  - Actual GitHub viewport pass: PASS; EN/RU при `1280x900` и `390x844`, 7 rows и CTA найдены, page-level overflow отсутствует, mobile tables прокручиваются до максимального `scrollLeft`; portable/AppImage, macOS/Android/updater caveats и оба source code blocks найдены и читаемы, console errors = 0.
  - Delivery pass: исходный content commit `458cef7` после local rebase соответствует `b658411`, evidence commit `760f353` — `f9416bb`; lifecycle prerequisite PR #275 merged как `118c2dc`, local branch rebased на актуальный `origin/main`, local S1 validator PASS; remote PR #274 пока остаётся на `760f353` до force-with-lease push и нового rerun.
- Evidence inspected: `artifacts/documentation-validation/release-check.json`, `parity-check.json`, `structural-check.json`, `github-viewport-check.json`, локальные GFM HTML, 16 viewport screenshots, `git diff origin/main...HEAD`, GitHub branch render, merged PR #275 и PR #274
- Depth checklist: happy path, unsupported/unknown claims, future tag normalization, EN/RU drift, protected prose, external-link drift, mobile overflow, signing/update overclaim, rollback и unrelated-diff проверены
- No-findings justification: после исправления copy-review findings повторные release/diff/copy и actual-render проверки не выявили открытых BLOCKER/HIGH/MEDIUM замечаний

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | copy/update | Первичная формулировка обещала broad package-manager detection, хотя код проверяет managed Velopack install | Привязать claim точно к `Velopack.IsInstalled` | fixed |
| MEDIUM | copy/signing | Первичная формулировка могла связывать отсутствие опубликованного evidence с обязательным OS warning | Разделить отсутствие evidence и возможную реакцию ОС; назвать владельца evidence | fixed |
| MEDIUM | spec consistency | Две design-формулировки child spec после README-fix всё ещё использовали причинное `поэтому` для signing evidence и OS warning | Разделить факты также в outcome/table самой spec и повторить поиск | fixed |
| MEDIUM | render evidence | Первые screenshots закрывали таблицу, но не code blocks и platform caveats из S1-AC-09 | Проверить нижний install/source block в обеих локалях и viewports, сохранить DOM metrics и screenshots | fixed |
| MEDIUM | delivery | Draft PR содержал placeholder о будущем viewport evidence, а Post-EXEC spec updates ещё не были отправлены | Commit/push evidence journal, обновить PR body и перевести PR в ready после зелёных checks | fixed |
| HIGH | CI prerequisite | Evidence-only commit выявил pre-existing fixture cleanup/Headless false-await race; повторный `All tests` был cancelled по 30-minute timeout | Исправить lifecycle отдельной approved child spec/PR, пройти exact full suites и только затем rebase/rerun PR #274 | fixed by PR #275; local 606/606, Headless 31/31 и PR #275 GitHub `All tests` PASS; PR #274 rerun pending push |
| LOW | scope contract | Удаление backlog включает соседний пустой separator вне первоначального line allowlist | Зафиксировать separator boundaries и проверять semantic hunks | fixed |
| LOW | responsive UX | На 390 px трёхколоночная таблица требует локальной горизонтальной прокрутки | Проверить достижимость правого края и отсутствие page overflow; пересмотреть IA в stage 7 | accepted follow-up |
| LOW | future release | `v`-prefixed raw tag может расходиться с normalized version в asset names | Разделить raw tag/normalized version в stage-3 canonical manifest и dry run | follow-up stage 3 |

- Fixed before final report: copy/scope/spec-consistency/render findings исправлены; lifecycle prerequisite исправлен и merged в PR #275; local branch rebased на `origin/main@118c2dc`; PR body/evidence journal update, force-with-lease push и повторные checks pending
- Checks rerun после rebase: `pwsh -File artifacts/documentation-validation/validate-stage1.ps1`; release 22/22, parity 20/20, protected sections, 6 scoped hunks, local/external links, Markdown/GFM и `git diff --check` PASS; actual GitHub desktop/mobile render повторяется после force-with-lease push
- Validation evidence: release 22/22; advertised patterns 10/10; parity 20/20; scoped hunks 6/6; protected sections 2/2; external URLs 4/4; table viewports 4/4; lower-content target checks 28/28; screenshots 16; console errors 0
- Unrelated changes: не обнаружены; stage-1 diff ограничен четырьмя документационными файлами
- Needs human: для stage 1 — нет; Stage-2 child spec уже отдельно approved, но её EXEC заблокирован до merge PR #274
- Residual risks / follow-ups: native/package smoke evidence остаётся stage 3; signing evidence — stage 9; root README information architecture и mobile table ergonomics — stage 7

## Approval
Подтверждено пользователем 2026-07-17 точной фразой `Спеку подтверждаю`. EXEC stage 1 разрешён; это approval не распространяется автоматически на следующие child specs.

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | Выполнить stage-1 freshness gate | 1.00 | Нет | Составить child spec | Нет | Не применимо | HEAD/origin main и latest release повторно проверены | `specs/2026-07-17-readme-install-safety.md` |
| SPEC | Спроектировать узкий install/source correction | 0.98 | Только independent review | Пройти multi-role post-SPEC review | Нет | Не применимо | Не смешивать correction с later packaging/full-docs stages | `specs/2026-07-17-readme-install-safety.md` |
| SPEC | Исправить findings независимых review | 1.00 | Нет | Повторить release/validation/delivery/UX reviews | Нет | Не применимо | Закрыты scope, support/signing/update, full asset set, mapping, parity, protected section, viewport и deferral findings | `specs/2026-07-17-readme-install-safety.md` |
| SPEC | Завершить post-SPEC gate | 1.00 | Только formal child approval | Запросить точную фразу `Спеку подтверждаю` | Да | Запрос ещё не отправлен | Все независимые re-review дали PASS; EXEC по roadmap требует отдельного approval | `specs/2026-07-17-readme-install-safety.md` |
| EXEC | Принять approval child spec | 1.00 | Нет | Повторить freshness gate и создать branch | Нет | Пользователь дословно сообщил `Спеку подтверждаю` и попросил выполнить все этапы | Разрешён только stage-1 EXEC; следующие child gates сохраняются | `specs/2026-07-17-readme-install-safety.md` |
| EXEC | Выполнить freshness/base gate | 1.00 | Нет | Изменить README по allowlist | Нет | Не применимо | После fetch HEAD = `origin/main` = tag `1.27.0` commit `5aebebc`; branch создан от `origin/main`, latest release = 22 assets | `README.md`, `README.RU.md`, `specs/2026-07-17-readme-install-safety.md` |
| EXEC | Уточнить separator boundaries | 1.00 | Нет | Применить protected-section/scope validation | Нет | Не применимо | Удаление финального backlog закономерно включает preceding blank separator; semantic scope не расширен | `specs/2026-07-17-readme-install-safety.md` |
| EXEC | Обновить install/source guidance | 0.98 | Независимый copy/render review | Прогнать deterministic и external checks | Нет | Не применимо | EN/RU получили 7-row published-build matrix, caveats, source commands; historical migration/backlog удалены | `README.md`, `README.RU.md` |
| EXEC | Исправить copy-review findings | 1.00 | Нет | Повторить полный validation set | Нет | Независимый reviewer сначала нашёл broad package-manager wording, signing-evidence precision и ложную причинность OS warnings; после fixes вернул PASS | Updater привязан к `Velopack.IsInstalled`; absence of evidence отделена от реакции ОС | `README.md`, `README.RU.md` |
| EXEC | Выполнить deterministic/external/GFM gate | 1.00 | Только actual GitHub viewport после push | Commit и push draft PR | Нет | Не применимо | 22/22 assets, 20/20 parity, 6 scoped hunks, protected hashes, local/external links, Markdown и GFM API PASS | `README.md`, `README.RU.md`, `artifacts/documentation-validation/*` |
| EXEC | Доставить docs patch | 1.00 | Только actual GitHub viewport | Проверить branch render | Нет | Не применимо | Исходный commit `458cef7` отправлен в `docs/readme-install-safety`, открыт draft PR #274; после lifecycle rebase тот же content commit = `b658411` | `README.md`, `README.RU.md`, обе spec, GitHub PR #274 |
| EXEC | Проверить фактический GitHub render | 1.00 | Нет | Провести финальный post-EXEC review | Нет | Независимый review потребовал дополнить первоначальные table-only screenshots нижними командами/caveats; finding закрыт повторным capture | EN/RU desktop/mobile PASS: 7 rows, CTA, no page overflow, table horizontal scroll reachability, 28/28 lower-content target checks, console errors = 0 | `artifacts/documentation-validation/github-viewport-check.json`, 16 screenshots |
| EXEC | Завершить stage-1 post-EXEC gate | 1.00 | Нет | Обновить PR и перейти к SPEC stage 2 | Нет | Не применимо | Открытых BLOCKER/HIGH/MEDIUM findings нет; residual risks маршрутизированы в stages 3/7/9 | `specs/2026-07-17-readme-install-safety.md`, `specs/2026-07-17-readme-reliability-roadmap.md` |
| EXEC | Устранить CI lifecycle prerequisite | 1.00 | Нет | Rebase PR #274 на merged fix и повторить S1/GitHub gates | Нет | Пользователь отдельно approved lifecycle child spec и поручил выполнить все этапы | PR #275 merged в `main` как `118c2dc`; exact local 606/606, Headless 31/31 и все PR #275 GitHub checks PASS | `specs/2026-07-17-test-fixture-lifecycle.md`, GitHub PR #275 |
| EXEC | Повторить freshness/S1 gate после rebase | 1.00 | Только actual branch render после push | Force-with-lease push, обновить PR body, дождаться checks | Нет | Не применимо | Local branch rebased на `origin/main@118c2dc`; remote PR #274 ещё на `760f353`; docs-only 4-file diff, release 22/22, parity 20/20, protected/scoped/link/GFM gates PASS | `README.md`, `README.RU.md`, обе spec, `artifacts/documentation-validation/*` |
