# Сведения о Git-синхронизации в настройках и видимость индикатора

## 0. Метаданные
- Фаза: EXEC; пользователь подтвердил дополнение 11.10.2026 фразой «спеку подтверждаю».
- Форма: expanded по central templates/specs/_template.md; межкомпонентная логика config/state и две UI-поверхности исключают short.
- Профили: dotnet-desktop-client, ui-automation-testing; contexts/testing-dotnet. Local UI override обязателен.
- Владелец: пользователь; реализация — Codex в существующем worktree git-dirty-indicator, ветка feat/git-dirty-status-indicator, HEAD e987023dc93daba6753440eec813edcbaa468da9.
- Поверхность: Codex desktop, Windows/PowerShell. Effective model/client version не подтверждены; план не зависит от конкретной модели. Behavioral model eval не применим: меняется приложение.
- Основа: specs/2026-10-09-git-dirty-status-indicator.md; прежний approved SHA256 595A13D85742FD8C719DC6BA5E463DCE7A49369EFDAF361208A6ACE759236533 остаётся историческим approval первоначальной функции.
- Historical evidence: docs/testing/git-dirty-status-indicator/README.md от 09.10; Headless59/59, новые main24/24, full main1422/1423. Это не проверка нового дополнения.
- Ограничения: только локальные code/tests/docs после нового exact approval; без push/merge/install/release и пользовательских task-space writes. Базовый phone CalendarDatePicker defect остаётся отдельным открытым blocker, не включён в product scope этого дополнения.

## 1. Overview / Цель
Исходное поручение 11.10.2026: «Ещё нужно сведения синхронизации показывать в настройках, в разделе синхронизации git. А индикатор показывать только для пространств, где синхронизация включена».

Success means: пользователь видит текущие сведения того же активного пространства в Git-разделе настроек; шапка показывает индикатор только при включённой Git-синхронизации файлового пространства. Включение/отключение и смена пространства отражаются без перезапуска. Открытие настроек не выполняет Git-команд.

Output: дополненный локальный feature diff, содержательные coordinator/settings/UI tests, просмотренные кадры настроек и шапки, build/TRX и post-EXEC audit. Stop: текущая фаза заканчивается review-ready SPEC; зависимый код начинается после «Спеку подтверждаю» для этого дополнения. Старый validation blocker не отменяется.

## 2. AS-IS
- MainControl.axaml всегда создаёт видимый GitStatusIndicator; GitStatusSourceBinding считает applicable любое FileStorage, без проверки BackupEnabled.
- GitStatusViewModel уже предоставляет Summary/Details/LastKnown, файлы, nullable ahead/behind, conflicts, checked time, stale и safe read/operation error; background observer не делает remote/write operations.
- SettingsControl.axaml имеет Git-раздел BackupTitle, BackupAutoCheckBox→SettingsViewModel.GitBackupEnabled, прежний BackupStatusText, conflict panel и Connect/Sync/Pull/Push actions. Полные новые сведения там не отображаются.
- GitBackupEnabled читает существующий GitSettings.BackupEnabled; ReloadActiveTaskSpaceSettings обновляет этот флаг для активной проекции. App использует его также при persistence/scheduler wiring; SelectedTaskSpace может быть неактивным и не является источником runtime identity.
- MainControl и SettingsControl имеют разные DataContext: MainWindowViewModel и SettingsViewModel. Нужна ссылка на один source-bound GitStatus, а не второе наблюдение.
- Existing UI fixtures получали видимый indicator даже при disabled default config. Их нужно явно настроить под новый контракт, сохранив отдельные disabled/server проверки.

## 3. Проблема
Показ сведений сейчас не согласован с местом настройки и признаком включения синхронизации активного пространства: подробности доступны только из шапки, а шапка показывает Git и для отключённых пространств.

## 4. Цели дизайна
Один sample/operation-state для двух поверхностей; немедленная честная реакция на active settings; отсутствие старых данных A в B; сохранение файлов при Syncing/Attention/stale; никаких новых persisted настроек и дополнительных observers/timers; прежние sync/conflict controls остаются рабочими.

## 5. Non-Goals
Не менять Git sync/merge/retry алгоритмы, scheduler или persistence receipt; не добавлять историю синхронизаций и выдуманное время последнего успешного push; не исправлять planning/calendar layout; не считать наличие .git или remote признаком включения; не запускать sync при открытии раздела; не создавать новую кнопку перехода в те же настройки внутри раздела.

## 6. TO-BE
### 6.1 Ответственности
- MainWindowViewModel.ConfigureGitStatus связывает Main/Settings с одним экземпляром; replacement/dispose не оставляет Settings на старом owner.
- SettingsViewModel предоставляет ссылку/проекцию GitStatus и локализованное disabled/not-applicable presentation без собственного observer или timer.
- MainControl.axaml привязывает IsVisible индикатора к active eligibility; отключение закрывает flyout и не занимает Auto-column ширину. Фокус после закрытия не возвращается в скрытую кнопку.
- SettingsControl.axaml добавляет компактный информационный блок в существующий Git-раздел, рядом с текущим status/conflict/actions; long text wrapping и RU/EN resources.
- GitStatusSourceBinding + GitStatusViewModel согласуют source identity, active settings eligibility, read epoch и видимость. App/TestHost используют одинаковое wiring; subscriptions принадлежат owner и снимаются при dispose.
- SettingsViewModel владеет transient маркером проекции: source Id/path/version и признак завершённого reload. Он инвалидируется до первого изменения полей ReloadActiveTaskSpaceSettings и публикуется только после загрузки всех полей; activation/reload wiring передаёт фактическую identity. Selected/Header option не заменяют этот маркер. Ничего из маркера не сохраняется в config.

### 6.2 Детальный дизайн
Сведения в Git-разделе: текущее состояние; число незакоммиченных файлов; commits впереди/позади; наличие конфликтов; время последнего наблюдения; stale/read/operation error и пояснение local refs без fetch. Это информация из существующих Details, без копирования текста в отдельный cache. Время наблюдения не называется временем последней успешной синхронизации. Прежние BackupStatusText/конфликтные действия сохранены; новый блок различает состояние worktree и structured operation result, а не доверяет normal return/Connected как success receipt.

При disabled показывать «Git-синхронизация отключена» без старых counters; для server — «Git-синхронизация не применяется к серверному пространству». При enabled без repo/read — Unknown и неизвестные значения «—», не 0. Операционные ошибки не включают credentials/raw exception/private key paths. AutomationIds: GitSyncInformationPanel, GitSyncInformationText; существующий GitStatusIndicator сохранён.

Eligibility определяется текущим активным sourceManager.ActiveSource и актуальной Settings-проекцией этого source, не SelectedTaskSpace. Основное условие: файловое пространство и GitBackupEnabled=true. До совпадения source/settings identity и во время IsTaskSpaceSwitching индикатор скрыт, Settings не показывают counters A как B. На неудачной смене восстановить информацию прежнего active source после проверки identity.

MatchedActiveProjection означает completed Settings projection с Id/path/version текущего runtime source, а не совпадение выбранного пункта или нескольких уже обновлённых полей. Начало reload инвалидирует marker до setter notifications; завершение атомарно публикует полную identity на UI dispatcher. Ошибка reload оставляет marker invalid и сведения недоступными, с существующим error status. При rollback A marker вновь публикуется только после полного reload A. Обычный live toggle после completed reload сохраняет identity и меняет eligibility немедленно. App/TestHost обеспечивают одинаковый порядок activation→invalidate→reload→complete; guard проверяет фактический active source перед complete, чтобы поздний reload A не маркировал B как готовый.

Флаг UI-редактирования действует немедленно; existing persistence pending/error показывается своим прежним статусом и не объявляется успешным сохранением настройки. Наличие repo/branch/remote, нулевые scheduler intervals, IsBusy, conflict или read error не отменяют enabled condition. У enabled missing repo индикатор видимый Unknown.

Disabled прекращает periodic/storage-triggered reads и отменяет pending observation; повторное enabled начинает новое измерение. Source identity generation и read eligibility epoch разделены: поздний read до отключения не возвращается после off→on, но source-bound operation Started/Terminal для того же active source продолжает учитываться при скрытой UI. Off→on во время операции не теряет busy и не рисует ложный Idle. Source/path change очищает прежний sample/operation state как в исходном контракте. Native LibGit2 read не обещает физическое прерывание; reply отбрасывается по epoch/source guard.

Remote/branch являются observation target context, а не новой source identity. При изменении этих полей того же active source binding обновляет captured context, инвалидирует read epoch, очищает прежние target-dependent ahead/behind до неизвестных и запрашивает fresh read при enabled. Старый in-flight ответ не применяется. Последние dirty/conflict факты того же worktree могут сохраняться с исходным временем и stale semantics; counters commits старого target не показываются как новые. Этот путь не вызывает SetSource/reset операции: Started/Terminal IDs текущего source сохраняются, в том числе при disabled. Настоящая смена Id/path/storage по-прежнему меняет source generation и очищает operation state. Нельзя использовать существующий same-source early return без проверки context, либо SetSource для mere target update.

Отдельный lifecycle-owner MainControl по-прежнему определяет видимость всего экрана/окна/ancestors. Скрытие самой кнопки при disabled не отключает сведения в настройках enabled source. Настройки и шапка используют один debounce300ms/fallback15s/stale30s; второй polling loop не появляется. Новые subscriptions не меняют scheduler/persistence algorithms.

Wireframe (план, не rendered evidence):
~~~text
Шапка, включено:    [✎ 4] [Пространство A] [+]
Шапка, отключено:         [Пространство B] [+]

Настройки → Git-синхронизация
[✓] Включена
Состояние: синхронизация / требуется внимание / есть изменения
Незакоммиченных файлов: 4
Commits впереди / позади: 1 / 0
Конфликты: нет
Проверено: 18:02:13   (при stale — соответствующая пометка)
Счётчики commits по локальным refs; без fetch
[существующие Connect / Sync / Pull / Push / conflict actions]

[ ] Включена
Git-синхронизация отключена
~~~

Visual plan: baseline текущих Settings/header из безопасного fixture после approval; after enabled/disabled/server/missing repo/Syncing/Attention/settings comparison при 800×600, RU/EN × light/dark. Video при доступном native test window; иначе реальные Headless PNG+trace с причиной отсутствия HWND и inspected after frames. Монтаж PNG не считается live video.

### 6.3 User-Observable Scenarios
EXEC clarification within approved outcome: Started/Terminal diagnostics из production/TestHost несут transient runtime token, captured на outermost operation start через существующий storageFactory. UI callback сопоставляет token при apply, а не generation старого UI на arrival: queued SetSource B→Started B до drain сохраняет Busy; late старый runtime при повторном Id/path отклонён. Untagged legacy/test events сохраняют прежний arrival-generation guard. Sync algorithm/receipt/schema не меняются. Flyout закрывается явно при status/DC transfer и detach; source/config binding переходит на новый MainVM.GitStatus, Settings reference сохраняется. Это in-scope review fixes, не новый user-owned outcome/gate.

| S | Trigger | Видимый результат | Evidence / AC |
| --- | --- | --- | --- |
| S1 | Enabled space, открыть Git-настройки | Полные сведения совпадают с шапкой/details | UI PNG+comparison, AC1/4 |
| S2 | Отключить/включить существующий переключатель | Шапка скрывается/возвращается; disabled text/fresh sample без restart | actual checkbox UI, AC2/3 |
| S3 | A-enabled→B-disabled→C-enabled→server | Нет count/operation A в B/C; server без header indicator | actual selector UI + delayed replies, AC2/3/5 |
| S4 | Enabled missing repo/read error/sync error/conflict | Unknown/Attention/stale честны; Dirty retained при sync/error | coordinator + rendered settings, AC4 |
| S5 | Off→on при текущей Git-операции | Busy сохраняется, старый read не оживает | controlled operation + UI, AC3/4 |
| S6 | Открыть/закрыть настройки, заменить VM/скрыть окно | Один observer, корректные subscriptions/fallback/focus | counters/lifetime UI, AC5/6 |

### 6.4 State / Interaction Matrix
| Before | Trigger | Result | Guard |
| --- | --- | --- | --- |
| Enabled, Dirty | Disabled | Header/flyout hidden; Settings disabled; reads stopped | Operation bookkeeping того же source не потерян |
| Disabled | Enabled | Header Unknown→fresh state; Settings тот же owner | New read epoch; old reply rejected |
| Enabled | Missing repo/error | Header visible Unknown/stale; Settings honest values | Не подменять disabled и unknown |
| A enabled | B activation/pending projection | Header hidden, counters unavailable until identity matched | LateA не применяется |
| A switch pending | Switch failure | Restore activeA settings/state after identity guard | Не считать выбранный target активным |
| Source B уже active, Settings ещё reload | Partial field notifications | Нет header/counters до completed marker B | Late complete A отклонён; rollback A также требует reload complete |
| Same source, read/operation pending | Remote/branch changed | Старые divergence counters неизвестны до fresh read | Read epoch changed, Started/Terminal IDs сохранены |
| Enabled syncing | off→on before Terminal | Syncing до actual Terminal; Dirty evidence only fresh/current | No lost operation state / ложный Idle |
| File | Server | Header hidden; Settings not-applicable | Не менять BackupEnabled автоматически |

### 6.5 Decision Ledger
| Decision | Owner | Chosen | Risk | Needs user before EXEC |
| --- | --- | --- | --- | --- |
| Settings + conditional indicator | user11.10 | Обе части этого поручения | Нет подмены исходной цели | Нет отдельного вопроса |
| Enabled source | agent | Active file source + существующий GitBackupEnabled | Draft/persisted различие обозначено | Нет |
| Какие сведения | agent | Существующие Details, без фиктивного last-push time | Одно состояние для двух экранов | Нет |
| Disabled/no .git | agent | Disabled text против enabled Unknown | Нельзя проверять только repository existence | Нет |
| Product calendar fix | user scope | Не включён | Старый AC8 blocker остаётся | Не является частью approval этого дополнения |
| Фаза EXEC дополнения | user gate | Exact approval получен11.10 | Новый outcome/settings surface и eligibility | «спеку подтверждаю» |

### 6.6 Runtime / Config / Data Matrix
| Area | Truth | Change | Migration / check |
| --- | --- | --- | --- |
| Enabled | Active Settings projection, Git.BackupEnabled | Только eligibility/display; live toggle реагирует немедленно | Новых settings нет; A/B overrides UI test |
| Source | ActiveSource Id/path/version | Eligibility читает matched projection | SelectedTaskSpace не runtime identity |
| Projection | Settings completed transient Id/path/version | Invalidate before reload; publish after all fields | Partial B/late A/rollback A tests, no persisted field |
| Observation | Source-bound GitStatus | Read epoch + disabled guard | Cancel/late off→on tests |
| Target context | Active Git remote/branch | Same-source update invalidates read only | Divergence unknown/fresh; current operation retained |
| Operations | Existing structured diagnostics | Сохраняются при скрытой UI того же source | Start→off→on→Terminal test |
| UI ownership | MainWindowVM | Settings reference того же owner | Replacement/dispose counters |
| Git bytes/task schema | Реальные storage/index/refs | Никаких новых writes/network | Existing manifests/spy regression |

## 7. Правила / алгоритмы
HeaderVisible = matchedActiveProjection && !IsTaskSpaceSwitching && actualFileSource && GitBackupEnabled. True не требует успешного repo read. Settings enabled использует тот же sample, disabled/not-applicable presentation не показывает historical counters. Off→on инвалидирует read epoch независимо от source-bound operation lifecycle. Изменение eligibility не инициирует ручной sync, fetch или config write.

## 8. Интеграции / триггеры
ConfigureGitStatus/replacement, active-source activation, Settings GitBackupEnabled/IsServerMode/IsTaskSpaceSwitching/projection reload, MainControl attach/detach/ancestor visibility. Binding согласует capture с фактической active identity и completed projection marker перед apply на UI dispatcher. Remote/branch snapshot обновляется через отдельный same-source context path §6.2, который не сбрасывает operation lifecycle. Dispose снимает все дополнительные handlers. App и TestHost реализуют одинаковый контракт.

## 9. Модель состояния
Только calculated presentation/reference/eligibility/read-epoch. Новых persisted settings, task fields, migrations, operation receipt или enum вместо legacy BackupStatusState нет. Settings не владеют отдельным timer/observer и не освобождают общий GitStatus независимо от MainVM.

## 10. Rollout / rollback
После approval — локальная реализация и проверки в существующей ветке. Startup disabled без indicator; startup enabled до read Unknown. Откат только diff дополнения, сохраняя первоначальный feature diff/approved SPEC и чужие edits. Установка/публикация отдельно не разрешены.

## 11. Acceptance Criteria / tests
| AC | Verifier | Evidence |
| --- | --- | --- |
| 1 Settings section показывает существующие сведения активного source | Reference identity + actual Settings UI visible text | settings-enabled PNG RU/EN/light/dark |
| 2 Header только enabled file spaces, в том числе .git+disabled; enabled без .git остаётся visible Unknown | Real checkbox/selector, disabled/server/missing repo matrix; hidden header consumes no slot | header/settings comparison PNG |
| 3 Live off→on, no restart; cancel/reject old read, сохранить текущую operation | Controlled delayed observer, Started→off→on→Terminal, actual checkbox | state/read-count/operation trace |
| 4 Dirty/error/stale/files/commits/time честны на обоих экранах | Real repo save, sync/error/conflict/historical clean; compare model/controls | same-source details/settings assertions + pixels |
| 5 Actual source switch/failed switch/path/DC replacement/dispose корректны | Active selector A-enabled/B-disabled/C-enabled/server; delayed callback/config mismatch; failed-switch restore | cross-source/lifetime trace |
| 6 One observer, no reads while disabled; safe focus when flyout disappears; observation без новых writes/network | Counting observer + preservation/spy; Enter/Space/Escape then actual checkbox disables | counters/manifest/keyboard UI |
| 7 Regression gates не уменьшены | Affected main/coordinator/settings + full main, latest full Headless, solution build | TRX/build logs; no PASS при known main failure |

Existing GitStatusIndicatorHeadlessTests/TaskSpacesHeadlessTests/SettingsViewModelTests/GitStatusIndicatorTests дополняются: explicit enabled временные fixture settings там, где нужен visible indicator, и отдельный disabled default case. TestHost scheduler stub/no remote setup сохраняются; новые assertion не заменяют ordinary repository save/selector/checkbox пользовательский flow. Полный main обязателен из-за общего eligibility/source/lifecycle поведения и ранее утверждённого gate; full Headless из-за двух экранов/TestHost. Native acceptance не заявляется из Headless.

Обязательные дополнительные negative checks для AC3/4/5: runtime B active до завершения SettingsB reload (включая старое BackupEnabled A и частично обновлённые поля B); late complete A; rollback A после failed switch; same-source remote/branch change при pending read и активной operation Started, затем Terminal. Проверяются отсутствие wrong-source/old-target counters, completed-marker guard, fresh divergence и сохранение Busy до Terminal. Это coordinator/Settings tests вместе с actual selector/checkbox UI coverage.

Команды после approval из worktree: dotnet build src/Unlimotion.sln -m:1 --disable-build-servers; отдельный dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-restore -m:1 --disable-build-servers; main/Headless DLL через TUnit --treenode-filter для affected discovered classes, затем оба полных набора с --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx. Expected long main benchmark учитывается до запуска, logs/updates сохраняются. Exact future minimum/count/paths определяются discovery, не берутся слепо из historical59/1423.

Stop: не уменьшать assertions/suites после failure; сначала evidence/root cause и baseline comparison. После baseline reproduction не гонять unchanged repeats ради случайного green; known red оставляет AC7/старый AC8 NEEDS-FIX. Это дополнение не даёт права на calendar product fix.

## 12. Риски / objections
EXEC refinement11.10: дополнительно пробуется native FlaUI+window recording на временных данных. Native App сохраняет real scheduler: initial jobs могут выполниться, поэтому config URL и repo.origin указывают только на созданный локальный bare remote, credentials пусты, последующий интервал86400s. Это не observation-only native trace и не remote integration. Headless сохраняет scheduler stub/no network. Server selector matrix использует real TaskSourceManager/coordinator и descriptor Kind=Server с explicit offline local storage carrier; ServerStorage/transport не проверяются. TestHost cleanup захватывает фактический final active storage после switch/rollback и освобождает его при session disposal, проверяется callback watcher до/после close.
| Objection | Причина | Mitigation / Status |
| --- | --- | --- |
| «Отключил, а индикатор всё ещё виден» | Нет subscription к live flag | Actual checkbox test; mitigated |
| «В настройках не те данные» | Второй cache или выбранный неактивный source | Same reference + matched active projection; mitigated |
| «Включено, но нет repo — ничего не видно» | Visibility ошибочно зависит от Known | Visible Unknown при enabled; mitigated |
| «Старая информация вернулась после off→on» | Native read завершается поздно | Independent read epoch; mitigated |
| «Пропало состояние текущей операции» | Cancellation очищает operation state | Source generation отделён от read epoch; mitigated |
| «Почему overall не PASS при зелёном UI» | Предыдущий full main red | Regression gate открыт явно, не списан на baseline; mitigated |

Rework prevention: исходный двухчастный outcome сохранён; S1–S6→AC; решения и gate записаны; disabled/unknown/server/operation/lifetime objections покрыты; роли и визуальный план присутствуют; EXEC evidence path определён.

## 13. План
После approval: eligibility/read epoch + tests; shared Settings reference/presentation + Settings UI; header visibility/flyout focus; production/TestHost wiring + actual checkbox/selector tests; rendered review; build/affected/full suites последовательно; post-EXEC review и честный gate verdict. Предыдущий baseline product bug в этапы не включён.

## 14. Открытые вопросы
Для самого дополнения существенных неизвестных user-owned решений нет. Выбор Settings fields следует существующим Details. Отдельный ранее заданный вопрос о разрешении calendar fix пока без ответа и не включён в этот scope. Новый exact approval требуется только для изменённого outcome/риска данного дополнения; первоначальную SPEC повторно подтверждать не требуется.

## 15. Соответствие профилю
UI dispatcher/background read, optional observer lifecycle/source guards; local MUST add/run relevant UI tests; AppAutomation existing Headless authoring patterns, rendered pixels и keyboard; testing-dotnet/TUnit serial filtering; safe local temp data и no hidden network; pending/spec evidence не выдаётся за EXEC acceptance.

## 16. Изменения файлов после approval
| File / area | Change | Purpose |
| --- | --- | --- |
| src/Unlimotion.ViewModel/GitStatusViewModel.cs | Eligibility/read epoch, retain operation lifecycle | Off/on без late read/busy loss |
| src/Unlimotion.ViewModel/MainWindowViewModel.cs + SettingsViewModel.cs | Same GitStatus reference + presentation + transient completed projection marker | Settings/header согласованы без partial reload |
| src/Unlimotion/Services/GitStatusSourceBinding.cs + App.axaml.cs | Active config/source subscriptions, projection sequencing, same-source context update | Live eligibility identity без потери operation |
| src/Unlimotion/Views/MainControl.axaml(.cs) | Conditional indicator/flyout focus | Header only enabled |
| src/Unlimotion/Views/SettingsControl.axaml | Information block | Git settings visible data |
| Strings.resx / Strings.ru.resx | Disabled/info copy при необходимости | RU/EN |
| TestHost + main/Headless tests | Explicit fixture flags + новые assertions/flows | Prove two surfaces and off/on |
| README / feature audit | Обновлённый контракт и actual results | Только в EXEC, без ложного PASS |

## 17. Было → стало
| Area | Было | Стало |
| --- | --- | --- |
| Header disabled/file/server | Indicator всегда visible | Только enabled file source |
| Git settings | Legacy operation status | Legacy controls + shared detailed information |
| Off/on | Не влияет на observer | Stops reads; fresh after enable; bookkeeping retained |
| State ownership | MainVM only | Settings projection того же instance |

## 18. Альтернативы
Проверять .git вместо BackupEnabled нельзя: нарушает вторую часть поручения. Второй observer в Settings проще локально, но создаёт разные samples/дублирование reads и lifecycle. Читать всегда, скрывая лишь header, оставляет бесполезные reads disabled source; выбран eligibility guard без изменений sync scheduler. Переносить всю функцию из header в Settings нельзя: пользователь просит обе поверхности для enabled spaces.

## 19. Quality gate / review
### SPEC Linter Result (author)
| № | Status | Evidence |
| --- | --- | --- |
| 1 | PASS | §1 обе части поручения и наблюдаемый outcome |
| 2 | PASS | §2 текущие Settings/Main/source binding проверены |
| 3 | PASS | §3 данные недоступны в настройках, видимость не учитывает включение |
| 4 | PASS | §4 одно состояние, live toggle, отсутствие stale cross-source |
| 5 | PASS | §5 sync algorithms/calendar/history/publication вне scope |
| 6 | PASS | §6.1 ownership |
| 7 | PASS | §8 live config/source hooks |
| 8 | PASS | §7 eligibility + independent read epoch |
| 9 | PASS | §§6.2/6.4 disabled/error/failed-switch recovery |
| 10 | PASS | Single observer, disabled stop, existing debounce/fallback |
| 11 | PASS | §9 calculated state/eligibility/read epoch, общий owner |
| 12 | PASS | §9 нет persisted schema changes, сохранены legacy controls |
| 13 | PASS | §10 откат только diff дополнения, исходный feature сохранён |
| 14 | PASS | §11 AC1–7 измеряют Settings/header/toggle/lifetime/regression |
| 15 | PASS | §11 AC→UI/coordinator evidence, delayed/disabled/error cases |
| 16 | PASS | §11 build/TUnit commands и stop без случайного green |
| 17 | PASS | §13 последовательность implementation/validation после gate |
| 18 | PASS | §§6.5/14 defaults определены; calendar fix отдельно открыт |
| 19 | PASS | §0 expanded из-за config/state и двух UI-поверхностей |
| 20 | PASS | §15 applicable profiles/local MUST |

### SPEC Rubric Result (author)
| Criterion | Score | Reason |
| --- | --- | --- |
| Goal/bounds | 5 | Две user-visible поверхности, explicit existing blocker |
| AS-IS | 5 | Actual Settings/Main/source binding/projection inspected |
| Design | 5 | Shared owner, eligibility/read epoch/operation separation |
| Safety | 5 | No persisted schema/sync commands; rollback own addition |
| Tests | 5 | AC→actual checkbox/selector/delayed observations/pixels/full gates |
| Autonomy | 5 | Selected defaults explicit; exact approval граница |

Author score30/30 — design completeness, не реализация и не override gates.

### Role-Based Review Result (author)
| Role | Question | Verdict / evidence |
| --- | --- | --- |
| Workflow analyst | Обе части поручения и active config? | PASS §§1/6/7; existing selector/draft distinction |
| UX/designer | Info в нужном разделе, disabled/unknown/focus не путаются? | PASS wireframe/state matrix + future inspected PNG |
| Tester | Every AC, late toggle/switch/operation? | PASS §11, inherited failed full-main не снят |
| Architect | Same instance, lifecycle, no second timer? | PASS §§6.1/6.2/8/9 |
| Operations/security | No reads/writes/secret leaks by disabled or opening settings? | PASS observer-only boundary, safe errors, manifests/spy |

### Post-SPEC Review
Статус: PASS для готовности SPEC к exact approval; не implementation acceptance.
Scope reviewed / evidence inspected: central AGENTS/routing/QUEST/template/linter/rubric/review, prior SPEC/audit, actual SettingsViewModel/SettingsControl/MainVM/MainControl/GitStatusSourceBinding/App/TestHost/selector tests и current git status. Отдельный reviewer прочёл новую SPEC целиком и узкие Settings/App/TestHost/source-binding/status/lifecycle участки. Код/build/tests по дополнению не выполнялись.
Review passes: author contract/design/linter/rubric/role review; отдельный adversarial fallback → две MEDIUM; SPEC-only fix → reviewer re-review §§6.1/6.2/6.4/6.6/8/11/16 → обе закрыты, новых findings нет. Original requirement сохранён, old approval не распространён на новую поверхность/eligibility; no unrelated code writes.
Author adversarial checks: .git+disabled; enabled+missing repo; nonactive SelectedTaskSpace; draft persistence error; failed source switch; off→on before Terminal; late read from old eligibility epoch; flyout closes while focused; observer/default owner replacement. Design их явно покрывает.

| Severity | Finding | Disposition / re-review |
| --- | --- | --- |
| MEDIUM | matchedActiveProjection не определял owner/complete и допускал partial Settings A/B | Fixed §§6.1/6.2: transient source marker, invalidate-before-notifications/complete-after-all-fields, active guard; §11 partial B/late A/rollback A. Reviewer подтвердил closure |
| MEDIUM | Remote/branch update мог игнорироваться или сбросить operation через SetSource | Fixed §6.2: separate target context update/read epoch, unknown old divergence, retain operation IDs; §11 pending read+Started/Terminal. Reviewer подтвердил closure |
| MEDIUM (author) | Config identity/read epoch/operation-state separation | Fixed §§6.2/7/8; no disabled=successful-sync interpretation |

Reviewer environment: danger-full-access, approval never. Technical read-only sandbox недоступен; это отдельный adversarial fallback с read-only действиями в writable session, не технически изолированный independent review. Residual risk — ограничение изоляции; фактических writes/build/tests reviewer не выполнял. Не подменяет future implementation tests.
Depth checklist: all matrices заполнены; конкретные source/config/operation контрпримеры рассмотрены; no unsupported actual UI/result claims; no schema migration; original/regression gate сохранён; docs only SPEC; rendered proof future; approval boundary explicit. Manual-review challenge: старый baseline main bug не разрешён этой SPEC; flag draft не receipt persisted config; неизвестные commits не превращаются в zero.
Исторический Stop decision Post-SPEC: SPEC-ready PASS после исправления и затронутого re-review. Нерешённых обязательных условий именно SPEC-фазы не было; ожидалось exact approval дополнения, полученное11.10 ниже. Это не implementation/AC PASS и не снятие старого full-main blocker.

### Post-EXEC Review
Локальный EXEC и bounded Post-EXEC review завершены после exact approval11.10. Реализованы shared Settings information, conditional eligibility/read epoch и lifecycle transfer. Целевые main103/103 PASS; selector matrix PASS; Settings popup replacement first FAIL → focus correction → relevant rerun PASS. Complete solution PASS0errors/57warnings50.97s. Full Headless first ABORTED: dump подтвердил terminal worker/orphaned Dispatch; bounded fail-fast fixture correction сохраняет primary exception, relevant5/5 PASS. Следующий full60/62 выявил off-UI metadata setters и transitional source-switch assertion; UI dispatcher/persistent metadata session и wait completed projection исправлены, relevant2/2+2/2 PASS. Original assertions сохранены. Reviewer все findings статически/targeted закрыл; wrong-thread platform-init trigger не объявлен изолированным. Latest full Headless63/63 и full main1428/1428 PASS (main1:08:56.809, TRX15_52_42.1622159); финальная solution build с -m:1 --disable-build-servers --no-restore PASS0errors/5warnings29.75s; bounded final review завершён, новых findings нет; actual final TRX/build самостоятельно сверены reviewer. AC7 дополнения и old AC8/overall acceptance остаются NEEDS-FIX из-за inherited intermittent phone-layout blocker; текущий зелёный прогон не закрывает его причину. Просмотрены rendered PNG; native попытки не приняты: input-desktop probe win32Error5/чёрный capture требуют PNG/trace fallback, не native PASS. Текущие evidence и review findings: `docs/testing/git-dirty-status-indicator/settings-2026-10-11.md`. Old Headless59/new24 остаются historical evidence.

## Approval
Получено 11.10.2026: «спеку подтверждаю» для дополнения. Approval не разрешает product calendar fix/push/merge/install/release/live task writes.

## 20. Журнал действий агента
| Event | Decision / evidence | Остаток / next | Human decision | Artifact |
| --- | --- | --- | --- | --- |
| SPEC11.10 | Actual BackupEnabled/active projection и разные DC inspected; shared-status/eligibility design | Separate review, затем показать SPEC | Пользователь потребовал Settings info + enabled-only indicator; exact approval ещё нет | Этот файл |
| Review11.10 | Separate writable-session read-only-actions fallback: две MEDIUM → SPEC-only fixes → relevant re-review обеих закрыт; SPEC-ready PASS | Показать дополнение; EXEC только после exact approval, old main blocker остаётся | Нового approval пока нет; calendar fix не разрешён | §§6/8/11/16/19 |
| EXEC11.10 | Exact approval получен, начинается локальная реализация дополнения в прежнем worktree | Code/UI tests/rendered review/regression gates; calendar blocker отдельно | Пользователь: «спеку подтверждаю» | Этот файл |
| EXEC validation11.10 | Shared owner/eligibility/runtime-token/focus реализованы; target main103/103 и UI flows PASS; terminal worker/metadata/source-switch fixture failures разобраны и исправлены с сохранёнными assertions | Latest full Headless63/63 и main1428/1428 PASS; final build PASS; bounded final review / inherited gate NEEDS-FIX | Нового user gate для этих in-scope исправлений нет; calendar fix по-прежнему вне scope | Amendment audit |

| Final Post-EXEC11.10 | Main1428/1428, Headless63/63 и required solution build PASS; bounded reviewer exact evidence подтвердил, новых findings нет | AC1–6 PASS; AC7/old AC8/overall NEEDS-FIX inherited calendar blocker; native/video blocked, actual Headless PNG/trace fallback | Calendar fix/изменение gate не разрешены approval дополнения; Git delivery не выполнялся | Amendment audit + final PNG |

| PR delivery11.10 | Пользователь поручил Оформи PR / От текущего мейна; rebase на origin/main4ca32268, конфликты fixtures разрешены с сохранением main CLI implementation; integration review без новых findings | Post-rebase solution PASS, targeted103/103 и fullHeadless63/63 PASS; full main нового head ожидает CI; draft / inherited gate NEEDS-FIX | Commit/push/PR разрешены новым поручением; merge/install/release не разрешены | docs/testing/git-dirty-status-indicator/delivery-2026-10-11.md |
