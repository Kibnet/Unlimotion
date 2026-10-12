# Сброс каталога SSH-ключей на стандартный

## 0. Метаданные

- Форма: Expanded, поскольку меняется сохранённая Git-настройка и возможен сброс выбранного ключа. Масштаб medium, ограниченный локальный UI/config outcome; не новый security protocol, migration или deployment.
- Профили: dotnet-desktop-client, ui-automation-testing; context testing-dotnet. Owners: central routing, model/tool/collaboration/testing baselines, quest-governance/mode, spec-linter/rubric/review-loops. Локальный AGENTS.override.md применяется поверх central stack.
- Источник шаблона: C:/Users/Kibnet/.codex/agents/templates/specs/_template.md.
- Задача: 59e89dbe-180b-4c9b-8803-bd4761a1a7eb, Prepared. Все родители: 232a9bf4-fee5-434c-b93c-ac0e53a2a557 «Фичи» → a5ef0164-8def-417b-9b7d-d235516dd8c7 «♾️Unlimotion»; описания пустые, execution отсутствует в snapshot12.10.
- Локальная база чтения: e987023dc93daba6753440eec813edcbaa468da9, C:/Projects/Education/Unlimotion Space/Unlimotion. Актуальная база исполнения: main fa1bfc03043f58fc9d6d626c1dedb27767bd0103, подтверждена GitHub API12.10. До EXEC перечитать actual head/owner и port SPEC на новую базу; локальный checkout/refs ночью не обновлялись. Ветка не создана, релиз не назначен.
- Surface: Codex local Windows. Фактический model/tier/effort не проверен; настройки модели не изменялись. Целевой behavior baseline каталога — GPT-6 Astra, не утверждение о runtime. Model eval не применим: настройка UI, не смена модели.
- На SPEC изменён только этот документ; tracked product files не менялись. Другие untracked SPEC принадлежат прежним задачам/владельцам и сохраняются.

## 1. Overview / Цель

Исходное поручение: «Добавить кнопку сброса каталога ssh ключей на стандартный». Пользователь в настройках SSH может одним явным действием вернуть стандартный каталог, увидеть целевой и действующий путь, не искать и не вводить его вручную.

Success means: кнопка очищает override текущего пространства через обычную настройку; resolver выбирает платформенный default; UI и список ключей обновлены; выбор ключа сохраняется или очищается по уже действующему правилу; эффект сохраняется после пересоздания настроек. Файлы ключей и Git repository не изменяются.

Output: локальное UI/config изменение с VM/UI/persistence проверками и просмотренным visual evidence. SPEC не является реализованной функцией.

Stop rules: до exact approval нет EXEC. После реализации нельзя объявлять completion без обязательных green и проверки обычного сценария. При конфликте source/ownership перечитать scope; не исправлять посторонний baseline или менять тестовые допуски автоматически.

## 2. Текущее состояние (AS-IS)

Исследованы SshKeyStoragePathResolver.cs полностью, SettingsViewModel.cs:122,322–329,772–799,931–950,1283–1297, SettingsControl.axaml:450–490 и SettingsViewModelTests.cs:1060–1110,1780–1820.

- ResolveSshDirectory(null/empty/whitespace) использует GetDefaultSshDirectory; непустой override — Path.GetFullPath.
- Windows default: Environment.SpecialFolder.UserProfile + .ssh; остальные OS: HOME, при пустом HOME — UserProfile, затем .ssh. Не закреплять путь машины автора и не подменять правило на cwd/каталог приложения.
- Setter SshKeyStoragePath сохраняет обычную Git-настройку через SetTaskSpaceSetting и вызывает ReloadSshPublicKeys. Persistence может быть отложена штатным режимом task-space settings.
- ReloadSshPublicKeys сохраняет выбранный public key, только если найден в новом списке; иначе очищает selected/public/private paths. Это существующий и тестируемый side effect, а не удаление ключа.
- BackupViaGitService.GetSshPublicKeys только перечисляет *.pub верхнего уровня; отсутствующий каталог возвращает пустой список. В отличие от GenerateSshKey он НЕ создаёт каталог и НЕ запускает ssh-keygen.
- IOException/UnauthorizedAccessException и ошибки промежуточного пути уже обрабатываются ReloadSshPublicKeys как пустой список. Дизайн не выдаёт это за успешную SSH-аутентификацию или доказанное отсутствие файлов.
- Сейчас есть textbox, Browse и Effective path. Специальной reset command/button в исследованной области нет. Ручное очищение уже включает default-семантику.
- SettingsControlResponsiveUiTests использует SafeHeadlessUnitTestSession и automation IDs; space-scoped section отключена при IsTaskSpaceSwitching. Предыдущие результаты тестов не являются текущим EXEC evidence.
- Дополнительный drift review: на pinned main fa1bfc03 resolver, SettingsViewModelTests и ResponsiveUiTests имеют те же Git blob SHA, что локальная база. SettingsViewModel/view изменились: добавлены GitProjection/provider и GitSyncInformationPanel. Remote source реально прочитан: setter/default/selection и существующий SSH FieldGroup сохранены, reset ещё отсутствует. Новую projection/provider и панель нельзя потерять при переносе diff. Evidence локально: C:/Obsidian/kibnet/Unlimotion/Ночной агент/Материалы/20261012T035805-unlimotion2/main-source-identity.json, main-settings-source.cs и main-settings-view.axaml. Git fetch/checkout не выполнялись.

## 3. Проблема

Вернуть standard mode можно только неочевидным ручным удалением текста. Пользователь не видит стандартный целевой путь до сброса и может ожидать, что кнопка переносит файлы или автоматически выбирает другой ключ.

## 4. Цели дизайна

Явный reset рядом с полем, стандартный путь виден без догадок, существующие resolver/persistence/reload переиспользуются. Не вводить вторую семантику default и не скрывать последствия для selected key. Сохранить Browse, клавиатуру, localization, узкую ширину и действующие task-space guards.

## 5. Non-Goals

Не сбрасывать каталог Tasks, не менять TaskStoragePath или источник пространства. Не переносить/удалять/создавать ключи/каталоги, не читать private key content, не выполнять connect/pull/push/fetch/keygen или сеть. Не менять remote/auth type/passphrase, known_hosts, модель SSH credential resolution. Не сохранять вычисленный absolute default как новый override. Нет rollout/install/release/commit/push/PR/Tracker writes.

Не сохранять любой прежний selected key вопреки существующему правилу ReloadSshPublicKeys: ключ вне нового списка очищается. Не менять несвязанные settings/errors/security workflows ради этой кнопки.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Ответственности

SettingsViewModel: ResetSshKeyStoragePathCommand, computed DefaultSshKeyStoragePath и localized display text, доступность команды. SettingsControl: явная кнопка и текст стандартного пути с wrapping. Resolver/service: существующие правила, без нового production алгоритма. Localizations: RU/EN labels/hints. VM/UI tests: observable reset, guards, key selection и persistence.

### 6.2 Детальный дизайн / visual planning artifact

Command выполняет SshKeyStoragePath = string.Empty через обычный setter на UI owner. CanExecute: !IsTaskSpaceSwitching && !string.IsNullOrWhiteSpace(SshKeyStoragePath), с реактивным обновлением. Для direct command invocation guard тот же; операция не захватывает tracker lease.

DefaultSshKeyStoragePath вычисляется существующим resolver GetDefaultSshDirectory без файловых операций. Действующий EffectiveSshKeyStoragePath и его notifications сохраняются. Новый localized default text использует format {0}; обновляется при смене культуры тем же механизмом SettingsViewModel, что соседний effective path.

Wireframe, локальный текстовый design artifact — не screenshot реализованного UI:

~~~text
Каталог SSH-ключей
[ <custom/keys>                         ] [Обзор]
[Сбросить]  Стандартный: <platform-default, перенос строк>
Действующий каталог: <effective, перенос строк>
Ключи не перемещаются. Если активного ключа нет в стандартном
каталоге, его выбор сбросится.
~~~

Кнопка и default text в WrapPanel/эквивалентном responsive контейнере внутри FieldGroup после существующей строки textbox/Browse. Не добавлять третью жёсткую Grid-колонку: на360px две текстовые кнопки не должны вытеснять поле. «Сбросить»/«Reset» и tooltip с точным default; видимая подпись сообщает последствия, без success toast «подключено». Automation IDs: ResetSshKeyStoragePathButton, SshKeyStorageDefaultPathTextBlock; существующие IDs не меняются.

Никакой long-running работы не добавляется: переиспользуется одно штатное обновление списка *.pub, без рекурсивного обхода, network и дополнительных повторных refresh. Если обычный reset в EXEC обнаружит UI stall, это mandatory blocker; async/state изменение не импровизировать вне согласованного scope.

UI video: новая feature — passing «после» из автоматизированного run. Meaningful baseline есть textbox/Browse/manual clearing: characterization «до» возможно. При объективной невозможности video обязательны причина/команда/диагностика и next-best inspected PNG + UI action trace; монтаж картинок не video. Recorder capability проверяется в EXEC, сейчас запись не выполнялась.

### 6.3 User-Observable Scenarios

| Scenario | Действие | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Custom → default | Открыть SSH настройки, нажать Reset | Override пуст, default/effective совпали, кнопка disabled, список обновлён | Headless click + PNG/trace | 1–3 |
| Key selection | Reset при ключе вне default; затем при ключе в default | Первый очищен, второй сохранён; файлы не тронуты | VM fixture + manifest | 3–4 |
| Restart | Сохранить обычным path, пересоздать/перезапустить приложение | Standard mode не превращён в absolute override | WritableJson fixture + обычный app smoke | 5 |
| Guard / localization | Space switching,360px,RU/EN,light/dark,keyboard | Нет записи во время switching; элементы видимы/доступны, default/effective локализованы | UI tests + inspected open panel | 6–7 |

### 6.4 State / Interaction Matrix

| State | Trigger | Result | Edge / notes |
| --- | --- | --- | --- |
| Nonblank override | Reset | Empty override → existing resolver/reload | Explicit default literal тоже очищается: effective может не измениться |
| null/empty/whitespace | Reset | Disabled/no-op | Не бесконечные refresh/записи |
| Switching | Mouse/key/direct invocation | Disabled/no write | После switching availability пересчитана |
| Missing/denied default dir | Reset | Default mode, штатный empty list/error handling | Не создаёт каталог; нет claim «ключей физически нет» |
| Old key found/not found | Reload | Existing match сохраняется / selection очищается | Public/private config paths меняются только по штатной нормализации |
| Other space | Save/reset current | Другие пространства/config не меняются | Проверка двух независимых config fixtures |

### 6.5 Decision Ledger

| Decision | Owner | Choice | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Default representation | agent | Empty string, existing resolver | 0.99 | Absolute path pin нарушил бы portability | Нет |
| Key selection semantics | agent | Preserve existing reload match/clear | 0.98 | Нельзя обещать сохранение ключа вне каталога | Нет |
| Control placement | agent | Wrap second line, явная текстовая кнопка | 0.9 | Узкая ширина; покрыта visual acceptance | Нет |

Содержательных user-owned решений в Ledger не осталось; общий exact approval — отдельный phase gate в разделе Approval. Prepared не разрешение реализации. У пользователя не требуется выбирать инженерное представление empty string. Материальное изменение scope требует новой редакции; old Git/relation SPEC approvals к этому документу не применяются.

### 6.6 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Expected change | Compatibility | Verification |
| --- | --- | --- | --- | --- |
| Directory | Git.SshKeyStoragePath/current settings | Override → empty | Existing field/schema, no migration | Config read-back/recreate |
| Default | SshKeyStoragePathResolver | None | Windows/UserProfile, non-Windows HOME fallback | Resolver tests; no env mutation on real account |
| Selection | Existing reload/match/setters | May clear public/private config paths | Deliberate existing behavior | Selected key present/absent fixtures |
| Files / repository / network | Isolated byte manifests / backup spy | None | No copy/delete/keygen/ref writes | Before/after equal + zero calls |
| Task tracker | Explicit Tasks source | None | No status/description/lease writes | Scoped diff/task writes forbidden |

## 7. Бизнес-правила / алгоритмы

Reset означает вернуть стандартный **режим настройки**, не физический каталог. Возможен пустой список ключей. Он не является доказательством готового подключения. Единственное прямое persisted изменение — directory override; public/private selection paths могут очищаться/нормализоваться уже существующими setters. Остальные Git/settings поля byte/semantic preserved в fixture.

## 8. Точки интеграции

Command binding в SettingsControl, CanExecute subscriptions на path/switching, существующий setter и ReloadSshPublicKeys, culture refresh для нового computed text. Disposal/lifetime команды по действующему VM pattern; не создавать скрытые подписки на каждый render.

## 9. Модель данных / состояния

Новых persisted полей нет. Новые команда и computed display свойства только runtime. Пустой override поддерживается старыми данными; выбор literal default не блокирует reset.

## 10. Миграция / Rollout / Rollback

Миграция не нужна. Runtime rollout ограничен локальной проверкой после approval, без установки пользовательского релиза. Откат реализации — собственный diff; откат пользовательской настройки — снова Browse/ввести прежний путь и выбрать прежний ключ при его наличии. Никаких резервных копий private key в artifacts; прежние значения допустимы только в isolated fixtures, не пользовательский secret dump.

## 11. Тестирование и критерии приёмки

AC1: кнопка запускает обычный reset custom path → empty, effective и default сразу совпадают; повтор disabled/no-op.

AC2: null/empty/whitespace и explicit absolute default дают правильный режим; Windows/non-Windows default policy сохранена.

AC3: список *.pub обновлён; ключ внутри default сохраняется, вне default очищается по существующему правилу, без ложного selection другого ключа.

AC4: нет keygen/network/directory/file/repository writes; settings вне directory и допустимой selection нормализации сохранены, другое пространство не изменено.

AC5: override сохранён через обычный WritableJson configuration; после recreate и обычного app restart standard mode/effective path сохранены. Fake IConfiguration без persistence не закрывает AC.

AC6: switching guard работает для мыши, клавиатуры и direct command; после смены пространства reset пишет только текущую настройку.

AC7: open SSH panel360/720px, RU/EN, light/dark; button и default/effective text видимы без horizontal clipping; keyboard focus/activation работают. Для закрытия AC настоящие кадры должны быть просмотрены; wireframe не visual proof реализации.

AC8: solution build и все обязательные Main/Headless suites green. Новый config action требует full по testing-baseline; targeted green не заменяет полный. Existing intermittent calendar failure не исключается из suite и не лечится автоматически чужим scope.

### Acceptance-to-Test Matrix

| AC | Automated | Ordinary / visual check | Artifact | Пока не проверено |
| --- | --- | --- | --- | --- |
| 1–2 | New ResetSshKeyStoragePath tests + existing resolver cases | Click custom and literal default | TRX + action trace | SPEC only |
| 3 | Found/missing selected key; missing/denied directory | Список/selection после Reset | TRX + field snapshots | SPEC only |
| 4 | Backup spy0 calls, isolated file manifests, two-config compare | No remote operation/status success claim | Manifest/spy JSON | SPEC only |
| 5 | WritableJson save/recreate | Ordinary desktop restart in isolated data | TRX + persistence trace | SPEC only |
| 6 | SettingsControlResponsiveUiTests switching/direct command | Keyboard activation | TRX + trace | SPEC only |
| 7 | SettingsControlResponsiveUiTests widths/cultures/themes | Inspect open panel/long path/disabled button | PNG/video/fallback cause | SPEC only |
| 8 | Full Main and Headless after build | Compare counts to discovery; no skips hidden | Full TRX/logs | SPEC only |

Planned commands из implementation checkout, не исполнены на SPEC:

~~~powershell
dotnet build src/Unlimotion.sln -m:1 --disable-build-servers
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -m:1 --disable-build-servers
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --list-tests
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/SettingsViewModelTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory artifacts/reset-ssh/vm
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --treenode-filter '/*/*/SettingsControlResponsiveUiTests/*' --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory artifacts/reset-ssh/ui
dotnet src/Unlimotion.Test/bin/Debug/net10.0/Unlimotion.Test.dll --maximum-parallel-tests 1 --report-trx --results-directory artifacts/reset-ssh/full-main
dotnet tests/Unlimotion.UiTests.Headless/bin/Debug/net10.0/Unlimotion.UiTests.Headless.dll --maximum-parallel-tests 1 --report-trx --results-directory artifacts/reset-ssh/full-headless
~~~

Текущий csproj net10.0/TUnit подтверждён по источнику; до исполнения проверить actual runner/SDK, discovery и ownership общего output. Full executed counts должны совпасть с discovery, zero-test exit не PASS. Использовать применимые appautomation/run-tunit-tests skills в EXEC. Builds и shared UI runs последовательны. До long full сообщить progress/log path и историческую длительность (Main может занимать часы), не придумывать short timeout. При failure сохранить evidence, установить cause/in-scope vs baseline; повторять только по новой гипотезе, не сокращать обязательный набор.

## 12. Риски и edge cases

Основной риск — пользователь воспримет reset как перенос ключей или сохранение любого active key. Прозрачная подпись и explicit selection tests закрывают это в дизайне. Ошибка чтения каталога/permissions не означает «чистое успешное подключение». Persistence failure остаётся обычным settings failure; не маскировать exception success toast.

### Expected User Review Objections

| Возражение | Почему | Mitigation | Status |
| --- | --- | --- | --- |
| «Ты удалил/перенёс ключи?» | Название reset двусмысленно | Visible no-move copy, manifests/keygen spy | mitigated |
| «Почему пропал выбранный ключ?» | Стандартный каталог другой | Явная подпись, match/clear contract, no arbitrary auto-selection | mitigated |
| «Путь сохранился, но кнопки не видно» | Прежний save≠visibility lesson | Narrow/open-panel actual screenshots/UI action trace | mitigated |
| «После restart опять custom?» | In-memory tests недостаточны | WritableJson and ordinary restart acceptance | mitigated |

Rework prevention: исходное действие/видимый результат названы; все scenarios имеют AC/evidence; решения и gate разделены; роли ниже; AC verifiers, не список планирования; EXEC имеет обычный путь проверки.

## 13. План выполнения

После approval: characterization existing empty/path/selection; command/computed text/localizations; UI placement и guards; targeted VM/UI and isolated persistence; ordinary restart/visual evidence; build/full mandatory suites; post-EXEC review. Exact порядок только для red-before-fix и последовательных shared outputs; остальные внутренние детали выбирает исполнитель.

## 14. Открытые вопросы

Содержательных блокирующих вопросов нет. Exact approval получен 12.10.2026, см. Approval. Read failure/selected-key side effect явно приняты как существующий контракт дизайна, не новая бизнес-дилемма. Если implementation выявит необходимость изменить общий settings/IO/security contract, обновить SPEC до зависимого EXEC.

## 15. Соответствие профилю

dotnet-desktop-client: binding/owner/lifetime, обычная сборка/тесты, без новых длительных операций. ui-automation-testing/AGENTS.override: existing UI suite, новые stable IDs без разрушения старых, visual artifact, actual open-state verification, обязательные UI tests и video/fallback. creator-vibe влияет на явную кнопку/целевой путь/объяснение результата вместо скрытой очистки поля. Никаких side-effect gates skill не отменяет.

## 16. Таблица изменений файлов

| Файл | План | Причина |
| --- | --- | --- |
| src/Unlimotion.ViewModel/SettingsViewModel.cs | Command/computed localized text/availability/culture lifetime | Normal reset path |
| src/Unlimotion/Views/SettingsControl.axaml | Wrap reset/default/no-move hint, automation IDs | Visible/narrow-safe action |
| src/Unlimotion.ViewModel/Resources/Strings.resx и Strings.ru.resx | RU/EN reset/default/consequence resources | Localization, без hardcoded runtime text |
| src/Unlimotion.Test/SettingsViewModelTests.cs | Reset/persistence/selection/guard cases | Contract |
| src/Unlimotion.Test/SettingsControlResponsiveUiTests.cs | Ordinary click/key/narrow/disabled cases | Observable UI |
| specs/2026-10-12-reset-ssh-key-directory.md | Audit/journal | Единственная фактическая SPEC mutation |

Исходный план переиспользования getter уточнён по native evidence: в deferred mode configured getter читает старый каталог и оставляет старый ключ выбранным до restart. Для исходного AC3 добавляется **узкая optional read capability** ISshPublicKeyDirectoryReader: чтение публичных имён из явно переданного текущего UI override. Старый IRemoteBackupService/GetSshPublicKeys() не меняется; authentication/keygen/config persistence не меняются, resolver и алгоритм Directory.GetFiles переиспользуются. Production BackupViaGitService реализует capability, VM предпочитает её, legacy doubles/providers сохраняют старый getter. Это конкретизация того же reset/list/selection результата и medium риска, не новый security/IO algorithm; новое user-owned решение не возникает. Не менять очередь persistence ради reload и не скрывать это дополнительное source изменение.

Дополнительные файлы: src/Unlimotion.ViewModel/IRemoteBackupService.cs (optional capability), src/Unlimotion/Services/BackupViaGitService.cs (explicit read entry point, общий существующий listing), tests/Unlimotion.UiTests.FlaUI/Tests/SettingsSshResetFlaUiTests.cs (ordinary restart/native/video). Regression: actual BackupViaGitService + UseDeferredTaskSpaceSettingsPersistence, старый configured path остаётся в fixture config, но UI list уже default и outside key очищен; manifests unchanged. Native assertion должен проверять selection, не только textbox/config path. Согласованная граница key/auth/files/network остаётся неизменной.

## 17. Было → стало

| Область | Было | Стало |
| --- | --- | --- |
| Reset | Ручное очищение textbox | Явная кнопка, target path виден |
| Storage policy | Existing empty/default and selection rule | Та же policy, новый entry point |
| File/network effects | Browse/list/keygen независимы | Reset не запускает keygen/connect/move |

## 18. Альтернативы и компромиссы

Hardcode absolute default — отвергнут: portability/config drift. Icon-only reset — компактнее, но действие неочевидно. Третья жёсткая колонка — меньше высоты, риск narrow overflow. Wrap action со стандартным путём сохраняет явность и поле; немного увеличивает высоту только одного FieldGroup. Новая логика выбора ключа не нужна: существующая match/clear достаточна и тестируется.

## 19. Quality gate и review

### SPEC Linter Result

1 PASS outcome§1; 2 PASS primary source§2; 3 PASS problem§3; 4 PASS goals§4; 5 PASS limits§5; 6 PASS ownership§6.1; 7 PASS bindings§8; 8 PASS reset/default/selection§7; 9 PASS missing/denied/persistence§6.4/12; 10 PASS one existing list/no new expensive flow§6.2; 11 PASS persisted/calculated§9; 12 PASS same schema/no migration§10; 13 PASS own diff/config restore§10; 14 PASS observable AC1–8; 15 PASS exact mapping/negative cases§11; 16 PASS runner/full/stop§11; 17 PASS stage dependencies§13; 18 PASS decisions§6.5/14; 19 PASS expanded medium config§0; 20 PASS actual profiles/local override§15. Итог: ГОТОВО к запросу approval, не EXEC PASS.

### SPEC Rubric Result

| Критерий | Балл | Evidence |
| --- | ---: | --- |
| Цель/границы | 5 | Single directory action, explicit non-goals |
| AS-IS | 5 | Resolver/setter/service/selection tests read |
| Дизайн | 5 | Empty string/guard/wireframe/IDs and culture |
| Безопасность/rollback | 5 | No files/network, permitted selection changes, config rollback |
| Проверки | 5 | AC matrix, ordinary restart/actual rendered state/full gates |
| Автономность | 5 | No open product decision; phase gate preserved |

30/30 — design readiness, не доказательство внедрения или заменитель approval.

### Role-Based Review Result

| Role | Applicability | Проверка | Verdict | Required changes |
| --- | --- | --- | --- | --- |
| Domain/workflow | applicable config | Reset возвращает mode, не copy keys | PASS | Clarified selection side effect |
| UX/designer | applicable | Path visible before action, narrow layout, no secret dump | PASS | Wrap second line, visible consequence copy |
| Tester | applicable | Negatives/persistence/selection/switching/full vs targeted | PASS | Ordinary restart and full gates retained |
| Architect | applicable | Existing setter/resolver/contracts/lifetime | PASS | No second resolver |
| Operations/security | applicable | No keygen/network/file mutations; config scope | PASS | No private material in artifacts |

### Post-SPEC Review

Scope reviewed: current SPEC, central owners/template/linter/rubric, profiles/override, source list§2, service GetSshPublicKeys, ResponsiveUiTests and csproj; planned files§16; no open semantic decision. Base/head/dirty state read, other specs preserved. Это main-agent multi-pass, **не независимый reviewer**; medium bounded config UI, не large/high-risk protocol/deploy/migration. При расширении security/storage behavior требуется повторная классификация и independent review по owner.

Scope/Evidence pass: task/parents read via fresh CLI snapshot; no completion inferred. Contract pass: normal settings setter, default policy, stored selection exception included, approval separate. Adversarial pass: empty path, literal default, stale selected key, nonexistent/denied directory, switching/direct command, narrow/EN, persistence failure challenged. Role-Based pass: five applicable roles above, no blanket unrelated changes.

| Severity | Area | Finding | Action | Status |
| --- | --- | --- | --- | --- |
| HIGH | Config selection | «Только path» скрывает normal public/private selection clearing | Explicit allowed derived config changes + tests | fixed in design |
| MEDIUM | UX | Third fixed column может скрыть кнопки на360px | Wrap second row/wireframe/AC7 | fixed in design |
| MEDIUM | Evidence | In-memory fixture не доказывает сохранение после restart | WritableJson + ordinary restart AC5 | fixed in design |
| LOW | Runtime | Default directory list uses existing synchronous IO | No new recursive/repeated list; EXEC stall is blocker | residual, no unconditional responsiveness claim |

Fix and re-review: после этих изменений повторно сверены setter→Reload→Selected setter/service, AC3–5/7, Decision Ledger и File/Runtime matrices. Контракты совпали: clearing selection — данные, не удаление файла; missing dir не создаётся. No-findings justification для оставшейся поверхности: every scenario mapped, default portable, guards and no-op specified, network/private writes excluded; no UI/implementation evidence claimed prematurely.

Depth checklist: scope/unrelated preserved; AC/evidence mapped; approval/scenarios/objections populated; current phase validation is design/source review only; no unsupported installed/live/performance claims; edge cases covered; no changelog/version delivery at SPEC; hidden selection effect named; manual-review challenge «покажите реальный reset/restart при ключе вне default» имеет конкретную planned verification. Residual: platform runtime и UI footage ещё не выполнены, full baseline may fail; это не закрытые EXEC AC.

Stop decision исторического post-SPEC: PASS **design only**. Approval уже получен, не запрашивать повторно. Итоговый post-EXEC review ниже: полный local validation и исходный ordinary reset/restart сценарий закрыты; это не установка или публикация.

### Post-EXEC Review — итог12.10.2026, PASS локальной реализации

- Scope/Evidence pass: просмотрены approved SPEC, git status/diff/stat, VM command/CanExecute/locale, optional interface и реальный listing service, AXAML/resources, VM regression и native fixture. Из существующих tests нет изменённых assertions/исключений; generic rendered helper получил только optional class selector. Сборка `dotnet build src/Unlimotion.sln --no-restore -m:1` после остановки занятых test hosts: 55 warnings/0 errors, 01:10.46 (`artifacts-reset-ssh-isolated-solution-build-clean.log`).
- Contract pass: AC1–5/7 подтверждаются VM82/82, responsive классом18/18 (один test выполняет8 render cases), reviewed PNG и native1/1 с обычным restart. AC6 имеет CanExecute/direct-call guard, rendered disabled state и existing task-space guard coverage. AC8 закрыт: full Main1482/1482 и full Headless63/63,0skip, exit0/0, discovery1482, TRX counters прочитаны. Main44m37.943s включает полный benchmark3000tasks/600reads; он не исключался. Headless clean-first63/63 и повтор63/63; baseline63/63. Старые interrupted runs не засчитаны.
- Adversarial risk pass: найден и исправлен deferred config/list mismatch; последующий native test требует selection null до restart, а не только empty textbox. Найден mixed renderer cache в full Main: existing17 builders восстановлены,8-case matrix перенесена в процесс с первым Skia initializer. Проверено отсутствие key/auth/network/ref mutations; synthetic fixture files сохранены byte-for-byte. После согласованной свободной записи при расширенном просмотре baseline0.5s обнаружено system toast из другого чата: baseline MP4 и потенциально загрязнённые native screenshots удалены, не доставляются. Чистые baseline10s/14s PNG сохранены; after0.5s/10s/14s просмотрены без постороннего контента. After MP4 без монтажа, ffprobe PASS. Pair-video fallback объективный: OS notification не предотвращается обещанием не переключать окна, приватность важнее сохранения baseline видео; inspected baseline frame + after raw video + matched native TRX. Новое recording availability разрешение не выводится из старых двух минут.
- Role-Based pass: BA — empty override означает переносимый standard mode, literal default тоже resettable; UX — просмотрены8 after PNG RU/EN,360/720,light/dark, default/effective text и перенос длинных строк; Tester — targeted/native PASS не заменяет full AC8; Developer — optional capability сохраняет legacy IRemoteBackupService и configured getter, один resolver/listing algorithm, no command subscriptions; Operations/security — isolated app/config/synthetic keys, no user secrets copying, no installation/commit/push/task writes.
- Fix and re-review: actual-service deferred regression82/82 и native selection/restart1/1 PASS; restored old builders + process-isolated rendered matrix18/18 PASS (child1/1, все8 состояния, failures aggregated), та же matrix PASS в full Main. Baseline/new native test runs1/1 каждый; video evidence только after MP4 + clean baseline PNG. Ошибочный concurrent build с locked DLL не принят: тесты остановлены по проверенным own PIDs, clean solution build PASS; full Main1482/1482, Headless63/63 PASS. Прежний bootstrap failure не воспроизвёлся в baseline и двух clean candidate Headless runs, его infrastructure source не изменён.
- Depth checklist: scope/unrelated — own detached fa1bfc03, только SSH behavior и его tests; scenarios/matrix/objections — reset/default/selection/portability/persistence/switching/wrap проверены, full gate закрыт; unsupported claims — нет install/CI/production PASS; regression/edge — null/empty/whitespace/literal default/key present/key absent/actual deferred mode; docs/comments — historical approval superseded, схема без миграции, journal хранит неуспешные проверки; hidden contract — explicit reader improvement для current UI override документирован §16; manual-review challenge — actual service с deferred persistence + ordinary restart показаны, fake getter/textbox-only evidence не использованы как достаточные.
- No-findings justification для остальной проверенной source поверхности: command guards прямые вызовы, reuse resolver/listing без новой auth/file semantics, locale notifications и wrapping видны в frames; новых source defects на этой поверхности не найдено. Validation находка закрыта complete green suites, не accepted-risk. По final git status/stat/diff/check нет unrelated edits; main checkout/user changes не перезаписаны.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | AC3/ordinary app | Config-backed list при deferred save оставляет outside key до restart | Explicit current-override reader + actual-service/native regression | fixed; re-review PASS |
| MEDIUM | Rendered test isolation | Skia cases после semantic backend дают cache/hit-test failures | Existing process helper + complete8-case matrix, вернуть old builders | fixed; responsive18/18 PASS |
| BLOCKER | AC8/full validation | Прежние interrupted runs и bootstrap/thread errors недостаточны для PASS | Complete clean rerun + fa1bfc03 baseline; без исключений/чужого source fix | fixed; Main1482/1482, Headless63/63,0skip; re-review PASS |

Stop decision: **PASS локальной EXEC реализации**. Needs human: нет для закрытого approved scope; любые task transitions/commit/PR/install — отдельное поручение. Residual: native проверен на Windows, не physical Android/iOS; CI/публикация/установка не выполнены; full baseline video удалено из-за OS notification, fallback доказательства выше. Прежняя Headless bootstrap intermittency не объявлена исправленной. Review выполнен главным агентом, не назван независимым. Classification medium локальный settings/UI reset, без новой security/IO algorithm/migration/deploy/public contract; large/high-risk independent gate не заявлен применимым.

### Итоговая привязка AC к проверенному evidence

| AC | Result | Evidence |
| --- | --- | --- |
| 1 | PASS | portable reset VM; rendered mouse360/keyboard720; native button/path/disabled |
| 2 | PASS | VM null/empty/whitespace3 cases и literal default; full resolver tests |
| 3 | PASS | key-present/absent cases, real BackupViaGitService/deferred regression, native selected item null до restart |
| 4 | PASS | throwing mutator fake/zero calls, unrelated config/other VM preserved, native synthetic byte manifests equal/no repo |
| 5 | PASS | WritableJson recreate + ordinary app two-process config/read-back/native1/1 |
| 6 | PASS | switching CanExecute/availability events/direct command guard, rendered disabled state, full task-space guard coverage |
| 7 | PASS | reviewed8 PNG states RU/EN360/720 light/dark; actual click/Enter, bounds; native after MP4 + clean baseline PNG fallback |
| 8 | PASS | clean solution build0errors; full Main1482/1482 + Headless63/63,0skip; TRX and exit0/0 |

Evidence: `artifacts-reset-ssh-isolated-solution-build-clean.log`, `artifacts/reset-ssh/isolated-full-main/*.trx`, `isolated-full-headless/*.trx`, `isolated-full-gates.json`, `isolated-responsive/*.trx`, `vm-deferred/*.trx`, `agreed-after-trx/*.trx`, `agreed-before-trx/*.trx`, `video-verification.json`. Исходный after15s MP4 не редактировался; дополнительный QA contact sheet seconds0..14 просмотрен без notifications/overlays.

## Approval

Подтверждено пользователем 12.10.2026 точной фразой «Спеку подтверждаю» в этом чате. Подтверждённая редакция SHA-256 `FC878EC67E58337FC76BC0FBE624F09A0F26F6BE0FD9AFA5DCB06E5CCC0BEE96`. Переход в EXEC; approval не включает Tracker writes, commit/push/PR/merge/install/publication.

## 20. Журнал действий агента

| Фаза/событие | Решение/основание | Evidence/остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC12.10 | Medium expanded: existing Git config/reset entry point | Primary resolver/setter/selection/source inspected; code unchanged | Review passes and precise AC | Не получено | Этот файл |
| Post-SPEC | Selection/UX/persistence findings fixed in design, re-review PASS | No implementation/ordinary restart/UI footage | Exact approval, then scoped EXEC | Ожидается; старые approvals не перенесены | Этот файл; night checkpoint |
| Drift re-review | Git main стал fa1bfc03 после PR322; remote VM/view прочитаны, сохраняем projection/panel | Resolver и existing tests byte identity по Git blobs; relevant setter/SSH section unchanged | EXEC preflight на actual main, не переносить весь старый файл | Новое содержательное решение не требуется | SPEC и pinned source evidence |
| Approval12.10 | Exact «Спеку подтверждаю», approved SHA FC878EC6…0BEE96 | Current main fa1bfc03 подтверждён повторно GitHub API; отдельный detached worktree ssh-reset-20261012 | Scoped implementation/VM/UI/persistence/full tests и review | Получено, не publication/task writes | Рабочая SPEC в implementation worktree |
| Native adversarial review | На viewed video после reset выбран synthetic key вне default, после restart исчезает | Config-backed listing отстаёт при deferred save; AC3 ещё не PASS. Старый full run остановлен по точному own PID, не засчитан | Narrow optional read capability, actual-service regression, повторные native/full gates | Same approved result/risk, без новой auth/persistence semantics | native-video-after-2/frame-10s.png, §16 refinement |
| EXEC validation checkpoint | Optional directory reader реализован; старый getter/interface сохранён | VM82/82, responsive UI25/25; full solution build77 warnings/0errors; native ordinary restart+selection1/1 PASS. Tests main1489/headless63 running with progress trace | Дождаться full gates, post-EXEC review | Approval не повторяется | artifacts/reset-ssh/* |
| Video privacy gate | Region recorder захватил перекрывшее окно; видео не accepted | Ошибочные generated videos удалены; source/user files не затронуты. Native test behavior PASS не равен video PASS | Не снимать без свободного окна; запрос доступности отправлен, независимый full run продолжается | Свободное окно ещё не подтверждено | native TRX, inspected Headless PNG; команда/diagnostics сохранены |
| Согласованная запись12.10 | Пользователь разрешил запись и не переключать окна; запись окончена, ограничение снято | Baseline1/1 и after1/1 native PASS; обе MP4 3078×1384,15fps,15s. Просмотрены after0.5s/10s и baseline10s: нужное окно, custom→default, очищенный selection; baseline сохраняет старый selection | Хранить исходные пары без монтажа; новые full gates | Получено только разрешение записи, не публикация | artifacts/reset-ssh/agreed-before/manual-clear-before.mp4; agreed-after/reset-after.mp4 |
| Full-suite rendering isolation | В полном main mixed semantic/rendered backend дал RU hit-test errors и HeadlessGlyphRunStub→Skia cast | Старый full main остановлен по проверенному own PID265972; это не PASS. Изменение17 существующих builders возвращено. Используется существующий ImportanceRenderedProcess с optional class selector | Новый matrix test выполняет все8 width/locale/theme сценариев с aggregation failures в отдельном процессе; discovery1482 вместо1489 (8rows→1matrix), no exclusion/skip | Same acceptance, только test harness refinement | artifacts-reset-ssh-full-main-final.log; isolated-responsive и isolated-full-* |
| Чистая повторная проверка | Own Headless PID263172 остановлен после bootstrap thread/disposed-session failures и locked-DLL build conflict | Interrupted full gates=-1/-1 не PASS. Clean solution build55 warnings/0errors; matrix class18/18 PASS. Новый Main1482/Headless63 runner и baseline Headless63 в отдельном checkout запущены | Дождаться полного evidence; baseline difference, не scope expansion | Новых вопросов нет | isolated-full-*; baseline-full-headless; Post-EXEC review |
| Расширенный video privacy review | Baseline0.5s содержал OS notification из другого чата, хотя sampled10s был чистым | Загрязнённый baseline MP4/initial frame/потенциальные native screenshots удалены по exact owned paths. Baseline10s/14s inspected PNG retained; after0.5s/10s/14s чистые | Не доставлять baseline video и не заявлять pair-video PASS; fallback clean baseline PNG + raw after video + native TRX. Дополнительный recording не требуется для AC7 | Не продлевать старое разрешение не переключать окна | artifacts/reset-ssh/video-verification.json; agreed-before/frame-last.png; agreed-after/reset-after.mp4 |
| Clean Headless verification | Baseline fa1bfc03 и candidate выполнены после clean builds | Baseline63/63, candidate clean-headless-first63/63,0skip; counters проверены из TRX. Прежний bootstrap failure не воспроизводится в clean run | Завершить Main1482 и итоговый review; runner дополнительно повторит Headless из-за прежней intermittency | Новых решений нет | baseline-full-headless и clean-headless-first TRX |
| Final EXEC13:03MSK | Полные обязательные gates и full main-agent review закрыты | Main1482/1482, Headless63/63,0skip, exit0/0; clean solution0errors. Исходный reset/list/selection/ordinary restart и8 UI states checked. git diff --check PASS, только scoped own changes | Передать local результат/доказательства; без publication/installation/task transition | Не требуется для approved local scope; delivery side effects не разрешены | Итоговый Post-EXEC review; isolated-full-gates.json; после-видео и чистый baseline PNG |
| Delivery approval12.10 | Пользователь прямо поручил «Перебазируй и Влей в мейн» | Разрешены scoped commit/branch/push/PR/merge данного SSH изменения; новый main18e820f1 после PR323. Пересечение только localization resources; отдельного product redesign нет | Commit scoped diff, rebase origin/main, affected tests/build, full CI, merge при green без bypass | Получено; не install/release/TaskJSON transition | Этот журнал; новая delivery validation/PR |
