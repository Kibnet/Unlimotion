# Подготовка Unlimotion к отправке в F-Droid

## 0. Метаданные
- Тип (профиль): delivery / build infrastructure / release metadata
- Владелец: Kibnet
- Масштаб: medium
- Целевое семейство / behavior baseline: Android `com.Kibnet.Unlimotion`, updater-free `FdroidBuild=true`, исходный код текущей ветки
- Поверхность: F-Droid main repository, GitHub source/release, локальный и официальный F-Droid BuildServer
- Effective runtime: .NET SDK `10.0.100` stable, Android workload для .NET 10, Android NDK `27.2.12479018`, F-Droid image `registry.gitlab.com/fdroid/fdroidserver:buildserver@sha256:9bae53bb4ddbf8fa5bb7385bf2e62e7c6318f99ab0d25b2a551ad38abb528068`
- Eval baseline / evidence: предыдущая спека `specs/2026-08-08-fdroid-build-variant.md`; commit `94e4ae2f9546208473ee629a26176db4d6de570a`; локальные Android/unit/headless прогоны из её Post-EXEC
- Целевой релиз / ветка: кандидат `1.28.0`, Android versionCode `1028000`, ветка `feat/fdroid-build-variant`
- Ограничения: основной F-Droid-каталог не имеет подтверждённого рецепта .NET/Avalonia; merge и публикация зависят от review F-Droid; внешний push/tag/release/MR выполняется только после отдельного явного подтверждения
- Связанные ссылки:
  - https://f-droid.org/en/docs/Submitting_to_F-Droid_Quick_Start_Guide/
  - https://f-droid.org/en/docs/Build_Metadata_Reference/
  - https://f-droid.org/en/docs/Inclusion_Policy/
  - https://gitlab.com/fdroid/fdroiddata/blob/master/CONTRIBUTING.md
  - https://github.com/libgit2/libgit2/security/advisories/GHSA-j2v7-4f6v-gpg8
  - https://openssl-library.org/news/vulnerabilities-3.0/
  - https://github.com/Kibnet/nodify-avalonia/tree/codex/avalonia-12-support

## 1. Overview / Цель
Подготовить минимальный, проверяемый набор upstream-артефактов для отправки Unlimotion в F-Droid: source-built замены локальных native/Nodify пакетов, Fastlane metadata, draft `fdroiddata` recipe и доказательство сборки в официальном F-Droid-окружении либо честный Request For Packaging (RFP) с воспроизводимым блокером. Остальные managed зависимости восстанавливаются через NuGet и остаются отдельным reviewer/BuildServer gate.

Outcome contract:
- Success means:
  - текущая ветка содержит воспроизводимый arm64 F-Droid build path без собственного updater-а;
  - Nodify и Android native libraries для F-Droid строятся из закреплённых публичных исходников, а tracked binaries удаляются рецептом до scanner-а;
  - metadata и draft recipe проходят доступные F-Droid lint/scanner checks;
  - официальный BuildServer PoC либо строит APK, либо оставляет точный лог и готовый RFP без ложного заявления о готовом MR;
  - результат закоммичен локально; внешняя отправка остаётся отдельным delivery gate.
- Итоговый артефакт / output: локальный commit с source-build scripts, Fastlane metadata, draft recipe, submission runbook и validation evidence.
- Stop rules:
  - не обходить scanner через `scanignore`; ненужные tracked binaries только удалять через `scandelete`/`rm`;
  - не скачивать готовый `LibGit2Sharp.NativeBinaries` или Nodify package в F-Droid build phase;
  - не считать обычный Docker build эквивалентом `fdroid build --server`;
  - при несовместимости .NET/NuGet с BuildServer остановить MR-path и подготовить RFP;
  - не делать push/tag/release/fork/MR/RFP без отдельного явного подтверждения пользователя.

## 2. Текущее состояние (AS-IS)
- Commit `94e4ae2` добавляет `FdroidBuild=true`, arm64-only variant без updater service, `REQUEST_INSTALL_PACKAGES` и update `FileProvider`; standard build сохраняет прежнее поведение.
- Проверены standard/F-Droid Release builds, manifest/APK/assembly markers, 832 unit/integration tests и 36 headless UI tests.
- `global.json` задаёт `10.0.100`, но `rollForward: latestFeature` позволял выбрать preview `10.0.400`; это недостаточное доказательство воспроизводимости.
- Official F-Droid buildserver image — Debian 13/Java 21/Android SDK, но без `dotnet`.
- В `fdroiddata` не найден действующий .NET/NuGet recipe; ревью такого toolchain будет ручным.
- Репозиторий содержит scanner-sensitive tracked binaries:
  - `libgit2-3f4182d.so` — не нужен F-Droid recipe и должен удаляться до scan;
  - `artifacts/nuget-local/NodifyAvalonia.6.6.0-unlimotion.a12.1.nupkg` — нужен обычным локальным сборкам, но F-Droid должен удалить его и собрать замену из исходников после scan.
- `.native/libgit2-src` уже закреплён как submodule; native scripts строят OpenSSL, libssh2 и libgit2, но текущий pack script начинает с готового upstream `.nupkg`, поэтому для F-Droid нужен отдельный source-only packer.
- Nodify fork публичен и MIT; требуемый commit подтверждён как `a8c9a96c80bc5e666aa34c9d3ce5947376e37722`. Команда паковки поддерживает override версии через `dotnet pack ... -p:Version=6.6.0-unlimotion.a12.1`.
- Upstream Fastlane metadata отсутствует; есть icon и локализованные screenshots в `media/readme`.
- Последний GitHub release — `1.27.0`; current branch существенно опережает его. Исторические теги `4.4.x` делают автоматический выбор «наибольшего semver tag» небезопасным.
- F-Droid указывает обычное появление приложения через 24–48 часов после merge metadata; листинг в каталоге сегодня не является управляемым outcome.

## 3. Проблема
Updater-free APK уже существует, но F-Droid не может принять его без source-auditable dependency path, store metadata, точного recipe и доказательства сборки в своём окружении.

## 4. Цели дизайна
- Оставить standard Android/release pipeline без поведенческих изменений.
- Сделать F-Droid arm64 path явным и воспроизводимым.
- Строить изменённые локальные packages из закреплённых FOSS-исходников после scanner phase.
- Не прятать бинарные находки и не заявлять неподтверждённую BuildServer-совместимость.
- Хранить metadata upstream, чтобы F-Droid мог её импортировать.
- Свести будущий version bump к явным `versionName`, `versionCode` и commit SHA.

## 5. Non-Goals (чего НЕ делаем)
- Не гарантируем merge, приём F-Droid или появление в каталоге в конкретный день.
- Не меняем UI, модель задач, storage, sync, Git/Telegram behavior.
- Не удаляем updater из standard/GitHub APK.
- Не добавляем x86_64 APK в F-Droid.
- Не добиваемся reproducible-byte-for-byte совпадения с GitHub APK и не используем `Binaries:`.
- Не переносим весь NuGet dependency graph в исходные submodules в этой итерации; допустимость FOSS NuGet restore проверяет BuildServer/reviewer.
- Не решаем отдельно 16 KB page-size warning, если он не блокирует сборку/scanner; блокирующий результат фиксируется как follow-up.
- Не включаем automatic tag update: исторические `4.4.x` теги могут выбрать неверную версию.
- Не выполняем внешние действия публикации в рамках EXEC без отдельного delivery approval.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `global.json` — запрет silent preview/feature-band roll-forward.
- `.gitmodules`, `.native/nodify-avalonia-src` — публичный pinned source Nodify.
- `scripts/pack-nodify-fdroid.sh` — pack Nodify из submodule в local NuGet feed.
- `scripts/pack-libgit2sharp-nativebinaries-fdroid.sh` — создать минимальный native package из собранных arm64 OpenSSL/libssh2/libgit2 без готового upstream `.nupkg`.
- `scripts/build-fdroid-android.sh` — единый deterministic entrypoint: prerequisites, source packages, restore/build, version stamping, APK path.
- `scripts/test-fdroid-publication.ps1` — static contracts metadata/scripts/recipe и отсутствие опасных shortcuts.
- `src/Unlimotion.Android/Unlimotion.Android.csproj` — не включать x64 native paths в `FdroidBuild=true`.
- `fastlane/metadata/android/{en-US,ru-RU}` — title, short/full descriptions, changelog, icon, screenshots.
- `fdroid/com.Kibnet.Unlimotion.yml` — reviewable draft recipe для переноса в `fdroiddata/metadata` после точного release commit.
- `fdroid/README.md` — команды PoC/submission, signing caveat и решение MR-vs-RFP.
- `scripts/test-android-build-scripts.ps1` — обновлённые F-Droid native-path contracts.

### 6.2 Детальный дизайн
1. SDK pin:
   - `version: 10.0.100`;
   - `rollForward: latestPatch`;
   - `allowPrerelease: false`.
2. Nodify:
   - submodule URL `https://github.com/Kibnet/nodify-avalonia.git`;
   - gitlink exactly `a8c9a96c80bc5e666aa34c9d3ce5947376e37722`;
   - pack only `Nodify/Nodify.csproj` with version `6.6.0-unlimotion.a12.1`;
   - output into `artifacts/nuget-local` after pre-existing tracked package is removed by recipe.
3. Native package:
   - build arm64-v8a only with NDK `27.2.12479018`;
   - использовать OpenSSL `3.0.21`, libssh2 `1.11.1` и официальный libgit2 `1.6.5` commit `155578578b78efc6bae7383a708d470eb206e36a`;
   - generate a minimal `.nuspec` and `.nupkg` containing only required `runtimes/android-arm64/native/*` entries;
   - never download/extract upstream `LibGit2Sharp.NativeBinaries.*.nupkg` in the F-Droid entrypoint.
4. Android build:
   - set `AVALONIA_TELEMETRY_OPTOUT=1`;
   - require explicit `VERSION_NAME` and numeric `VERSION_CODE`;
   - pass `FdroidBuild=true`, `RuntimeIdentifier(s)=android-arm64`, `ApplicationDisplayVersion`, `ApplicationVersion`;
   - output one unsigned Release APK for F-Droid signing.
5. Scanner recipe:
   - `submodules: true`;
   - удалить tracked root `libgit2-3f4182d.so` через `scandelete`, а старый Nodify `.nupkg`, libgit2 test/fuzzer fixtures и unlocked `package.json` — через `rm` до scanner-а;
   - no `scanignore`;
   - record exact full Unlimotion release commit SHA only after the local release commit exists.
6. .NET provisioning:
   - recipe pins SDK `10.0.100` and validates SHA-512 for any downloaded SDK archive/install package;
   - arbitrary floating install scripts are forbidden;
   - exact mechanism is accepted only after it works in official BuildServer image/server mode and is documented in recipe comments/runbook.
7. Metadata:
   - reuse existing icon and two or more current screenshots per locale;
   - text describes local-first task management and Git sync without claiming F-Droid availability before merge;
   - changelog `1028000.txt` mentions initial F-Droid-compatible build and absence of self-updater.
8. Versioning:
   - candidate `1.28.0`, code `1028000 = major*1_000_000 + minor*1_000 + patch`;
   - initial recipe uses manual update mode to avoid historical `4.4.x` tag ambiguity;
   - tag/release are created only after tests and separate delivery approval.
9. Submission choice:
   - BuildServer PASS + lint/scanner PASS -> prepare `fdroiddata` MR payload;
   - BuildServer/toolchain unresolved -> prepare RFP with source URL, license, package id, commit, logs and known .NET blocker.
10. Visual planning artifact: не применимо — UI/layout не меняются; используются уже существующие inspected screenshots.
11. UI test video evidence: не применимо — UI automation behavior не меняется.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| F-Droid build | Maintainer builds candidate `1.28.0` | Один arm64 APK versionName `1.28.0`, versionCode `1028000`, без self-updater | BuildServer log, manifest/APK inspection | AC-1, AC-2 |
| Store listing | Reviewer imports Fastlane metadata | EN/RU description, icon и screenshots отображаются без missing assets | metadata lint, file/dimension check | AC-3 |
| Source audit | Reviewer запускает scanner | Tracked `.so`/Nodify `.nupkg` удалены; пакеты собираются из pinned source; `scanignore` отсутствует | scanner log, submodule SHA, script assertions | AC-4 |
| Toolchain unsupported | BuildServer не может выполнить .NET recipe | Готов честный RFP и лог точного blocker-а; MR не заявлен buildable | runbook/RFP payload | AC-5 |
| Existing GitHub APK user | Пользователь пытается перейти на F-Droid-signed APK | Документировано, что подписи различаются и direct update может потребовать uninstall/backup | runbook text review | AC-6 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Source checkout clean | recipe starts | binaries удалены, submodules pinned, source packages built | missing/wrong submodule SHA -> fail before APK build | No floating branch use |
| BuildServer lacks dotnet | provisioning runs | exact 10.0.100 available | hash/download/install mismatch -> hard fail and RFP | No fallback to preview/latest |
| Packages built | Android build runs | arm64 F-Droid APK emitted | x64 lookup/missing native file -> test/build fail | F-Droid-only condition |
| Validation PASS | delivery gate approved | push/tag/release/MR path may begin | no approval -> remain local | External state protected |
| Validation blocked | stop rule reached | RFP payload prepared | no invented PASS | Log is required evidence |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Release candidate | agent | `1.28.0` after latest `1.27.0` and substantial main changes | 0.90 | User may prefer another product version | Нет; explicit in approval |
| Android versionCode | agent | `1028000` deterministic semantic mapping | 0.88 | Different channel uses another sequence | Нет; F-Droid signing/channel independent |
| Architecture | agent | arm64 only | 0.98 | x64 users excluded | Нет; agreed previous spec/minimal F-Droid path |
| Nodify provenance | agent | pinned public fork as submodule | 0.96 | Fork remains maintenance burden | Нет |
| Tracked package handling | agent | retain for standard builds, `rm` in F-Droid recipe | 0.92 | Reviewer may request upstream removal | Нет; safer compatibility default |
| Automatic updates | agent | disabled initially | 0.95 | Manual metadata updates needed | Нет; avoids wrong historical tag |
| MR vs RFP | agent | evidence-driven branch | 0.99 | RFP is slower than buildable MR | Нет; prevents false claim |
| External publication actions | user | separate explicit approval | 1.00 | Without approval nothing is published externally | Нет для EXEC; Да before delivery phase |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| .NET SDK | `global.json` latestFeature | stable 10.0.1xx patch only, no prerelease | May intentionally fail machines without stable SDK | `dotnet --version`, script contract |
| Android package | Android csproj | F-Droid excludes x64 native includes | Standard still arm64+x64 | standard + F-Droid builds |
| Nodify | tracked local nupkg | F-Droid rebuilds same id/version from pinned source | Standard feed unchanged | pack contents/hash/source SHA |
| Native binaries | current custom pack based on upstream nupkg | F-Droid minimal source-only package | Standard workflow unchanged | archive entries and no download marker |
| Store metadata | absent | Fastlane EN/RU | additive | fdroid lint + asset checks |
| fdroiddata | absent | draft YAML with manual updates | copied after release SHA exists | lint/readmeta/buildserver |
| Signing | GitHub/maintainer secrets | F-Droid signs its artifact | Existing upstream signature cannot directly update | runbook warning |

## 7. Бизнес-правила / Алгоритмы
1. F-Droid build всегда `FdroidBuild=true` и `android-arm64`.
2. `versionCode = major*1_000_000 + minor*1_000 + patch`; для `1.28.0` — `1028000`.
3. Recipe commit — только полный 40-символьный SHA release commit, не branch/tag.
4. Любой готовый binary в source checkout либо удаляется до scan, либо блокирует submission; `scanignore` запрещён.
5. Source-generated binaries появляются только в build phase.
6. Невалидный SDK/archive hash, missing submodule, unexpected package entry или updater marker немедленно останавливает сборку.
7. BuildServer PASS разрешает подготовку MR, но не заменяет отдельное подтверждение внешней отправки.
8. BuildServer FAIL/unsupported переводит результат в RFP, а не в «готово к публикации».

## 8. Точки интеграции и триггеры
- `scripts/build-fdroid-android.sh` — единственная upstream команда полного F-Droid build path.
- `fdroid/com.Kibnet.Unlimotion.yml` вызывает тот же entrypoint в `build:`.
- `scripts/test-fdroid-publication.ps1` вызывается локально и в validation evidence.
- Existing `scripts/test-android-build-scripts.ps1` защищает standard/F-Droid csproj contracts.
- External trigger после EXEC: release tag `1.28.0`, затем перенос YAML в fork `fdroiddata` или RFP.

## 9. Изменения модели данных / состояния
- Persisted application data не меняются.
- Добавляются только build/release metadata и git submodule state.
- Android package id остаётся `com.Kibnet.Unlimotion`.

## 10. Миграция / Rollout / Rollback
- Код приложения и storage migration отсутствуют.
- Rollout:
  1. локальный source-build commit;
  2. validation/Post-EXEC;
  3. отдельное approval на push/tag/release/submission;
  4. MR или RFP по фактическому BuildServer verdict.
- Из-за другой подписи F-Droid APK не обязан обновлять GitHub-signed APK поверх установленного; пользователь должен сделать backup и при необходимости uninstall/reinstall. Это фиксируется в runbook.
- Rollback: revert локального публикационного commit возвращает предыдущий build variant; standard APK не затрагивается. External tag/release не создаётся до отдельного approval.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
- AC-1: F-Droid entrypoint с source-built Nodify/native replacements собирает unsigned arm64 APK с `1.28.0`/`1028000` и `FdroidBuild=true`.
- AC-2: APK/manifest не содержит updater service, `REQUEST_INSTALL_PACKAGES`, update `FileProvider`, update URL/assembly markers; standard build сохраняет updater contract.
- AC-3: Fastlane EN/RU metadata полна, assets читаемы, changelog соответствует versionCode.
- AC-4: recipe использует exact full commit SHA, `submodules: true`, явные `rm`/`scandelete`, не содержит `scanignore` и готовых-package downloads; Nodify/libgit2 gitlinks exact.
- AC-5: официальный BuildServer/server-mode PoC имеет PASS либо сохранённый точный failure log и готовый RFP payload; неизвестный результат не допускается.
- AC-6: runbook объясняет подпись, сроки 24–48h после merge как ориентир, MR-vs-RFP и external approval gate.
- AC-7: affected tests, full `Unlimotion.Test`, full headless UI suite, `git diff --check` проходят после всех изменений.

### Команды проверки
```powershell
pwsh -File scripts/test-fdroid-publication.ps1
pwsh -File scripts/test-android-build-scripts.ps1
dotnet --version
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Release
dotnet test src/Unlimotion.AppAutomation.Headless.Tests/Unlimotion.AppAutomation.Headless.Tests.csproj -c Release
git diff --check
git status --short
```

```bash
AVALONIA_TELEMETRY_OPTOUT=1 VERSION_NAME=1.28.0 VERSION_CODE=1028000 \
  bash ./scripts/build-fdroid-android.sh
fdroid lint com.Kibnet.Unlimotion
fdroid scanner com.Kibnet.Unlimotion
fdroid build --server com.Kibnet.Unlimotion:1028000
```

Stop rules для validation:
- не повторять одинаковый network/toolchain failure более двух раз без новой гипотезы;
- scanner timeout без verdict не считать PASS;
- тест, запущенный до последнего изменения, не является финальным evidence;
- если official server-mode недоступен по инфраструктурной причине, сохранить команду/лог и выбрать RFP.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | source build + archive/APK assertions | version inspection | build log/APK path | — |
| AC-2 | script contracts + unit/headless tests | `aapt2`/zip/strings inspection | manifest/APK log | — |
| AC-3 | metadata paths/text/image checks | inspect reused screenshots | lint output | — |
| AC-4 | static script + fdroid scanner | review YAML and gitlinks | scanner/readmeta output | — |
| AC-5 | `fdroid build --server` | classify MR/RFP | server log/RFP draft | PASS may be blocked only by external infra, then RFP is required |
| AC-6 | text assertions | read runbook | `fdroid/README.md` | — |
| AC-7 | TUnit/unit/headless + diff check | status review | test summaries | — |

## 12. Риски и edge cases
- F-Droid reviewer может не принять скачиваемый .NET SDK или NuGet dependency model из-за отсутствия precedent; mitigation — exact hashes, FOSS licenses, server PoC и RFP fallback.
- `latestPatch` может остановить локальный build на машине без stable 10.0.1xx; это намеренный fail-fast, не повод вернуть preview roll-forward.
- Nodify submodule может усложнить clone; recipe и runbook используют recursive submodules и exact SHA assertion.
- Tracked local Nodify package остаётся в upstream source; recipe удаляет его через `rm`, но reviewer может попросить удалить его полностью из upstream. Это accepted follow-up, потому что немедленное удаление ломает существующие локальные сборки.
- Native library/SDK downloads должны иметь pinned version/hash; любой floating URL/hash является blocker.
- F-Droid signature конфликтует с upstream signature; migration warning обязателен.
- Existing historical tags мешают safe AutoUpdateMode; initial manual update is intentional.
- Listing copy/screenshots могут потребовать правок reviewer-а, но не блокируют source build evidence.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Я хочу опубликовать сегодня, почему не обещан каталог?» | F-Droid review/merge и 24–48h индексация внешние | Сегодняшний управляемый outcome — commit + MR payload или RFP; сроки названы явно | mitigated |
| «Почему нужен ещё один submodule?» | Усложняет repo | Он заменяет недоказуемый precompiled Nodify package точным публичным source SHA только для compliance | mitigated |
| «Почему не удалить все `.nupkg`?» | Scanner не любит binaries | F-Droid удаляет tracked package до scan; standard local build остаётся совместимым | accepted-risk |
| «Почему 1.28.0?» | Версия — product decision | Это следующий release после 1.27.0 и включает большой накопленный main diff; явно входит в approval | mitigated |
| «Почему не auto-update?» | Ожидается автоматизация | Старые 4.4.x теги создают неверный выбор; manual безопаснее для первой версии | mitigated |
| «Сборка Docker прошла — почему ещё RFP?» | Docker и server mode различаются | AC-5 требует official `--server` verdict; иначе только RFP | mitigated |

### Rework Prevention Checklist
- [x] Спека называет видимый результат: APK, listing metadata, MR/RFP payload.
- [x] Каждый user-visible scenario имеет evidence.
- [x] Release/version/source/toolchain решения записаны.
- [x] Вероятные замечания и signing caveat закрыты.
- [x] Role-based review выполнен ниже.
- [x] Acceptance criteria проверяют результат, а не перечисляют подготовку.
- [x] EXEC имеет stop rules и path для доказательства либо честного blocker-а.

## 13. План выполнения
1. Добавить red/static contracts для SDK pin, source-only scripts, metadata и recipe.
2. Добавить pinned Nodify submodule и packer; доказать id/version/commit/output.
3. Добавить source-only arm64 NativeBinaries packer без upstream package download.
4. Условно исключить x64 native includes из F-Droid csproj path; сохранить standard contract.
5. Добавить единый F-Droid build entrypoint с version/env/hash/prerequisite guards.
6. Добавить EN/RU Fastlane metadata и переиспользовать существующие media assets.
7. Добавить draft recipe/runbook с placeholder, который заменяется на полный release commit только после локального commit; не создавать tag.
8. Запустить static/affected tests и source-only arm64 build; проверить APK.
9. Запустить F-Droid lint/scanner и official BuildServer/server-mode PoC.
10. Зафиксировать MR-ready либо RFP-ready outcome в runbook/spec Post-EXEC.
11. Запустить fresh full unit/headless validation, diff/status review, Post-EXEC review.
12. Создать отдельный conventional commit. Остановиться перед push/tag/release/submission.

## 14. Открытые вопросы
Блокирующих вопросов до EXEC нет. Версия `1.28.0`, versionCode `1028000` и внешний delivery gate входят в явное подтверждение этой спеки.

## 15. Соответствие профилю
- Профиль: delivery / build infrastructure / release metadata.
- Выполненные требования профиля:
  - exact toolchain/source versions;
  - no-secrets/no-signing boundary;
  - reproducibility and scanner gates;
  - rollback and external side-effect gate;
  - UI tests остаются в regression suite, хотя UI behavior не меняется;
  - MR-vs-RFP outcome основан на evidence.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `global.json` | stable SDK roll-forward | воспроизводимость |
| `.gitmodules`, `.native/nodify-avalonia-src` | pinned Nodify source | source audit |
| `scripts/pack-nodify-fdroid.sh` | source pack | заменить tracked binary в F-Droid build |
| `scripts/pack-libgit2sharp-nativebinaries-fdroid.sh` | minimal source-only package | исключить upstream prebuilt base |
| `scripts/build-fdroid-android.sh` | orchestration/version/toolchain | единый recipe entrypoint |
| `scripts/test-fdroid-publication.ps1` | contracts | fail-fast regression coverage |
| `scripts/test-android-build-scripts.ps1` | x64/F-Droid assertions | сохранить standard behavior |
| `src/Unlimotion.Android/Unlimotion.Android.csproj` | conditional x64 native items | arm64-only source package |
| `fastlane/metadata/android/...` | EN/RU texts/media/changelog | store listing |
| `fdroid/com.Kibnet.Unlimotion.yml` | draft recipe | review/submission payload |
| `fdroid/README.md` | runbook/RFP/signing caveat | безопасная доставка |
| эта спека | EXEC evidence/Post-EXEC | audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| SDK | preview может быть выбран | stable 10.0.1xx only |
| Nodify | tracked package без source link in build | exact source submodule + packer |
| Native package | repack готового upstream nupkg | F-Droid minimal package from built sources |
| F-Droid metadata | отсутствует | EN/RU Fastlane + recipe draft |
| Scanner | binary findings/timeout без verdict | explicit scandelete + bounded verdict |
| Submission | общий план | evidence-based MR/RFP payload |
| External actions | не определены | отдельный explicit approval gate |

## 18. Альтернативы и компромиссы
- Удалить tracked Nodify `.nupkg` глобально:
  - Плюсы: чище source tree.
  - Минусы: ломает обычный restore до source pack; расширяет scope.
  - Почему не выбрано: `scandelete` + source rebuild минимальнее и rollback-safe.
- Использовать fdroiddata `srclib` для Nodify:
  - Плюсы: не добавляет submodule upstream.
  - Минусы: отдельный fdroiddata srclib metadata, хуже self-contained и docs рекомендуют submodule.
  - Почему не выбрано: pinned upstream submodule проще проверять и повторять.
- Сразу подать buildable MR без server PoC:
  - Плюсы: быстрее внешне.
  - Минусы: высокая вероятность отклонения неизвестного .NET toolchain.
  - Почему не выбрано: RFP fallback честнее и экономит reviewer time.
- Оставить `latestFeature`:
  - Плюсы: локально легче собрать.
  - Минусы: preview build не доказывает pinned release toolchain.
  - Почему не выбрано: compliance важнее silent fallback.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | цель, границы, AS-IS и outcome заданы |
| B. Качество дизайна | 6-10 | PASS | source/toolchain/version/contracts определены |
| C. Безопасность изменений | 11-13 | PASS | standard path, rollback, signing и external gate защищены |
| D. Проверяемость | 14-16 | PASS | AC-to-test, scanner и BuildServer verdict обязательны |
| E. Готовность к автономной реализации | 17-19 | PASS | порядок, файлы и stop rules конкретны |
| F. Соответствие профилю | 20 | PASS | delivery/security role применена |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | каталог не обещан; локальный/MR/RFP outcome разделены |
| 2. Понимание текущего состояния | 5 | commit, binaries, SDK, BuildServer и tags проверены |
| 3. Конкретность целевого дизайна | 5 | exact scripts, versions, SHAs и metadata structure |
| 4. Безопасность (миграция, откат) | 5 | standard path, signing caveat и external gate |
| 5. Тестируемость | 5 | scanner/buildserver/APK/full regression matrix |
| 6. Готовность к автономной реализации | 5 | нет blocking questions; stop rules explicit |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Соответствует ли outcome желанию опубликовать максимально быстро? | PASS | Разделены controllable submission today и external listing delay |
| UX / designer | applicable | Корректны ли store copy/assets и migration visibility? | PASS | Требуются EN/RU assets и signing warning в runbook |
| Tester / validation | applicable | Есть ли evidence для каждого AC и negative path? | PASS | BuildServer FAIL превращён в проверяемый RFP outcome |
| Developer / architect | applicable | Согласованы ли source/package/build boundaries? | PASS | Dedicated source-only packers не меняют standard pipeline |
| Delivery / operations / security | applicable | Учтены ли scanner, secrets, signatures, tags и rollback? | PASS | Запрещены scanignore/floating SDK/external action без approval |

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: `specs/2026-08-09-fdroid-submission-readiness.md`, central QUEST/delivery stack, current branch/commit, planned files, official F-Droid docs and buildserver evidence.
- Decision: можно запрашивать подтверждение.
- Review passes:
  - Scope/Evidence pass: PASS — UI/product scope не затронут, evidence sources перечислены.
  - Contract pass: PASS — source-only, versioning, scanner, signing и external gates формальны.
  - Adversarial risk pass: PASS — неизвестный .NET precedent не скрыт, RFP fallback обязателен.
  - Role-Based pass: PASS — пять ролей рассмотрены.
  - Re-review after fixes / Fix and re-review: PASS после трёх исправлений ниже.
  - Stop decision: запросить точное подтверждение; EXEC до него запрещён.
- Evidence inspected:
  - current Git refs: HEAD `94e4ae2f...`, origin/main `e11cae9a...`, latest release `1.27.0`;
  - official F-Droid Quick Start, Inclusion Policy, Build Metadata and CONTRIBUTING;
  - official buildserver image contents/digest and lack of dotnet;
  - fdroiddata absence of a working .NET/NuGet recipe;
  - tracked binary list and scanner rules;
  - native build/pack scripts, Android csproj, global.json;
  - Nodify fork commit/project/package metadata.
- Depth checklist:
  - Scope drift / unrelated changes: не найдено; planned changes только build/delivery metadata.
  - Acceptance criteria: все значимые outcomes в AC-1..AC-7.
  - User-observable scenarios / Decision ledger / Expected objections: заполнены.
  - Validation evidence: required fresh runs and server-mode verdict.
  - Unsupported claims: обещание «сегодня в каталоге» удалено.
  - Regression / edge case: standard updater, x64 standard build, preview SDK, signatures, historical tags.
  - Comments/docs/changelog: Fastlane changelog и runbook обязательны.
  - Hidden contract change: F-Droid signature incompatibility explicitly documented.
  - Manual-review challenge: вероятные находки — upstream prebuilt base package, false Docker equivalence, unsafe tag auto-update; все закрыты.
- No-findings justification: после fixes scope имеет explicit blockers, validation and delivery gates; незакрытых HIGH/MEDIUM design gaps не осталось.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | evidence | «Docker image запускается» не доказывает F-Droid server build | Требовать `fdroid build --server` либо RFP | fixed |
| HIGH | source | Existing native packer скачивает готовый upstream nupkg | Добавить отдельный minimal source-only packer | fixed |
| MEDIUM | versioning | `UpdateCheckMode: Tags` может выбрать historical `4.4.x` | Initial manual update mode | fixed |
| MEDIUM | delivery | «Опубликовать сегодня» смешивает submission и catalog listing | Разделить local/MR/RFP/listing outcomes | fixed |

- Fixed before continuing: source-only native packer, manual update mode, server-mode/RFP gate, realistic listing boundary.
- Checks rerun: manual linter/rubric/role/adversarial re-review этой спеки.
- Needs human: только exact approval фразой ниже; отдельное delivery approval потребуется после EXEC.
- Residual risks / follow-ups: F-Droid может потребовать иной способ provisioning .NET/NuGet или полную source сборку зависимостей; это определяется reviewer-ом/RFP.

### Post-EXEC Review
- Статус: ASK-HUMAN
- Scope reviewed: commits `94e4ae2f`..`1289a92f`, текущий recipe/runbook diff, source package contents, Fastlane metadata, official fdroidserver `readmeta`/`lint`/`scanner` output, TUnit и headless evidence.
- Decision: локальная подготовка и draft recipe готовы к commit, но не к заявлению BuildServer PASS. Для следующего delivery evidence нужен отдельный внешний gate: push source commit `1289a92f...`, затем public-source scanner и `fdroid build --server`.
- Review passes:
  - Scope/Evidence pass: PASS — изменения ограничены build/compliance/metadata, UI и standard updater behavior не менялись.
  - Contract pass: PASS — exact SDK/source/archive/package versions, arm64-only F-Droid path, no signing secrets, no `scanignore`, manual update mode и external approval gate формализованы.
  - Adversarial risk pass: PASS после fixes — review обнаружил vulnerable libgit2 `1.6.4` и устаревший OpenSSL `3.0.14`; source pins обновлены до official libgit2 `1.6.5` и OpenSSL `3.0.21`. Reviewer sandbox не был технически read-only, поэтому этот проход считается adversarial fallback, а не независимым read-only approval.
  - Scanner pass: PASS для local-mounted source — найденные ранее libgit2 test/fuzzer fixtures и unlocked `package.json` перенесены в `rm`, tracked root `.so` остаётся в `scandelete`; `scanignore` отсутствует. Public-source повтор остаётся после push.
  - Regression pass: BLOCKED для full-green gate — affected Android project contract `1/1` и отдельный `Unlimotion.UiTests.Headless` `36/36` прошли; полный TUnit дал `829/832`, а `RoadmapGraphUiTests` — `44/47`. Три одинаковых межтестовых сбоя (`Collection was modified`/dispatcher ownership) прошли изолированно `1/1` каждый, но это не превращает общий suite в PASS.
  - Build evidence pass: ASK-HUMAN — OpenSSL/libssh2/libgit2, native package `2.0.324-android.7.fdroid.2` и Nodify package собраны из закреплённых исходников; fresh APK не получен на host из-за `NETSDK1147` для отсутствующего/рассинхронизированного `wasm-tools` workload, а официальный server-mode нельзя честно запустить до публикации source SHA.
- Evidence inspected:
  - source commit `1289a92f3df58ff6dab0b1cd82e547b4bd44c128` и submodules `155578578b78efc6bae7383a708d470eb206e36a`/`a8c9a96c80bc5e666aa34c9d3ce5947376e37722`;
  - source-built nupkg contents: Nodify managed output и NativeBinaries только `runtimes/android-arm64/native`, без x64;
  - `test-fdroid-publication.ps1`, `test-android-build-scripts.ps1`, targeted/full TUnit и headless logs;
  - актуальный `fdroiddata` master `f6dcb517...` от 2026-08-21;
  - official fdroidserver container: `readmeta` exit 0, exact upstream recipe `lint` exit 0, local mounted source scanner exit 0 без problems;
  - ожидаемый public-source scanner blocker до push: source commit `1289a92f...` отсутствует в public GitHub repository.
- Fixed before continuing:
  - отдельные Nodify `*.fdroid.1` и native `*.fdroid.2` package versions исключили конфликт с tracked standard package/cache;
  - libgit2 переведён с vulnerable fork commit на official security-fixed v1.6.5; Android compatibility patch хранится явно, применяется временно и снимается даже при ошибке;
  - OpenSSL обновлён до `3.0.21` с exact SHA-256;
  - x64 native items оставлены только в standard path;
  - libgit2 `tests`, `fuzzers`, unlocked `package.json` и tracked old Nodify nupkg удаляются через `rm`; tracked root `.so` — через `scandelete`;
  - recipe использует категорию `Writing`, exact source commit `1289a92f...`, exact SDK SHA-512 и explicit `None` update modes.
- Needs human: разрешение на push ветки/source commit. Tag/release/MR/RFP остаются отдельными внешними действиями и не подразумеваются разрешением на push, если пользователь не назовёт их явно.
- Residual risks / follow-ups:
  - F-Droid reviewer может не принять provisioning .NET SDK/workloads или NuGet-managed dependencies; тогда требуется RFP с server log;
  - `fdroid build --server` и свежий APK/manifest proof остаются обязательными после push;
  - full-green gate заблокирован межтестовой изоляцией трёх существующих `RoadmapGraphUiTests`; исправление не включено в минимальную publication scope;
  - reused landscape screenshots валидны как PNG metadata, но reviewer может запросить нативные phone screenshots;
  - F-Droid signature несовместима с GitHub release signature; migration warning находится в runbook.

### Post-EXEC Review Addendum: public source and buildserver-side APK
- Статус: PASS для public scanner и buildserver-side recipe/APK PoC; ASK-HUMAN для tag/release/PR/fdroiddata MR/RFP и полного `fdroid build --server` orchestration.
- Scope reviewed: опубликованная ветка `feat/fdroid-build-variant`, source commits `2bc0d06e` и `eb58cb73`, final draft recipe, public-source scanner, official buildserver `--on-server` output, APK manifest/ABI/signature evidence, fresh NuGet restore и headless UI regression.
- Decision: draft recipe готов к внешнему F-Droid review payload. Нельзя называть доказанным полный client-to-VM `fdroid build --server`: официальный Docker client дошёл до server orchestration, но остановился на отсутствующем Python-модуле `vagrant`; успешный `--on-server` исполняет buildserver-side path, но не доказывает транспорт/VM lifecycle.
- Review passes:
  - Public source pass: PASS — `fdroid scanner` получил `eb58cb7327471be2ca95b43338a437e77f1bcf4e` из GitHub, инициализировал submodules, применил `rm`/`scandelete` и завершился `Finished`, exit `0`, без findings.
  - Metadata pass: PASS — final recipe совпадает с temporary `fdroiddata` metadata по SHA-256; official `fdroid lint` exit `0`; static publication и Android script contracts прошли.
  - Buildserver-side pass: PASS — official image `fdroid build --on-server --verbose com.Kibnet.Unlimotion:1028000` exit `0`, созданы source tarball и unsigned APK.
  - APK pass: PASS — `57418795` bytes, SHA-256 `a68f495886b36ae7a917a4aebef38229b1626f10a56fa0d5917525f29269d9a2`, package `com.Kibnet.Unlimotion`, version `1.28.0`/`1028000`, compile/target SDK `36`, min SDK `23`, native-code только `arm64-v8a`; `REQUEST_INSTALL_PACKAGES`, updater `FileProvider` и `apk_file_paths` отсутствуют; `apksigner` подтвердил отсутствие upstream signature.
  - Dependency/security pass: PASS — revoked ReactiveUI graph заменён на upstream re-signed `ReactiveUI 23.2.28` и `ReactiveUI.Avalonia 12.0.2`; fresh restore с `DOTNET_NUGET_SIGNATURE_VERIFICATION=true` прошёл, NuGet bypass не добавлен.
  - UI regression pass: PASS — отдельный `Unlimotion.UiTests.Headless` после dependency update прошёл `36/36` serially.
- Fixed before continuing:
  - добавлен Debian `libicu76`, необходимый .NET runtime в clean buildserver image;
  - через buildserver `sdkmanager` устанавливаются API `36`, build-tools `36.0.0` и platform-tools из F-Droid transparency log;
  - F-Droid build script явно передаёт `AndroidSdkDirectory` в restore/build и включает NuGet signature verification;
  - recipe закреплён на final source commit `eb58cb7327471be2ca95b43338a437e77f1bcf4e`; будущий tag `1.28.0` должен указывать на этот SHA, а не на metadata-only commit.
- Residual risks / follow-ups:
  - full `fdroid build --server` нужно повторить в поддерживаемой Vagrant/libvirt buildserver-среде или отправить RFP с текущим `--on-server` evidence;
  - managed NuGet dependency model и `MANAGE_EXTERNAL_STORAGE` остаются предметом F-Droid reviewer policy review;
  - прежний full TUnit result остаётся `829/832` с тремя order-dependent `RoadmapGraphUiTests`, каждый из которых проходил изолированно; это не заявляется full-green;
  - tag, GitHub Release, PR, `fdroiddata` MR и RFP не создавались и требуют отдельного разрешения пользователя.
- Needs human: выбрать и отдельно разрешить следующий внешний шаг — PR/release tag или сразу RFP/`fdroiddata` contribution workflow.

### Post-EXEC Review Addendum: publication PR and full regression
- Статус: PASS для публикационной ветки и PR validation; ASK-HUMAN для merge, tag, GitHub Release и `fdroiddata` MR.
- Scope reviewed: PR #283, .NET 10 test invocation, headless UI lifecycle isolation, publication contract scripts и полный serial TUnit regression.
- Decision: PR #283 открыт и готов к review. После исправления test-runner compatibility и межтестовых утечек полный regression прошёл; это не означает, что приложение уже слито, выпущено или добавлено в каталог F-Droid.
- Review passes:
  - CI compatibility pass: PASS — executable `dotnet test` calls явно используют `--project`, как требует .NET 10/Microsoft.Testing.Platform.
  - Headless lifecycle pass: PASS — окна, view-model bindings, queued dispatcher work и асинхронные `GraphControl` builds завершаются до очистки fixture.
  - Focused UI pass: PASS — `RoadmapGraphUiTests` `47/47` serially.
  - Full regression pass: PASS — `Unlimotion.Test` `832/832` serially; отдельный `Unlimotion.UiTests.Headless` `36/36` serially.
  - Publication contracts pass: PASS — `test-fdroid-publication.ps1` и `test-android-build-scripts.ps1` завершились успешно.
  - Delivery trace pass: PASS — GitHub PR #283 открыт ready for review; ранее разрешённый F-Droid RFP создан как work item #4304.
- Residual risks / follow-ups:
  - полный client-to-VM `fdroid build --server` по-прежнему не доказан из-за отсутствующего Python-модуля `vagrant`; подтверждён buildserver-side `--on-server` path;
  - NuGet-managed dependencies и `MANAGE_EXTERNAL_STORAGE` остаются предметом F-Droid reviewer policy review;
  - F-Droid signing несовместим с текущей GitHub release signature; migration warning остаётся обязательным;
  - initial recipe остаётся arm64-only.
- Needs human: merge PR #283, tag/release и `fdroiddata` MR являются отдельными внешними действиями и не выполняются без явного разрешения.

## Approval
Получено 2026-08-21: пользователь написал точную фразу «Спеку подтверждаю».

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Baseline audit | 0.98 | Нет для design | Сформировать source/submission scope | Нет | Нет | Предыдущий variant уже validated/committed | git/docs/scripts inspection |
| SPEC | Official F-Droid audit | 0.94 | Reviewer verdict unknown | Ввести server-mode/RFP gate | Нет | Нет | .NET recipe precedent отсутствует | official docs/fdroiddata/buildserver |
| SPEC | Dependency provenance | 0.96 | Нет для Nodify/native design | Pin source and add source-only packers | Нет | Нет | Tracked/prebuilt packages недопустимы как единственный source path | Nodify/native scripts |
| SPEC | Version/release design | 0.90 | External delivery approval later | Use candidate 1.28.0/1028000 | Нет до EXEC | Нет | Последний release 1.27.0; branch сильно опережает | tags/log/recipe design |
| SPEC | Post-SPEC review | 0.97 | Exact user approval | Остановиться и запросить фразу | Да | Нет | QUEST gate обязателен для новой итерации | эта спека |
| EXEC | Approval gate | 1.00 | Нет | Начать test-first implementation | Нет | Да: пользователь написал «Спеку подтверждаю» 2026-08-21 | Получено точное обязательное подтверждение | эта спека |
| EXEC | Expected red contracts | 0.99 | Нет | Реализовать pinned source и публикационные артефакты | Нет | Нет | `test-fdroid-publication.ps1 -SkipRecipe` ожидаемо остановился на отсутствующем Nodify submodule | `scripts/test-fdroid-publication.ps1` |
| EXEC | Source-built custom dependencies | 0.98 | Official BuildServer verdict | Собрать recipe после фиксации полного source commit SHA | Нет | Нет | Nodify `a8c9a96c...` и libgit2 `15557857...` закреплены; пакеты `*.fdroid.1`/`*.fdroid.2` собраны без загрузки готовых nupkg | `.gitmodules`, `.native/*`, `scripts/pack-*-fdroid.sh`, Android/shared csproj |
| EXEC | Build orchestration and metadata | 0.97 | Official scanner/server verdict | Зафиксировать source commit, затем pin recipe на его полный SHA | Нет | Нет | Добавлены stable SDK guard, archive hashes, arm64 orchestration, EN/RU Fastlane metadata и безопасный delivery runbook | `global.json`, `scripts/build-fdroid-android.sh`, `fastlane/`, `fdroid/README.md` |
| EXEC | Static and affected contracts | 0.99 | Нет | Продолжить regression и recipe validation | Нет | Нет | `test-fdroid-publication.ps1 -SkipRecipe`, `test-android-build-scripts.ps1` и Android project TUnit contract `1/1` прошли | scripts и `PlatformShellProjectContracts.cs` |
| EXEC | Local source build | 0.93 | Чистый официальный Linux workload environment | Перенести APK proof в F-Droid buildserver после публикации source commit | Нет | Нет | OpenSSL 3.0.21/libssh2/libgit2 1.6.5 и оба локальных nupkg собраны; APK restore остановлен `NETSDK1147` для `wasm-tools`; глобальный workload не ремонтировался | source build log, `artifacts/nuget-local/` (ignored) |
| EXEC | Security review remediation | 0.99 | Official BuildServer verdict | Обновить recipe на новый source commit | Нет | Нет | Adversarial review обнаружил libgit2 1.6.4 GHSA и устаревший OpenSSL 3.0.14; обновлено до official libgit2 1.6.5 и OpenSSL 3.0.21, native package bumped to `.fdroid.2` | submodule, native scripts, explicit Android patch |
| EXEC | Full regression | 0.94 | Причина общей headless изоляции вне publication scope | Не заявлять full-green; сохранить точное evidence | Нет | Нет | Full TUnit `829/832`, RoadmapGraph suite `44/47`; три одинаковых сбоя прошли изолированно `1/1` каждый; отдельный AppAutomation Headless `36/36` | TUnit/HTML reports under ignored `TestResults/` |
| EXEC | Commit sequencing adjustment | 0.99 | Full SHA первого commit | Создать source commit, затем recipe commit | Нет | Нет | Recipe не может pin сам себя; поэтому source/build/metadata фиксируются первым commit, а draft recipe с его SHA — вторым логическим commit | git history, `fdroid/com.Kibnet.Unlimotion.yml` |
| EXEC | Source commit | 1.00 | Нет | Pin recipe на полный SHA | Нет | Нет | Создан security-updated source commit `1289a92f3df58ff6dab0b1cd82e547b4bd44c128`; recipe и runbook закреплены на нём | git commit, recipe `commit` |
| EXEC | Official metadata checks | 0.99 | Нет | Проверить source scanner | Нет | Нет | На актуальном `fdroiddata` official container: `readmeta` exit 0 и exact GitHub recipe `lint` exit 0; warning только о permissions временного `config.yml` | temp fdroiddata/container logs |
| EXEC | Scanner remediation | 0.98 | Public commit availability | Зафиксировать recipe и запросить push approval | Нет | Нет | Первый реальный scan нашёл libgit2 fixtures/unlocked Node manifest; после `rm`/`scandelete` повторный local-mounted source scan завершился exit 0 без problems | `fdroid/com.Kibnet.Unlimotion.yml`, official scanner logs |
| EXEC | External delivery gate | 1.00 | Разрешение на push; затем BuildServer verdict | Остановиться после локального commit и запросить отдельное разрешение | Да | Нет | Source SHA ещё не опубликован; public-source scanner, `fdroid build --server`, tag/release/MR/RFP до отдельного approval запрещены | runbook, Post-EXEC, git remote state |
| EXEC | Branch push approval | 1.00 | Нет для branch push | Опубликовать source commits и повторить public checks | Нет | Да: пользователь разрешил push `feat/fdroid-build-variant` | Ветка и source SHA опубликованы; разрешение не распространяется на tag/release/PR/MR/RFP | Git remote branch |
| EXEC | Revoked package remediation | 0.99 | Нет | Проверить clean signed restore и UI regression | Нет | Нет | Два direct pins переведены на upstream re-signed releases без NuGet bypass; fresh restore и Headless `36/36` прошли | `src/Directory.Packages.props`, restore/headless logs |
| EXEC | Public scanner | 1.00 | Нет | Выполнить buildserver-side recipe | Нет | Нет | Official scanner получил final public SHA `eb58cb73...` и завершился без findings | official scanner log |
| EXEC | Buildserver-side APK PoC | 0.99 | Полный client-to-VM `--server` lifecycle | Зафиксировать evidence и запросить отдельный delivery approval | Да для внешнего delivery | Нет | После ICU, Android SDK components и explicit SDK path official `--on-server` создал unsigned arm64 APK с корректными version/manifest | recipe, APK SHA-256/`aapt`/`apksigner` evidence |
| EXEC | GitHub PR | 1.00 | Нет для создания и обновления PR | Довести PR #283 до green CI | Нет | Да: пользователь написал «Делай» после предложения открыть PR и подготовить ветку | PR открыт ready for review; merge/tag/release не входят в это разрешение | GitHub PR #283, PR body |
| EXEC | .NET 10 CI compatibility | 1.00 | Нет | Использовать explicit test project selection | Нет | Нет | Все executable `dotnet test` вызовы переведены на `--project`, устраняя pre-test MTP failure | workflow и evidence scripts |
| EXEC | Headless test isolation | 0.99 | Remote CI verdict | Зафиксировать teardown и проверить PR CI | Нет | Нет | Завершены queued dispatcher/build операции и разорваны bindings до fixture cleanup; focused `47/47`, full `832/832`, standalone headless `36/36` | UI contract tests, local TUnit evidence |
