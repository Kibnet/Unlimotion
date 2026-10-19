# Исправление Windows-регистрации ссылок на задачи

## 0. Метаданные

- Фаза: EXEC, validation blocked только установленным сценарием; пользователь подтвердил «Спеку подтверждаю» 2026-10-19, Europe/Moscow. Все доступные локальные build/test gates green; общий acceptance не закрыт.
- Форма: expanded, medium. Меняется существующий OS/config-контракт установки, поэтому short не подходит.
- Владелец: Павел; подготовка и последующая реализация — Codex.
- Профили: `dotnet-desktop-client`, `ui-automation-testing`; context `testing-dotnet`.
- Instruction stack: central AGENTS/routing; creator-vibe-lens, model-behavior-baseline, tool-execution-baseline, collaboration/testing-baseline, QUEST governance/mode, spec-linter/rubric/review-loops; локальный AGENTS.override.
- Canonical template: `C:/Users/Kibnet/.codex/agents/templates/specs/_template.md`.
- Поверхность: Codex Desktop, Windows/PowerShell. Effective model/effort/client version не проверялись; model eval не применим, меняется приложение.
- Базовый HEAD: `690d41841687641e523c1d6a3953a12afecd1de2`; на EXEC создана рабочая ветка `fix/deep-links-windows-registration`. Коммитов и публикации нет.
- Целевой релиз: следующий согласованный Windows-релиз; номер, публикация и установка не входят в approval этой SPEC.
- Связанные материалы: `specs/2026-10-07-extract-task-deep-links.md`; исходная реализация `44f31f45`; установленный релиз `1.33.0+4ca32268b10817601a48f1e38a6540959e61a458`.
- Отдельно разрешено пользователем в этом чате: восстановить регистрацию на его компьютере и проверить конкретную ссылку. Это оперативное действие не является EXEC постоянного исправления.

## 1. Overview / Цель

Исходный симптом: ссылка из Obsidian не открывает задачу в установленном Unlimotion.
Исправить вычисление пути EXE при регистрации `unlimotion://`, чтобы установка и обновление записывали существующий executable из каталога содержимого Velopack.

Outcome contract:
- Пользователь нажимает существующую ссылку `unlimotion://task/<id>` и видит обычную карточку этой задачи.
- Установка и обновление исправляют также старую ошибочную регистрацию без ручного редактирования реестра.
- Артефакт EXEC: код, содержательные regression/UI tests и evidence проверки установленного пакета в изолированной Windows-среде.
- Stop rules: закончить после обязательных green-проверок и review; при блокере установленного сценария явно оставить acceptance незакрытым. Не подменять ShellExecute прямым запуском EXE в утверждении installed acceptance.

## 2. Текущее состояние (AS-IS)

- Активный процесс установленного релиза находится в `%LOCALAPPDATA%/Unlimotion/current/Unlimotion.Desktop.exe`.
- До восстановления HKCU `Software/Classes/unlimotion/shell/open/command` содержал `"%LOCALAPPDATA%/Unlimotion/Unlimotion.Desktop.exe" "%1"`; файла по этому пути нет. Override `UrlAssociations/unlimotion/UserChoice` отсутствовал.
- `WindowsTaskProtocolRegistrar.ResolveStableLauncherPath()` берёт `RootAppDir` и basename `ProcessExePath`, теряя `current`.
- `Program.Main` регистрирует протокол в `OnAfterInstallFastCallback` / `OnAfterUpdateFastCallback`, удаляет в `OnBeforeUninstallFastCallback`.
- `WindowsTaskProtocolRegistrationService` пишет command, DefaultIcon, URL Protocol и owner. Unregister удаляет ключ только при совпадении owner.
- Тест `WindowsTaskProtocolRegistrationTests.Registration_QuotesStableLauncherAndUriArgument` проверяет строки на fake registry, но не resolver и не наличие executable. FlaUI deep-link test запускает EXE напрямую и также не покрывает ошибочную OS-регистрацию.
- В используемом NuGet `Velopack 0.0.1298` XML-контракт содержит `AppContentDir` и `ThisExeRelativePath`: каталог файлов приложения и путь EXE относительно этого каталога. `RootAppDir` — другой контракт.
- После первой ручной правки пользователь повторно получил «Приложение не найдено» при обычном открытии ссылки; скриншот подтверждает отказ на реальном task URI (личный ID не публикуется). Прежний вывод о полном восстановлении отозван: совпадение карточки и корректные значения реестра сами по себе не доказывают успешную OS-активацию.
- При первоначальном восстановлении уведомление Shell не отправлялось; registrar также явно его не вызывает. Microsoft требует `SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, null, null)` для изменения зарегистрированных протоколов: [официальный контракт](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify). Какие уведомления делает сам installer, ещё должен установить installed lifecycle test.
- После явного уведомления выполнен `ShellExecuteExW` со ссылкой со скриншота: success=true, Win32 error=0, получен child PID. Затем визуально открыта целевая карточка. Это положительное evidence нового Shell-вызова; не доказательство того, что cache был единственной причиной отказа из уже работающего Obsidian.
- Пользователь после этого повторно подтвердил именно «Приложение не найдено». Следовательно, уведомление Shell не считается достаточным решением исходного flow. Дополнительный `ShellExecuteExW` с настоящим null verb и `AssocQueryStringW(ASSOCF_IS_PROTOCOL, ..., null)` успешны; явный `open` не объясняет различие. Уровень integrity PowerShell, Explorer, Obsidian и Unlimotion одинаков: Medium (`S-1-16-8192`).
- Запрос `Win32_ProcessStartTrace` через WMI отклонён с «Отказано в доступе»; трассировка не получена. `Shell.Application.ShellExecute` вернулся без исключения, но его возврат не является результатом клика в уже работающем Obsidian. Штатный перезапуск Obsidian остановлен Computer Use из-за `user input was detected in this window`; другой способ закрытия в обход защиты не используется.
- Итоговое подтверждение пользователя после согласованного штатного перезапуска Obsidian: «Карточка открывается». Локальный flow восстановлен последовательностью path correction → Shell notification → normal client restart. Причинный вклад notification и конкретный механизм cache отдельно не изолированы; гарантировать, что одной notification достаточно для уже запущенного клиента, нельзя.

## 3. Проблема

Регистрация конструирует несуществующий launcher из корня установки; после смены association необходимо также обеспечить уведомление Windows Shell. URI должен достигать механизма запуска/IPC через обычного внешнего клиента, а не только совпадать с текстом реестра.

## 4. Цели дизайна

- Использовать явный контракт расположения файлов Velopack, без предположения о root launcher.
- Разделить resolver и запись реестра для проверки настоящего installed layout.
- Сохранить синтаксис URI, кавычки, per-user регистрацию, owner guard, warm/cold activation и фильтры.
- Не создавать новую подсистему установки или универсальное восстановление реестра.

## 5. Non-Goals

- Изменения parser, IPC, task storage, данных/статусов задач, UI layout, Linux/macOS/Android.
- Автоматический захват чужой регистрации на каждом обычном запуске, регистрация portable/debug builds.
- Push, PR, merge, release и установка новой сборки на пользовательский компьютер.
- Изменение Windows UserChoice, прав доступа или политики безопасности.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `WindowsTaskProtocolRegistrar.cs`: вычисление installed executable через locator; lifecycle adapters; прежняя запись/удаление реестра.
- Там же компактный platform adapter уведомления Shell и injectable seam для теста порядка, без новой общей подсистемы Windows settings.
- `WindowsTaskProtocolRegistrationTests.cs`: filesystem-backed resolver regression, кавычки и owner compatibility.
- `TaskDeepLinkPackagingContractTests.cs`: lifecycle coverage без признания текстовых assertions installed acceptance.
- `tests/Unlimotion.UiTests.FlaUI/Tests/InstalledTaskProtocolFlaUiTests.cs`: отдельный OS-протокол на изолированном установленном fixture; существующий `TaskDeepLinkFlaUiTests.cs` сохраняет direct-EXE coverage.
- Windows packaging workflow: только необходимое подключение installed acceptance, если текущий runner обеспечивает изоляцию; не добавлять live installation на машине пользователя.

### 6.2 Детальный дизайн

Resolver использует `Path.GetFullPath(Path.Combine(locator.AppContentDir, locator.ThisExeRelativePath))` после проверки обязательных значений и относительности `ThisExeRelativePath`. Проверить, что нормализованный путь остаётся внутри AppContentDir и указывает на существующий файл, прежде чем писать хотя бы одно registry value. Не переходить молча к RootAppDir или текущему рабочему каталогу при ошибке. Ошибка должна содержать проверяемый путь/причину и попадать в доступный lifecycle log; не объявлять регистрацию успешной.

Существующий service продолжает формировать `"<absolute-exe>" "%1"`, `"<absolute-exe>",0` и owner для одного и того же executable. Каталог может содержать пробелы и не-ASCII символы. Для текущего Windows layout итог — `.../Unlimotion/current/Unlimotion.Desktop.exe`; не hardcode-ить `%LOCALAPPDATA%`, имя пользователя или имя `current` вместо locator contract.

После успешной записи полного набора значений отправить `SHChangeNotify(SHCNE_ASSOCCHANGED=0x08000000, SHCNF_IDLIST=0, IntPtr.Zero, IntPtr.Zero)`. То же уведомление отправить после фактического удаления своей регистрации. При отказе validation, отсутствии изменения в unregister или foreign owner уведомление не отправляется. Проверить порядок через fake notifier: сначала registry mutation, затем notification. Не требовать перезапуска Explorer/выхода из Windows как способа применения штатной регистрации.

Граница живого внешнего клиента: notification не объявляется гарантией сброса его внутреннего состояния. Installed acceptance проверяет клик из клиента, запущенного до обновления. Если он сохраняет отказ, отдельно фиксируется этот результат и проверяется штатный перезапуск только внешнего клиента; после него ссылка обязана открыть нужную карточку. Краткий troubleshooting-текст описывает этот fallback без ручного редактирования реестра и без перезапуска Explorer/Windows. Автоматически закрывать чужие приложения при update запрещено.

Install/update callbacks переписывают прежние значения собственной регистрации на правильные. Обычный startup не получает новый registry side effect. Сохранить текущую install/update ownership policy; не вводить silent repair чужих owners вне этих callbacks.

Unregister: сохранить guard для правильного owner и совместимость с legacy owner `RootAppDir + basename`, созданным старым релизом. Legacy ключ удалять лишь при точном совпадении legacy owner и legacy command с ожидаемым путём этой установки; иные owner/command не удалять. Для cleanup при uninstall не требовать наличия уже удаляемого EXE: отделить вычисление identity от filesystem precondition регистрации.

Производительность: синхронные вычисления пути и одна проверка наличия файла только в fast callbacks; без сети, сканирования диска и ожиданий UI.

Visual planning artifact: новый макет не применим — layout не меняется. Текстовый storyboard: `Obsidian → Windows unlimotion handler → existing EXE → startup/IPC → та же обычная карточка с целевыми ID и названием`. Error path отсутствующей задачи сохраняет предыдущую карточку и локализованное уведомление.

UI video: до/после из автоматизированного installed UI run на безопасном fixture. На личных задачах видео не сохранять; для локального восстановления используются inspected screenshot и выборочные UIA title/ID. Если recorder/desktop технически недоступен, указать конкретную причину, команду, registry readback, UIA assertions и PNG как fallback; screenshot полного desktop не использовать.

### 6.3 User-Observable Scenarios

| Scenario | Trigger | Видимый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| S1 warm | Нажать ссылку в Obsidian при открытой другой карточке | Нужная карточка в существующем окне | ShellExecute + UIA ID/title + PNG/video, count окна | AC1, AC4 |
| S2 cold | Открыть ссылку при закрытом fixture Unlimotion | Приложение запускается и показывает нужную карточку | Изолированный установленный fixture, UIA ID/title | AC1, AC4 |
| S3 update | Обновить fixture с ошибочной старой регистрацией при уже работающем внешнем клиенте | Запись исправлена без ручного реестра; проверен живой клиент и, при сохранении отказа, обычный restart этого клиента с успешным кликом | Реальный lifecycle update, readback, notification, результат до/после client restart | AC2 |
| S4 ownership | Удаление своей установки / иной owner | Своя регистрация удалена; чужая остаётся | Registry fixture before/after | AC3 |

### 6.4 State / Interaction Matrix

| Состояние | Trigger | Результат | Ошибка/конкурентность |
| --- | --- | --- | --- |
| Installed, legacy command | Install/update callback | Command/icon/owner указывают на content EXE | Отсутствующий EXE: отказ до registry writes |
| Valid owner | Before uninstall | Удаление своего protocol key | Файл может уже отсутствовать; cleanup не зависит от File.Exists |
| Legacy owner | Before uninstall | Удаление лишь подтверждённой legacy записи этой установки | Несовпадение command: сохранить ключ |
| Foreign owner | Before uninstall | Сохранить регистрацию | Не удалять ключ |
| Внешний клиент открыт до изменения | Registration/update | Notification и проверка обычного клика | При сохранении отказа — документированный штатный restart внешнего клиента, затем успешный клик; новый PowerShell не заменяет проверку |
| Running/minimized | URI | Показать целевую карточку, восстановить окно | Один основной процесс; existing IPC contract |
| No task / invalid URI | URI | Прежняя обработка без создания задач | Regression текущих tests |

### 6.5 Decision Ledger

| Decision | Owner | Выбор | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Источник пути | agent | AppContentDir + ThisExeRelativePath | 0.95 | Callback layout должен подтвердиться installed test | Нет |
| Recovery старых установок | agent | Через install/update callbacks, без startup repair | 0.95 | Старая версия до обновления требует локального workaround | Нет |
| Legacy uninstall | agent | Owner + command guard | 0.9 | Нельзя удалять чужой ключ по одному похожему пути | Нет |
| Применение association | agent | Уведомление Shell после успешной mutation | 0.95 | Одна запись реестра не закрывает живой внешний клиент | Нет |
| Текущее восстановление компьютера | user | Отдельно прямо разрешено | 1.0 | Обновление старым кодом может вернуть дефект | Нет |
| EXEC кода | user | Только после «Спеку подтверждаю» | 1.0 | Без approval менять код нельзя | Нет design-вопроса; отдельный фазовый gate |

Существенных нерешённых design-вопросов нет; последняя строка — обычный approval gate, не неизвестное проектное решение.

### 6.6 Runtime / Config / Data Contract Matrix

| Area | Source of truth | Изменение | Совместимость | Проверка |
| --- | --- | --- | --- | --- |
| Layout | Locator установленного Velopack | Использовать content path | Нет pin на личный путь | Filesystem fixture + actual installer |
| Protocol | HKCU Software/Classes/unlimotion | Исправленный command/icon/owner | URI прежний; HKLM/UserChoice не менять | Readback и ShellExecute |
| Shell association | SHChangeNotify / обычный внешний клиент | Уведомление после register/unregister | Без перезапуска Explorer и обхода UserChoice; возможен обычный restart внешнего клиента | Notifier test + live-client и restart fallback |
| Ownership | owner + command | Guard правильного/legacy владельца | Чужой owner сохраняется | Negative tests |
| Task data | Активное пространство | Нет изменений | ID/title только для acceptance | UIA; не создавать/менять личные задачи |

## 7. Бизнес-правила / Алгоритмы

1. Resolve и validate до registry mutation; регистрировать только существующий content executable.
2. Все пути нормализовать одинаково; owner comparisons Windows case-insensitive.
3. Missing locator values/rooted или escaping relative path — явный отказ; fake filesystem должен доказать отсутствие registry writes.
4. Cleanup own current/legacy registration не зависит от наличия EXE и не удаляет чужую запись.
5. Task URI и выбор активного пространства остаются прежними.
6. Registry readback и совпадение выбранной карточки не являются достаточным launch evidence: фиксировать конкретное событие открытия, результат Shell API и смену с другой карточки; для исходного flow отдельно подтвердить клик из Obsidian.

## 8. Точки интеграции и триггеры

Сохранить три Velopack callbacks в `Program.Main`. Не добавлять регистрацию в `App.Init`/VM. Проверить callback paths после настоящей установки/обновления: прямой вызов service не заменяет lifecycle acceptance.

## 9. Изменения модели данных / состояния

Только существующие HKCU protocol values. Поля задач, Settings.json, schema и публичный URI не меняются. Test filesystem/registry данные изолированы от личной установки.

## 10. Миграция / Rollout / Rollback

- Следующее согласованное обновление исправляет старый command. Повторный callback идемпотентен.
- Уже выполненное локальное восстановление: экспорт protocol key до изменения, затем command/icon/owner на существующий `current` EXE. Приложение не переустанавливалось.
- Дополнение после пользовательского контрпримера: отправлен `SHCNE_ASSOCCHANGED`; новый ShellExecuteExW успешен, но пользователь ещё получал ошибку. После штатного перезапуска Obsidian пользователь подтвердил обычный клик: «Карточка открывается». Локальное восстановление завершено; code/package acceptance будущего изменения этим не закрывается.
- Откат локального восстановления: импорт сохранённого `.reg`; это возвращает прежнюю неисправную регистрацию и нужен только для отмены операции.
- Откат кода: revert согласованного change set. Откат на старый релиз способен снова создать ошибочный путь; после него обязательны readback и при необходимости отдельно разрешённое восстановление.
- Release/push/установка на личную машину требуют самостоятельного поручения; временную VM/CI для acceptance использовать без затрагивания активной установки.

## 11. Тестирование и критерии приёмки

AC1: registry command, icon и owner после install указывают на существующий content EXE; пробелы/кириллица сохранены, URI — ровно один quoted argument.
AC2: update с legacy registration автоматически исправляет запись и уведомляет Shell. Проверен клиент, открытый до update: либо ссылка сразу работает, либо отказ до штатного client restart явно зафиксирован, а после restart обычный клик успешно открывает нужную карточку согласно troubleshooting. Перезапуск Explorer/Windows и ручной registry edit не нужны; installer не закрывает внешние приложения сам. Повторная регистрация безопасна; missing/escaping locator values не приводят к частичной записи.
AC3: uninstall удаляет свою current и подтверждённую legacy регистрацию, сохраняет foreign/mismatched owner/command; отсутствие EXE не мешает cleanup.
AC4: OS-протокол открывает нужную карточку при cold/warm/minimized activation; тот же ID/title, одно окно, сохранение фильтров; missing-task поведение прежнее.
AC5: установленный пакет проверен через реальный lifecycle, а не только прямой запуск бинарника; обязательные build/test gates green.

### Acceptance-to-Test Matrix

| AC | Automated test | Runtime/visual check | Evidence artifact | If not tested |
| --- | --- | --- | --- | --- |
| AC1 | Filesystem resolver + quoted command tests | Clean install registry readback | TRX, registry JSON, path existence | Unit PASS; реальный install blocked средой |
| AC2 | Legacy overwrite/idempotency/invalid-path no-writes, notifier after mutation | Update fixture при уже работающем внешнем клиенте; при отказе проверен штатный client restart и обычный клик | TRX, callback logs, before/after JSON, notification trace, результат restart fallback | Unit PASS; isolated update/live-client не выполнены |
| AC3 | Current/legacy/foreign owner и missing EXE tests | Uninstall fixture | TRX + registry before/after | Unit PASS; реальный uninstall не выполнен, личная установка сохранена |
| AC4 | Existing Headless + updated installed FlaUI | Нужные ID/title, счётчик окон, фильтры | TRX + видео либо обоснованный PNG/UIA fallback | Headless 66/66 и direct-EXE native 1/1 PASS; installed OS-протокол pending |
| AC5 | Full core + Headless, affected build, Windows installed run | Install/update/cold/warm/uninstall на fixture | Logs/TRX/version/package identity | Общий acceptance blocked до реального installed lifecycle |

Обязательный набор: targeted registration/deep-link regression; Desktop build; полный core и Headless (OS/config и публичный пользовательский flow); installed Windows FlaUI lifecycle scenario. До code fix получить RED filesystem-backed regression при layout `root/current` без `root/EXE`.

План команд (на EXEC уточнить SDK availability/runner help, minimum test count и output dirs; это не выполненные команды):

```powershell
dotnet --info
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Release
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/WindowsTaskProtocolRegistrationTests/*" --report-trx
dotnet run --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Release -- --report-trx
dotnet run --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Release -- --report-trx
dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Release -- --treenode-filter "/*/*/TaskDeepLinkFlaUiTests/*" --report-trx
dotnet run --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Release -- --treenode-filter "/*/*/InstalledTaskProtocolFlaUiTests/*" --minimum-expected-tests 1 --report-trx
```

Последняя команда требует `UNLIMOTION_PROTOCOL_INSTALL_ACCEPTANCE=1`, `UNLIMOTION_PROTOCOL_BASELINE_DIR`, `UNLIMOTION_PROTOCOL_CANDIDATE_DIR` и действительно disposable GitHub-hosted Windows runner. Без opt-in она пропускается; zero executed с `--minimum-expected-tests 1` не является PASS. Нынешний direct-EXE тест AC5 не закрывает. Installer commands/версию vpk брать из текущего Windows packaging workflow после preflight; не устанавливать сборку на хост пользователя ради fixture. При timeout сохранить progress/logs; повторять только после новой проверяемой гипотезы. Публикация CI не разрешена этой SPEC; локальная недоступность обязательной проверки остаётся blocker.

## 12. Риски и edge cases

- Callback исполняется в другом layout: проверяется настоящим install/update, а не фиксацией строки `current` в тесте.
- Uninstall после удаления EXE: отдельно чистая identity resolution и guard.
- Drift реестра: snapshot/readback и отказ перезаписать неожиданно изменившуюся запись во время ручного восстановления.
- Старый installer/update возвращает дефект до постоянного исправления. Это остаточный риск локального workaround.
- Cache/live-client риск: свежий Shell-процесс может работать при отказе обычного клиента. Acceptance включает клиент, запущенный до обновления; успешный HRESULT без смены нужной карточки не закрывает flow.
- Privacy: видео только на синтетических задачах; личные paths/карточки не включать в публичные PR artifacts.

### Expected User Review Objections

| Likely objection | Почему | Mitigation | Status |
| --- | --- | --- | --- |
| «После обновления снова сломается» | Workaround меняет только реестр | Обязательный update legacy→fixed acceptance | Код готов; реальный installed run остаётся blocker |
| «Тесты зелёные, установленное приложение не работает» | Прежний тест обходил registry | AC5 требует реальный lifecycle и ShellExecute | mitigated |
| «Реестр правильный, но Obsidian всё ещё пишет “Приложение не найдено”» | Пользователь уже предъявил такой контрпример | Notification, live-client check и явный restart fallback; фактический обычный клик после restart подтверждён пользователем | mitigated; future package acceptance pending |
| «Проверили открытие программы, а не моей задачи» | Launch сам по себе недостаточен | UIA ID/title и открытая карточка | mitigated |
| «Удалится чужая ассоциация» | Legacy cleanup расширяет owner guard | Exact owner+command matching, negative tests | mitigated |

Rework Prevention Checklist: исходный сценарий назван; S1–S4 связаны с evidence; решения перечислены; objections обработаны; роли проверены ниже; AC являются проверяемыми состояниями; EXEC имеет путь проверки installed flow.

## 13. План выполнения

После exact approval: Git/toolchain preflight → filesystem regression RED → resolver/cleanup fix → targeted green → build/full обязательные suites → isolated package lifecycle/UI run → post-EXEC review. Эти зависимости сохраняют проверку причины до исправления и installed acceptance после сборки.

## 14. Открытые вопросы

Блокирующих вопросов для SPEC нет. После согласованной паузы клиент штатно перезапущен (PID 173164 → 180776, HWND 114496034 → 9572320); пользователь подтвердил «Карточка открывается». Локальный пользовательский сценарий закрыт именно этим подтверждением. Точный внутренний механизм cache не установлен; отдельное исследование кеша Obsidian не входит в scope. Предусмотрен честный restart fallback внешнего клиента, без обещания бесшовной смены association в уже работающем процессе.

Доступность изолированной Windows installation/UI среды проверяется на EXEC; её отсутствие не разрешает заменять AC5 исходниками или ставить кандидат на личную машину.

## 15. Соответствие профилю

На SPEC выполнены UI storyboard, AC→tests и план сборки/тестов. На EXEC добавлена проверка ID видимой карточки, подготовлен отдельный installed lifecycle test, выполнен native UI run с проверенным видео; полные suite и build evidence фиксируются ниже. Shell/UI проверка текущего релиза — отдельное оперативное evidence, не tests будущего исправления.

## 16. Таблица изменений файлов

| Файл | План | Причина |
| --- | --- | --- |
| src/Unlimotion.Desktop/Services/WindowsTaskProtocolRegistrar.cs | Resolver + legacy cleanup guard + Shell notifier | Исправить установленный layout и применение association |
| src/Unlimotion.Test/WindowsTaskProtocolRegistrationTests.cs | Filesystem regression и negative tests | Закрыть причину и cleanup риски |
| src/Unlimotion.Test/TaskDeepLinkPackagingContractTests.cs | При необходимости адаптировать lifecycle contract | Сохранить интеграцию |
| tests/Unlimotion.UiTests.FlaUI/Tests/InstalledTaskProtocolFlaUiTests.cs | Новый opt-in installed OS-protocol acceptance с защитой disposable runner | Реальные install/update/uninstall и ShellExecute без изменения личного профиля |
| tests/Unlimotion.UiTests.FlaUI/Tests/TaskDeepLinkFlaUiTests.cs | Ожидание старта внешней записи перед переходами | Первое видео пропустило смену карточек из-за компиляции capture helper |
| tests/Unlimotion.UiTests.Headless/Tests/TaskDeepLinkHeadlessTests.cs | Проверять также ID видимой карточки | Не ограничиваться title/VM assertions |
| .github/workflows/windows-packaging.yml | Только при необходимости подключить isolated check | Проверка выпускаемого пакета |
| README.RU.md, существующий раздел ссылок/Windows | Краткое troubleshooting-дополнение о штатном перезапуске внешнего клиента при старой association | Зафиксировать проверенный fallback без ручного реестра |
| Эта SPEC | Evidence/review | Фазы и проверяемость |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Путь | RootAppDir + basename | AppContentDir + relative executable |
| Проверка регистрации | Строки fake registry | Реальный filesystem + installed ShellExecute |
| Update | Повторяет ошибочную запись | Исправляет legacy запись |
| Применение | Нет явного уведомления Shell в registrar | Notification после успешного изменения; проверка живого клиента |
| Uninstall | Только один owner path | Own current + подтверждённый legacy; foreign сохранён |

## 18. Альтернативы и компромиссы

- `RootAppDir/current/EXE`: устраняет конкретный симптом, но дублирует структуру Velopack. Выбран locator contract.
- Startup self-heal: шире требуемого изменения, создаёт новые side effects у portable/debug и риск takeover. Не выбран.
- Только ручной registry workaround: уже решает текущую проблему, но не защищает следующий lifecycle. Не заменяет кодовую работу.

## 19. Результат quality gate и review

### SPEC Linter Result

| № | Блок | Статус | Основание |
| --- | --- | --- | --- |
| 1 | A | PASS | Outcome/S1–S4 |
| 2 | A | PASS | Process/version/registry/filesystem и исходники проверены |
| 3 | A | PASS | Root вместо content path |
| 4 | A | PASS | Инварианты §4 |
| 5 | A | PASS | Границы §5 |
| 6 | B | PASS | Ответственности §6.1 |
| 7 | B | PASS | Callback integration §8 |
| 8 | B | PASS | Алгоритмы §7 |
| 9 | B | PASS | No writes при invalid path; uninstall missing-file случай |
| 10 | B | PASS | Только локальный path/existence check |
| 11 | C | PASS | Registry-only state, task data неизменны |
| 12 | C | PASS | Legacy update и cleanup |
| 13 | C | PASS | Registry backup/import и code revert |
| 14 | D | PASS | AC1–AC5 измеримы |
| 15 | D | PASS | Матрица tests и негативные случаи |
| 16 | D | PASS | Команды, RED→green, zero-tests/timeout stop rules |
| 17 | E | PASS | Последовательность §13 |
| 18 | E | PASS | Decision ledger, design questions отсутствуют |
| 19 | E | PASS | Medium expanded для OS/config |
| 20 | F | PASS | Build/UI/video и installed evidence обязательны |

Итог после финального пользовательского подтверждения и re-review: ГОТОВО к утверждению SPEC. Runtime recovery подтверждён; до code/package acceptance на EXEC ещё обязательны проверки AC1–AC5. Restart fallback явно включён в дизайн и не выдаётся за бесшовное обновление живого клиента.

### SPEC Rubric Result

| Критерий | Балл | Обоснование |
| --- | ---: | --- |
| Цель/границы | 5 | Конкретный protocol flow и Non-Goals |
| AS-IS | 5 | Сопоставлены реестр, executable, версия, код |
| Дизайн | 5 | Locator contract и owner/command guards |
| Безопасность/rollback | 5 | Backup, legacy guard, нет личной переустановки |
| Тестируемость | 5 | Причинный RED и installed lifecycle acceptance |
| Автономность | 5 | Нет design-вопросов, среда проверяется явно |

Итого после финального re-review: 30/30. Полнота дизайна включает проверенный restart fallback и ограничения причинных выводов. Балл не заменяет exact approval или обязательные проверки EXEC.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Изменения |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Откроется нужная карточка из Obsidian? | PASS | S1 и UIA ID/title |
| UX / designer | applicable | Не меняются фильтры/layout и виден результат? | PASS | Storyboard + существующая карточка |
| Tester / validation | applicable | Может ли тест пройти при отсутствующем root EXE? | PASS | Filesystem RED и installed tests |
| Developer / architect | applicable | Не ломает ли File.Exists uninstall? | PASS | Отделены identity и registration validation |
| Delivery / operations / security | applicable | Сохранены ownership, privacy и release boundary? | PASS | Guard, isolated fixture, нет публикации |

### Post-SPEC Review

- Статус / Stop decision: PASS для SPEC после закрытия §14 пользовательским подтверждением и уточнения границы живого клиента; кодовый EXEC не начинается без exact approval.
- Scope reviewed: эта SPEC; central stack выше; registrar, Program callbacks, registration/packaging tests, FlaUI и Headless deep-link tests, Windows packaging workflow, NuGet locator XML.
- Scope/Evidence pass: текущая 1.33.0 действительно содержит разницу root/current; string test и direct-EXE UI test обходили причинный контракт.
- Contract pass: S1–S4→AC1–AC5; repo UI test gate сохранён; личное восстановление отделено от реализации/релиза.
- Adversarial risk pass: missing EXE при uninstall, legacy owner после path fix, rooted/escaping relative path, foreign registration, stale callback layout и reinstall regression рассмотрены.
- Role-Based pass: применимые пять ролей проверены в таблице выше, self-review; independent review не заявляется. Scope medium, high-risk runtime/security changes не планируются.
- Findings: MEDIUM/compatibility — простой path fix оставил бы legacy ключ после uninstall; fixed в §6.2/AC3. MEDIUM/evidence — текущий FlaUI test напрямую запускает EXE; fixed в AC5 и плане installed scenario. MEDIUM/safety — File.Exists в общем resolver ломал бы cleanup удалённого EXE; fixed разделением identity/validation. HIGH/acceptance — пользователь опроверг вывод об исправлении одним registry path; fixed в SPEC уведомлением Shell, live-client update acceptance и запретом преждевременного runtime PASS.
- Fix and re-review: после финального подтверждения повторно сверены AS-IS, §6.2, S3, матрицы, AC2/AC3/AC5, §10/§14, objections, file plan и журнал. Убрано неподтверждённое обещание отсутствия restart внешнего клиента; добавлены troubleshooting и обязательная проверка fallback. Runtime recovery отделён от будущего code/package acceptance. По code design блокеров не осталось.
- Evidence inspected: executable/version, registry command/icon/owner и отсутствие UserChoice; файлы tests и lifecycle adapters; locator XML; текущая open-card проверка зафиксирована отдельно ниже.
- Depth checklist: scope drift отсутствует; каждый AC имеет evidence plan; unsupported installed/CI claims отсутствуют; legacy/missing/foreign edge cases включены; текущие комментарии/названия resolver подлежат актуализации; новые startup side effects исключены; docs/changelog не требуют изменения сейчас.
- Manual-review challenge: «Не повторяет ли тест выдуманную структуру установки?» — filesystem RED дополняется настоящим installed lifecycle AC5; без него EXEC не PASS.
- No-findings justification: после исправлений нет нерешённых BLOCKER/HIGH/MEDIUM текущей SPEC; каждый выявленный counterexample имеет contract и проверку.
- Needs human: только обычное утверждение SPEC для будущей реализации; подтверждение «Карточка открывается» не является этим approval. Residual risks: конкретный механизм cache не установлен, live-client fallback требует явного evidence; доступность isolated Windows runner и installer проверяется на EXEC.

### EXEC validation evidence (2026-10-19)

Все пути ниже относительно корня репозитория, артефакты ignored и не входят в diff.

| Проверка | Фактический результат | Evidence |
| --- | --- | --- |
| Причинный RED до исправления | 1 expected failure: resolver выбрал отсутствующий root launcher вместо content EXE | `artifacts/protocol-registration/red.log`, `red/*.trx` |
| WindowsTaskProtocolRegistrationTests, Release | 23/23 PASS, 0 skipped | `artifacts/protocol-registration/targeted.log`, `targeted/*.trx` |
| Полный core, Release | 1531/1531 PASS, 0 skipped, exit 0; 31m 33s | `artifacts/protocol-registration/core-full.log`, `core-full/*.trx` |
| Полный Headless, Release | 66/66 PASS, 0 skipped, exit 0 | `artifacts/protocol-registration/headless-full.log`, `headless-full/*.trx` |
| Native TaskDeepLinkFlaUiTests | Повторный run 1/1 PASS, 0 skipped; recorder readiness исправлена и проверена визуально | `artifacts/protocol-registration/native-recording-fixed.log`, `native-recording-fixed/*.trx` |
| Desktop Debug / Release build | Обе PASS, 0 errors; прежний NU1903 по SignalR Common 8.0.8 | `artifacts/protocol-registration/desktop-debug-build.log`, `desktop-release-build.log` |
| FlaUI Release build после всех уточнений installed fixture (evidence path, visible-card, legacy icon) | PASS, 0 errors | `artifacts/protocol-registration/flaui-final-build.log` |
| Installed test local safety guard | 1 skipped, 0 executed, exit 8 при minimum expected 1; это НЕ PASS installed acceptance | `artifacts/protocol-registration/installed-local-guard.log`, `installed-local-guard/*.trx` |
| Packaging YAML / whitespace | YAML разобран; acceptance раньше asset upload, evidence upload с always(), локальный NuGet feed directory создаётся перед restore; `git diff --check` PASS | Локальная static проверка, без запуска CI |

Native video: `tests/Unlimotion.UiTests.FlaUI/bin/Release/net10.0-windows7.0/artifacts/deep-links/native/0597926cd8e54add9d529c194f631ab7/activation-after.mp4`. File exists, 159353 bytes; ffprobe: 8.0s, 2856×1486. Просмотрены извлечённые кадры 3s (`deadlock blocked task 6`) и 6s (`Blocked task 7`); переходы видны. Это синтетический fixture и direct-EXE/IPC flow, не ShellExecute установленного кандидата.

Headless PNG: `tests/Unlimotion.UiTests.Headless/bin/Release/net10.0/artifacts/deep-links/appautomation/bbb56e2ad784443ca2bcf211d108a4a3/`. Просмотрены `opened-filtered-True.png`, `missing-ru.png`, `missing-en.png`: видны карточка/ID и сохранённый фильтр; локализованные ошибки не меняют карточку. ID дополнительно проверен полным текстом видимого control, скриншот отображает усечение по ширине.

### Post-EXEC Review

- Статус: NEEDS-FIX до обязательного isolated installed acceptance (AC5); локальная реализация не объявляется полностью принятой.
- Scope reviewed: утверждённая SPEC; `git status --short`, `git diff --stat`, полный diff registrar/tests/README/workflow и новый `InstalledTaskProtocolFlaUiTests.cs`; locator XML Velopack 0.0.1298; Program lifecycle callbacks; synthetic launch host/config; логи RED/targeted/build/native и inspected кадры видео. Все изменения относятся к задаче; generated artifacts ignored, публикации нет.
- Scope/Evidence pass: причинный filesystem regression сначала падал на выборе root EXE, после исправления targeted 23/23. Полный core 1531/1531, полный Headless 66/66, native 1/1; пропусков нет. Desktop Debug/Release и final FlaUI Release builds без ошибок; прежний NU1903 за пределами scope. Production resolver использует `AppContentDir + ThisExeRelativePath`, не конструирует `current` вручную; до записи проверяет identity, границу content и существование файла. Native direct-EXE run подтверждает обычный cold/warm/minimized flow; не считается installed OS-активацией.
- Contract pass: AC1–AC3 покрыты filesystem/fake registry тестами; AC4 дополнен visible ID assertion в Headless и native window transition evidence. Новая installed fixture проверяет также UIA IsOffscreen=false для title/ID и вызывает реальные Setup/apply/uninstall и ShellExecute, но ещё не исполнялась в допустимой среде. AC1–AC5 на уровне установленного пакета остаются незакрытыми до этого прогона; общий PASS запрещён.
- Adversarial risk pass: проверены missing/empty/rooted/escaping paths, пробелы/кириллица, nested EXE, частичный отказ записи без success notification, удалённый EXE при uninstall, current/legacy owner+command guard и foreign/mismatched preservation. Registration takeover policy не расширена обычным startup; notifier вызывается только после завершённой mutation. Скрытых изменений parser/IPC/task data/layout нет.
- Role-Based pass: domain — нужная карточка определяется ID/title; UX — сохранены фильтры и существующая карточка, fallback документирован; tester — RED причинный, zero tests/skipped не считается PASS; architect — validation отделена от uninstall identity; operations/security — установленный тест opt-in, только GitHub-hosted disposable profile без существующего Unlimotion, пути с GUID и синтетические данные. Это self-review; независимый read-only reviewer не заявляется, scope medium.
- Fix and re-review: первая native-запись визуально показывала одну карточку, хотя UI assertions проходили. Обнаружена задержка компиляции внешнего capture helper. Добавлено ожидание ненулевого video header перед переходами; повторный native run 1/1 и просмотр кадров 3s/6s подтверждают разные карточки. Также evidence новой installed fixture привязано к `GITHUB_WORKSPACE`: TUnit меняет рабочий каталог, и относительный путь потерял бы artifacts при upload. Сверены writer/upload paths и пересобран затронутый FlaUI проект. Следов установки кандидата на личный профиль нет.
- Delivery boundary: Windows packaging job теперь проверяет установленный пакет перед загрузкой бинарных assets в уже опубликованный Release. Workflow по-прежнему запускается на `release.published`, поэтому это не gate самого создания Release и не проверка PR. Workflow/CI не запускались и не публиковались. Baseline пакуется из текущего исправленного приложения с package version 0.0.1; перед настоящим apply искусственно воспроизводятся legacy owner/command/icon. Это проверка восстановления legacy записи callback-ом, а не запуск старого ошибочного бинарника.
- Live-client boundary: новый тест использует один живущий до/после apply внешний ShellExecute caller; Obsidian в нём не установлен. Проверка именно Obsidian после update кандидата и fallback остаётся неподтверждённой; прежний пользовательский restart подтверждает только локальное восстановление релиза 1.33.0. Уведомление Shell не выдаётся за доказанную причину успешного restart.
- Depth checklist: unrelated changes отсутствуют; AC и user objections сверены; executable validation/cleanup и live-client counterexamples рассмотрены; README актуализирован, public API `Unregister` получает optional compatibility argument, старые вызовы сохранены; changelog/version не менялись до согласованного релиза; performance ограничен filesystem path check в fast callbacks; before-video заменён причинным RED и legacy registry JSON, поскольку несуществующий launcher не создаёт окно. After-video получен в native direct-EXE run и не закрывает installed video.
- Manual-review challenge: «Может ли всё быть зелёным, а callback настоящего update всё ещё писать неверный путь?» — да, пока isolated installed run отсутствует. Поэтому отсутствие среды оставлено обязательным blocker, а не accepted-risk.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | UI evidence | Первая запись пропустила переходы при запуске recorder | Дождаться capture и повторно просмотреть кадры | fixed; native 1/1 и разные карточки на кадрах |
| MEDIUM | CI evidence | Относительный output мог попасть в test bin и не загрузиться workflow | Привязать writer к GITHUB_WORKSPACE и сверить upload path | fixed; путь совпадает с artifacts/deep-links/installed |
| MEDIUM | CI restore | На чистом runner отсутствовал каталог local feed из src/nuget.config | Создать artifacts/nuget-local перед dotnet run, как в test workflow | fixed; YAML/run command повторно проверены |
| HIGH | validation / installed lifecycle | AC5 и installed части AC1–AC4 не выполнены | Реальный disposable Windows Setup → seeded legacy → apply → ShellExecute → uninstall, сохранить logs/TRX/UI evidence | blocked: WindowsSandbox.exe отсутствует, Hyper-V Get-VM отклонён политикой авторизации; обход не выполнялся |
| MEDIUM | live-client evidence | Автоматизированный caller не равен Obsidian | На isolated fixture проверить обычный клиент до/после update, при отказе документированный штатный restart | open в границах обязательной installed приёмки |

- No-findings justification: «Нет находок» не заявляется: две обязательные границы validation остаются открытыми. Других in-scope нарушений реализации по просмотренному diff не найдено.
- Stop decision: сохранить реализацию и воспроизводимый test/CI путь; завершение EXEC как PASS заблокировано средой. Пользовательское решение о публикации/установке не подразумевается approval SPEC. Для снятия blocker нужна допустимая изолированная Windows-среда, а не повторение уже зелёных unit/UI прогонов.

## Approval

Получено: «Спеку подтверждаю» 2026-10-19. Разрешена реализация в согласованных границах; push/release/установка новой сборки на личную машину не входят.

Дополнительное разрешение 2026-10-19: «Запушь ветку, создай PR и запусти проверку установки, обновления, удаления и перехода по ссылкам на изолированном Windows runner в GitHub Actions». Разрешены commit/push этой ветки, PR и необходимые CI-запуски; прежнее исключение push/PR/CI для этого этапа снято. Merge, release и установка кандидата на личный компьютер по-прежнему не разрешены.

Для исполнения добавлен `.github/workflows/windows-protocol-acceptance.yml`: `pull_request`/`workflow_dispatch`, GitHub-hosted `windows-2022`, только `contents: read`, тестовые package versions 0.0.1 → 0.0.2, без загрузки в GitHub Release. Проверяется точный head SHA PR. Общий core/Headless workflow остаётся отдельным gate. PR первоначально draft до installed validation. Синтетические UI artifacts и callback logs загружаются всегда; в опубликованной SPEC убраны личный task ID, предмет карточки и дата заметки, локальная полная версия сохранена в ignored evidence.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| Диагностика | Ошибочный root launcher отсутствует, current EXE существует | Установленная 1.33.0, registry readback, registrar исходники | Локальное восстановление и SPEC | Пользователь поручил проверить причину | Read-only evidence в чате |
| Оперативное восстановление | Обновлены только command, DefaultIcon, UnlimotionOwner | Backup `%LOCALAPPDATA%/Unlimotion/protocol-recovery/20261019-123030/unlimotion-before.reg`; `registration-after.json` рядом | Проверить URI в UI | Явное «Исправь регистрацию на моём компьютере» | Внешний HKCU key, backup вне repo |
| Предварительная проверка текущего релиза | Наблюдалась нужная карточка, но вывод о полном восстановлении отозван после пользовательского контрпримера | UIA/PNG совпадали с target, однако сам обычный клик оставался неподтверждённым; позднее пользователь получил «Приложение не найдено» | Проверять событие запуска и обычный клиент | Пользователь сообщил «не работает» и прислал скриншот | Прежний финальный PASS не является acceptance |
| Дополнительная проверка Obsidian | Прямая ссылка найдена в заметке клиента; клик не считаем подтверждённым | Computer Use дважды остановил input с `user input was detected in this window`; позднее карточка сменилась при параллельной работе. ShellExecute проверка выше остаётся подтверждённой | Повторить без параллельного ввода либо явно сохранить эту границу проверки | Запрошено короткое окно без ввода; это не запрос разрешения на уже согласованную правку | Содержание заметки не менялось агентом |
| SPEC / post-SPEC | Expanded medium, review PASS | AC1–AC5 и planned tests, код pending | Представить SPEC | Подготовка SPEC поручена; exact approval пока нет | Только эта SPEC в repo |
| Контрпример / продолжение восстановления | Правильный registry command не завершил восстановление для пользователя | Скриншот ошибки для реального task URI; свежий AssocQueryString возвращает правильный current EXE | Уведомить Shell и повторить обычный клиент | Продолжение исходного разрешённого восстановления | HKCU повторно не переписывался |
| Shell refresh / повторная проверка | Отправлен SHCNE_ASSOCCHANGED; ShellExecuteExW вернул true/error=0 и child PID, визуально видна целевая карточка | Исходная ссылка найдена в Obsidian; итог клика ещё не подтверждён. Пользователю предложено повторить обычный клик после refresh | Принять результат live-client проверки и обновить evidence | Ответ о результате ожидается | SPEC дополнена notifier и live-client acceptance; код не менялся |
| Повторный контрпример | Пользователь подтвердил ту же ошибку после notification; готовность полного решения отозвана | Новый API вызов с null verb успешен, integrity одинаков; WMI trace denied. Стандартный Alt+F4 остановлен защитой concurrent input | Штатный перезапуск клиента в окно без пользовательского ввода; затем обычный клик | Уточнён ответ «Снова ошибка “Приложение не найдено”»; запрошено 20 секунд без ввода | SPEC NEEDS-FIX до закрытия §14; код и новые registry values не менялись |
| Штатный перезапуск клиента | После согласованной паузы Obsidian закрыт Alt+F4 и запущен как установленное приложение | Новый PID=180776, HWND=9572320; та же заметка открыта; принудительный kill не использовался. Клики автоматизации без error dialog не показали надёжную смену двух разных карточек, поэтому PASS не выставлен | Получить результат пользовательского клика после перезапуска | «Да, сейчас не трогаю мышь и клавиатуру»; затем предложена проверка нового состояния | Код не менялся, registry values сохранены |
| Дополнительные исключающие проверки | Различия битности, прав и package registry mapping не подтвердились | Registry32/64 одинаковы; все процессы Medium, package identity отсутствует; NT mapping указывает на реальный SID_Classes. Quoted/unquoted и null/open ShellExecuteEx успешны; это не подтверждение обычного клиента | Сохранить незавершённую границу проверки | Ответ о новом клике ожидается | Только эта SPEC и local-only evidence вне repo |
| Пользовательская приёмка локального восстановления | Обычный клик из перезапущенного Obsidian открывает карточку | Прямой ответ пользователя «Карточка открывается»; registry backup сохранён, код не менялся | Локальное восстановление завершено; подготовленная SPEC ожидает отдельного exact approval | Подтверждён результат проверки, не разрешение на EXEC | Обновлены AS-IS, client restart fallback, AC2, troubleshooting plan и post-SPEC review |
| EXEC / approval и preflight | Создана ветка fix/deep-links-windows-registration от 690d4184; unrelated changes отсутствуют | SDK 10.0.402 доступен; WindowsSandbox.exe отсутствует, Hyper-V Get-VM отклонён политикой авторизации; изолированный installed run локально пока blocked | Причинный regression RED, implementation, обязательные build/test; installed lifecycle готовится для isolated runner | «Спеку подтверждаю» | Эта SPEC, согласованные code/test/docs files |
| EXEC / причинное исправление | Filesystem RED воспроизвёл потерю content directory; resolver переведён на locator content/relative contract | Targeted 23/23, validation до registry writes; legacy/current cleanup требует owner+command и не зависит от существования EXE; Shell notifier после mutation | Полные build/UI/core gates | Scope утверждён | Registrar, regression tests, README |
| EXEC / UI и review corrections | Проверяется ID реально видимого control; ожидание recorder header устранило поздний старт capture | Headless 66/66; native 1/1, ffprobe и разные карточки на кадрах; installed fixture проверяет видимость title/ID | Реальный isolated package lifecycle пока недоступен | Новая личная установка не разрешена и не выполнялась | UI tests, синтетические PNG/video, logs/TRX |
| EXEC / CI preparation | Отдельная opt-in fixture выполняет Setup/apply/uninstall только в пустом disposable GitHub-hosted профиле | Legacy owner/command/icon воспроизводятся перед apply; output закреплён под GITHUB_WORKSPACE, создан local NuGet feed directory; final FlaUI build PASS | Запустить в допустимой isolated среде; общий acceptance до этого NEEDS-FIX | Push/PR/release/запуск CI не подразумеваются approval | Windows packaging workflow, InstalledTaskProtocolFlaUiTests.cs |
| EXEC / финальные локальные gates и post-EXEC review | Все доступные проверки завершены; unrelated changes нет | Core 1531/1531, Headless 66/66, targeted 23/23, native 1/1; Desktop/FlaUI builds PASS, diff whitespace PASS; видео и PNG просмотрены | Снять обязательный installed/live-client validation blocker в изолированной Windows; до этого общий NEEDS-FIX | Новых разрешений на внешние действия не запрашивалось | §19 validation table и post-EXEC review; logs/TRX в ignored artifacts |
| GitHub delivery / дополнительное поручение | Ветка опубликована, создан draft PR #326; отдельный read-only PR workflow запущен на head 3d6bf668 | https://github.com/Kibnet/Unlimotion/pull/326 ; run https://github.com/Kibnet/Unlimotion/actions/runs/39263127273 | Анализировать установленный flow и исправлять найденные in-scope ошибки | Явное поручение push/PR/изолированный Actions run | Тестовые пакеты только в disposable runner, без Release |
| Первый installed run / review finding | Реальный install/registry/readback и UI cold/warm/minimized прошли; затем тестовая обвязка вызвала WaitForExitAsync на Process, закрытом FlaUI | InvalidOperationException: No process is associated with this object; 1 failed test, без общего PASS. PNG cold/warm скачаны; cold PNG просмотрен | Отдельный process lifetime handle для ожидания, повторный isolated run | Исходное поручение проверки сохраняется | Логи/TRX/PNG первого run; FFmpeg отсутствовал, был явный PNG fallback |
| Installed fixture / fix and re-review | Ожидание выхода переведено на независимый process handle; добавлены failure capture, явная cold precondition и ожидаемая OS-ошибка broken legacy URI перед update | Продуктовый resolver не менялся; Shell notification при seed — только тестовая настройка. На runner добавляется FFmpeg для after-video | Сборка затронутого проекта и новый CI run на исправленном head | Повторная проверка входит в разрешённый scope | Installed FlaUI test и PR workflow |
| Второй installed run / уточнение finding | https://github.com/Kibnet/Unlimotion/actions/runs/39263970476 снова упал до update: исходный Process уже освобождён во время GetMainWindow, до Close | В FlaUI 5.0.0 GetMainWindow → WaitWhileMainHandleIsMissing вызывает Dispose и заменяет внутренний Process; первоначальное исправление ожидания было неполным. Cold/warm/minimized прошли, FFmpeg записал видео | Attach по PID создаёт отдельный экземпляр для FlaUI; исходный observer и helper больше с ним не делятся | Продолжение разрешённой проверки | https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.Core/Application.cs ; local build после исправления |
| Третий installed run / cleanup finding | https://github.com/Kibnet/Unlimotion/actions/runs/39264788014 прошёл весь основной сценарий, включая expected legacy URI failure → apply → нужную карточку и удаление registry key | После основного uninstall updater ещё существует до завершения асинхронной самоочистки. Finally повторно вызвал uninstall и получил exit 1; общий run FAIL не скрыт | Сохранять факт успешного uninstall и не повторять cleanup по одному File.Exists | Разрешённая повторная проверка; продуктовый код неизменен | Registry installed/updated JSON, legacy error, updated PNG, видео и TRX третьего run |
