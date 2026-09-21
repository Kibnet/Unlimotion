# Иконка E: круглые петли и белая подложка

## Контекст

- Владелец визуального решения: пользователь; исполнитель: агент. Профиль dotnet-desktop-client, целевой UI smoke по ui-automation-testing.
- Short по центральному `_template-small.md`: одна обратимая замена статического оформления через уже существующий генератор и inventory. Новых платформ, packaging/config/API/storage изменений нет; число генерируемых ресурсов не означает отдельные функциональные изменения. Если потребуется менять платформенные контракты, остановиться и пересмотреть scope.
- AS-IS: рабочее дерево чистое, ветка `feat/loading-animation-brand-icon`; текущие иконки — графитовый D. Генератор `tools/Unlimotion.IconGenerator`, inventory `assets/branding/manifest.json`, валидатор `scripts/test-icon-resources.ps1`. Оба README используют `assets/branding/png/unlimotion-512.png`. Существует `ApplicationIconUiTests.MainWindow_LoadsPackagedDIcon`.
- Выбранный образец: `C:/Users/Kibnet/.codex/visualizations/2026/09/17/01a0af54-2bee-7c23-b184-8fe9fe05e310/infinity-underlay/round-v2/exports/variant-E-1024.png`.
- SHA-256 образца: `8DBA6986663B694E2D412EFEF900AFD8E3A7FCC512CE49CC965A10E7F1C54727`. Векторный источник рядом: `round-v2/FrozenIconD.cs`; сравнение `round-v2/exports/comparison.png`. Это local-only planning artifacts, при реализации источник переносится в репозиторий без зависимости от внешнего пути.

## Пять содержательных проверок

| Проверка | Решение / ожидаемый результат | План проверки → фактическое evidence |
| --- | --- | --- |
| 1. Результат / границы | E во всех уже существующих слотах Windows, Linux, macOS, iOS, Android, Browser, F-Droid и обоих README. Чёрные дорожки, одинаковые круглые петли, чёткая белая бесконечность с выносом 20 единиц макета, прежний характер сине-фиолетовых бликов. Анимацию, загрузку задач, данные, версии, конфигурацию публикации не менять. | Изменяются фиксированный источник/параметры генератора, ресурсы его inventory, branding docs и релевантный icon UI test. README получают E по существующей ссылке, без ненужной правки текста. |
| 2. Решения / разрешения | Пользователь выбрал E и подтвердил применение ко всем иконкам и README с исключением анимации. Геометрия образца: центры (150,125)/(330,125), радиус дорожек 80, наружный радиус белой подложки 123, круглые отверстия радиуса 37. Вынос 20 — не фиксированные экранные px. Перенести зафиксированный источник E; существующие имена путей D допустимо оставить ради совместимости, документация должна объяснять актуальный вариант. | Exact approval ожидается ниже. Commit/push/PR/release/отправка поста не разрешены этим scope. Открытых художественных вопросов нет. |
| 3. AC→evidence | AC1: master совпадает с одобренным E по силуэту, чёрному цвету, круглым отверстиям и бликам. AC2: все текущие PNG/ICO/ICNS и платформенные слоты обновлены, alpha/opaque контракты сохранены. AC3: 16–64 px и большие размеры не обрезаны; оба README показывают E. AC4: повторная генерация воспроизводима; missing/corrupt fixtures отклоняются. AC5: окно загружает новый упакованный значок; код анимации неизменён. | Сопоставление 1024 px с образцом, светлый/тёмный контактный лист и мобильные маски; generator `--check --self-test --preview`, `pwsh -File scripts/test-icon-resources.ps1`; обновить/запустить `ApplicationIconUiTests`, регрессионные `MainScreenLoadingUiTests`, Desktop build. UI тесты запускать TUnit `--treenode-filter`. Проверить diff анимации пустой. Видео безопасного тестового окна при доступном recorder; при технической невозможности зафиксировать причину и screenshots/Headless/resource evidence. Нативные Apple/Android/Linux launcher проверки не подменять статическими. |
| 4. Существенный риск / rollback | Старое увеличение малого значка 110% может обрезать широкую E: не переносить его автоматически. Базовый масштаб E — 100%, как в одобренном листе; разрешена только необходимая размерная адаптация бликов и безопасных полей, не новая художественная форма. Слишком узкие отверстия/обрезанная подложка — дефект. Кэш ОС может показывать D. | Проверять реальные 16/24/32/48/64 px и край alpha, adaptive masks, свежий test host. Не чистить глобальный кэш. Сначала рендер/декодирование комплекта, затем замена; откат только собственного diff, не reset дерева. |
| 5. Findings / disposition / stop | Проверены существующий generator/inventory contract, branding README, ссылки обоих README, наличие icon UI coverage и файл выбранного E. На review выявлен риск старого 110% масштаба; устранён в плане базовым масштабом E и проверкой краёв. Остальных блокирующих находок SPEC нет. | До EXEC фактические build/test результаты отсутствуют. При несовпадении с E, stale ресурсах или падении целевых UI тестов не заявлять завершение. Не исправлять посторонние сбои без согласования. |

## Quality gate и review

- Post-SPEC: PASS для запроса approval. Пять критериев проверены; объём ограничен существующим статическим комплектом, художественный эталон зафиксирован hash, размерные риски и UI/resource evidence предусмотрены. Нативная проверка всех ОС не обещается. Результат не зависит от изменения живой анимации.
- Post-EXEC: PASS. Проверены diff генератора/теста/docs, полный inventory, контактный лист и кадр нативного окна. Геометрия/цвет совпадают с E; PNG 1024 имеет тот же SHA-256, что эталон. Старое увеличение 110% удалено; test проверяет все четыре края каждой ICO-картинки. Убрано неиспользуемое поле тени. Анимация и runtime-код не изменены; оба README используют обновлённый ресурс. Незапрошенных изменений нет.

### Фактическая проверка EXEC

- `dotnet run --project tools/Unlimotion.IconGenerator -- .` — 65 ресурсов с manifest; PNG/ICO/ICNS декодированы до записи.
- `dotnet run --project tools/Unlimotion.IconGenerator -- . --check --self-test --preview` — PASS; negative missing/corrupt fixtures PASS. После форматирования и удаления неиспользуемой тени `--check` повторно PASS.
- `pwsh -File scripts/test-icon-resources.ps1` — PASS: 64 hash, iOS opaque RGB/slots, Browser links, Android adaptive reference, Linux paths, store icons.
- Desktop Debug build — PASS, 0 ошибок; имеются существующие compiler warnings и предупреждения LF/CRLF. FlaUI Release host build — PASS.
- `dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj --no-restore -- --treenode-filter "/*/*/ApplicationIconUiTests/*" --maximum-parallel-tests 1 --minimum-expected-tests 1 --report-trx --results-directory=artifacts/validation/icon-e/icon-tests-final` — 1/1 PASS после финального усиления edge assertions.
- MainScreenLoadingUiTests с отдельным фильтром `/*/*/MainScreenLoadingUiTests/*` — 4/4 PASS, отчёты `artifacts/validation/icon-e/loading-tests`. Первый объединённый OR-фильтр выбрал 0 тестов; это ошибка выбора, не успешная проверка; исправлена отдельными запусками классов.
- `pwsh -File scripts/record-task-spaces-evidence.ps1 -Phase After -RecorderScriptPath C:/Users/Kibnet/.codex/skills/record-app-screen/scripts/record_app_window.ps1 -OutputPath artifacts/validation/icon-e/after.mp4 -DurationSeconds 30` — native FlaUI A→B→A PASS, TestExitCode 0, ScenarioSucceeded true. Запись Captured, ffprobe: 30 секунд, 1002×540, 336942 байта. Кадр 3 секунды осмотрен: тестовое окно, значок в title bar, прежняя анимация. Manifest: `artifacts/validation/task-spaces-evidence/20260921T142035819Z-540edf21b05947e2bd32de000a038cb0/manifest.json`.
- Visual evidence local-only: `artifacts/validation/icon-d/contact-sheet.png`, `artifacts/validation/icon-e/after.mp4`, `artifacts/validation/icon-e/video-frame.png`. Осмотрены light/dark, 16–128 px, мобильные маски. Это выбранное новое оформление, не исправление пользовательского flow; исходные D-ресурсы сохранены в Git, отдельная baseline-видеозапись этой итерации не делалась.
- `git diff --check` — PASS. Полный main suite не запускался: изменены статические ресурсы и независимый build-time renderer, выполнены целевые UI и native smoke. Apple/Linux/Android установка и launcher на целевых ОС не проверены; ресурсные проверки не выдаются за native validation. Публикация не выполнена.

## Approval

### Уточнение после визуального review: плотность B

Пользователь выбрал B и поручил «Применяй». Это размерная адаптация того же E
в ранее утверждённом scope, без изменения формы/анимации/публикации; новый exact gate
не требуется. Для app icons холст 444 вместо 480, поля по 9 единиц вокруг силуэта
426; масштаб +8,1%, без поворота. Adaptive сохраняет свой безопасный коэффициент 0.69.
README остаётся визуально прежним: отдельный generated `readme-logo-512.png` на холсте 480.
Это уточнение заменяет требование 100% масштаба для app icons в таблице выше.
Проверки: сравнение с B, сохранение hash прежнего README PNG, generator check/validator,
обновлённый icon UI test (заполнение >240 из 256 px, субпиксельные края без непрозрачного обрезания).

Post-EXEC B: generator `--check --self-test --preview` PASS, 66 ресурсов;
resource validator PASS, 65 hashes. ApplicationIconUiTests 1/1 PASS (TRX/HTML:
`artifacts/validation/icon-b/tests`). PNG 1024 совпадает с выбранным B по SHA-256
`0776BD855E355CC7BC45900DF414D83E71F19BF2194DDBD8944152DF9D78120B`.
Контактный лист осмотрен: края сохранены, круги не растянуты. README отделён от
компактной версии через `CompactCanvas=false`; для него сохранены геометрия,
масштаб и свет E. Видео A→B→A выше относится к предыдущему масштабу E, не B;
новая запись не делалась, пользовательский flow и анимация не изменены.
Review: PASS для размерного уточнения; packaging contracts и данные не затронуты.

Delivery preflight: ветка перебазирована без конфликтов на актуальный на момент
проверки `origin/main` `5cc31dd9`; поверх main остался один commit. После rebase
generator check/self-test/preview и resource validator повторно PASS; Desktop build
PASS после обязательного restore новых зависимостей main; icon UI 1/1 и loading UI
4/4 PASS (`artifacts/validation/icon-b/post-rebase-{icon,loading}`). Первый build
с `--no-restore` ожидаемо обнаружил stale package assets (`UseAtomicWrites` отсутствовал),
после `dotnet restore` повтор прошёл; продуктовый код для этого не менялся.

Получено «Спеку подтверждаю». EXEC разрешён в указанном scope, без публикации.

## Журнал действий агента

| Фаза / событие | Решение | Evidence / остаток | Следующий шаг | Фактическое решение человека |
| --- | --- | --- | --- | --- |
| SPEC, 2026-09-21 | Подготовлена замена D → E во всех существующих статических ресурсах; анимация исключена | Проверены preview E, текущий генератор, ссылки README и UI test; код/ресурсы не менялись | Exact approval, затем реализация и проверки | «Мне нравиться E», затем «Да» на применение ко всем размерам и README |
| EXEC, 2026-09-21 | Перенос E в независимый генератор | Реализация и проверки начаты | Обновить ресурсы и UI test | «Спеку подтверждаю» |
| EXEC завершён, 2026-09-21 | E применён ко всему существующему inventory и README через общий ресурс | Master hash совпал; validator/reproducibility/build/UI 1+4/native 1 PASS; видео осмотрено | Передать локальный результат, без commit/push | Дополнительных решений не требовалось |
| EXEC уточнение B, 2026-09-21 | Уменьшены поля app icons, README сохранён отдельным ресурсом | B hash совпал; generator/validator/icon UI PASS | Передать локальный результат без публикации | «B», затем «Применяй» |
| DELIVERY, 2026-09-21 | Подготовить PR от актуального main | Rebase на `5cc31dd9`; post-rebase build и UI 1+4 PASS | Self-review, push, PR | «Оформи PR от актуального мейна» |
