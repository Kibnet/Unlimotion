# Проверка индикатора Git — 09.10.2026

Исторический отчёт исходного feature. Дополнение о настройках и enabled-only видимости проверяется отдельно: [отчёт 11.10.2026](settings-2026-10-11.md).

Перенос на свежий main и PR delivery отражены отдельно: [доставка 11.10.2026](delivery-2026-10-11.md). Числа ниже относятся к исходной базе.

**Post-EXEC: NEEDS-FIX.** Реализация и её проверки выполнены локально; обязательный AC8 не принят из-за одного failure полного main-набора. Точно такой же phone-layout failure воспроизведён на чистом baseline. Это подтверждает существующий intermittent дефект, но не снимает обязательный green gate.

Ветка feat/git-dirty-status-indicator; worktree C:/Users/Kibnet/.codex/worktrees/git-dirty-indicator/Unlimotion; база e987023dc93daba6753440eec813edcbaa468da9. Approved SPEC: specs/2026-10-09-git-dirty-status-indicator.md; исходный SHA256 595A13D85742FD8C719DC6BA5E463DCE7A49369EFDAF361208A6ACE759236533 повторно совпал в основном checkout. Primary содержит только исходную untracked SPEC, feature-код там не менялся. Commit/push/PR/merge/install/release/live task-space writes не выполнялись.

## Поведение и scope

Компактная кнопка в шапке показывает distinct незакоммиченные файлы, сохраняет count при Syncing/Attention/stale и открывает подробности. Commits впереди/позади отделены от files и вычисляются по локальным refs; без upstream они неизвестны, а свежий чистый worktree может быть Idle. Нет repo/свежего достоверного sample — Unknown; non-file source — NotApplicable. Historical clean при ошибках чтения не становится Idle. Source/path/generation captured immutable; A→B очищает sample и отвергает late A. Enter/Space открывают flyout, Escape возвращает фокус, settings action открывает существующую вкладку.

Observer читает background под общим GitLock с bounded 2s acquisition; не вызывает runner/App wrapper, stage/fetch/commit/push/network. Optional storage signal публикуется после commit/rollback decision/finally и означает возможность изменения bytes, не receipt успешного save. Optional operation diagnostics Started/Terminal сохраняют sticky failure и одну outermost пару при nested operations; существующие Git algorithms/retries/toasts не переписаны.

Fallback 15s и stale 30s работают только при observer, applicable source и видимом control/ancestor chain/window вне Minimized. DataContext transfer выключает fallback прежнего владельца; detach снимает ancestor subscriptions; dispose отменяет late replies и отписывает storage/diagnostics/culture. Default status без source/observer не создаёт timer notifications. Task JSON/schema/legacy BackupStatusState не заменены; миграции нет.

Дополнительный test-only scope: containment failure message сохраняет ancestor bounds/offset без изменения допуска. CLI live-refresh fixture использует owned persistent dispatcher на целый async flow и обычный Avalonia SynchronizationContext; все product assertions и реальные CLI/external-writer операции сохранены. Planning layout и product CLI/storage algorithms этой fixture-правкой не менялись.

## Выполненные проверки

Все команды ниже выполнены в worktree последовательно; builds не шли одновременно с UI/timing tests. В таблицах evidence/ означает artifacts/git-indicator/evidence/.

| Проверка | Результат | Evidence от корня worktree |
| --- | --- | --- |
| Production solution, все platform targets | PASS, exit0; 0 errors/123 warnings; 18:37.06 | evidence/logs/build-solution-visibility-final.log |
| Последняя Headless сборка после fixture correction | PASS, exit0; 0 errors/1 warning; 8.22s | evidence/logs/build-headless-cli-context.log |
| Полный main текущего production кода | FAIL, 1423 executed/1422 passed/1 failed/0 skipped; 2:13:56.215 | artifacts/git-indicator/full-main-visibility-final/Kibnet_DESKTOP-AUDO1TJ_2026-10-09_14_54_14.7987107.trx |
| Все новые main cases | PASS24/24 внутри завершённого полного main | GitStatusIndicatorTests в том же TRX; 8 manifests/spy в evidence/preservation |
| Полный latest Headless, включая post-CLI fixtures | PASS59/59, exit0; failed/skipped/aborted/timeout=0; 3:05.862 | artifacts/git-indicator/full-headless-cli-context-final/Kibnet_DESKTOP-AUDO1TJ_2026-10-09_15_20_34.2459769.trx |
| CLI external writer после fixture correction | PASS4/4; 19.698s | artifacts/git-indicator/cli-context-correction; evidence/logs/test-cli-context-correction.log |
| Latest replacement/ancestor/window lifetime | PASS в full59; parent/window Hide/Show, logical minimize, owner transfer/detach | evidence/replacement-lifetime-final/flow-trace.txt |
| Latest rendered save/keyboard/negative matrix | PASS в full59; 12 PNG просмотрены | evidence/rendered-cli-context-final |
| Latest actual TaskSpaceSelector A→B | PASS в full59; оба PNG просмотрены | evidence/source-switch-cli-context-final |
| Baseline exact phone method 360/390/430 | Первый run3/3 PASS; repeat1:2/3, width430 FAIL | artifacts/git-indicator/baseline-phone-layout; baseline-phone-repeat-1; evidence/logs/test-baseline-phone-repeat-1.log |
| Candidate exact phone method | Isolated2/3; затем diagnostics-only build и 6 fresh runs18/18 | artifacts/git-indicator/candidate-phone-layout; phone-diagnostic; phone-diagnostic-1…5 |
| Baseline exact history-animation case | PASS1/1 | artifacts/git-indicator/baseline-history-timing; evidence/logs/test-baseline-history-timing.log |
| Baseline исходный visible connection | PASS1/1, tracked diff пустой | artifacts/git-indicator/baseline-evidence-visible; evidence/baseline-visible |

SDK10.0.401/TUnit1.44.0/MTP2.2.2; production Avalonia12.0.3, Headless package12.0.4. Последние main diagnostics и CLI fixture изменения — test-only; production после полного main не менялся. Главная production solution собрана до этих последних test-only правок; latest Headless build включает их собственный test project и зависимости. Main diagnostic build PASS0 errors/53 warnings/31.76s, exact phone assertions выполнены18/18. Старые 58/58 и interrupted runs не подменяют latest59 или failed main.

## Baseline failure и validation gate

Единственный full-main failure: CurrentTaskCard_PhoneWidthLayout_DoesNotOverflowAndKeepsRelationEditorUsable(390), CalendarDatePicker:CurrentTaskPlannedBeginPicker, left=-3/right293/viewport340. Candidate isolated run того же метода failed430: left=-2/right334/viewport380. Baseline e987023d сначала3/3, затем первый bounded repeat воспроизвёл **тот же failure430 с точно теми же bounds**. Baseline tracked app/test diff пустой; candidate assemblies в baseline не копировались; его untracked BaselineGitEvidenceTests не входит в exact filter. Причинный механизм и влияние feature не установлены. Шесть последующих candidate repeats18/18 не делают full main green.

Central [testing-baseline](C:/Users/Kibnet/.codex/agents/instructions/core/testing-baseline.md) MUST требует «Перед успешным завершением получать green для всего обязательного набора проверок». [review-loops](C:/Users/Kibnet/.codex/agents/instructions/governance/review-loops.md) запрещает заменять обязательный AC baseline/accepted-risk/next-best evidence. Поэтому общий статус NEEDS-FIX и AC8 incomplete. Approved SPEC §11 требует остановить unchanged repeats после baseline reproduction. Исправление product planning layout выходит за feature scope; разрешение такого scope contradiction ещё не получено. Containment assertion не ослаблен, product planning code не менялся.

## Acceptance-to-Test Matrix

| AC / scenario | Status | Проверка / evidence / предел |
| --- | --- | --- |
| 1 / S1 save→Dirty | PASS | Coordinator_SaveAndRemoveRemeasureRealRepository; rendered real vm.taskRepository.Update; dirty-save PNG |
| 2 / S2 sync→fresh | PASS | Coordinator_SyncTimeoutErrorRetryAndConfirmedConflictHaveCorrectPriority; controlled diagnostic + explicit fixture Commit → measured Idle. Не remote integration |
| 3 / S3 failure/conflict | PASS | Real conflict/divergence, 7 Diagnostics_RealServiceOutcomesAreStickyAndExceptionIsolated, nested/swallowed error; Syncing/Attention retainedDirty PNG |
| 4 / S4 honest unknown/stale | PASS | unborn/detached/missing/non-file, lock timeout/cancel, historical clean→repeated read error, retained dirty stale; negative PNG |
| 5 / S5 source/lifetime | PASS | late A/path generation/dispose/debounce; actual selector; A→null→default/configuredB; parent/window hide/show, logical minimize, detach |
| 6 / S6 layout/keyboard | PASS по утверждённым verifiers | 800×600, target≥32DIP, Enter/Space/Escape/focus; RU/EN × light/dark matrix inspected. Main phone failure отдельно BLOCKER AC8 |
| 7 / pure observation | PASS | 8 equal before/after full-byte/index/refs manifests; real configured service/loopback remote spy 0 Started/Terminal/accepted connections; static read-call review |
| 8 / compatibility | INCOMPLETE / BLOCKER | Production build PASS; Headless59/59 PASS; mandatory main1422/1423 FAIL |

Spy — bounded evidence реального configured service/HTTP remote плюс manifests/static call review, не universal native interceptor. Save/remove/commit выполняет fixture явно. Пользовательское task space не читалось и не менялось.

## Визуальные доказательства и границы

Latest реальные rendered PNG/trace скопированы из full59 и просмотрены: evidence/rendered-cli-context-final (12 PNG), evidence/source-switch-cli-context-final (2 PNG), evidence/replacement-lifetime-final (trace). Dirty count1 остаётся рядом с ↻ и !; unknownB очищает count; server показывает —. English frames также показывают честный pending/stale overlay. Inspector crop syncing-badge-inspection.png только увеличивает участок исходного кадра для чтения count; основной evidence — неизменённые полные PNG. SourceB task отображается, старый sample/operationA не принимается; transient breadcrumbA не выдаётся за исправление существующего breadcrumb flow.

Video fallback: настоящие Headless PNG + flow-trace; Avalonia Headless не имеет native HWND для window recorder. PNG montage не выдаётся за live video. Installed/native/FlaUI/physical minimize/remote rollout не проверены; WindowState — логический контракт. Baseline из отдельного checkout e987023d, source/assembly provenance сохранены; первоначальный cache provenance superseded собственной реальной baseline сборкой.

## Post-EXEC review

Scope/evidence pass: approved hash, source/resources/tests/README, status/diff, optional storage/activation/operation APIs, observer/coordinator/source binding, production/TestHost lifetime, header/flyout/localization, 24 main cases, full59, baseline exact tests/dumps, real PNG/trace/manifests/spy и solution/TRX logs. Instruction stack: central QUEST/routing/review-linter/rubric, dotnet-desktop-client/ui-automation/testing-dotnet, local UI override; skills appautomation/run-tunit-tests и recorder fallback.

Contract pass: S1–S6 и AC1–8 mapped выше. Signal не receipt; unknown не превращается в zero; source ownership и separate approval boundaries сохранены. Non-goals preserved: Git sync algorithms, persisted settings/task schema и legacy status. Общий PASS не выдан из-за AC8.

Отдельный adversarial reviewer git_indicator_review работал в danger-full-access/approval never: технической read-only изоляции нет. Все его действия были чтением, без edits/build/tests. Это отдельный adversarial review в writable session, НЕ enforced read-only independent PASS. Он подтвердил baseline classification, unchanged assertions, после-window source initialization, timer/ownership/ancestor lifetime и CLI fixture symmetry. Final empirical disposition NEEDS-FIX подтверждён reviewer после чтения full59/main TRX, latest audit/SPEC/journal и закрытия LOW consistency findings. Новых findings в final consistency pass нет; AC8/main BLOCKER остаётся open.

| Severity | Area | Finding | Required action / Status |
| --- | --- | --- | --- |
| BLOCKER | AC8 / main validation | Mandatory main1422/1423, pre-existing phone failure | OPEN: установить причину и получить green в согласованном scope; baseline не снимает gate |
| HIGH | UI test environment | Candidate/baseline CLI class зависает после2PASS | FIXED: owned dispatcher на полный async flow + ordinary Avalonia context;4/4 и full59PASS; первоначальная worker exception не изолирована |
| MEDIUM | Localization | Mojibake glyphs | FIXED UTF-8; codepoints/real PNG inspected |
| MEDIUM | UI coverage | Нет actual selector и Space | FIXED real selector + Enter/Space/Escape/focus passing UI |
| MEDIUM | Observation evidence | При восстановлении test source потерян spy | FIXED configured service/remote spy0/0 + manifests |
| MEDIUM | UI fixture lifetime | PerTest reset терял initial queued replies | FIXED source binding/init после окна с custom-owner guard; isolated continuous dispatcher rendered child |
| MEDIUM | Ownership | Старый DC owner active, новый терял fallback | FIXED transfer; replacement UI PASS |
| MEDIUM | Visibility | Window.Hide оставляет locally visible attached control | FIXED ancestor chain/public PropertyChanged; parent/window/logical minimize PASS |
| LOW | Default timer | Нет source/observer, но timer notifications | FIXED applicable+observer gate; quiet default UI PASS |
| LOW | Audit | Устаревшие pending/classification строки | FIXED актуальные totals и baseline/fixture findings в этом audit |
| LOW | Docs | No-upstream смешан с отсутствием repo | FIXED nullable commits и fresh clean Idle пояснены |

Fix/re-review: in-scope findings исправлены; review повторён только по изменённой source/test поверхности и новым failure/evidence. Fixture cleanup и последующие post-CLI tests эмпирически подтверждены full59. Unchanged passes повторно не запускаются. Main blocker оставлен открытым, не переименован в accepted risk.

| Role | Проверенный вопрос | Evidence |
| --- | --- | --- |
| Workflow analyst | Dirty после настоящего save; files отдельно от commits | real repo/storage fixture, AC1/7 |
| UX/designer | Count при Syncing/Attention; honest unknown; keyboard/themes/languages | latest inspected14PNG и AC3/4/6 |
| Tester | late source/nested errors/cancel/dispose/visibility; полнота gates |24 newPASS/fullUI59; main blocker mapped |
| Developer/architect | single-flight/coalescing, immutable capture, optional contracts, lifetime | static review + targeted concurrency/UI assertions |
| Operations/security | Zero observation writes/network; owner/provenance/rollback |8 manifests/spy0/0; baseline tracked diff empty |

Depth checklist: исходный outcome/ownership/approval mapped; все mandatory AC имеют verifiers/status; negative/late/nested/lifetime paths tested; unsupported installed/remote claims исключены; docs отражают local refs и server mode; no migration/persisted API break; CHANGELOG отсутствует, version bump для локальной реализации не требуется; generated evidence ignored; no unrelated product fix. Manual-review challenge: главным найденным препятствием остаётся known main AC8, а не спрятанный baseline exception.

No-findings justification для source/visual: historical-clean read failure, dirty→sync timeout/error, nested swallowed outcome, lateA→B/dispose, owner/ancestor/window lifecycle и ordinary save имеют assertions и reviewed pixels. **Общего no-findings/PASS нет:** обязательный main validation red.

Stop decision: **NEEDS-FIX / AC8 incomplete**. Авторизованный feature diff и in-scope fixture corrections подготовлены, обязательные прогоны и baseline evidence сохранены. Unchanged repeats остановлены согласно SPEC; product planning fix автоматически не добавлен. Residual bounds: GitLock cooperative, external writer snapshot nonatomic; native read нельзя отменить внутри LibGit2 call, late/cancelled reply не применяется; local refs могут быть старее remote. Rollback — удалить собственный feature diff; пользовательские task/index/refs не изменены.

## Диагностическая история

Старый full main прерван после history-animation intermediate-frame failure; baseline exact1/1 и latest fullmain этот case PASS, причина старого failure не установлена. Initial lifetime Fresh(A) timeout сохранён; позднее expanded PASS и latest full59PASS, причина прежнего timeout не изолирована. OR-filter0 tests/exit1 не считается PASS. Старые/interrupted результаты не заменяют current evidence.

Current fullmain benchmark выполняет600полных reads×3000tasks=1,800,000parses; run завершён нормально. Discovery1423; checkpoint600/600=4021358.3ms. Стек DeserializeTaskSnapshot/CreateFile — normal read sample, не deadlock proof; CPU/IO прогресс проверен. Heap dump Windows0x80070001 — tooling limitation, не test failure. Triage/stacks/discovery logs сохранены.

Первый latest Headless run stopped после2PASS; baseline exact CLI class также завис. Matching Post→Dispatch/WaitForUi→Dispatch stacks в evidence/logs/headless-visibility-stacks.log и baseline-cli-refresh-stacks.log; dumps на D:/CodexArtifacts/git-dirty-indicator-20261009. [AppAutomation1.9.0 Dispatch](https://raw.githubusercontent.com/Kibnet/AppAutomation/v1.9.0/src/AppAutomation.Avalonia.Headless/Session/HeadlessRuntime.cs) блокирует queued session call; [Avalonia12.0.4 session](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.0.4/src/Headless/Avalonia.Headless/HeadlessUnitTestSession.cs) в PerTest пересоздаёт dispatcher на callback. Fixture correction устраняет эти риски; первоначальное исключение, завершившее worker, не установлено. Вмешательство только в собственные test PID после commandline/ownership проверки и сохранения logs.

Ранний NuGet path1 failure был до compilation candidate/baseline; другой process environment/disable-build-servers позволил restore, единственная причина не изолирована. Собственные bin/obj перенесены на D через проверенные junctions после заполнения C, чужие данные не перемещались.

## Команды final validation

Из корня worktree:

~~~powershell
dotnet build src/Unlimotion.sln -m:1 --disable-build-servers
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-restore -m:1 --disable-build-servers
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory artifacts/git-indicator/full-main-visibility-final --output Detailed
dotnet tests/Unlimotion.UiTests.Headless/bin/Debug/net10.0/Unlimotion.UiTests.Headless.dll --maximum-parallel-tests 1 --minimum-expected-tests 59 --report-trx --results-directory artifacts/git-indicator/full-headless-cli-context-final --output Detailed
~~~
