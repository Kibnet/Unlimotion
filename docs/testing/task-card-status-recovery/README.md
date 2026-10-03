# Восстановление смены статуса из карточки

Проверено на синтетических задачах 2–3 октября 2026 года, Windows x64, .NET SDK 10.0.401, Avalonia 12.0.4, AppAutomation 1.6.0 и TUnit 1.44.0. Установленное приложение и пользовательское хранилище не изменялись. Точная причина индивидуального отказа пользователя не установлена.

Согласованный контракт и post-EXEC review: [SPEC](../../../specs/2026-10-02-task-card-status-recovery.md).

## Текущее размещение: ⚙ → Обновить

По уточнению пользователя от 3 октября «Обновить» находится первым пунктом меню шестерёнки. Отдельной кнопки в заголовке карточки нет. Пункт доступен без ошибки, блокируется во время чтения и для удалённой задачи; команда, доступное имя и стабильный automation ID сохранены.

Чистый baseline `46711e60` запускался с новым recovery-драйвером, без изменений production-кода. Native-сценарий завершился с единственным ожидаемым нарушением `RefreshUnavailable`: обновление было недоступно. Снимок показывает карточку после окончания временного уведомления.

![До: обновление недоступно](before.png)

Текущая карточка с постоянной ошибкой и открытым меню; Skia rendered frame, русская локализация, тёмная тема, ширина 1400:

![Обновить первым пунктом меню шестерёнки](menu-ru-dark-wide.png)

Текущая узкая карточка, ширина 760:

![Русская карточка и меню, тёмная тема, ширина 760](ru-dark-narrow.png)

Все восемь актуальных кадров RU/EN × Light/Dark × 1400/760 просмотрены. Тесты требуют непустой кадр с различающимися пикселями и проверяют локализованный menu item. Это rendered evidence; новый native-прогон меню не завершён.

## Проверки последнего размещения

| Проверка | Результат | Evidence |
| --- | --- | --- |
| Menu placement, actual binding, accessible name, busy | RED на прежней отдельной кнопке → GREEN 1/1 | [RED](menu-action-red.log), [GREEN](menu-action.log) |
| Полный Headless suite после усиления recovery assertion | 52/52 PASS, 0 SKIP, 3м09с | [menu-headless-full.log](menu-headless-full.log) |
| Recovery: отказ → ⚙ → Обновить → явный повтор | PASS внутри full Headless; `FlowCompleted=true`, `FailureIds=[]`; перед retry ошибка исчезает, JSON содержит NotReady и одну новую запись истории | [observations](menu-headless-observations.json), [suite log](menu-headless-full.log) |
| Rendered RU/EN/theme/width matrix | 8/8 PASS, 1м01с | [menu-rendered-matrix.log](menu-rendered-matrix.log) |
| Удаление открытой dirty-карточки во время чтения | 1/1 PASS; menu item disabled, текст можно скопировать, файл не восстанавливается | [menu-missing-card.log](menu-missing-card.log) |
| Desktop и phone layout с полным набором пунктов ⚙ | 4/4 PASS: desktop 1, phone 3 | [desktop](menu-layout-desktop.log), [phone](menu-layout-phone.log) |
| Обычная Desktop-сборка с пунктом меню | PASS, 0 предупреждений / 0 ошибок | [menu-desktop-build.log](menu-desktop-build.log) |
| Последняя компиляция FlaUI adapter | PASS, 0 ошибок; 3 существующих предупреждения в ServerStorage/TaskStorageBuilder/ConflictResolutionControl | [menu-flaui-build.log](menu-flaui-build.log) |
| Native FlaUI последнего меню | Не подтверждён: `SendInput` получил Win32 Access denied при первом status click, до открытия ⚙. Текущий Windows-сеанс отключён/заблокирован | [menu-native-blocked.log](menu-native-blocked.log) |

Recovery-тест теперь ждёт одновременно enabled status и исчезновения постоянной ошибки. Доступность status сама по себе не доказывает выполнение Reload: после StorageFailed она уже восстановлена. Adversarial source re-review подтвердил исправление этой проверки. Headless использует существующий repository fallback для detached flyout bindings; actual MainControl test отдельно проверяет привязку MenuItem к ReloadTaskCommand.

## Предыдущие проверки recovery до переноса в меню

Эти результаты относятся к исходному исправлению с отдельной кнопкой в commit `11808873`; они не подтверждают native-путь последнего меню.

| Проверка | Результат | Evidence |
| --- | --- | --- |
| Native FlaUI recovery с отдельной кнопкой | 1/1 PASS, `FlowCompleted=true`, `FailureIds=[]`; JSON NotReady и ровно одна новая запись истории; Reload не менял файл | [flaui.log](flaui.log), [ошибка](after-error.png), [после повтора](after-retry.png) |
| Весь класс status/reload ViewModel | 41/41 PASS; включая два TaskNotFound race cases | [viewmodel.log](viewmodel.log) |
| Полный Main suite 2026-10-02 | 1193 PASS / 1 FAIL / 0 SKIP; 32м34с; до последнего раннего Missing guard | [main-full-excerpt.log](main-full-excerpt.log) |
| Упавший Workspace executable spec отдельно | 1/1 PASS; причину сбоя в общей серии это не устанавливает | [workspace-focused.log](workspace-focused.log) |

После status command TaskNotFound применяется до editor drain, чтобы локальные правки не создали удалённую задачу заново. Два deterministic race cases дали RED до исправления и GREEN после; последний VM-класс прошёл 41/41. Full Headless также проверяет исправленный startup/handoff тестовых сессий до и после recovery.

Логи сохраняют результаты прогонов; абсолютный корень рабочей копии заменён на `<worktree>`. Main-файл — явно обозначенная выдержка с ошибкой и итогом. Полные локальные HTML/TRX, дампы и остальные снимки находятся в `artifacts/status-recovery/` implementation worktree.

## Воспроизведение

После сборки соответствующего проекта, из корня checkout:

```powershell
dotnet build src/Unlimotion/Unlimotion.csproj --no-restore
dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj --no-restore
& './tests/Unlimotion.UiTests.Headless/bin/Debug/net10.0/Unlimotion.UiTests.Headless.exe' --maximum-parallel-tests 1 --report-trx
```

Main запускается из `src/Unlimotion.Test/bin/Debug/net10.0`, поскольку fixtures используют относительные пути. Для native recovery нужен активный разблокированный Windows desktop:

```powershell
dotnet build tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj --no-restore
& './tests/Unlimotion.UiTests.FlaUI/bin/Debug/net10.0-windows7.0/Unlimotion.UiTests.FlaUI.exe' --treenode-filter '/*/*/MainWindowFlaUiTests/TaskCardStatusRecovery_FailureRefreshRetry' --maximum-parallel-tests 1 --report-trx
```

## Video fallback

Проверенная пара before/after MP4 не получена: recording не прошёл проверку средней частоты кадров, ожидание готовности baseline либо закончился ранним выходом ffmpeg. Неудачные MP4 не представлены как доказательство успешного flow. Предусмотренный SPEC fallback для исходного исправления: чистый baseline RED, native GREEN с persisted read-back, просмотренные снимки и rendered matrix. Для последнего размещения добавлены актуальные menu frames и полный Headless; прежний native GREEN не переносится на новое меню. Исходные recording-логи сохранены локально в `artifacts/status-recovery/`, включая `baseline-diagnostics/`.

## Остаток до ready for review

Подтвердить native recovery последнего меню в активном Windows-сеансе. Разобрать сбой Workspace executable spec в полной Main-серии и подтвердить полный Main gate с последним Missing guard. Успешные focused-прогоны не заменяют эти проверки; общий post-EXEC verdict — NEEDS-FIX, PR остаётся draft.
