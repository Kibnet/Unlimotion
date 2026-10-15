# Android: восстановление EGL после возврата из фона

## 0. Метаданные
- Профиль: delivery-task, UI bugfix / dependency migration; масштаб medium, multi-module.
- Владелец: Codex; задача e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6, ветка `perf/android-e74c5d1a-task-loading`, PR #325.
- Поверхность: Codex desktop / Windows PowerShell; точный model ID runtime не предоставлен. Model eval не применим: меняются зависимости приложения.
- Основание: поручение последовательно выполнить scope; 15.10 пользователь разрешил эмулятор вместо недоступного телефона и автоматическое подтверждение исправленных/reviewed SPEC.
- Parent contract: cold usable list ≤5s, live-process resume ≤1s, local freshness ≤5s. Эта спека не меняет пороги и не закрывает parent сама по себе.
- Evidence root (private): `D:/CodexArtifacts/android-task-loading-20261012` (далее DROOT). Содержит копию реальных задач; сырые кадры/JSON не публикуются.
- Sources: [upstream fix #21816](https://github.com/AvaloniaUI/Avalonia/pull/21816), [release 12.1.1](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.1).

## 1. Overview / Цель
Устранить воспроизведённый чёрный экран после HOME/resume живого Android-процесса обновлением согласованного семейства Avalonia с 12.0.3 до минимальной подтверждённой стабильной версии 12.1.1, содержащей upstream EGL fix.

Outcome contract: пользователь возвращается к читаемому списку и может открыть ту же задачу; результат — согласованные зависимости, UI regression coverage и native before/after evidence. Наличие upstream fix — гипотеза для нашего случая, а не доказательство устранения. Stop: любой black/ANR/crash, повреждение fixture или несовместимость — остановить серию, сохранить evidence, исправить причину в рамках задачи. Не повторять валидный failure ради зелёной статистики.

## 2. Текущее состояние (AS-IS)
`src/Directory.Packages.props` содержит восемь Avalonia-пакетов 12.0.3; отдельный IconGenerator явно использует Headless/Skia 12.0.3. Headless runner и ReadmeMedia явно используют Headless12.0.4. ReactiveUI.Avalonia 12.0.2, Nodify и DialogHost имеют собственные версии и не обновляются автоматически.

На обычной baseline APK без instrumentation, API34/x64/host GLES, собственная копия 3094 задач: cold run1 живой PID4424; три 30s HOME/resume дали корректные root/card, четвёртый — чёрный content после возврата. PID не менялся, в его logcat EGL_BAD_SURFACE / abandoned BufferQueue; force-stop выполнен только после failure. Серия 300s не начата. Raw: `lifecycle-resume-30s-run4` (фактические пути в `lifecycle-resume-series.json`), `lifecycle-resume-black-device.log`, `lifecycle-resume-black-exit-info.txt`.

В исходнике Avalonia 12.0.3/12.1.0 cached EGL native handle readonly и не обновляется после recreation; 12.1.1 обновляет его. Независимая проверка источников подтвердила соответствие механизма, но GPU-specific взаимодействия пока не исключены. Product reader уже восстановлен rollback563ed4f5 после отклонённого worker-эксперимента.

## 3. Проблема
Устаревшая EGL surface при смене native handle способна оставлять живое приложение с чёрным экраном. Начинать cache/повторное подключение хранилища без проверки выпущенного framework fix не обосновано.

## 4. Цели дизайна
Использовать поддерживаемый upstream fix, единые версии family и неизменную модель/формат задач. Разделить native EGL regression, Headless compatibility и абсолютную производительность. Сохранить обычный запуск приложения.

## 5. Non-Goals
Новые cache/lazy projections, ручные OnResume invalidation обходы, обновление прочих библиотек без доказанной необходимости, переоформление UI, release/установка на телефон. Задачи resume-state и search остаются следующими отдельными ветками после выполнения первой. Исторические render baseline и документы не переписываются заменой номера версии.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
Central props задаёт восемь Avalonia family versions 12.1.1. IconGenerator Headless/Skia и явные Headless pins в tests/Unlimotion.UiTests.Headless и tests/Unlimotion.ReadmeMedia также 12.1.1. Исходный MainActivity не получает дополнительной логики. UI tests проверяют сохранность загруженного экрана при hide/show; native emulator collector проверяет реальный EGL resume.

### 6.2 Детальный дизайн
NuGet restore должен разрешить согласованные family assemblies без downgrade/conflicts; проверить зависимости ReactiveUI.Avalonia, custom Nodify, DialogHost. Если нужна несовместимая смена API/сторонней библиотеки — остановить эту реализацию и расширить reviewed SPEC до правки.

EXEC compatibility amendment15Oct: первый restore остановлен NU1605. Avalonia.FreeDesktop12.1.1 требует Tmds.DBus.Protocol≥0.94.1, central pin0.93.0 ниже. Выбрана минимальная0.94.1 того же пакета после targeted review PASS и autoapproval; это объявленный обязательный transitive requirement, не произвольное обновление. Проверить desktop DBus API build + existing shutdown/activation tests/fullCI; не подавлять downgrade и не менять source API без нового обоснования. Windows checks не подтверждают Linux DBus runtime.

Headless compatibility amendment15Oct (targeted review PASS, autoapproved): upstream12.1.1 DispatchCore теперь ловит исключение EnsureIsolatedApplication, fault-ит конкретный caller TCS и продолжает worker. Старый negative-init test ожидал terminal worker fault12.0.4; два dispatch повторяют AfterSetup, где test-only Entered.SetResult создаёт вторичное исключение. Обновить characterization contract: pending/future callers получают тот же исходный sentinel (включая OperationCanceledException), worker остаётся live до Dispose, затем успешно join/CTS disposed. EntryPoint signal сделать TrySetResult для повторяемой инициализации; добавить count повторных попыток. Отдельные worker-fault/null-reference negative controls сохраняются без ослабления. Cold-worker publication workaround factory остаётся, потому что upstream StartNew12.1.1 всё ещё публикует session до присваивания Task; только version comments синхронизировать. Исходники обеих версий сохранены в DROOT. Это test infrastructure migration, не обход product failures.

Visual storyboard: корректный root/list → открытая карточка → закрытая карточка/root → HOME (30/300s) → тот же root с читаемым содержимым → карточка той же задачи с 24 children. Чёрный content/пропавшее дерево — failure. Layout/цвета/тексты сохраняются; новый макет не требуется.

До: автоматизированные видео short resume1–4 и кадры baseline с failure4. После: те же native сценарии, реальные скриншоты root/card/last и логи; Headless PNG — только compatibility evidence, не EGL proof. No-observer APK, SDK10.0.401, AOT/default arm64+x64, свой package, API34/1080×2400/3CPU/2560MB/host GLES и fixture сохранены. Новый boot cohort явно обозначен.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Visible result | Evidence | AC |
| --- | --- | --- | --- | --- |
| Возврат в живой процесс | HOME30s и300s | Root читаем; та же card открывается, нет black/ANR | 5+5 native cases, PID/log/video/PNG | A2 |
| Обычный запуск | Force-stop/start | Список/карточка доступны, fixture цела | cold capture, APK/runtime proof | A3 |
| Desktop UI compatibility | Loaded MainScreen hide/show | Экран/репозиторий/выбор сохраняются; overlay не появляется заново | новый Headless test +PNG, full existing suite | A4 |
| Локальное обновление | Title в собственной копии | Ожидаемый новый Title виден; исходные bytes восстановлены | 5 foreground cases, manual endpoint | A3 |

### 6.4 State / Interaction Matrix
| State | Trigger | Result | Edge/error | Notes |
| --- | --- | --- | --- | --- |
| Loaded/live | HOME/resume | Рендер актуальной surface | Black/ANR/crash → stop | PID unchanged mandatory |
| Process stopped | Start | Обычная cold загрузка | No list/card → incomplete/failure | Не считать live resume |
| Loaded desktop | Hide/show | Та же загруженная модель | Reconnect/overlay/reset → test failure | Headless не EGL |
| Capture interrupted | ADB timeout/lock/watchdog | Сохранён partial | Не считать PASS/0ms | Только технически incomplete можно повторять с новым attempt |

### 6.5 Decision Ledger
| Decision | Owner | Chosen | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Минимальная stable с подтверждённым fix | agent | 12.1.1 всей family | .95 | Другая несовместимость | Нет |
| Device | user | Собственный эмулятор | 1 | Нет physical Fold6 evidence | Нет |
| Не добавлять OnResume workaround | agent | Сначала upstream release | .95 | Может потребоваться дальнейшая локализация | Нет |
| Завершение parent | user contract | Только после исходных5/1/5 и остальных AC | 1 | Microfix не решает cold целиком | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source of truth | Change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Avalonia | Central props / tool csproj | 12.1.1 | Без формата данных | resolved assets + actual packaged assembly metadata обоих ABI |
| Android build | Own shadow source + exact SDK401 | Только version delta | Own package/AOT/default2ABI | source hashes/build log/APK hash/no observer |
| Task storage | Own3094 copy + SHA manifest | Только controlled freshness Title | Exact byte restore | full before/after hashes |
| Original installed apps | User installation | Без изменений | Не применимо | Только свой package/AVD |

## 7. Бизнес-правила / Алгоритмы
Граф, статусы, сохранение, выбор задачи и поиск не меняются. Нельзя объявлять native EGL исправленным по Headless PASS или одному успешному возврату. Время root и действие открытия card — отдельные clocks.

## 8. Точки интеграции и триггеры
Framework Android surface recreation после жизненного цикла Activity/SurfaceView; upstream EglGlPlatformSurface обновляет cached handle. Не создавать новое событие reconnect/перезагрузки tasks на OnResume.

## 9. Изменения модели данных / состояния
Нет новых persisted полей/миграций. Внутренний EGL handle принадлежит framework; приложение сохраняет существующий repository.

## 10. Миграция / Rollout / Rollback
Restore/build/test, затем только own diagnostic APK на AVD. Сохранить старую baseline APK/SHA и новый артефакт отдельно. Rollback — отдельный Conventional Commit восстановления props/tool pins; task data не мигрируют. PR остаётся draft до acceptance; release не входит в поручение.

## 11. Тестирование и критерии приёмки
| Acceptance criterion | Automated check | Visual/log check | Artifact | If missing |
| --- | --- | --- | --- | --- |
| A1 согласованные12.1.1 | restore/build, assets dependency audit | Final APK SHA256; extracted/decoded Avalonia.OpenGL payload обоих ABI связан с exact resolved NuGet12.1.1 и SHA256 linked DLL, package/informational version; AssemblyVersion alone недостаточен | build/packaged-version-proof JSON | Block change acceptance |
| A2 нет повторения native black | 5×30s+5×300s samePID series | root/card/last каждого case, ownPID EGL_BAD_SURFACE/abandoned logs; до/после видео | native lifecycle directories | Любой failure stop; не закрывать |
| A3 ordinary startup/freshness сохранены | cold +5foreground freshness same setup | expected Title endpoint/negative preceding frame, all3094 SHA restored | timing bounds JSON/video/PNG | Неполное не PASS |
| A4 UI compatibility | Новый MainScreen hide/show + relevant startup UI, full Main/Headless CI, IconGenerator build | inspect generated PNG; existing baseline differences review | TRX/commands/PNG | Headless не заменяетA2 |
| A5 parent outcome честно учтён | Separate cold/resume/freshness clocks | Absolute5/1/5 unchanged, background freshness remains separate pending protocol | parent SPEC/CLI result | Parent stays InProgress |

A2 functional regression acceptance — 10/10 observable successful cases, один исходный PID и continuous QEMU всей непрерывной серии; между cases запрещены force-stop/relaunch/reinstall/reset. Проверить matching rendering errors собственного PID за весь HOME→background→resume→card interval. Полный guest logcat -v epoch пишется заранее, guest epoch boundary markers ставятся до HOME и после card; host monotonic clock не сравнивается с guest wall clock. Не очищать логи и не терять transition errors; matching PID учитывает произвольный whitespace. Android unrelated system log errors учитываются отдельно. Не требовать 33% worker profile improvement: тот эксперимент отклонён и это другой механизм. A3 before/after публикует conservative intervals; source-of-truth thresholds5/1/5 не заменяются relative gain.

Commands: `dotnet restore`, Release builds для Main/Headless/IconGenerator; `dotnet run --project <test csproj> -c Release --no-build -- --treenode-filter ...` по run-tunit skill; repository CI. Native scripts — reviewed external measurement helpers с pinned APK/fixture/QEMU, не product telemetry. После dependency delta адаптировать source/build metadata и verifier до capture; не переиспользовать старые payload claims.

Stop: обязательный failure требует разбирательства; green checks не повторять без изменения/риска. Existing render baselines обновлять только при осмысленном inspected diff, не массовой заменой metadata версии. Background-after-local-edit acceptance не закрывается foreground5 и требует отдельного протокола до завершения parent.

## 12. Риски и edge cases
12.1 minor может затронуть шаблоны/layout/ReactiveUI/custom Nodify; resolved versions, full UI suite и native path снижают риск. Эмуляторный EGL не равен физическому Fold6. Возможна другая причина black — тогда upstream hypothesis опровергнута/недостаточна, не объявлять исправление.

| Likely objection | Why | Mitigation | Status |
| --- | --- | --- | --- |
| Почему обновляется и desktop? | Central family dependency | Единые versions, compatibility coverage | mitigated |
| Исправили ли собственно долгую загрузку? | Это первый scope | Parent5/1/5 остаётся открытым; измерить после | mitigated |
| Не потерялись ли задачи? | Реальная fixture | Own copy, exact3094 SHA before/after, no original writes | mitigated |

Rework checklist: user flow/storyboard, assumptions/decisions, AC mapping, objections заполнены; independent role review pending; EXEC evidence path существует. Физическое acceptance не заявляется.

## 13. План выполнения
1. Review/fix SPEC, autoapprove по поручению пользователя.
2. Закончить baseline freshness и сохранить visual adjudication; остановить свой AVD перед тяжёлыми builds.
3. Изменить pins/add UI regression; restore/build/targeted checks, review compatibility.
4. Собрать no-observer candidate с proven packaged12.1.1, провести native cold/resume/freshness и сопоставить evidence.
5. Full tests/CI и post-EXEC review; записать результат CLI. Продолжить parent до выполнения его AC, затем merge и только потом следующая задача.

## 14. Открытые вопросы
User-owned блокирующих вопросов нет. Техническая гипотеза проверяется candidate; не требует нового выбора пользователя.

## 15. Соответствие профилю
Delivery/UI/performance: SPEC gate, отдельные measurement/behavior claims, Conventional Commits, native+Headless UI coverage, review, rollback и private evidence.

## 16. Таблица изменений файлов
| File | Change | Reason |
| --- | --- | --- |
| src/Directory.Packages.props | Eight family12.1.1 | Released EGL fix |
| src/Directory.Packages.props (compatibility amendment) | Tmds.DBus.Protocol0.93.0→0.94.1 | Required by Avalonia.FreeDesktop12.1.1, NU1605 proof |
| tools/Unlimotion.IconGenerator/Unlimotion.IconGenerator.csproj | Headless/Skia12.1.1 | Consistency |
| tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj; tests/Unlimotion.ReadmeMedia/Unlimotion.ReadmeMedia.csproj | Explicit Headless12.1.1 | No mixed12.0.4 runner |
| src/Unlimotion.Test/MainScreenLoadingUiTests.cs | Loaded hide/show regression | UI compatibility |
| tests/Unlimotion.UiTests.Headless/Tests/HeadlessSessionFactoryTests.cs; Infrastructure/HeadlessSessionFactory.cs | Characterize caught initialization failures12.1.1, retain true worker-fault controls; version comments | Upstream DispatchCore contract changed |
| specs/2026-10-15-android-egl-resume.md; parent execution SPEC | Decisions/evidence | Audit |
| Private DROOT helpers | Candidate metadata / packaged proof / capture | No private content committed |

## 17. Таблица соответствий (было → стало)
| Area | Before | After |
| --- | --- | --- |
| EGL recreation | Cached handle stale in12.0.3 | Upstream12.1.1 handle updated |
| Native resume | Baseline black4 | Candidate10cases pending, no claim yet |
| Parent completion |5/1/5 unmet | Unchanged pending validation |

## 18. Альтернативы и компромиссы
App-specific invalidate/recreate workaround: дополнительная lifecycle логика без доказательства необходимости. Mixed Avalonia versions: несовместимые assembly contracts. Nightly/custom framework fork: выше maintenance risk. Более новая stable: больше непроверенных изменений;12.1.1 — минимальная подтверждённая выпущенная версия с нужным fix.

## 19. Результат quality gate и review
Post-SPEC review: PASS, 15.10.2026. Scope reviewed: вся SPEC, central review-loops/spec-linter/rubric, props, IconGenerator, explicit Headless runner/ReadmeMedia pins, MainScreenLoadingUiTests fixture и upstream source mapping. Advisory reviewer `/root/startup_profile_review` работал в writable среде только чтением; техническая read-only isolation недоступна. Выполнен отдельный adversarial pass; residual risk — отсутствие технического запрета записи, фактических мутаций reviewer не выполнял.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | A1 | AssemblyVersion не доказывает final APK fix | Final APK SHA + decoded OpenGL обоих ABI ↔ exact NuGet12.1.1 ↔ linked SHA/informational version | fixed, re-review PASS |
| MEDIUM | A2 | PID per-case и after-return logs допускают ложный PASS | Один PID/QEMU на10cases, no reset, guest markers/full HOME→card log | fixed, re-review PASS |
| LOW | Evidence | Неверный short-run path | Исправить на30s-run4 | fixed |

Scope/Evidence pass подтвердил реальный baseline failure и минимальный released fix. Contract pass сохранил parent5/1/5 и границы own AVD. Adversarial pass проверил stale APK, reset между cases, потерянные transition errors, Headless-only false proof; необходимые исправления внесены и перечитаны reviewer. Role-Based: domain PASS (root→same card, parent не закрывается), UX PASS (storyboard/native readable frames), tester PASS (real Connect/loaded selection/card/overlay/connect count; native отдельно), architect PASS (coherent versions), delivery/security PASS (private data/draft/rollback).

Linter A1–5/B6–10/C11–13/D14–16/E17–19/F20: PASS после исправлений; rubric цель/AS-IS/дизайн/безопасность/проверяемость/автономность по5, итог30/30. Depth checklist: scope/unrelated, AC/evidence, assumptions/objections, unsupported claims, regression/edgecases, docs/baselines, hidden lifecycle/data contracts и manual challenge проверены. No-findings justification после re-review: payload lineage исключает старую APK, непрерывный PID и полные transition logs исключают reset/потерю ошибок; Headless claim ограничен compatibility. Stop decision PASS для EXEC; actual validation остаётся обязательной.

Post-EXEC: не выполнен. No evidence/no pass.

## Approval
Автоматически подтверждено 15.10.2026 после advisory review, исправлений и targeted re-review PASS по прямому поручению пользователя. EXEC разрешён в описанных границах; release/physical acceptance не предоставлены этой записью.

## 20. Журнал действий агента
| Phase | Decision | Evidence / remaining | Next | Human | Artifacts |
| --- | --- | --- | --- | --- | --- |
| SPEC15Oct | Minimal coherent12.1.1 proposed | baseline black4 + upstream source; candidate absent | independent review/fixes | Emulator/autoapproval already authorized | This SPEC, DROOT |
| Review→EXEC15Oct | All3 findings fixed, re-review PASS; autoapproved | Payload/PID/log criteria tightened; implementation pending | Apply pins and meaningful UI test | Standing user instruction | This SPEC |
| EXEC amendment15Oct | Tmds0.94.1 targeted advisory PASS, autoapproved | Exact minimum confirmed local nuspec; no direct API use; Windows checks do not prove Linux DBus runtime | Apply minimum and rebuild | Standing autoapproval | Central props, build log |
| EXEC Headless amendment15Oct | Targeted advisory PASS, autoapproved; test contract follows inspected upstream12.1.1 | First full suite64/66: two obsolete init-fault expectations; raw failure retained | Characterize per-caller failures/live worker; retain true worker-fault controls | Standing autoapproval | Headless tests/factory comments |
| EXEC validation15Oct17:53 | MainScreen5/5 + factory5/5 PASS, zero skips; IconGenerator build PASS; ReadmeMedia restore PASS | First mixed renderer run preserved4fail; existing fresh-child pattern fixes isolation; PNG loaded card inspected. Advisory diff PASS. Full exact-head CI/native still pending | Push draft for remote CI; native10cases on quiet host | Standing delivery scope | TRX/log/PNG in DROOT |
| Candidate payload15Oct | APK876F12B15C1E36C444173D43D03F3A877049EEF5D4205F2ECF3DDDA55653CB39 verified | Archive/source405files/compiled reader match baseline; actualSDK401/AOTtrue/default2ABI; decoded6payloads/observer0/OpenGL12.1.1+e33 bothABI. Verifier initial path separator mismatch fixed, successful proof saved | Native cohort | Own package/emulator authorized | egl1211-plain-apk.json/final-payload-proof.json |
| Helper review15Oct | Advisory findings provenance/log markers/unconditional logger cleanup fixed/re-reviewed PASS | Unique guest epoch markers confirmed in saved log, continuousPID/QEMU, fail-closed incomplete; immutable prior raw artifacts | Native capture | No new human decision | External measure-egl1211-* / verifier helpers |
| CI/visual compatibility15Oct18:20 | CI369b3724: Main1538/1541, only3 Importance baseline Avalonia-version mismatches; Headless66/66 | All24 actual PNG SHA/pixels exact baseline, other192 comparator metadata values equal; parent/reviewer inspected24pairs, twofullcards; advisory PASS | Update only24Avalonia metadata values/README re-certification, rerun exact-headCI | Existing inspected-baseline allowance | CI38755454915, pixel audit; noPNG/comparator changes |
