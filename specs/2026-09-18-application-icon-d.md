# Иконка приложения D для всех существующих платформ

## 0. Метаданные
- Профиль: dotnet-desktop-client + ui-automation-testing; context visual-feedback/testing-dotnet.
- Форма: Expanded по центральному templates/specs/_template.md: затрагиваются ресурсы нескольких платформ и packaging config. Риск medium, локальные обратимые изменения, без публикации.
- Владелец визуального решения: пользователь. D выбран явно; фраза «продолжай» запускает подготовку отдельной SPEC, не заменяет exact gate.
- Среда: Codex, Windows/PowerShell, текущий worktree 2b72. Model-specific eval не применим: меняются графические ресурсы.
- Ветка/релиз: текущая ветка, без изменения версий, commit/push/release.

## 1. Цель и outcome contract
Одинаковая узнаваемая иконка D в приложении, пакетах и существующих каналах публикации. Результат: воспроизводимый master, размерные варианты, платформенные контейнеры и подключение в проекты. Завершение после целевых тестов, визуального осмотра и отчёта о недоступных платформенных проверках; публикация не входит.

## 2. AS-IS
- Windows executable: src/Unlimotion.Desktop/Assets/Unlimotion.ico; окно: src/Unlimotion/Assets/Unlimotion.ico, MainWindow.axaml.
- macOS: Desktop/Assets/Unlimotion.icns; ci/osx/generate-osx-app.sh и ForMacBuild.csproj используют этот путь.
- Linux: ForDebianBuild.csproj и ci/deb/unlimotion.desktop сейчас устанавливают ICO в hicolor/48x48; .github/workflows/deb_packaging.yml берёт Android/Icon.png.
- Android: Icon.png подключён AndroidResource как drawable/Icon.png.
- F-Droid: fastlane/metadata/android/{en-US,ru-RU}/images/icon.png, валидатор scripts/test-fdroid-publication.ps1.
- Browser: favicon.ico в wwwroot и AppBundle; в wwwroot/index.html нет явного icon link.
- iOS: проект и Info.plist существуют; Assets.xcassets/AppIcon не найден, конфигурация публикации не готовится этой задачей.
- Dirty tree содержит предыдущую анимацию и её тесты; эти изменения сохраняются.

## 3. Проблема
Одобренный кадр D ещё не связан с ресурсами приложения, а единый уменьшенный bitmap не гарантирует читаемость на маленьких размерах.

## 4. Цели дизайна
Сохранить силуэт, разрывы и холодный свет D; отдельная оптическая адаптация малых размеров; один фиксированный источник, повторяемая генерация и проверяемый inventory.

## 5. Non-Goals
Не менять анимацию, бизнес-логику, IDs пакетов, подписи, сертификаты, версии и данные пользователя. Не публиковать сборки или метаданные в магазины, не добавлять новые платформы/каналы распространения. Не обещать успешную нативную сборку Apple-платформ на Windows.

## 6. TO-BE
### 6.1 Ответственности
- assets/branding: фиксированный исходник D, master PNG с alpha и непрозрачным фоном, документация размеров/provenance.
- tools/scripts генерации: рендер и сборка ICO/ICNS/PNG, проверка полного inventory; не полагаться на C:/Temp после внедрения.
- Платформенные ресурсы/конфигурация: ссылки на сгенерированные варианты, без изменения логики приложения.
- Tests: чтение контейнеров, проверка геометрии/alpha/размеров и UI загрузки реального ресурса.

### 6.2 Детальный дизайн
Источник D: кадр 315, фаза 315/720 = 0.4375 текущего 24-секундного renderer, соответствующий выбранному кандидату. До изменения ресурсов сохранить фиксированный источник геометрии/света либо самостоятельный векторный snapshot, чтобы будущая правка анимации не меняла бренд. Мастер минимум 2048×2048, экспорт из вектора, без растягивания видео.

Desktop: прозрачная квадратная композиция; ICO 16/24/32/48/64/128/256, ICNS стандартные 1x/2x представления до 1024. PNG 16/24/32/48/64/96/128/192/256/512/1024/2048. Linux — настоящие PNG hicolor нужных размеров и согласованный desktop Icon; исключить ICO как единственный Linux источник. Android — density-specific legacy icons и адаптивная композиция с безопасными полями. Store/F-Droid — непрозрачный квадратный вариант на светлом фоне. Browser — ICO, PNG и явные favicon links в реально используемых entry points. iOS — AppIcon asset catalog с непрозрачным store master и привязкой в проекте; подтвердить актуальный формат официальной документацией перед EXEC.

Малые размеры 16–48: допустимы уменьшение числа слабых световых деталей, усиление кромки/главного блика и оптический масштаб без изменения основных контуров. Большие размеры должны визуально совпасть с D. Не добавлять произвольный новый фон/символ. Для неподдерживаемого контейнера/неполной генерации fail-fast, не оставлять частично обновлённый комплект; сначала staging, затем валидированная замена. Runtime рендеринга новой иконки нет: только статические ресурсы.

Visual planning artifact: C:/Temp/unlimotion-icon-candidates/candidates.png и D-master-transparent-2048.png. Во время EXEC сохранить выбранный образец в долговечном источнике проекта. Desktop UI evidence: безопасная запись существующего test host и крупный screenshot иконки; другие OS — статические проверки и честная граница native evidence.

### 6.3 User-Observable Scenarios
| Сценарий | Результат | Evidence/AC |
| --- | --- | --- |
| Открыть Desktop | D в окне/пакете, не старый значок | AC1, UI smoke + screenshot/video |
| Посмотреть favicon/launcher/store | Тот же D в подходящем формате | AC2, inventory и decoded contact sheet |
| Уменьшить до 16–48 | Разрывы и силуэт читаются, нет грязных ореолов | AC3, actual-size light/dark sheet |
| Перегенерировать assets | Тот же набор без зависимости от temp | AC4, repeat run/hash manifest |

### 6.4 State / Interaction Matrix
Статические ресурсы: состояния loading/error/navigation не меняются. Missing/corrupt icon должен быть обнаружен проверкой до передачи. OS icon cache может показывать старую версию; не очищать глобальный кэш автоматически, проверить свежий test host и содержимое пакета.

### 6.5 Decision Ledger
| Решение | Владелец | Выбор | Нужен выбор до EXEC |
| --- | --- | --- | --- |
| Художественная основа | user | D, уже выбран | Нет |
| Малые размеры | agent | оптическая коррекция, без изменения силуэта | Нет |
| Фон | agent | прозрачный desktop; светлый непрозрачный для обязательных store slots | Нет |
| Публикация | user | не входит | Нет |

### 6.6 Runtime / Config / Data Contract
Файлы иконок и project resource references — source of truth. Packaging references обновляются согласованно. IDs/versions/secrets/data не меняются. XML/JSON/plist синтаксис и существование ссылок проверяются автоматически. Нативные platform builds выполняются только при доступном SDK; отсутствие SDK не выдаётся за green.

## 7. Инварианты
Все варианты происходят из D; square canvas, отсутствие обрезанных основных дуг, корректные alpha/opaque варианты; маленький вариант не создаётся простым неконтролируемым downscale.

## 8. Интеграции
Desktop shared WindowIcon + executable + Windows packaging; mac bundle copy; Linux package/desktop entry; Android resources; fastlane; Browser entry points; iOS asset catalog.

## 9. Данные и состояние
Изменений пользовательского состояния/хранилища нет. Только build-time assets/config.

## 10. Rollout / Rollback
Поставка только в рабочее дерево. Откат — обратный scoped diff этой задачи и удаление только добавленных ею ресурсов; не применять reset всего worktree. Сначала staging/export validation, затем подключение.

## 11. Acceptance-to-Test Matrix
| AC | Проверка | Evidence |
| --- | --- | --- |
| AC1 D действительно загружается Desktop | новый/обновлённый Avalonia.Headless icon-resource test; Desktop build | test output, screenshot, test-host video |
| AC2 Все перечисленные потребители подключены | validator: декодирование ICO/ICNS/PNG, inventory, XML/JSON/plist links, alpha | manifest и decoded contact sheet; Apple native check отдельно |
| AC3 Качество каждого масштаба | осмотр 16/24/32/48/64/128/256 и больших на light/dark, мобильных масках | контактный лист; при деградации повторный экспорт |
| AC4 Воспроизводимость | два запуска генератора, размеры и hashes; негативный missing/corrupt fixture | validation log |
| AC5 Нет регрессии загрузки | существующие MainScreenLoadingUiTests | TUnit результат |

Команды: dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug; dotnet test src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -- --treenode-filter '/*/*/ApplicationIconUiTests/*'; отдельно MainScreenLoadingUiTests аналогичным фильтром; scripts/test-fdroid-publication.ps1 после проверки его параметров; новый генератор/валидатор документировать с точной командой. Не запускать многократно полный suite ради бинарных assets; при неизвестном platform SDK сохранить причину и выполнить next-best file/resource проверки.

## 12. Риски / Expected User Review Objections
| Замечание | Предотвращение |
| --- | --- |
| Выглядит не как выбранный D | замороженный snapshot и side-by-side master |
| В маленьком размере всё исчезло | отдельная оптическая адаптация и actual-size sheet |
| На тёмном фоне не видно | light/dark проверка кромки, не менять выбранный дизайн без причины |
| На одной платформе осталась старая иконка | inventory потребителей + проверка контейнеров и resource paths |
| Все платформы объявлены проверенными без сборки | раздельные статусы asset/static/build/native evidence |

## 13. План
Зафиксировать D → генератор и тесты → staging всех размеров → визуальный review → подключение потребителей → scoped builds/UI tests → финальный review. При новой художественной развилке запросить выбор, не подменять D.

## 14. Открытые вопросы
Блокирующих художественных вопросов нет. Нужен exact SPEC approval. SDK availability проверяется в EXEC и ограничивает лишь native validation, не скрывается.

## 15. Профили
dotnet-desktop-client: без runtime нагрузки, build/test; ui-automation-testing + локальный override: icon-resource UI coverage и запуск, selectors неизменны. Expanded из-за межплатформенной конфигурации.

## 16. Файлы
assets/branding + tools/scripts генерации; перечисленные в AS-IS ico/icns/png; Desktop Debian project/desktop entry; Android resources/config; Browser icon links; iOS catalog/project; src/Unlimotion.Test/ApplicationIconUiTests.cs; документация воспроизводимости. CI меняется только если необходима ссылка на новый icon source, не build/release workflow semantics.

## 17. Было → стало
Старые несогласованные resources → единый D; Linux ICO → PNG; Android single drawable → размерные ресурсы; iOS missing catalog → AppIcon; неявный favicon → явная ссылка.

## 18. Альтернативы
Один PNG с автоскейлом проще, но проигрывает на 16px и не закрывает контейнеры платформ. Генерация нового рисунка отвергнута: пользователь уже выбрал конкретный кадр. Встроенная зависимость branding от текущей фазы меняющейся анимации отвергнута: нужен замороженный источник.

## 19. Quality gate / review
SPEC linter: 1 PASS цель §1; 2 PASS проверенный AS-IS §2; 3 PASS проблема §3; 4 PASS дизайн §4; 5 PASS границы §5; 6 PASS ответственности §6.1; 7 PASS интеграции §8; 8 PASS инварианты §7; 9 PASS staging/fail-fast §6.2; 10 PASS static-only §6.2; 11 PASS состояние §9; 12 PASS config compatibility §6.6; 13 PASS scoped rollback §10; 14 PASS AC §11; 15 PASS positive/negative evidence §11; 16 PASS commands/stop §11; 17 PASS plan §13; 18 PASS decisions §6.5/14; 19 PASS Expanded §0; 20 PASS профили §15.

Rubric: цель/границы 5, AS-IS 5, дизайн 5, безопасность 5, проверяемость 5, автономность 5 = 30/30. Оценка относится к готовности плана, не выполненной реализации.

Full post-SPEC self-review: Scope/Evidence — inspected candidate renderer, resource references, project files, iOS plist, browser entry, git status, central QUEST/linter/rubric/review and desktop/UI profiles. Contract — выбранный D не пересогласуется; исходная approval анимации не расширяется на packaging. Adversarial — обнаружены Linux ICO, отсутствие iOS asset catalog, зависимость temp renderer от живой анимации; исправлены планом PNG, asset catalog и freeze snapshot. Fix/re-review — inventory и AC теперь включают эти три случая. Depth: unrelated dirty changes сохраняются; output и negative checks определены; неподтверждённые native claims исключены; docs и rollback заданы; hidden delivery change ограничен icon refs. Manual-review challenge: проверить не только красивые PNG, но и реальные resource references/контейнеры.

Role-based: UX PASS (D + small/light/dark); Tester PASS (AC→checks, corrupt/missing); Developer PASS (static frozen source, reproducible); Delivery PASS (no publishing, native limits); business/domain N/A (логика не меняется). Independent read-only sandbox здесь не подтверждён, поэтому выполнен отдельный adversarial self-review, не объявляем его независимым. Остаточный риск: Apple native packaging требует соответствующей среды. Stop decision PASS для запроса approval; открытых HIGH/MEDIUM findings нет после поправок плана.

### Post-EXEC review — 2026-09-19

Scope/Evidence: просмотрены diff/status, generator + frozen renderer, manifest, source resource references, iOS MSBuild ImageAsset evaluation, Linux Content evaluation, Android APK resources, native contact sheet и первый скриншот окна. Артефакты предыдущей анимации сохранены и не входят в новый diff задачи. Мастер D SHA256 `548729C319CD7A918D9325382293AE2055DACB6E6EA85FE3955786D0F890D52D` совпадает с выбранным кандидатом в C:/Temp. 65 сгенерированных файлов, включая manifest; в manifest 64 entries. Вектор заморожен в tools/Unlimotion.IconGenerator/FrozenIconD.cs, живой renderer не менялся. Малые размеры усилены и упрощены; контактный лист в artifacts/validation/icon-d/contact-sheet.png осмотрен на белом/тёмном и круглой mobile mask.

Contract/AC: AC1 — ApplicationIconUiTests 1/1 (реальный MainWindow, embedded resource matrix/hash); AC2 — decoded ICO/ICNS payloads, PNG, scripts/test-icon-resources.ps1 PASS (64 hashes, iOS slot sizes/opaque RGB, Android reference, browser links, 8 Linux paths, stores); AC3 — actual-size sheet осмотрен, крупный D побитово идентичен выбору; AC4 — повторный --check PASS и --self-test missing/corrupt PASS; AC5 — MainScreenLoadingUiTests 2/2.

Build evidence: Desktop Debug и Browser Debug прошли; Android arm64 Debug прошёл (0 errors, существующие предупреждения native libraries/16KB и API levels); Release Desktop/test host также собраны в evidence workflow. iOS asset catalog включён SDK как ImageAsset и XSAppIconAssets установлен; native iOS/macOS и установка Linux-пакета на целевой ОС НЕ проверены. Полный solution suite не запускался. Валидатор F-Droid -SkipRecipe остановился на старом требовании global.json SDK 10.0.100; отдельно icon metadata/image проверки прошли, SDK проекта ради этого не менялся.

Adversarial findings/fixes:
- MEDIUM, iOS: SDK ReadAppManifest читает XSAppIconAssets из Info.plist, не из одноимённого публичного csproj property. Перенесено в Info.plist; подтверждено локальным Xamarin.Shared.targets и документацией Microsoft, добавлен XPath assertion в validator. Нативная сборка по-прежнему не заявляется.
- MEDIUM, packaging: wildcard RecursiveDir создавал Windows backslashes в LinuxPath. Заменено восемью явными POSIX paths; повторно проверены XML validator и MSBuild evaluation.
- LOW, test harness: Headless WindowIcon.Save — stub, первоначальный тест упал. Заменён на проверку настоящего embedded ICO и window.Icon; повторный UI run PASS. Это не объявляется проверкой нативного encoder.
- LOW, negative checks: Skia бросала ArgumentNullException для мусорного PNG; добавлен явный null codec guard; отрицательный тест PASS.
- LOW, validator: PowerShell byte shift обрезал width 1024; добавлено приведение int; iOS slot validator PASS.
- LOW, maintainability: удалены ненужные lifecycle/animation callbacks из frozen source; повторный hash check подтвердил неизменность ресурсов.

UI video fallback: попытка `scripts/record-task-spaces-evidence.ps1 -Phase After ... -OutputPath C:/Temp/unlimotion-animation-evidence/after-icon-d.mp4` технически не завершилась: Win32 access denied (5) для SendInput и FFmpeg gdigrab. Run `20260918T212127066Z-7c0f797b30ea456a8c54c45308f2417b` и ошибки сохранены. Не обходили desktop restrictions и не объявляем FlaUI green. Next-best evidence: прошедший Headless icon UI test, contact sheet и `artifacts/validation/task-spaces-evidence/20260918T212127066Z-7c0f797b30ea456a8c54c45308f2417b/screenshots/space-a.png` с иконкой в заголовке. Полный recorded A→B→A остаётся недоступен из-за окружения, а не выдаётся за результат.

Role-based self-review: UX — D сохранён, actual-size/masks осмотрены; Developer — runtime не изменён, frozen source/staging decode; Tester — positive/negative + scoped UI/build; Delivery — все icon refs подключены, no publishing, нативные ограничения указаны; Business N/A. Independent sandbox не подтверждён, это adversarial self-review, не независимая проверка. Depth: unrelated changes сохранены; новые runtime/API/data contracts отсутствуют; docs/commands/rollback проверены; скрытых release действий нет. Stop decision: PASS для локальной реализации и доступных checks, с явными ограничениями native Apple/Linux/FlaUI/F-Droid aggregate validation. Не release-ready claim.

## Approval
Пользователь подтвердил точной фразой «Спеку подтверждаю» 2026-09-19. Фаза EXEC.

## 20. Журнал действий агента
| Фаза | Блок | Уверенность | Missing | Next | Human handoff | Actual contact | Причина | Артефакт |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Inventory + выбранный D | 0.98 | native SDK availability | design/review | Нет | пользователь выбрал D и попросил продолжить | новая область packaging | read-only source inspection |
| SPEC | Full review и исправление рисков | 0.95 | exact approval | запросить подтверждение | Да | в финальном ответе | expanded multi-platform scope | эта спецификация |
| EXEC | Approval и frozen D | 1.0 | Нет | генерация | Нет | точная фраза получена 2026-09-19 | выбранный D сохранён побитово | generator/master |
| EXEC | Platform integration | 0.98 | native Apple/Linux run | проверки | Нет | Нет | подключены все указанные потребители | assets/configs |
| EXEC | Validation + fix/re-review | 0.97 | desktop capture access; Apple/Linux environment | handoff | Да | итоговый ответ | 3 UI tests, 3 builds, reproducibility/negative/static PASS; ограничения зафиксированы | tests, contact sheet, logs |
