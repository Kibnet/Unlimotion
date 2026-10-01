# Поддержка `unlimotion://task/{id}` в Linux, macOS и Android

## 0. Метаданные

- Тип (профиль): `delivery-task`; `dotnet-desktop-client` + `ui-automation-testing` + `ui-feature-parity`; контекст `testing-dotnet`.
- Владелец: Павел — product owner; Codex — проектирование, реализация и локальная проверка после approval.
- Масштаб: medium, форма **Expanded**. Меняется публичный OS-протокол и packaging/runtime activation сразу в трёх платформах.
- Целевое семейство / behavior baseline: текущая Avalonia 12 / .NET 10 архитектура Unlimotion.
- Поверхность: Codex desktop, локальный Windows worktree. Нативные Linux/macOS/Android runtime smoke недоступны на текущем хосте и отделены от проверяемой здесь реализации.
- Effective runtime: GPT-6 Astra в Codex; на контракт приложения не влияет.
- Eval baseline / evidence: существующий Windows flow и тесты `TaskDeepLink*`, `MainWindowViewModelTests`, Headless navigation; после реализации — platform contract tests, затронутые builds, UI test и полный `Unlimotion.Test` suite.
- Целевой релиз / ветка: `feat/daily-feed`; commit/push/release этой SPEC не разрешены автоматически.
- Ограничения: в фазе SPEC меняется только этот файл. Код — только после точной фразы «Спеку подтверждаю».
- Связанные ссылки: `specs/2026-09-22-task-deep-link-protocol.md`; официальные контракты Avalonia `IActivatableLifetime`, Android intent filters, Apple `CFBundleURLTypes`, freedesktop Desktop Entry Specification.

## 1. Overview / Цель

Расширить уже реализованное открытие задач по ссылкам `unlimotion://task/{id}` с Windows на Linux, macOS и Android без изменения формата ссылок и без копирования навигационной логики.

Outcome contract:

- Исходное поручение / симптом и точка применения результата: ссылки работают во внешнем Obsidian на Windows; пользователь выбрал пункты 1 и 2 — Linux + macOS desktop и Android.
- Success means: установленная сборка соответствующей платформы объявляет схему `unlimotion`; холодная и повторная активация доставляют только валидный URI в общий pipeline; Unlimotion открывает существующую карточку задачи активного пространства.
- Итоговый артефакт / output: platform adapters и package metadata, автоматические contract/unit/UI tests, проверяемые команды сборки.
- Stop rules: не добавлять iOS/browser/HTTPS Universal Links; не публиковать пакеты; не заявлять native runtime PASS без запуска на соответствующей ОС/устройстве.

## 2. Текущее состояние (AS-IS)

- `TaskDeepLink` в общем проекте строго разбирает только `unlimotion://task/{safe-id}`.
- `App` умеет поставить ссылку в очередь до готовности repository и затем открыть задачу через `MainWindowViewModel.TryOpenTaskById`.
- `Unlimotion.Desktop.Program` принимает URI из аргументов, а `TaskDeepLinkActivationBroker` передаёт повторный Windows-запуск первому процессу через named pipe.
- Windows регистрирует протокол per-user через Velopack lifecycle.
- Linux DEB уже устанавливает `unlimotion.desktop`, но без `%u` и `x-scheme-handler/unlimotion`.
- macOS `.app` уже собирается с `Info.plist`, но `CFBundleURLTypes` отсутствует.
- Android `MainActivity` уже имеет `LaunchMode.SingleTask`; manifest не объявляет intent filter для `unlimotion`.
- Avalonia 12 предоставляет `IActivatableLifetime` и `ProtocolActivatedEventArgs` для macOS/Android. Подписка должна происходить синхронно в начале `OnFrameworkInitializationCompleted`, иначе cold-start событие может быть пропущено.
- Текущий репозиторий имеет build/packaging workflows для Linux, macOS и Android, но текущий Windows-хост не доказывает нативную установку и щелчок из Obsidian на этих платформах.

## 3. Проблема

Сами Markdown-ссылки кроссплатформенны, но Linux, macOS и Android не объявляют Unlimotion системным обработчиком схемы и не передают OS activation в уже существующий общий pipeline.

## 4. Цели дизайна

- Сохранить один формат URI и один строгий parser.
- Отделить OS registration/activation от навигации и task storage.
- Поддержать cold start и warm/repeated activation без потери ссылки.
- Не открыть через URI команды изменения или удаления данных.
- Сохранить Windows-поведение и обычный multi-instance запуск без URI.
- Дать тестируемый контракт даже там, где нативная ОС недоступна на текущем хосте.

## 5. Non-Goals

- iOS, browser build, HTTPS App Links / Universal Links.
- Поиск задачи по всем пространствам: ссылка по-прежнему разрешается только в активном task space.
- Новый экран, диалог или визуальный дизайн.
- Автоматическая публикация DEB/macOS package/APK либо изменение пользовательской системы вне тестовых процессов.
- Гарантированное принудительное поднятие окна поверх других окон в Wayland: compositor может запретить focus stealing; состояние задачи всё равно обязано обновиться в существующем окне.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

- `TaskDeepLink`: неизменный канонический parser/security boundary.
- `App`: ранняя подписка на Avalonia protocol activation, очередь до готовности ViewModel и единая навигация.
- `Program` + `TaskDeepLinkActivationBroker`: Windows/Linux URI из process arguments и передача повторного URI первому desktop-процессу.
- Linux `.desktop`: объявление scheme handler и передача одного URI через `%u` публичному launcher.
- macOS `Info.plist`: `CFBundleURLTypes` для `unlimotion`; доставка через Avalonia `IActivatableLifetime`.
- Android `MainActivity`: `ACTION_VIEW` intent filter с `DEFAULT`/`BROWSABLE`, scheme `unlimotion`, host `task`; доставка cold/warm URI базовым Avalonia activity в `IActivatableLifetime`.
- Tests: parser/navigation не дублируются; добавляются adapter, manifest/package contract и user-flow проверки.

### 6.2 Детальный дизайн

#### Общий activation adapter

1. В начале `App.OnFrameworkInitializationCompleted`, до `await` и до `base.OnFrameworkInitializationCompleted`, приложение получает `IActivatableLifetime`.
2. Для macOS и Android подписывается на `Activated`.
3. При `ProtocolActivatedEventArgs` с `ActivationKind.OpenUri` URI передаётся в `TaskDeepLink.TryParse`.
4. Валидная ссылка проходит через существующий `QueueOrActivateTaskDeepLink`; невалидная игнорируется без side effect.
5. Handler снимается при завершении controlled lifetime, чтобы не удерживать старый `App` в тестовых/повторных сессиях.
6. Windows/Linux не используют этот путь как основной, чтобы один OS launch не был обработан дважды: URI для них приходит аргументом в `Program`.

#### Linux

- `unlimotion.desktop` получает `MimeType=x-scheme-handler/unlimotion;`.
- `Exec` использует установленный стабильный launcher `/usr/bin/Unlimotion %u`, который уже передаёт `"$@"` в `/usr/local/bin/Unlimotion.Desktop`.
- `%u` — отдельный аргумент, без ручного shell quoting и без интерпретации URI как команды.
- Cold start открывает задачу после инициализации repository.
- При уже запущенном приложении новый процесс передаёт ссылку owner-процессу через broker и завершается.
- Broker использует Windows namespace prefix `Local\` только на Windows; Unix получает переносимое имя. Named pipe остаётся bounded и принимает только URI, повторно проверяемый `TaskDeepLink.TryParse`.
- Главное окно восстанавливается и активируется best effort. На Wayland selection/card state является обязательным результатом, принудительный foreground — ограничение compositor и не является ложным AC.

#### macOS

- В packaging `Info.plist` добавляется `CFBundleURLTypes` с уникальным `CFBundleURLName` и `CFBundleURLSchemes = [unlimotion]`.
- LaunchServices доставляет custom scheme в `.app`; Avalonia преобразует его в protocol activation.
- `Program` не должен дополнительно обрабатывать URI-аргумент на macOS, если activation уже доставляется lifetime, чтобы избежать двойного открытия.
- Одинаковый handler работает для cold start и повторного щелчка в уже запущенном приложении.

#### Android

- На `MainActivity` добавляется intent filter `Intent.ActionView`, категории `Default` и `Browsable`, `DataScheme=unlimotion`, `DataHost=task`.
- Сохраняется `LaunchMode.SingleTask`: cold intent создаёт activity, warm intent приходит в существующую activity через `OnNewIntent` базового Avalonia класса.
- Ручной дублирующий разбор `Intent.Data` в `MainActivity` не добавляется, пока Avalonia 12 передаёт protocol activation: источник должен быть один.
- F-Droid и обычная APK используют одинаковый manifest contract.

#### Ошибки и безопасность

- Невалидные scheme/host/path/query/fragment/ID не вызывают навигацию.
- Отсутствующая задача показывает существующую локализованную ошибку и не меняет текущий выбор.
- Protocol metadata не даёт внешней ссылке возможностей кроме открытия существующей задачи.
- Ошибка desktop IPC приводит к сохранению cold-start fallback, а не к потере ссылки.

#### Производительность

- Parser и один event handler дают пренебрежимо малую стоимость.
- Никакого поиска по нескольким пространствам, сети или повторной индексации не добавляется.

#### Visual planning artifact

Новый layout отсутствует. Эквивалентный state-flow artifact:

```text
Obsidian / другое приложение
  -> OS handler (desktop entry / CFBundleURLTypes / Android intent filter)
  -> strict TaskDeepLink parser
  -> repository не готов? queue : navigate now
  -> режим «Задачи»
  -> выбранная задача
  -> открытая существующая карточка
```

UI video evidence: native Linux/macOS/Android recorder отсутствует в текущем Windows harness. Fallback — существующий Avalonia Headless user-flow test, contract tests package metadata и команды native smoke ниже. Перед релизным заявлением для каждой платформы требуется отдельный native smoke; он не подменяется локальным видео.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Linux cold | Щёлкнуть ссылку при закрытом Unlimotion | Запускается приложение, открывается карточка задачи | contract/build + native `xdg-open` smoke | AC1, AC4, AC7 |
| Linux warm | Щёлкнуть ссылку при запущенном Unlimotion | Существующий процесс выбирает задачу; окно активируется best effort | broker test + native smoke | AC2, AC4 |
| macOS cold/warm | Щёлкнуть ссылку из Obsidian | `.app` запускается/активируется и открывает карточку ровно один раз | plist contract + activation/UI test + native `open` smoke | AC1, AC3, AC4 |
| Android cold/warm | Щёлкнуть ссылку из заметки/ADB | Activity запускается/возвращается на передний план и открывает карточку | manifest contract + activation/UI test + device/emulator smoke | AC1, AC3, AC4 |
| Ошибка | Открыть невалидный URI или неизвестный ID | Команда не исполняется; неизвестный ID даёт локализованную ошибку | parser/ViewModel regression tests | AC5, AC6 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| App закрыто | Валидный URI | OS запускает shell, URI ждёт готовности repository, открывается task card | Repository error показывает существующую storage error | URI не теряется до connect |
| App запущено | Валидный URI | Сразу выбрать задачу, открыть details, восстановить/активировать окно | Linux Wayland focus best effort | Повторный пользовательский click после завершения предыдущего допустим |
| Startup выполняется | Второй валидный URI | Оба запроса обрабатываются в порядке доставки | Queue не блокирует UI | Итоговая выбранная задача — последняя доставленная |
| Любое | Невалидный URI | Нет перехода и side effect | Parser reject | Никаких произвольных команд |
| Любое | Валидный URI неизвестной задачи | Текущий выбор сохраняется, локализованная ошибка | Только активное пространство | Без скрытого переключения пространств |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope | user | Linux + macOS + Android; без iOS/browser | 1.0 | Лишнее расширение | Нет |
| URI format | agent, ранее approved | Сохранить `unlimotion://task/{id}` | 1.0 | Миграция ссылок | Нет |
| macOS/Android delivery | agent | Avalonia `IActivatableLifetime`, без дублирующего native parser | 0.9 | Возможное событие будет пропущено при поздней подписке | Нет; ранняя подписка и tests обязательны |
| Linux warm process | agent | Переиспользовать hardened broker | 0.85 | Unix-specific mutex/pipe несовместимость | Нет; platform-neutral name + Linux build/runtime smoke |
| Android link type | agent | Custom scheme, не verified App Link | 1.0 | Другой app может объявить ту же scheme | Нет; соответствует существующему формату, HTTPS вынесен за scope |
| Wayland foreground | agent | Best effort, task state обязателен | 0.9 | Окно может не выйти поверх другого | Нет; ограничение платформы явно сообщается |
| Native runtime evidence | agent | Не заявлять без соответствующего host/device | 1.0 | Ложный PASS | Нет |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| URI grammar | `TaskDeepLink` | Без изменений | Все существующие ссылки совместимы | parser tests |
| Linux registration | `ci/deb/unlimotion.desktop` | scheme MIME + `%u` | Следующая установка/обновление пакета регистрирует handler | parsed contract + package/native smoke |
| macOS registration | `ci/osx/Info.plist` | `CFBundleURLTypes` | Следующая `.app`/pkg сборка получает handler | plist test + bundle/native smoke |
| Android registration | `MainActivity` metadata / generated manifest | VIEW intent filter | Следующая APK получает handler; данные не мигрируют | source/manifest contract + APK/device smoke |
| Activation state | `App` queue + task repository | Добавляется lifetime source | Windows flow сохраняется | unit + Headless + full suite |
| Storage/data | Task repository | Без изменений | Миграция не нужна | existing tests |

## 7. Бизнес-правила / Алгоритмы

1. Поддерживается ровно route `unlimotion://task/{id}`.
2. ID ограничен существующим ASCII allowlist и длиной до 160.
3. Один OS activation обрабатывается одним platform source.
4. До готовности repository ссылка хранится в памяти; после готовности открывается либо выдаёт not-found.
5. URI не меняет task space и не ищет задачу вне активного пространства.
6. Порядок нескольких pending URI сохраняется.

## 8. Точки интеграции и триггеры

- `App.OnFrameworkInitializationCompleted` — ранняя подписка на platform activation.
- `App.InitializeStartupViewModelAsync` — drain pending links после connect.
- `Program.Main` — Linux/Windows argument + broker path.
- Linux `.desktop` — desktop environment/Obsidian click.
- macOS LaunchServices — `.app` URL activation.
- Android `ACTION_VIEW` — cold `OnCreate` и warm `OnNewIntent` базового Avalonia activity.

## 9. Изменения модели данных / состояния

- Persisted модель и task files не меняются.
- Добавляется только runtime subscription/очередь, используя существующий pending state.
- Настройки пользователя не добавляются.

## 10. Миграция / Rollout / Rollback

- Миграция данных отсутствует.
- Handler появляется после установки/обновления соответствующего пакета; portable binary сам по себе не обязан регистрировать схему.
- Rollback: удалить platform metadata и lifetime subscription; task data и Markdown остаются нетронутыми, ссылки снова станут неактивными на этой платформе.
- Linux/macOS package rollback выполняется установкой предыдущей версии; Android — предыдущей APK в рамках допустимой системой downgrade-политики.

## 11. Тестирование и критерии приёмки

Acceptance Criteria:

- **AC1:** Linux desktop entry, macOS plist и Android activity объявляют только схему `unlimotion` и маршрут `task` там, где host поддерживается metadata.
- **AC2:** Linux cold URI принимается аргументом; warm URI передаётся owner-процессу ровно один раз; обычный запуск без URI не становится глобально single-instance.
- **AC3:** macOS/Android protocol activation, полученная до или после готовности ViewModel, не теряется и не дублируется.
- **AC4:** валидный URI переключает в режим задач, выбирает существующую задачу и открывает карточку.
- **AC5:** невалидный URI ничего не открывает и не выполняет.
- **AC6:** неизвестная задача сохраняет текущий выбор и показывает существующую локализованную ошибку.
- **AC7:** затронутые desktop, Android и test projects собираются обычными командами; platform packaging metadata проходит contract validation.
- **AC8:** существующий Windows deep-link flow и внутренняя ссылка Ленты не регрессируют.

Обязательный набор проверок обусловлен публичным protocol/packaging контрактом и multi-module scope:

1. TDD/characterization: новые package/activation contract tests должны падать до реализации по ожидаемой причине.
2. Targeted TUnit: `TaskDeepLink*`, `PlatformShellProjectContractTests`, затронутые `MainWindowViewModelTests`.
3. UI: обновить/добавить Avalonia Headless flow, который подаёт platform activation source до и после startup и наблюдает режим задач + выбранную карточку. UI test обязателен локальным `AGENTS.override.md`.
4. Builds: основной Desktop, Mac build project, Debian build project, Android project и test/headless projects. Если Android/macOS toolchain объективно недоступен, exact failure классифицируется как environment blocker и не превращается в product PASS.
5. Full `Unlimotion.Test` suite, потому что меняется общий `App` startup/activation contract.
6. `git diff --check` и review scope.

Native smoke перед релизным утверждением:

- Linux: установить тестовый DEB; `xdg-mime query default x-scheme-handler/unlimotion`; `xdg-open 'unlimotion://task/<fixture-id>'` для cold/warm.
- macOS: собрать/установить `.app`; проверить `plutil -lint`; `open 'unlimotion://task/<fixture-id>'` для cold/warm.
- Android: установить APK; `adb shell am start -W -a android.intent.action.VIEW -d 'unlimotion://task/<fixture-id>' com.Kibnet.Unlimotion` для cold/warm и проверить foreground/card state.

Локальный Windows EXEC может завершить реализацию как **code/contract validated**, но не как native runtime verified для трёх платформ. Публикация релиза требует native smoke либо CI/device evidence.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | XML/source/desktop-entry contract tests | package inspection | test log | — |
| AC2 | broker + argument-routing tests | Linux cold/warm `xdg-open` | test/native log | native smoke только на Linux host |
| AC3 | activation adapter tests + Headless startup/warm flow | macOS `open`, Android `adb` | test/native log | native smoke только на целевой платформе |
| AC4 | Headless navigation test | открытая карточка | UI assertion/screenshot fallback | video recorder для этих OS отсутствует |
| AC5 | parser + activation negative tests | invalid URI smoke | test log | — |
| AC6 | existing ViewModel regression test | localized toast observation | test log | — |
| AC7 | `dotnet build` affected projects | `plutil`/APK/DEB inspection | build log | package scripts требуют native host |
| AC8 | Windows activation/broker and internal Feed task-link tests | Windows optional smoke | test log | — |

Stop rules validation:

- После timeout не повторять ту же команду без новой гипотезы/evidence.
- Red из-за отсутствующей workload/SDK/OS классифицировать отдельно от product defect.
- Не объявлять native platform behavior проверенным по одному лишь contract test.
- После green обязательного набора не расширять тесты без нового риска или изменения.

## 12. Риски и edge cases

- Слишком поздняя подписка теряет cold activation — подписка выполняется в начале framework initialization.
- Двойной источник на macOS может открыть задачу дважды — macOS исключается из argument route.
- Unix broker может зависеть от Windows mutex naming — namespace prefix становится platform-specific и получает regression test.
- Android может доставить повторный URI в существующую activity — `SingleTask` и Avalonia `OnNewIntent` являются единственным источником.
- Custom scheme не имеет криптографической привязки к приложению — parser оставляет только безопасное read/navigation действие; verified HTTPS links остаются follow-up.
- Wayland может запретить focus stealing — не обещаем невозможного, но selection/card state проверяется.
- Package metadata может быть корректна в source, но не попасть в итоговый artifact — contract дополняется native package inspection перед release.

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Ссылка при холодном старте потеряется» | Ранее startup ordering уже был критичен | ранняя subscription + pending queue + Headless cold test | mitigated |
| «При повторном щелчке откроется второй экземпляр» | Linux запускает command заново | broker только для URI, ordinary launch остаётся multi-instance | mitigated |
| «На macOS задача откроется дважды» | Возможны args и activation event | macOS использует один lifetime source | mitigated |
| «Android работает только при закрытом приложении» | cold/warm lifecycle различается | `SingleTask` + базовый `OnNewIntent` + warm test plan | mitigated |
| «Contract test не означает, что пакет реально работает» | Текущий host — Windows | native smoke явно обязателен перед release claim | mitigated |
| «Почему не iOS и web?» | Они были в исходном общем списке | пользователь выбрал только пункты 1 и 2; это Non-Goals | mitigated |

### Rework Prevention Checklist

- [x] Назван видимый результат: существующая карточка задачи.
- [x] Каждый сценарий связан с evidence и AC.
- [x] Решения и ограничения записаны в Decision Ledger.
- [x] Предсказаны cold/warm, duplicate, Wayland и false-validation objections.
- [x] Проведён role-based review.
- [x] AC описывают проверяемый результат.
- [x] EXEC имеет путь к contract/UI/build evidence и честную границу native smoke.

## 13. План выполнения

1. Добавить RED contract tests для Linux desktop entry, macOS plist, Android intent filter и platform activation adapter.
2. Реализовать общий ранний Avalonia activation adapter и lifecycle cleanup.
3. Сделать desktop argument/broker path переносимым для Linux без регрессии Windows.
4. Обновить Linux/macOS/Android package metadata.
5. Добавить/обновить Headless user-flow test cold/warm activation.
6. Выполнить targeted tests, affected builds и полный `Unlimotion.Test` suite.
7. Выполнить post-EXEC review; отдельно перечислить native smoke, который реально запускался, и недоступный.

## 14. Открытые вопросы

Нет блокирующих вопросов. HTTPS verified links и iOS остаются отдельным будущим scope.

## 15. Соответствие профилю

- `dotnet-desktop-client`: platform-specific код изолирован; UI thread не блокируется; navigation/recovery tests обязательны.
- `ui-automation-testing`: Headless UI coverage обязательно; video fallback обоснован отсутствием native recorder/harness.
- `ui-feature-parity`: перечислены platform gaps, guards и cold/warm flows; один общий observable result.
- `testing-dotnet`: TUnit запускается через `--treenode-filter`, staged validation и affected builds.
- Локальный `AGENTS.override.md`: UI test добавляется/обновляется и запускается либо exact blocker сообщается.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion/App.axaml.cs` | ранняя protocol subscription, routing, cleanup | общий macOS/Android activation path |
| `src/Unlimotion.Desktop/Program.cs` | platform guard argument path | исключить macOS duplicate |
| `src/Unlimotion.Desktop/Services/TaskDeepLinkActivationBroker.cs` | переносимое ownership name/Unix compatibility | Linux warm activation |
| `src/Unlimotion.Desktop/ci/deb/unlimotion.desktop` | `%u`, scheme MIME | Linux registration |
| `src/Unlimotion.Desktop/ci/osx/Info.plist` | `CFBundleURLTypes` | macOS registration |
| `src/Unlimotion.Android/MainActivity.cs` | intent filter metadata | Android registration |
| `src/Unlimotion.Test/TaskDeepLinkActivationBrokerTests.cs` | ownership/forward regression | desktop cross-platform contract |
| `src/Unlimotion.Test/TaskDeepLinkPackagingContractTests.cs` | Linux/macOS/Android package assertions | artifact contract |
| `src/Unlimotion.Test/PlatformShellProjectContracts.cs` | Android activation contract | shell parity |
| `src/Unlimotion.Test/*TaskDeepLink*Tests.cs` | adapter invalid/cold/warm tests при необходимости | shared activation behavior |
| `tests/Unlimotion.UiTests.Headless/Tests/MainWindowHeadlessTests.cs` | external activation user flow | обязательный UI evidence |

Точный набор test-файлов может быть редакционно уточнён в EXEC без изменения outcome/risk; unrelated production files не меняются.

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Linux | ссылка не зарегистрирована | DEB handler, cold/warm task navigation |
| macOS | `.app` не объявляет scheme | LaunchServices → Avalonia activation → task card |
| Android | intent не маршрутизируется | cold/warm `ACTION_VIEW` → task card |
| Parser | Windows/internal links | единый неизменный parser всех выбранных платформ |
| Windows | рабочий flow | сохраняется без изменения пользовательского поведения |

## 18. Альтернативы и компромиссы

- Отдельный native handler в каждой платформе: больше контроля, но дублирует parser/queue и создаёт drift. Не выбран.
- Только process arguments для macOS: плохо покрывает warm LaunchServices activation. Не выбран.
- Ручной `OnNewIntent` Android: дублирует Avalonia 12 `IActivatableLifetime` и создаёт риск double delivery. Не выбран.
- HTTPS App/Universal Links: безопаснее связывают домен с приложением, но требуют домена/server association и меняют публичный формат; отдельный follow-up.
- Новый процесс на каждый Linux click без broker: проще, но плодит окна/репозитории. Не выбран.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1–5 | PASS | Outcome, проверенный AS-IS, проблема, цели и Non-Goals заданы. |
| B. Качество дизайна | 6–10 | PASS | Ответственности, platform triggers, алгоритмы, recovery и performance определены. |
| C. Безопасность изменений | 11–13 | PASS | Persisted data не меняются; compatibility/rollout/rollback и protocol security описаны. |
| D. Проверяемость | 14–16 | PASS | Измеримые AC, matrix, negative cases, команды и stop rules заданы. |
| E. Готовность к автономной реализации | 17–19 | PASS | Этапы, решения, medium/Expanded scope и native evidence boundary определены. |
| F. Соответствие профилю | 20 | PASS | Desktop/UI automation/parity/local UI test требования отражены. |

Итог: **ГОТОВО**.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| 1. Ясность цели и границ | 5 | Три выбранные платформы и явные Non-Goals. |
| 2. Понимание текущего состояния | 5 | Проверены parser, App queue, Program/broker и package manifests. |
| 3. Конкретность целевого дизайна | 5 | Для каждой ОС задан registration, delivery и warm/cold path. |
| 4. Безопасность | 5 | Allowlist сохраняется; данных/миграции нет; rollback определён. |
| 5. Тестируемость | 5 | AC→unit/contract/UI/build/native smoke mapping. |
| 6. Готовность к автономной реализации | 5 | Блокирующих решений нет; platform limitations явно отделены. |

Итоговый балл: **30 / 30** — готово к автономному выполнению после approval.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Совпадает ли flow с «щелчок в Obsidian → задача»? | PASS | Нет |
| UX / designer | applicable | Получает ли пользователь тот же понятный результат без лишней поверхности? | PASS | Новый layout не нужен; state flow добавлен |
| Tester / validation | applicable | Есть ли cold/warm/invalid/not-found и package evidence? | PASS | Native smoke отделён от локального contract PASS |
| Developer / architect | applicable | Нет ли дублирования platform parser/activation? | PASS | Один parser, lifetime только macOS/Android, args Linux/Windows |
| Delivery / operations / security | applicable | Корректны ли registration, rollback и claims? | PASS | Release claim требует native package smoke |

### Post-SPEC Review

- Статус / stop decision: **PASS**, можно запрашивать exact approval.
- Scope/Evidence pass: просмотрены эта SPEC, central QUEST/testing/review owners, local UI-test override, предыдущая Windows SPEC, `Program`, broker, `TaskDeepLink`, `App` startup, Linux desktop/package assets, macOS plist/app script, Android activity/manifest, существующие deep-link/platform tests и workflows.
- Contract pass: Linux + macOS + Android реализуют один результат; iOS/browser/HTTPS и release publication исключены; UI test и full suite обязательны.
- Adversarial risk pass: проверены startup race, duplicate delivery, Unix ownership naming, Android warm intent, Wayland focus, scheme collision и source-vs-package false confidence.
- Role-Based pass: все пять применимых ролей дали PASS после добавления explicit native evidence boundary и state flow.
- Fix and re-review: первоначальный план «обрабатывать lifetime на всех desktop OS» заменён на mutually exclusive sources, чтобы macOS/Windows не получили double delivery; повторно сверены scenarios и AC2/AC3.
- Stop decision: PASS; блокирующих открытых решений нет.
- Evidence inspected: перечислено в Scope/Evidence pass; `git status` до SPEC был чистым на `feat/daily-feed`.
- Depth checklist:
  - Scope drift / unrelated changes: отсутствует; в SPEC-фазе добавлен только этот файл.
  - Acceptance criteria: 8 AC охватывают registration, cold/warm, UI, invalid/not-found, builds и Windows regression.
  - Scenarios / Decision ledger / objections: заполнены и взаимно согласованы.
  - Validation evidence: до EXEC только проверенный AS-IS и план; native runtime не заявлен.
  - Unsupported claims: Wayland focus и native runtime ограничены, не объявлены гарантированными.
  - Regression / edge case: Windows, internal Feed link, duplicate, startup queue, concurrent delivery учтены.
  - Comments/docs/changelog: README/changelog вне scope; package metadata является нужным public contract.
  - Hidden contract change: ordinary multi-instance desktop launch сохраняется.
  - Manual-review challenge: вероятная скрытая ошибка — source metadata не попадёт в package; поэтому native artifact inspection оставлена release gate.
- No-findings justification: после устранения double-source ambiguity нет BLOCKER/HIGH/MEDIUM findings; все обязательные pre-approval sections и evidence plans заполнены.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | activation design | Lifetime + args могли дважды обработать один macOS launch | Сделать источники mutually exclusive | fixed |
| LOW | platform UX | Wayland может запретить foreground | Зафиксировать best-effort focus и обязательный selected-task state | accepted-risk |

- Fixed before continuing: source exclusivity и native evidence boundary добавлены.
- Checks rerun: сценарии ↔ AC ↔ matrix, Decision Ledger, role review, linter/rubric.
- Needs human: только точное approval SPEC.
- Residual risks / follow-ups: native runtime smoke возможен лишь на Linux/macOS/Android host/device; HTTPS verified links — отдельная задача.
- Independent reviewer: не запускался, потому что текущая runtime-инструкция запрещает subagent без явного запроса пользователя; выполнен отдельный adversarial fallback, не называемый независимым review.

### Post-EXEC Review

- Статус / stop decision: **PASS с platform-smoke gate**. Реализация, контрактные проверки, затронутые сборки и локальный UI-flow завершены; публикация не выполнялась.
- Scope/Evidence pass: изменены только общий Avalonia activation adapter, `App` wiring, desktop argument/broker routing, Linux/macOS/Android registration metadata и связанные тесты. Формат URI, parser, storage и UI не менялись.
- Contract pass:
  - Linux `.desktop` передаёт один URI через `/usr/bin/Unlimotion %u` и объявляет MIME scheme handler;
  - macOS bundle объявляет `CFBundleURLTypes`, а process args отключены для URI, чтобы исключить duplicate delivery;
  - Android activity объявляет `VIEW`/`DEFAULT`/`BROWSABLE`, `unlimotion://task`, сохраняя `singleTask`;
  - macOS/Android доставляют cold/warm activation через один `IActivatableLifetime` adapter с pending queue.
- Adversarial risk pass: проверены invalid URI, activation до подписчика, warm delivery exactly once, dispose, Windows broker regression, обычные desktop builds и фактически сгенерированный Android manifest.
- Validation evidence:
  - `AvaloniaTaskDeepLinkActivationSourceTests`: **4/4 PASS**;
  - `TaskDeepLinkPackagingContractTests`: **6/6 PASS**;
  - `TaskDeepLinkActivationBrokerTests`: **1/1 PASS**;
  - Headless `Task_deep_link_opens_existing_task_card`: **1/1 PASS**;
  - Release builds Windows desktop, Debian desktop, macOS desktop: **PASS**;
  - Android Debug build/APK: **PASS**, generated manifest вручную проверен на `VIEW`, `DEFAULT`, `BROWSABLE`, host, scheme и `singleTask`;
  - полный последовательный `Unlimotion.Test`: **1780 PASS / 1 FAIL / 0 skipped**. Единственный fail — существующий headless hit-test `MoveHandlePointerDrag_MovesSelectedBlockToDropTarget`; немедленный изолированный повтор: **1/1 PASS**. Первый параллельный прогон также дал несвязанный autosave race, изолированный повтор: **1/1 PASS**.
- Fix and re-review: тестовый adapter переделан без поддельной реализации закрытого Avalonia lifetime interface; URI дополнительно проверяется на absolute; Unix mutex больше не получает Windows prefix; ложная попытка протащить App-event через headless host отклонена, потому что host создаёт ViewModel вне `App`.
- Findings: BLOCKER/HIGH/MEDIUM по изменённому коду не осталось. Наблюдаемые flaky headless failures не находятся в изменённых файлах и проходят изолированно.
- Residual risks / release gate: native click/runtime smoke на Linux, macOS и Android не выполнялся с Windows-хоста. Перед release обязательны `xdg-open`, `open` и `adb` cold/warm smoke на целевых системах; Wayland foreground остаётся best effort. Android build сохраняет существующие предупреждения LibGit2Sharp NativeBinaries/16 KB page size.
- Independent reviewer: не запускался, потому что текущая runtime-инструкция запрещает subagent без явного запроса пользователя; выполнен отдельный ручной adversarial diff-review, не называемый независимым review.

## Approval

Получено: **«Спеку подтверждаю»**. Approval относится только к EXEC этой SPEC; commit/push/release отдельно не разрешены.

## 20. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека, если требовалось | Затронутые артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC, 2026-09-23 | Выбран Expanded scope: публичный OS protocol и три platform shells | Проверены Windows baseline, package metadata, Avalonia activation contract и тестовые поверхности | post-SPEC review | «Давай реализуем 1 и 2» определило Linux + macOS + Android, но не заменяет exact approval | этот SPEC |
| Post-SPEC, 2026-09-23 | PASS после устранения double-source ambiguity | Linter 20/20 блоков PASS, rubric 30/30, role/adversarial review PASS; native runtime остаётся release-stage evidence | Ожидать «Спеку подтверждаю» | Ожидается | этот SPEC |
| EXEC, 2026-09-23 | Exact approval получен; разрешена реализация Linux/macOS/Android scope | Пользователь: «Спеку подтверждаю» | TDD contracts → implementation → staged validation → post-EXEC review | «Спеку подтверждаю» | spec, далее файлы из таблицы изменений |
| Post-EXEC, 2026-09-23 | PASS с обязательным native smoke перед release | 4 adapter + 6 packaging + broker + UI tests PASS; 3 desktop builds и Android build PASS; full suite 1780/1781, единственный flaky UI test изолированно PASS | Перед публикацией проверить cold/warm click на Linux/macOS/Android | Не требуется; commit/push не запрашивались | adapter, App/Program/broker, platform metadata, tests, этот SPEC |
