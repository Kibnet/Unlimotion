# F-Droid build-вариант без встроенной загрузки APK

## 0. Метаданные
- Тип (профиль): `delivery-task`; `dotnet-desktop-client` + `ui-automation-testing`
- Владелец: Kibnet / Unlimotion
- Масштаб: medium
- Целевое семейство / behavior baseline: GPT-5.6 family baseline
- Поверхность: Work / Codex
- Effective runtime: model ID и reasoning mode средой не раскрыты; fallback не применялся
- Eval baseline / evidence: source/build contract checks, TUnit unit/headless UI tests, standard и F-Droid Android builds; модельные eval не применимы
- Целевой релиз / ветка: после approval — `feat/fdroid-build-variant` от текущего `origin/main`; релиз не назначен
- Ограничения:
  - на SPEC меняется только этот файл;
  - обычная Android/GitHub-сборка сохраняет текущее поведение;
  - первая F-Droid-сборка — `android-arm64`;
  - публикация и внешние merge request не входят в итерацию;
  - environment blocker нельзя маскировать изменением продукта.
- Связанные ссылки: [F-Droid Inclusion Policy](https://f-droid.org/en/docs/Inclusion_Policy/), [.NET Android manifest overlays](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-items), `specs/2026-05-12-android-startup-after-update.md`.

## 1. Overview / Цель
Добавить upstream build-вариант `FdroidBuild=true`, который собирает Android-приложение без собственного APK-updater, `REQUEST_INSTALL_PACKAGES` и update `FileProvider`, не меняя обычную Android-сборку.

Outcome contract:
- Success means:
  - F-Droid arm64 variant компилируется;
  - его manifest не содержит APK-install permission/provider;
  - `AndroidApplicationUpdateService` не компилируется и не регистрируется;
  - standard variant по умолчанию сохраняет updater contract;
  - без update service UI находится в безопасном `Unsupported`, actions disabled, сетевых update-действий нет;
  - оба контракта защищены автоматическими проверками.
- Итоговый артефакт / output: MSBuild switch, standard-channel manifest overlay и regression coverage.
- Stop rules:
  - не выполнять EXEC до точной фразы `Спеку подтверждаю`;
  - не расширять scope на native upgrades, metadata, version scheme или submission;
  - build failure сначала классифицировать как product/environment;
  - без affected build и обязательных тестов результат остаётся незавершённым.

## 2. Текущее состояние (AS-IS)
- Android csproj всегда компилирует `AndroidApplicationUpdateService` и объявляет `android-arm64;android-x64`.
- Base `AndroidManifest.xml` всегда содержит `REQUEST_INSTALL_PACKAGES` и update `FileProvider`.
- `MainActivity.ConfigureAppServices()` всегда регистрирует Android updater.
- `App` уже short-circuit-ит automatic update flow без поддерживаемого service; `SettingsViewModel` имеет `Unsupported`.
- `SettingsControl` при `Unsupported` остаётся видимым, но update actions disabled.
- Android script tests не различают standard/F-Droid manifest contracts.
- Есть TUnit updater tests и Avalonia.Headless update-section tests.
- Локальная `main` чистая, но на два test-only коммита позади `origin/main`; EXEC начинается отдельной веткой от `origin/main`.
- Локально доступны native package `artifacts/nuget-local/LibGit2Sharp.NativeBinaries.2.0.324-android.7.nupkg` и Android workload `36.1.69/10.0.100`; `global.json` фактически выбирает установленный preview SDK `10.0.400-preview.0.26322.102`, поэтому EXEC обязан явно записать effective SDK в evidence, не выдавая этот прогон за доказательство воспроизводимости.

## 3. Проблема
Нет проверяемого upstream-способа собрать APK, в котором магазин управляет обновлениями, а собственный APK-updater и связанные Android-компоненты гарантированно исключены.

## 4. Цели дизайна
- Явный MSBuild contract вместо downstream patch.
- Полная обратная совместимость standard GitHub Android build.
- Store-neutral base manifest + opt-in updater overlay для standard channel.
- Статически проверяемое исключение updater source/resource.
- Existing `Unsupported` UI без нового экрана в первой итерации.
- Script/unit/headless UI/Android build evidence.

## 5. Non-Goals (чего НЕ делаем)
- Не подаём приложение и не создаём пока fdroiddata/Fastlane metadata.
- Не выбираем signing strategy, application ID и окончательную формулу `versionCode`.
- Не обновляем libgit2/OpenSSL/libssh2/NuGet packages.
- Не доказываем пока допустимость .NET Android/NuGet на official buildserver.
- Не удаляем self-update из GitHub channel.
- Не скрываем update section: F-Droid видит existing `Unsupported` state.
- Не меняем `MANAGE_EXTERNAL_STORAGE` и storage flow.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `Unlimotion.Android.csproj` — `FdroidBuild`, compiler constant, conditional compile/resource/overlay и arm64 default.
- `AndroidManifest.xml` — общий store-neutral manifest.
- `AndroidManifest.Updater.xml` — standard-only updater permission/provider overlay.
- `MainActivity.cs` — conditional registration по `FDROID_BUILD`.
- Android script test — статический contract обоих variants.
- `SettingsViewModelTests` — unsupported automatic-check contract.
- `SettingsControlResponsiveUiTests` — headless UI evidence безопасного state.

### 6.2 Детальный дизайн
- `FdroidBuild` default `false`.
- При `true`: добавить `FDROID_BUILD`; default RID `android-arm64`; `Compile Remove` updater service; `AndroidResource Remove` `apk_file_paths.xml`; updater overlay не подключать.
- При `FdroidBuild!=true`: подключать `AndroidManifest.Updater.xml` через `AndroidManifestOverlay`.
- `MainActivity` в standard branch сохраняет updater; в F-Droid branch вызывает `App.ConfigureUpdateService(null)` и не ссылается на исключённый namespace/type.
- Base manifest сохраняет общие network/storage entries; переносится только updater-specific permission/provider.
- Updater source остаётся upstream для GitHub channel; scanner/scandelete — следующая итерация.
- Ошибка: F-Droid startup продолжает работу через existing `Unsupported`; runtime performance impact отсутствует.

Visual planning artifact — layout не меняется, меняется только state:

```text
Standard: Settings -> Updates -> Idle/Checking/Available -> actions по состоянию
F-Droid:  Settings -> Updates -> Unsupported -> all update actions disabled -> no network
```

UI video: `Не применимо` — TUnit/Avalonia.Headless harness не записывает окно. Next-best evidence: assertions по automation-id, button states и status text; локальный screenshot допустим, но не коммитится по умолчанию.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Standard Android | Открыть настройки | Updater работает как раньше | Existing UI tests + standard build/manifest | AC-4, AC-5 |
| F-Droid Android | Запустить APK, открыть настройки | `Unsupported`, update buttons disabled | New headless UI test + F-Droid build | AC-1, AC-3 |
| F-Droid startup | Старый config содержит auto-check=true | Нет GitHub check/download | Unit test + compile exclusion | AC-2, AC-3 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Standard, service supported | Startup/action | Existing updater transitions | Existing error/concurrency unchanged | Regression-only |
| F-Droid, service absent | Startup | Остаётся `Unsupported`, no network | auto-check не запускается | Store-safe invariant |
| F-Droid settings | Render | `Unsupported`, buttons disabled | Нет pending update | Layout unchanged |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Build switch вместо global removal | agent | `FdroidBuild=false` default | 0.95 | Иначе ломается GitHub updater | Нет |
| Base manifest + overlay | agent | Updater only in standard overlay | 0.92 | Merge regression | Нет; build checks |
| ABI первой итерации | agent | arm64 | 0.90 | x86_64 отложен | Нет |
| F-Droid update UI | agent | Existing visible `Unsupported` | 0.82 | UX неидеален, но безопасен | Нет; утверждается spec |
| Signing/versionCode | agent | Следующая итерация | 0.95 | Пока нельзя подавать final metadata | Нет |
| Рабочая ветка | agent | От `origin/main` после approval | 0.95 | Иначе пропускаются два тестовых коммита | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Build mode | Отсутствует | `-p:FdroidBuild=true` | Default false | csproj/script + two builds |
| Manifest | Base manifest | Standard overlay | Standard итог эквивалентен текущему | manifest inspection |
| Registration | `MainActivity` | Compile-time conditional | Standard unchanged | compile/tests |
| ABI | arm64+x64 | F-Droid default arm64 | Data unaffected | MSBuild/build output |
| Settings | `Settings.json/Updates` | Без migration | auto-check безопасен без service | unit test |

## 7. Бизнес-правила / Алгоритмы
1. Без `FdroidBuild` standard behavior неизменен.
2. При `FdroidBuild=true` APK не умеет скачивать/устанавливать APK updates.
3. F-Droid manifest не содержит `REQUEST_INSTALL_PACKAGES` и update provider.
4. Сохранённый auto-check не вызывает network call без service.
5. F-Droid build failure не разрешает ослаблять NuGet/security policy без отдельной spec.

## 8. Точки интеграции и триггеры
- CLI trigger: `-p:FdroidBuild=true`.
- Project evaluation выбирает compile/resource/manifest items.
- `MainActivity.OnCreate -> ConfigureAppServices` выбирает updater registration.
- `App.ConfigureUpdateService(null)` активирует existing `Unsupported` state.
- Script test валидирует static contracts до дорогого build.

## 9. Изменения модели данных / состояния
- Persisted model не меняется.
- `FdroidBuild` — build-time property.
- Update settings не мигрируются.
- F-Droid runtime state: service absent, UI `Unsupported`.

## 10. Миграция / Rollout / Rollback
- После approval создать ветку от `origin/main`, сохранив spec.
- Внести conditional changes, затем staged validation.
- User data и package ID не меняются; переустановка не требуется.
- Standard workflow не передаёт `FdroidBuild`, значит сохраняет updater.
- Rollback: вернуть permission/provider в base manifest, удалить overlay/property/guards; data rollback не нужен.

## 11. Тестирование и критерии приёмки
Acceptance Criteria:
- AC-1: `FdroidBuild=true` компилирует arm64 project без updater service.
- AC-2: F-Droid manifest не содержит `REQUEST_INSTALL_PACKAGES`, `com.Kibnet.Unlimotion.fileprovider` и provider metadata.
- AC-3: без update service automatic flow не делает check/download; UI `Unsupported`, actions disabled.
- AC-4: standard default включает updater service/permission/provider и текущие два RID.
- AC-5: existing updater unit/headless UI tests остаются зелёными.
- AC-6: Android script regression test фиксирует conditional contracts.
- AC-7: полные `Unlimotion.Test` и `Unlimotion.UiTests.Headless` зелёные в свежих последовательных процессах.
- AC-8: diff содержит только утверждённые файлы, без generated artifacts.

Добавить:
- unit test: automatic update flow пропускает unsupported service;
- headless UI test: `ConfigureUpdateService(null)` даёт unsupported status и disabled buttons;
- script assertions: base manifest, updater overlay, conditional csproj/MainActivity contracts.

Команды, последовательно:

```powershell
dotnet --version
dotnet workload list
Test-Path -LiteralPath artifacts/nuget-local/LibGit2Sharp.NativeBinaries.2.0.324-android.7.nupkg
pwsh -File scripts/test-android-build-scripts.ps1
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/SettingsViewModelTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter "/*/*/SettingsControlResponsiveUiTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Release -p:RuntimeIdentifier=android-arm64 -p:RuntimeIdentifiers=android-arm64
dotnet build src/Unlimotion.Android/Unlimotion.Android.csproj -c Release -p:FdroidBuild=true -p:RuntimeIdentifier=android-arm64 -p:RuntimeIdentifiers=android-arm64
# Проверить оба generated/APK manifests через доступный aapt2 либо generated AndroidManifest.xml.
dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
dotnet test tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -- --maximum-parallel-tests 1 --output Detailed
git diff --check
git status --short
```

Перед Android/full-suite — SDK/workload/runner preflight. Progress evidence: MSBuild/TUnit detailed output и artifact timestamps. После timeout не повторять идентичную команду: проверить process/locks/SDK/restore/native feed. `Zero tests ran` не PASS.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-1 | F-Droid Android build | Compile exclusion in log/diff | Build output/APK | — |
| AC-2 | Script + manifest inspection | no-match for permission/provider | Manifest output | — |
| AC-3 | Unit + headless UI tests | automation-id/status assertions | TUnit output | Video unavailable in headless harness |
| AC-4 | Script + standard build | Standard manifest contains overlay | Build/manifest output | — |
| AC-5 | Existing targeted/full tests | No failed tests | TUnit output | — |
| AC-6 | Android script test | Success message | PowerShell output | — |
| AC-7 | Two full project runs | Green summaries | TUnit output | — |
| AC-8 | diff/status checks | Relevant diff review | Git output | — |

## 12. Риски и edge cases
- Overlay merge может изменить standard provider: проверить оба manifests.
- `Compile Remove` без conditional `using` сломает compile: guard namespace/type тем же symbol.
- CLI RID может конфликтовать с default: validation передаёт single RID явно.
- Updater source остаётся виден scanner: следующая fdroiddata-итерация решает `scandelete`/review expectation.
- Старый config включает auto-check: regression test доказывает short-circuit.
- Android workload/native feed может отсутствовать: честно фиксировать environment blocker.
- Local `global.json` сейчас выбирает preview SDK через `latestFeature`: результаты пригодны как local compatibility evidence, но pinning/reproducibility остаются отдельным publication blocker.
- Отстающая `main`: EXEC branch from `origin/main`.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Это ещё не полная публикация | Итерация намеренно мала | Non-Goals и следующие этапы явны | mitigated |
| Не сломается ли GitHub updater | Manifest разделяется | Default false + standard build/tests | mitigated |
| Почему виден неработающий update section | Минимальный layout-preserving change | Existing safe `Unsupported`; polish позже | accepted-risk |
| Доказывает ли это принятие .NET/NuGet | Главный внешний риск остаётся | Такой claim запрещён; нужен отдельный PoC | mitigated |
| Почему только arm64 | ABI split усложняет versionCode | Минимальный реальный канал; x64 позже | accepted-risk |

### Rework Prevention Checklist
- User-visible states названы: да.
- Каждый scenario имеет evidence: да.
- Assumed decisions перечислены: да.
- Likely objections обработаны: да.
- Role review: ниже; independent review обязателен до approval.
- AC являются проверками результата: да.
- EXEC proof path определён: да.

## 13. План выполнения
1. После approval создать branch от `origin/main`, сохранив spec.
2. Добавить failing/static contracts и targeted unsupported tests.
3. Перенести updater entries в standard manifest overlay.
4. Добавить `FdroidBuild` и compile/resource/MainActivity guards.
5. Быстрые script/targeted tests.
6. Standard и F-Droid arm64 builds + manifest inspection.
7. Full sequential TUnit runs + diff/status checks.
8. Independent post-EXEC review, fixes и reruns.
9. Остановиться; native security, buildserver, versioning и metadata — следующие specs.

## 14. Открытые вопросы
Блокирующих вопросов нет. Signing, versionCode, fdroiddata metadata и native upgrades сознательно отложены.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` + `ui-automation-testing`; context `testing-dotnet`; governance `QUEST`.
- Platform-specific conditional остаётся в Android project/MainActivity; automation-id стабильны; добавляется Avalonia.Headless coverage; запланированы targeted/full tests и Android builds; video fallback обоснован.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-08-08-fdroid-build-variant.md` | QUEST contract/log/reviews | Управляемая реализация |
| `src/Unlimotion.Android/Unlimotion.Android.csproj` | Property и conditional items | F-Droid variant |
| `src/Unlimotion.Android/Properties/AndroidManifest.xml` | Удалить updater entries | Store-neutral base |
| `src/Unlimotion.Android/Properties/AndroidManifest.Updater.xml` | Новый standard overlay | Сохранить GitHub updater |
| `src/Unlimotion.Android/MainActivity.cs` | Compile guard | Не регистрировать updater |
| `scripts/test-android-build-scripts.ps1` | Static contracts | Быстрая regression check |
| `src/Unlimotion.Test/SettingsViewModelTests.cs` | Unsupported auto-check test | Нет background update |
| `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` | Headless unsupported UI test | Local UI MUST |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Build channels | Один updater contract | Standard + `FdroidBuild=true` |
| Base manifest | APK install capability всегда | Store-neutral + standard overlay |
| F-Droid binary | Не существует | Updater source/resource/provider excluded |
| Standard APK | Updater enabled | Default unchanged |
| F-Droid UI | Нет channel | Existing `Unsupported`, actions disabled |
| Evidence | Общие tests | Explicit dual-build contracts |

## 18. Альтернативы и компромиссы
- Global updater removal: проще, но ломает GitHub users.
- Только downstream patch: маленький upstream diff, но хрупок и плохо проверяем.
- Отдельный Android project: сильная изоляция, но дублирование и maintenance cost.
- Выбрано MSBuild property + overlay: минимальный upstream diff и проверяемый store-safe build.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, одна проблема, goals и Non-Goals заданы. |
| B. Качество дизайна | 6-10 | PASS | Ownership, integration, state, errors и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Standard compatibility, branch и rollback заданы. |
| D. Проверяемость | 14-16 | PASS | AC, tests/builds/manifests и files сопоставлены. |
| E. Готовность к автономной реализации | 17-19 | PASS | Этапы/решения/компромиссы определены. |
| F. Соответствие профилю | 20 | PASS | Avalonia/TUnit/UI requirements отражены. |

Итог: ГОТОВО

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Одна задача и строгие Non-Goals. |
| 2. Понимание текущего состояния | 5 | Прослежены project/manifest/MainActivity/App/UI/tests. |
| 3. Конкретность целевого дизайна | 5 | Property, overlay и guards заданы. |
| 4. Безопасность | 5 | Default compatibility, no data migration, rollback. |
| 5. Тестируемость | 5 | Каждый AC имеет evidence. |
| 6. Автономная реализация | 5 | Нет блокирующих решений; stop rules заданы. |

Итоговый балл: 30 / 30
Зона: готово после approval

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Разделены ли store/GitHub channels без поломки users? | PASS | Нет |
| UX / designer | applicable | Безопасен ли F-Droid update state? | PASS с risk | `Unsupported` явен; polish позже |
| Tester / validation | applicable | Оба contracts имеют evidence? | PASS | Нет |
| Developer / architect | applicable | Минимальны ли boundaries? | PASS | Нет |
| Delivery / operations / security | applicable | Исключена ли install capability и есть rollback? | PASS | Independent check overlay/scanner |

### Post-SPEC Review
- Статус: PASS
- Scope reviewed: эта spec; central QUEST/tool/testing owners; .NET/UI profiles; Android project/manifest/MainActivity; updater App/ViewModel/UI/tests; Android scripts/workflow; Git state; official F-Droid/.NET docs.
- Decision: можно запрашивать approval; independent reviewer был запущен, но технически не вернул результат, поэтому выполнен отдельный adversarial fallback с явным residual risk.
- Review passes:
  - Scope/Evidence: выполнен.
  - Contract: scope ограничен store-safe split; standard invariant явен.
  - Adversarial risk: повторно проверены overlay support в установленном SDK, compile exclusion, stale config, scanner, branch drift, native package/workload и preview SDK.
  - Role-Based: BA, UX, tester, architect, delivery/security.
  - Fix and re-review: убран небезопасный `--no-restore` до первого restore; добавлен explicit toolchain/native preflight и preview-SDK risk; затронутые секции перечитаны.
  - Stop decision: `PASS` по adversarial fallback; независимость review недоступна и не заявляется.
- Evidence inspected: перечисленные files/docs, current Git status/origin diff, existing tests, installed `Xamarin.Android.Common.targets` с `ManifestOverlayFiles="@(AndroidManifestOverlay)"`, SDK/workload/native package preflight и memory hints, перепроверенные по repo.
- Depth checklist:
  - Scope drift: до approval только spec; origin delta не пересекается.
  - AC: восемь, все в matrix.
  - Scenarios/decisions/objections: заполнены.
  - Validation: команды заданы; первый targeted run теперь допускает restore; фактические результаты только EXEC.
  - Unsupported claims: F-Droid acceptance не заявляется.
  - Edge cases: перечислены.
  - Docs/changelog: changelog пока не нужен для internal build switch.
  - Hidden contract: standard default invariant; F-Droid UI risk явен.
  - Manual-review challenge: overlay/scanner могут оставить updater capability/source; нужны manifests и independent review.
- No-findings justification: не применимо — fallback review выявил и исправил validation/environment findings.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | validation | Targeted tests были запланированы с `--no-restore` без предшествующего restore | Убрать `--no-restore` либо добавить restore | fixed |
| MEDIUM | evidence | Local SDK selection уходит на `10.0.400-preview` из-за `latestFeature` | Зафиксировать effective SDK и не заявлять reproducibility | fixed |
| LOW | review evidence | Independent reviewer трижды не вернул findings даже после interrupt/follow-up without tools | Использовать документированный adversarial fallback; повторить independent review после EXEC | accepted-risk |

- Fixed before continuing: validation commands и environment evidence contract обновлены.
- Checks rerun: SPEC linter/rubric, acceptance-to-test mapping, affected Post-SPEC passes, `git diff --check`.
- Needs human: точная approval phrase после PASS.
- Residual risks: independent review unavailable on SPEC; official buildserver, native security, deterministic versioning, metadata/signing.

### Post-EXEC Review
- Статус: PASS по adversarial fallback
- Scope reviewed: подтверждённая spec; полный tracked diff; новый updater overlay; Git status; static/unit/headless tests; standard и F-Droid arm64 Release builds; merged manifests; F-Droid APK и managed assembly.
- Decision: iteration 1 реализована в утверждённых границах и готова к handoff; это не заявление о готовности к отправке в F-Droid.
- Review passes:
  - Scope/Evidence: фактические восемь изменённых/новых файлов совпадают с утверждённым scope; generated `obj/bin/TestResults` не попали в status.
  - Contract: standard default сохраняет updater service, permission/provider и base two-RID contract; `FdroidBuild=true` даёт arm64 variant без updater registration/service/resource/capability.
  - Adversarial risk: проверены stale incremental outputs через F-Droid `Rebuild`, merged manifests обоих вариантов, APK entries и бинарные updater-маркеры.
  - Role-Based: BA — channel split сохранён; UX — existing `Unsupported` state проверен; tester — AC matrix закрыта; architect — conditional boundary минимален; delivery/security — install capability отсутствует в F-Droid artifact.
  - Fix and re-review: Post-EXEC code findings не потребовали исправлений; все affected sections перечитаны после полных gates.
  - Stop decision: PASS по документированному fallback; независимый reviewer снова завис без статуса/findings и был прерван, поэтому независимость review не заявляется.
- Evidence inspected:
  - static script: PASS после ожидаемого pre-implementation RED и повторный финальный PASS;
  - targeted TUnit: `SettingsViewModelTests` 70/70, `SettingsControlResponsiveUiTests` 13/13;
  - standard arm64 Release: PASS, merged manifest содержит `REQUEST_INSTALL_PACKAGES`, FileProvider и `apk_file_paths`;
  - F-Droid arm64 Release `Rebuild`: PASS, merged manifest не содержит updater capability, APK не содержит updater-only resource entries, managed assembly не содержит updater markers;
  - full TUnit: `Unlimotion.Test` 832/832, `Unlimotion.UiTests.Headless` 36/36;
  - `git diff --check`: PASS; `git status --short --branch`: только утверждённые файлы.
- Depth checklist:
  - Scope drift: нет.
  - AC: AC-1..AC-8 сопоставлены с build/test/artifact/Git evidence.
  - Scenarios/decisions/objections: unchanged и реализованы.
  - Validation: все запланированные команды выполнены; detailed output заменён на Normal только для ограничения размера лога.
  - Unsupported claims: F-Droid acceptance/reproducibility не заявляются.
  - Edge cases: stale config и stale build outputs проверены; scanner/source expectation остаётся следующим этапом.
  - Docs/changelog: product behavior standard channel не меняется; отдельный changelog в этой internal build-switch итерации не нужен.
  - Hidden contract: package id/data model не менялись; standard updater manifest подтверждён artifact inspection.
  - Manual-review challenge: native warnings, preview SDK и F-Droid buildserver/scanner ещё блокируют полную публикацию.
- No-findings justification: diff мал и полностью покрыт compile/static/unit/headless/artifact checks; все выявленные warning/risk относятся к явно отложенным Non-Goals и не маскируются как готовность к публикации.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | delivery/security | Android builds сохраняют `NU1608`, native 16 KB page-size warnings и preview SDK | Устранить в следующей публикационной итерации до fdroiddata submission | accepted residual |
| LOW | review evidence | Independent Post-EXEC reviewer не вернул статус/findings после повторного запуска и follow-up | Зафиксировать adversarial fallback; повторить независимый review перед submission | accepted-risk |

- Fixed: in-scope Post-EXEC defects не обнаружены.
- Checks rerun: static Android script, targeted unit/UI, оба Android build variants, manifests/APK/assembly markers, оба full suites, diff/status.
- Validation not run: on-device install/manual video — headless harness не предоставляет recorder, а runtime behavior покрыт UI test; реальный F-Droid buildserver/scanner отложен по Non-Goals.
- Unrelated changes: отсутствуют.
- Needs human: выбрать и отдельно подтвердить следующую итерацию (native/toolchain или fdroiddata PoC).
- Residual risks: native package security/version alignment/16 KB, .NET buildserver support, reproducibility/versionCode/signing, metadata/privacy/scanner/storage permissions.

## Approval
Получено 2026-08-08: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Возобновление preflight | 0.95 | Нет | Прочитать central stack/repo evidence | Нет | `Продолжай` не является approval | QUEST разрешает только spec | Нет |
| SPEC | Минимальная первая итерация | 0.92 | Independent review | Создать/проверить spec | Нет | Нет | Build split закрывает один blocker без изменения standard channel | Эта spec |
| SPEC | Source/test/design inspection | 0.94 | Android build только в EXEC | Quality gates | Нет | Нет | Прослежены manifest, registration, UI state, tests | Эта spec |
| SPEC | Self linter/rubric/roles | 0.90 | Independent findings | Передать reviewer | Нет | Нет | Config/security scope требует independent review | Эта spec |
| SPEC | Independent review attempts | 0.80 | Reviewer не вернул ответ | Выполнить adversarial fallback | Нет | Reviewer запущен, дважды прерван и повторно вызван без tools; ответа нет | Не называть fallback независимым review | Эта spec |
| SPEC | Adversarial fallback и re-review | 0.91 | EXEC evidence | Запросить approval | Да | Нет | Исправлены restore/preflight gaps; residual risks явны | Эта spec |
| EXEC | Approval и создание рабочей ветки | 0.98 | Нет | Добавить failing/static contracts и targeted tests | Нет | Да: пользователь написал `Спеку подтверждаю` | Единственный SPEC -> EXEC gate пройден; ветка создана от актуального `origin/main` | Эта spec, Git branch `feat/fdroid-build-variant` |
| EXEC | Test-first contracts | 0.96 | Нет | Реализовать build split | Нет | Нет | Android script дал ожидаемый red на отсутствующем `FdroidBuild`; добавлены unit и headless UI characterization tests | `scripts/test-android-build-scripts.ps1`, `src/Unlimotion.Test/SettingsViewModelTests.cs`, `src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs` |
| EXEC | Реализация build split и точечная проверка | 0.96 | Результаты Release-сборок | Собрать standard и F-Droid варианты, проверить merged manifests | Нет | Нет | Base manifest очищен от install capability, standard overlay сохраняет updater; static script PASS, `SettingsViewModelTests` 70/70 и `SettingsControlResponsiveUiTests` 13/13 PASS | `src/Unlimotion.Android/Unlimotion.Android.csproj`, `src/Unlimotion.Android/Properties/AndroidManifest.xml`, `src/Unlimotion.Android/Properties/AndroidManifest.Updater.xml`, `src/Unlimotion.Android/MainActivity.cs` |
| EXEC | Android Release evidence | 0.97 | Полные TUnit-gates | Запустить полные `Unlimotion.Test` и Headless suites | Нет | Нет | Standard arm64 Release PASS и merged manifest содержит updater permission/provider; F-Droid arm64 `Rebuild` PASS, merged manifest и APK не содержат updater-only capability/resource. Сохранены известные warning-блокеры native package/16 KB/preview SDK вне scope этой итерации | Android Release `obj/bin` (ignored), эта spec |
| EXEC | Полные gates и Post-EXEC review | 0.96 | Независимый reviewer недоступен | Завершить handoff и предложить следующую отдельную итерацию | Да, для следующего scope | Независимый reviewer повторно запущен, получил follow-up, но не вернул ответ и был прерван | Full suites 832/832 и 36/36, static/build/artifact/diff gates PASS; adversarial fallback не выявил in-scope findings | Эта spec, весь утверждённый diff |
