# Отдельный экран настроек и доступ во время загрузки на main

## Основание и решение пользователя

- 28.10.2026 пользователь уточнил базу реализации: **main**, из `feat/daily-feed` взять только отдельный экран настроек и кнопку открытия; ленту и другие изменения этой ветки не переносить. Ранее пользователь подтвердил реализацию сценария точной фразой «Спеку подтверждаю»; это уточнение исправляет неверно выбранную базу и имеет приоритет над прежним предположением агента.
- Актуальный `origin/main` после fetch: `8f32c950c9bf4e4106bceac7cd4c30e70937de7c`. Работа в отдельном worktree/ветке `fix/settings-access-main`. Предыдущая незакоммиченная реализация на `0ec4d33c` сохранена в `C:/Users/Kibnet/.codex/worktrees/9b6d/Unlimotion` как справочная, её результат и тесты не выдаются за проверки новой базы.
- Источники требований: `C:/Projects/Education/Unlimotion Space/Unlimotion/specs/2026-10-22-settings-access-during-loading.md`, согласованная `2026-10-26-settings-access-during-loading-daily-feed.md` из старого worktree и реализации `GlobalSettingsButton`, `GlobalSettingsOverlay`, команд открытия/закрытия в `feat/daily-feed`.
- Профили: `dotnet-desktop-client`, `ui-automation-testing`, `testing-dotnet`; локальный `AGENTS.override.md` требует UI regression tests. Решение пользователя разрешает локальную реализацию; commit/push/merge/install/release и tracker writes отдельно не разрешены.

## Сценарий и границы

Во время долгой initial task load или переключения пространства пользователь нажимает кнопку настроек в доступной шапке и открывает отдельный экран с единственным существующим `SettingsControl`/`SettingsViewModel`. Видно, что операция продолжается; пользователь может читать настройки, менять оформление, закрыть экран и вернуться к продолжающейся загрузке. Пока источник занят, операции изменения источника и задачи не должны запускаться повторно из UI или прямым вызовом команды. После окончания и при ошибке состояние и доступность обновляются; Ask и recovery сохраняют приоритет. Загрузка не отменяется открытием окна.

На `main` `MainControl` содержит вкладку настроек и шапку, а `MainScreen` перекрывает всю поверхность при загрузке. Экран и команда из daily-feed адаптируются к этой архитектуре: шапка остаётся доступной, task-area закрывается loading barrier, настройки показываются поверх него в рамках существующего `DialogHost`. Старый `SettingsTabItem` заменяется экраном, Git status settings action открывает тот же экран. Используются существующие задачи, источники, строки, тема и UI control из `main`; никакие Feed/quick capture/workspace модели и переключатели из другой ветки не переносятся.

```text
[текущая шапка main] [⚙ настройки] [создать]
-------------------- task-area --------------------
    Загрузка задач…  (не перекрывает кнопку)

               отдельные настройки  [×]
               Загрузка продолжается…
               Оформление: доступно
               Источник: действия временно недоступны
```

## Дизайн и проверки

1. Создать transient `IsSettingsOpen` и команды открытия/закрытия на существующей MainWindowVM; перенести markup кнопки и экрана из daily-feed с адаптацией к main, сохранить `GlobalSettingsButton`, `GlobalSettingsOverlay`, `GlobalSettingsCloseButton` и keyboard/Escape. Сохранить DialogHost ownership, текущую выбранную вкладку и фокус по закрытии.
2. Переместить loading mask в task-area `MainControl` так, чтобы header не был перекрыт; не прятать продолжающуюся операцию, пока Settings поверх неё. Recovery/Ask должны иметь приоритет. Блокировать task creation/shortcuts во время load даже при открытых настройках.
3. Проецировать transient загрузку в SettingsVM; сохранять существующие predicates source-команд, запрещая их прямой Execute и смену источника/путь при загрузке/switch/backup/recovery. Проверять после picker/confirmation актуальность источника, не блокируя уже допущенную операцию её собственным busy-флагом. Appearance не получает общего запрета.
4. UI regression: настоящий pointer нажимает gear во время удержанной загрузки, статус виден, source disabled/appearance enabled; закрытие возвращает barrier; окончания success/error восстанавливают доступность; переключение между task tabs сохраняется; проверка direct Execute, stale callbacks, Ask/recovery и narrow layout. Сравнить реальный single `SettingsControl` и отсутствие Feed в `git diff`.
5. Characterization/RED на main до исправления; затем целевые UI, Settings/TaskSpace/VM tests и desktop build. Обязательные полные core/Headless suites — отдельный gate, не заменяется targeted PASS. Native before/after видео через synthetic task spaces, либо объективный fallback с причиной. Не переиспользовать результаты тестов/видео daily-feed как доказательство для main.

## Приёмка

- AC1: кнопка доступна во время initial load/switch, открывает именно отдельный SettingsControl; старый `SettingsTabItem` отсутствует, Git status открывает тот же экран.
- AC2: при открытых Settings прогресс продолжается; ×/Escape закрывают окно, возвращая прежний task context и активный barrier; по success/error UI актуален.
- AC3: source/task mutation через UI, shortcuts и direct command во время busy не даёт файловых/config/сетевых эффектов; stale picker/Ask не применяет изменения к новому источнику.
- AC4: appearance доступен, source read-only; modal/recovery ownership и существующие selectors не нарушены; при 320/680/1440 DIP окно и закрытие доступны.
- AC5: отсутствуют diff и зависимости по `Feed`, его storage/vault/quick capture/navigation; нет миграции данных. Native evidence, desktop build и обязательные tests зафиксированы на текущей базе. Full-suite отсутствие green означает NEEDS-FIX.

## Риски и rollback

Перемещение loading barrier может оставить активными task shortcuts или header commands, а source command может проверить состояние только до await. Сохранять admission на входе и при возвращении внешнего ответа; не менять исходный storage pipeline и не вызывать Connect повторно. Откат — выборочный revert изменений этой ветки без удаления пользовательских данных. Нативная проверка только с изолированным synthetic space.

## Исполнение и текущие evidence

- `git rev-parse HEAD` — `8f32c950` (`origin/main`); изменённых Feed-файлов нет. Использованы отдельные markup экрана/кнопки и идеи из `feat/daily-feed`, адаптированные к main: существующий `MainControl`, один `SettingsControl`, loading mask в task row и `DialogHost` поверх всего. Git status теперь открывает то же окно; тесты и README capture переключены со старой вкладки на кнопку.
- До кода добавлен regression `SettingsButton_OpensSeparateScreenDuringLoading_AndCloseReturnsToLoading`; RED `D:/CodexBuilds/settings-main-red.log` — на main не найден `GlobalSettingsButton`. В `MainScreenLoadingUiTests` добавлена проверка реального удержанного `Connect`, отказов source-команд, обоих help hosts/recovery/picker и размеров экрана. На предыдущем запуске до уточнения сценария 8/8 PASS (`settings-main-loading-results3`); актуальную полную проверку и размеры не считать одновременно зелёными до завершения full suite.
- `SettingsAdmissionUiTests` на main: **14/14 PASS** (`settings-main-recheck-admission-results`), включая прямые команды создания во время загрузки, отложенное подтверждение удаления/вставки, stale folder picker success/error, пересекающиеся busy, шесть прямых concrete task commands и deferred autosave. `SettingsViewModelTests` **82/82**, `MainWindowViewModelTests` **97/97**, `TaskItemViewModelStatusCommandTests` **44/44**, `MainControlTabsOverflowUiTests` **8/8**, отдельный settings-help test **1/1**. Результаты не подменяют полные suites.
- Headless AppAutomation: полный повтор на исправленной сборке **66/66 PASS** (`D:/CodexBuilds/settings-main-headless-lifecycle-results/`); в том числе settings remote type, task spaces, Git status, смена языка и recovery. Проекты FlaUI, Headless и ReadmeMedia собираются. Desktop build `D:/CodexBuilds/settings-main-native-lifecycle-build.log`: 0 ошибок.
- Responsive: отдельный запуск с Skia **3/3 PASS** (`settings-main-responsive-debug-results`), 320/light/100%, 680/dark/150% и 1440/dark/100%. Просмотрены `D:/CodexBuilds/settings-access-main/responsive-debug/connect-settings-320-light-100.png` и `connect-settings-680-dark-150.png`. 150% — масштаб шрифта приложения, не физический DPI Windows. При запуске вместе с тестами на другом headless builder кадры стали чёрными и три pointer-проверки упали (`settings-main-responsive-class.log`). Функциональные проверки переведены на общий builder класса; повтор класса **11/11 PASS**, включая настоящий клик (`settings-main-recheck-class.log`). Полный повтор остаётся обязательным.
- Native FlaUI с физическим нажатием: **1/1 PASS**, manifest `D:/CodexBuilds/settings-access-main/recordings/20261028T082229392Z-27ef81524f08412892862c452019befb/manifest.json`. Видео `settings-after-physical.mp4` проверено ffprobe: 45 секунд, 1002×540, 30 fps; просмотрен кадр `after-physical-4.png`: отдельные настройки, текущая операция, отключённые поля, ×. После закрытия виден switch и затем Space B. Создан и просмотрен видеосрез до изменения `settings-before.mp4` на архивной сборке чистого `8f32c950`: тест ожидаемо упал из-за отсутствующего `GlobalSettingsButton`, видно переключение пространства без кнопки. Manifest до содержит дополнительную ошибку recorder helper об ожидавшемся screenshot — это следствие того, что сценарий не дошёл до места сохранения кадра. Это repro отсутствующего входа, а не PASS baseline.
- Второй after-video `settings-after-final.mp4` не использовать как визуальное доказательство открытия: automated scenario PASS, но 20-секундная запись пропустила краткое открытие. Финальный physical run задерживает открытое окно на 2 секунды и показан выше.
- После исправления lifecycle выполнены ещё две нативные попытки на новой сборке (`recordings/20261028T093229184Z-70a5ffe96a5149d7b0109531a11b2b80` и `recordings/20261028T093418969Z-b886b90d2f3a4c3ab9f05b1ed29916b3`): обе остановлены `PointerInterferenceException` при выборе Space B до открытия настроек; окно потеряло foreground или цель перекрыта. Их видео не являются proof. Предыдущее успешное видео записано до исправления освобождения подписки и показывает ту же UI-реализацию; окончательная сборка прошла полный Headless.
- Отдельный reviewer работал только чтением в writable sandbox; обнаруженные stale delete/paste, own busy у maintenance/SSH и приоритет двух help overlays исправлены. Targeted code re-review PASS, runtime acceptance определяется тестами и full gates, не review.
- Первый полный core TUnit `D:/CodexBuilds/settings-main-full-core.log` имел 1535 PASS и три responsive failures в старой сборке; остановлен во время длительного benchmark после исправления, его полный итог отсутствует. Первый full AppAutomation Headless (`settings-main-full-headless.log`) дал 61/66 PASS, пять исключений смены языка из-за подписки уже закрытого окна на общий сервис локализации. Подписка снимается при освобождении VM, regression PASS (`settings-main-lifecycle-test-results`); UI loading class на исправленной сборке **11/11 PASS** (`settings-main-lifecycle-ui-results`), полный Headless **66/66 PASS**.
- Полный core на исправленной сборке (`settings-main-full-core-lifecycle.log`) остановлен после 769 PASS и 23 сбоев записи при исчерпании места на C:. Все девять упавших `TaskReloadStorageTests` повторно прошли на D: (`settings-main-disk-retest-results`, 9/9). Повтор `TaskSourceManagerTests` остановлен из-за повторного падения доступного места C: почти до нуля; до остановки 33 теста прошли, в их числе 11 из 14 упавших ранее (`settings-main-disk-source.log`), полный итог класса отсутствует. Не удалялись пользовательские файлы; для полного безопасного повтора необходимо стабильное свободное место на C:. Общий статус **NEEDS-FIX/проверка не завершена**.

## Журнал действий агента

| Событие | Решение / evidence | Следующий шаг |
| --- | --- | --- |
| 28.10: коррекция пользователя | Base — `origin/main` `8f32c950`; из daily-feed только settings screen/button; старый worktree сохранён | Реализовать на чистом worktree после characterization и проверить соответствие scope |
| 28.10: реализация и targeted review | Repro missing gear на main; перенесён один экран, не Feed; защищены source/task commands, stale callbacks и владение input | Targeted и native evidence выше; сначала завершить полные suites и проверить нестабильный responsive class |
| 28.10: полный Headless | Старый SettingsVM после закрытия окна получал уведомление о языке с UI-потока нового окна; исправлено снятие подписки при dispose | Regression PASS и полный повтор 66/66 PASS |
| 28.10: полный core | После 769 PASS место на C: исчерпалось; тесты файлов получили IOException, запуск остановлен | При стабильном свободном месте повторить полный набор; не выдавать выборочные проверки за полный PASS |

## Исправления после ревью PR

- Подтверждение удаления текущей задачи проверяет исходный контекст при ответе и перед каждой операцией; выбор в новом контексте не очищается.
- Изменение языка во время Git-операции обновляет перевод статуса, не сбрасывая её busy-состояние.
- Вставка удерживает собственную блокировку изменений источника до завершения; уже начатый узел заполняется перед проверкой отмены, последующие узлы не создаются после смены контекста. При внезапном изменении состояния ранее созданные узлы могут остаться как частичный результат.
- На исправленной сборке `SettingsAdmissionUiTests` **17/17**, обычные сценарии вставки **5/5**, `MainScreenLoadingUiTests` **12/12**, `SettingsViewModelTests` **83/83**. Первый запуск UI-класса после добавления сценария упал из-за неосвобождённых предыдущих тестовых моделей; фикстура освобождает их, повтор прошёл. Полный AppAutomation Headless **66/66 PASS** на исправленной сборке. Логи и TRX: `D:/CodexBuilds/settings-main-reviewfix-*-results/`. Ранее CI для коммита `9867df3b` прошёл полностью и не является проверкой последующего исправления.
