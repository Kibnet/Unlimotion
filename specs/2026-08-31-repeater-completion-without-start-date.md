# Доступность повторения только при заданной дате начала

## 0. Метаданные
- Тип (профиль): UI bugfix; `dotnet-desktop-client` + `ui-automation-testing`, context `testing-dotnet`.
- Владелец: пользователь; реализация и самопроверка — Codex.
- Масштаб: small; видимость блока настроек и сброс зависимого поля в ViewModel.
- Целевое семейство / behavior baseline: GPT-5.6; немодельная задача.
- Поверхность: Codex desktop, Windows, PowerShell.
- Effective runtime: model ID/effort не подтверждались и для результата приложения несущественны; SDK 10.0.400 проверен.
- Eval baseline / evidence: не применимо к моделям; для приложения нужны regression и UI evidence до/после.
- Целевой релиз / ветка: PR #287, `fix/repeater-requires-start-date`; исходный detached HEAD `f39b3245`, актуальная base после ребейза — `edf83000` (`origin/main`, PR #288).
- Ограничения: до «Спеку подтверждаю» изменяется только эта spec; нет работы с пользовательскими данными или Git delivery.
- Instruction stack: central AGENTS/routing; creator-vibe-lens; model-behavior-baseline; tool-execution-baseline; collaboration-baseline; quest-governance/mode; testing-baseline/dotnet; указанные профили; spec-linter/rubric/review-loops; локальный AGENTS.override.
- Canonical template: `C:/Users/Kibnet/.codex/agents/templates/specs/_template.md`.
- История решения: пользователь отклонил предложенное ранее создание повтора без начала. Эта редакция полностью заменяет прежний TO-BE; историческое имя файла сохранено, старый алгоритм не реализуется.

## 1. Overview / Цель
Не позволять интерфейсу обещать повторение, которое без даты начала не сработает. Пользователь выбрал правило: настройки повторения скрыты без начала; очистка начала отключает уже заданное повторение.

Outcome contract:
- Success means: нет начала — нет блока настройки; установили начало — блок появился; очистили начало — повтор сброшен и это сохранено.
- Итоговый артефакт / output: минимальные изменения ViewModel/XAML, regression/UI tests и evidence.
- Stop rules: SPEC заканчивается approval gate; EXEC — только после red/green, UI evidence, сборки, полных suites и review. Старое предложение fallback даты не выполнять.

## 2. Текущее состояние (AS-IS)
- `TaskTreeManager.HandleTaskStatusChange` создаёт повтор только при активном Repeater и заполненном PlannedBeginDateTime. По уточнению пользователя это условие остаётся правильным.
- `MainControl.axaml`: внешний блок `CurrentTaskRepeaterSection` не зависит от даты, поэтому настройки доступны и без неё.
- Поле `CurrentTaskPlannedBeginPicker` и команда `DateCommands.SetBeginNone` меняют одно свойство `TaskItemViewModel.PlannedBeginDateTime`.
- Подписка изменения начала в TaskItemViewModel сейчас обрабатывает перенос конца/длительности, но не сбрасывает Repeater при null.
- Изменения PlannedBeginDateTime и Repeater уже входят в общий throttled autosave. Model getter включает оба поля.
- Конструктор присваивает Model до Init. `WhenAnyValue` испускает начальное значение, а `Update(TaskItem)` поднимает изменения при `_isUpdatingFromModel=true` и загружает Repeater после дат. Это важно для защиты от сброса при загрузке.
- Существующие маркеры ↻ зависят от Repeater; присваивание null уведомляет UI и меняет подписку на параметры.
- В прошлой фазе выполнен baseline TaskStatusTransitionTests: 18/18, exit 0. Это проверка старого handler, не нового UI правила.

## 3. Проблема
UI допускает настройку повторения без обязательной даты начала и оставляет повтор включённым после удаления этой даты.

## 4. Цели дизайна
Единый сброс в ViewModel для обоих UI путей; скрытие всей секции без пустой рамки; сохранение через существующий autosave; отсутствие побочных записей при загрузке; неизменные календарные и серверные правила.

## 5. Non-Goals (чего НЕ делаем)
- Не создаём повтор без даты и не вводим fallback от завершения.
- Не меняем TaskTreeManager, календарный алгоритм, правила статуса, API/CLI или серверную валидацию.
- Не выполняем миграцию и автоматическую очистку старых записей при чтении.
- Не меняем конец, длительность, отношения, статус или прочие поля при очистке начала.
- Не добавляем подтверждающий диалог, новую настройку или автоматическое восстановление отменённого повтора.
- Не меняем общий дизайн карточки, фильтры, Git delivery, установку и релиз.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- MainControl.axaml: видимость внешнего CurrentTaskRepeaterSection по наличию PlannedBeginDateTime.
- TaskItemViewModel: синхронно сбросить Repeater при локальной очистке начала; не трогать его при model hydration/инициализации.
- Существующие autosave и уведомления: сохранить оба поля и обновить маркер.
- Tests: проверять UI, состояние модели, read-back, отсутствие ложного сброса и регрессии.

### 6.2 Детальный дизайн
1. Связать IsVisible внешнего Border CurrentTaskRepeaterSection с наличием даты. Использовать принятый nullable binding либо вычисляемый bool с корректными уведомлениями, если прямой binding неприменим.
2. Скрывать всю секцию: шаблон, тип, период, «после выполнения», дни недели, рамку и занимаемое место. Элементы не участвуют в Tab-навигации.
3. При локальном переходе начала из заданного значения в null присвоить `Repeater = null`. Сбросить объект целиком, а не только тип: старый период/дни/AfterComplete не должны возвращаться после повторной установки даты.
4. Общий обработчик свойства покрывает очистку CalendarDatePicker и SetBeginNone. Не дублировать логику в команде/коде окна.
5. Отличать изменение от начальной эмиссии WhenAnyValue и от Update(TaskItem): пропустить initial emission или проверять переход и учитывать _isUpdatingFromModel. Не запускать очищение сохранённых данных при открытии/синхронизации.
6. После сброса общий autosave сохраняет null в начале и Repeater. Отдельный параллельный save не добавлять; использовать текущий lifecycle и drain в тестах. Не обещать новую атомарность concurrent/external updates.
7. Повторная установка даты показывает блок с выключенным повторением; выбор режима остаётся действием пользователя.
8. Замена одной непустой даты другой сохраняет выбранный повтор. Прежний перенос конца/длительности сохраняется.
9. При очистке начала конец и PlannedDuration не меняются. Присваивание Repeater=null обновляет ↻ через существующие уведомления.
10. Старые записи «нет начала + есть Repeater» при загрузке не мигрируются: секция скрыта, данные сохраняются как есть. Установка начала для такой старой записи делает её существующие настройки доступными. Это отдельно от восстановления повтора после нового пользовательского сброса, который сохраняет null.
11. Ошибки сохранения идут через текущий SaveItemCommand/уведомления; скрытие секции само по себе не считается доказательством успешной записи.

Visual planning artifact: текстовый storyboard; новая геометрия/контролы не вводятся:
```text
Начало: [не задано]     → секции «Повторение» нет, пустой рамки нет
Начало: [дата]          → секция видна, можно выбрать повторение
Начало: [дата], ↻       → очистить начало
Начало: [не задано]     → секция исчезла, ↻ исчез, Repeater=null сохранён
Начало: [другая дата]   → секция вернулась, повторение выключено
```
UI evidence: в EXEC записать автоматизированные «до» и «после» на синтетических данных через FlaUI и существующий подход recording handshake.
Local-only пути: `artifacts/ui-evidence/repeater-start-date/before.mp4`, `after.mp4`.
Fallback вместо видео только по установленной технической причине с командой и next-best screenshots/logs; headless assertion не называть записью окна.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| S1 | Открыть задачу без начала | Секции повторения нет | UI layout/Tab test | AC1 |
| S2 | Установить начало | Секция появляется и доступна | UI test | AC1 |
| S3 | Включить повтор, очистить начало полем или «Нет» | Секция и маркер исчезают; повтор отключён | UI + сохранённая модель | AC2, AC3 |
| S4 | Установить начало снова | Повтор не восстанавливается автоматически | UI + reopen | AC3 |
| S5 | Заменить дату другой датой | Повтор сохранён | Regression test | AC4 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Начало null, Repeater null | Открыть | Секция скрыта | Нет записей от открытия | Новая/обычная задача |
| Начало есть, повтор есть | Очистить локально | Оба поля null; секция скрыта | При ошибке save текущий error flow | CalendarDatePicker и команда |
| Начало есть, повтор None/null | Очистить | Без исключения; секция скрыта | Повтор уже выключен | Нет лишних действий |
| Начало null после сброса | Установить дату | Секция видна, повтор null | Не восстановить старый объект | Новый выбор вручную |
| Начало есть | Другая непустая дата | Повтор сохранён | Прежний перенос конца | Без регрессии |
| Любое | Update/initial hydration | Принять authoritative поля без локального autosave | Старый inconsistent snapshot не мигрируется | Сохранить read-only загрузку |
| Дата+повтор корректны | Completed | Прежний следующий экземпляр | Прежние ограничения завершения | Handler не меняется |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Начало обязательно | user | Скрыть настройки без даты | 1.00 | Нет — прямое уточнение | Нет |
| Удаление начала | user | Отключить повтор | 1.00 | Нет — прямое уточнение | Нет |
| Представление отключения | agent | Repeater=null | 0.98 | Возврат старых параметров при частичном сбросе | Нет |
| Старые данные/синхронизация | agent | Не мигрировать при чтении | 0.95 | Неявная очистка данных выходит за запрос | Нет |
| Отмена удаления даты | agent | Повтор не восстановить автоматически | 0.98 | Иначе сброс неустойчив | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Поля задачи | TaskItem → ViewModel → autosave | Очистка начала сохраняет Repeater=null | Формат прежний, без миграции | Save/read-back/reopen |
| Видимость | MainControl binding | Наличие начала управляет секцией | Automation IDs сохраняются | UI at desktop/phone widths |
| Hydration | Update + _isUpdatingFromModel | Без побочного reset/save | Старые данные неизменны | Identity/model assertions и проверка existing CanAutosave guard в исходном коде |
| Генерация повтора | TaskTreeManager | Не меняется | Дата остаётся обязательной | Existing status tests |

## 7. Бизнес-правила / Алгоритмы
`Visible(RepeaterSection) = PlannedBeginDateTime.HasValue`.
Локальный переход `date → null` сбрасывает Repeater; `date A → date B` не сбрасывает.
После завершённого сброса последующая установка даты не включает повтор.
Initial load/model hydration не считаются пользовательской очисткой.

## 8. Точки интеграции и триггеры
Наличие даты в XAML; общий обработчик PlannedBeginDateTime; существующие сохранение/notification при Repeater=null. Покрыть SetBeginNone и очистку поля, а не только прямое присваивание в тесте.

## 9. Изменения модели данных / состояния
Новых persisted полей нет. Repeater=null — существующее представление отсутствия повторения. Вычисляемый bool для binding допустим при необходимости; новая настройка не нужна.

## 10. Миграция / Rollout / Rollback
Миграции нет. Открытие старой записи не пишет данные. Откат кода восстанавливает прежний UI, но не воссоздаёт повтор, который пользователь уже явно сбросил. Не изменять пользовательское хранилище при разработке.

## 11. Тестирование и критерии приёмки
- AC1: без начала вся секция скрыта без пустой рамки и Tab-stop; с началом доступна. Desktop и узкая ширина.
- AC2: очистка начала обоими UI путями сбрасывает Repeater/null и маркер; конец, длительность и прочие поля не изменяются.
- AC3: autosave/read-back/reopen подтверждают сброс; повторная установка даты не возвращает прежний повтор. Покрыть Daily и Weekly с параметрами/AfterComplete.
- AC4: date→date сохраняет повтор; initial load/Update не сбрасывают настройки и не вызывают лишнее сохранение; legacy snapshot проверен отдельно.
- AC5: корректная повторяемая задача с датой завершается как прежде; сборка и полные CI suites green; новые UI tests и before/after evidence выполнены либо конкретный blocker явно отражён как незавершённая проверка.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | MainControlRepeaterStartDateUiTests | Секция/рамка исчезают, скрытый ComboBox не получает focus; ширины 1400/390 | `artifacts/repeater-start-date-green.log`, `artifacts/ui-evidence/repeater-start-date/final/*.png` | PASS; native video заменено безопасными снимками |
| AC2, AC3 | Тот же UI класс + TaskItemRepeaterStartDateTests; RepeaterStartDateFlaUiTests | Маркер, конец/длительность, JSON read-back и повторная установка даты | Targeted 12/12, native 1/1; логи в Post-EXEC | PASS |
| AC4 | TaskItemRepeaterStartDateTests + existing layout/marker tests | Initial load/Update сохраняют модель и identity Repeater; CanAutosave guard просмотрен | Regression assertions + source review | PASS, включая расширенную identity assertion в полном прогоне |
| AC5 | TaskStatusTransitionTests; полный Unlimotion.Test + UiTests.Headless; focused FlaUI | Ненулевой test count, exit 0 | `artifacts/repeater-full-unit.log`, `artifacts/repeater-full-headless.log`, native/build logs | PASS: 916/916 основной, 38/38 Headless, 1/1 FlaUI; build exit 0 |

Команды EXEC, последовательно:
```powershell
dotnet --version
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter '/*/*/TaskItemRepeaterStartDateTests/*' --maximum-parallel-tests 1 --output Detailed
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter '/*/*/MainControlRepeaterStartDateUiTests/*' --maximum-parallel-tests 1 --output Detailed
dotnet test --project tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug -p:UseSharedCompilation=false -- --treenode-filter '/*/*/RepeaterStartDateFlaUiTests/*' --maximum-parallel-tests 1 --output Detailed
dotnet build src/Unlimotion.Desktop/Unlimotion.Desktop.csproj -c Debug -p:UseSharedCompilation=false
dotnet test --project src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
dotnet test --project tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug -p:UseSharedCompilation=false -- --maximum-parallel-tests 1 --output Detailed
git diff --check
```
Новые имена классов — план. Сначала воспроизводящие red UI/VM tests, затем fix, targeted green и full gates по .github/workflows/tests.yml. Сокращать throttle только test-local с восстановлением/изоляцией; проверять фактическое сохранение, а не ожидать произвольный sleep. До длинных запусков сообщать команду/progress, использовать реальные длительности этапов. Timeout/environment failure не product red; не повторять ту же команду без диагностики.

## 12. Риски и edge cases
- Начальная эмиссия WhenAnyValue может удалить старые настройки: исключить её и hydration.
- Сброс только в SetBeginNone оставит неисправной очистку поля: общий ViewModel обработчик.
- Скрытие внутренних контролов оставит рамку/отступ: скрыть внешний Border.
- Утечка старого Repeater через сохранённый объект или события: проверить null/отписку/повторную установку даты.
- Быстрые действия и autosave: ждать существующего сохранения, не вводить отдельную параллельную запись.
- Layout tests, ожидающие видимую секцию у задач без начала, нужно актуализировать осмысленными fixtures с датой; остальные проверки не ослаблять.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Настройки скрылись, а повтор остался | Одного IsVisible недостаточно | Repeater=null + save/read-back/reopen | mitigated |
| После возврата даты вернулись старые дни/период | Частичный сброс мог оставить объект | Сбрасывать весь объект, S4 | mitigated |
| Пропали настройки от открытия задачи | Подписка ловит initial/model values | Hydration guard и отдельные tests | mitigated |

### Rework Prevention Checklist
Сценарии S1–S5 видимы и связаны с AC; решения пользователя отделены от implementation choices; старый fallback отменён; guards для hydration/initial emission описаны; evidence включает UI и сохранение; роль UX применима.

## 13. План выполнения
1. Получить approval этой редакции.
2. Добавить reproducing VM/UI tests, сохранить automated before evidence.
3. Исправить видимость и общий сброс; актуализировать только затронутые fixtures.
4. Проверить сохранение, повторное открытие, hydration и keyboard/layout; after evidence, build и полные suites.
5. Post-EXEC review и итог с фактами/ограничениями; без Git delivery.

## 14. Открытые вопросы
Блокирующих продуктовых вопросов нет: пользователь выбрал правило и подтвердил эту редакцию фразой «Спеку подтверждаю».

## 15. Соответствие профилю
Async autosave без UI блокировок; automation IDs сохранены; обязательные UI tests и evidence; .NET build/full suites; только синтетические данные. Повторение с датой остаётся прежним.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| specs/2026-08-31-repeater-completion-without-start-date.md | Новая редакция и журнал | Уточнение пользователя |
| src/Unlimotion.ViewModel/TaskItemViewModel.cs | Сброс повтора при локальной очистке начала | Общая реакция для UI путей |
| src/Unlimotion/Views/MainControl.axaml | Видимость всей секции | Не показывать недоступные настройки |
| src/Unlimotion.Test/TaskItemRepeaterStartDateTests.cs | Новые regression cases | Состояние/save/hydration |
| src/Unlimotion.Test/MainControlRepeaterStartDateUiTests.cs | Новые UI cases | Поле, команда, видимость, маркер, reopen |
| src/Unlimotion.Test/MainControlTaskCardLayoutUiTests.cs | Fixture с датой для видимого блока | Сохранить layout coverage |
| tests/Unlimotion.UiTests.FlaUI/Tests/RepeaterStartDateFlaUiTests.cs | Автоматизированный desktop flow с JSON read-back | Безопасные снимки окна из UI test run; fallback описан ниже |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| Нет начала | Настройки повторения доступны | Секция скрыта |
| Очистка начала | Повтор остаётся | Повтор сбрасывается и сохраняется |
| Новая установка после сброса | Мог остаться старый повтор | Нужно выбрать повтор заново |
| Handler завершения | Требует дату | Без изменения |

## 18. Альтернативы и компромиссы
- Ранее предложенное создание без даты: отменено прямым уточнением пользователя.
- Только скрыть секцию: не выполняет требование сброса.
- Сбросить только Type: сохраняет параметры и риск восстановления; выбран null.
- Нормализовать все старые записи при чтении: лишняя миграция; не выбрана.
- Общий обработчик ViewModel + видимость Border: покрывает оба UI пути с минимальными production-правками.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункт | Статус | Комментарий |
| --- | --- | --- | --- |
| A | 1. Цель | PASS | Прямое уточнение пользователя |
| A | 2. AS-IS | PASS | UI, даты, autosave и hydration просмотрены |
| A | 3. Проблема | PASS | Доступность и очистка зависимого поля |
| A | 4. Дизайн | PASS | Общий VM handler, внешний Border |
| A | 5. Non-Goals | PASS | Генератор/миграция/delivery исключены |
| B | 6. Ответственность | PASS | UI/VM/storage разграничены |
| B | 7. Интеграция | PASS | Поле и SetBeginNone |
| B | 8. Правила | PASS | Переходы и legacy явно описаны |
| B | 9. Ошибки | PASS | Existing save error flow |
| B | 10. Perf | PASS | Без новых обходов и таймеров |
| C | 11. Данные | PASS | Repeater=null, без схемы |
| C | 12. Миграция | PASS | Нет фоновой очистки |
| C | 13. Rollback | PASS | Revert кода не воссоздаёт удалённую настройку |
| D | 14. AC | PASS | Видимость + state + persistence |
| D | 15. Tests | PASS | Red/green, UI, negative cases |
| D | 16. Команды | PASS | Existing TUnit workflow |
| E | 17. План | PASS | Approval → regression → fix → validation |
| E | 18. Вопросы | PASS | Правило выбрано пользователем |
| E | 19. Масштаб | PASS | Два production-файла |
| F | 20. Профиль | PASS | UI evidence, build/full tests |
Итог: ГОТОВО.

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---: | --- |
| Ясность цели/границ | 5 | Прямое уточнение, старый вариант отменён |
| AS-IS | 5 | Изучены binding, команды, init/update/autosave |
| TO-BE | 5 | Guard, сброс и поведение повторной установки |
| Безопасность | 5 | Нет миграции/неявного стирания при чтении |
| Тестируемость | 5 | UI, VM, save/reopen, hydration |
| Готовность к EXEC | 5 | Нет открытых решений, файлы и команды заданы |
Итог: 30/30; готово после approval. Это оценка spec, не подтверждение реализации.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Соответствует уточнению пользователя? | PASS | Начало обязательно, сброс указан |
| UX / designer | applicable | Скрыт ли весь блок, не потерян ли UI путь? | PASS | Border, Tab и оба способа очистки |
| Tester / validation | applicable | Доказан ли устойчивый сброс? | PASS | Save/reopen/restore date и hydration |
| Developer / architect | applicable | Не стираются ли данные от подписок? | PASS | Initial emission и model update guard |
| Delivery / operations / security | not applicable | Есть ли внешние изменения? | PASS | Нет deployment/config/secrets/Git |

### Post-SPEC Review
- Статус: PASS.
- Scope reviewed: текущая spec; ранее прочитанный central stack/override; MainControl repeater/planning blocks, TaskItemViewModel Init/Model/Update, DateCommands; layout/marker/deadline tests.
- Decision: можно запросить подтверждение новой редакции.
- Review passes:
  - Scope/Evidence: требование взято из текущего сообщения пользователя; прежний handler fix убран из всех исполняемых разделов.
  - Contract: требования скрытия/сброса соответствуют S1–S5 и AC1–AC5; old generation guard не меняется.
  - Adversarial risk: initial emission, hydration order, null/None, date→date, очистка поля, stale repeater subscriptions, old snapshots, layout expectations.
  - Role-Based: все применимые роли проверены, см. таблицу; self-review, не независимый review. Small change не требует отдельного reviewer.
  - Fix and re-review: добавлены initial/hydration guards и явное отсутствие миграции; повторно сверены разделы 5–12 и AC.
  - Stop decision: PASS для запроса approval; не переходить к коду.
- Evidence inspected: live source snippets и git status; предыдущий baseline 18/18 относится к старому handler, повторно в этой редакции не запускался. Новые UI tests до EXEC не выполнялись.
- Depth checklist: scope ограничен; все AC имеют evidence; предположения отражены; миграции/старый fallback исключены; UI на desktop/phone и keyboard предусмотрены; validation claims не опережают выполнение; релизные docs не меняются.
- Manual-review challenge: открытие задачи запускает первую эмиссию и может стереть повтор без действия пользователя — предотвращается guard и AC4.
- No-findings justification: неприменимо, ниже исправленные при review риски.
| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | data | Начальная эмиссия/Update могут стать ложной очисткой | Отличить локальный переход от загрузки | fixed в spec |
| MEDIUM | coverage | Только команда «Нет» не покрывает очистку поля | Общий handler и два UI теста | fixed в spec |
| MEDIUM | persistence | Скрытие не доказывает сброс | Null + read-back/reopen/установка даты снова | fixed в spec |
- Fixed before continuing: guards, оба UI пути, persisted result.
- Checks rerun: сверка сценариев/матриц/production allowlist и отсутствие противоречий с уточнением пользователя.
- Needs human: только approval новой spec.
- Residual risks: видео/полные suites остаются EXEC; old inconsistent snapshots не мигрируются.

### Post-EXEC Review
Статус: PASS. Self-review для small change; не независимый review.

- Scope/Evidence: два production-файла, три новых test-файла, одна корректировка fixture и эта spec. TaskTreeManager, доменные правила, API, схемы и пользовательские данные не изменены.
- Contract: локальное date→null синхронно присваивает Repeater=null; существующий autosave сохраняет оба поля; вся внешняя секция скрыта по null. Возврат даты не восстанавливает объект.
- Adversarial: `.Skip(1)` защищает начальную эмиссию, `_isUpdatingFromModel` — hydration. Identity assertion обнаруживает даже временное удаление/воссоздание повторителя. Проверены None/null, date→date, Daily/Weekly, AfterComplete, отписка старого объекта, оба пути очистки, read-back и восстановление из диска.
- Role-based: domain — правило пользователя соблюдено; UX — полный блок/рамка и focus, desktop/узкая ширина; validation — red/green, persisted JSON, реальное окно; architecture — существующие lifecycle/autosave без новых записывающих сервисов.
- Fix and re-review: layout fixture получает дату, чтобы продолжить проверять полный набор контролов, не ослабляя assertions. Оконный тест проверен также без опционального сохранения снимков: 1/1, `artifacts/repeater-flaui-without-capture.log`, 29.721 s.
- Manual-review challenge: скрытие UI могло замаскировать сохранённый повтор — это исключено JSON read-back после сброса и после повторной установки даты. Само чтение legacy данных не считается пользовательской очисткой.
- Stop decision: EXEC завершён. Полные suites и focused desktop flow прошли; реализации и обязательных проверок больше не осталось. Git delivery не запрашивался.

Production diff: 7 строк подписки в TaskItemViewModel и условие видимости внешней секции в MainControl.

Итоговые проверки:
- Expected red: 7/10 падений (3 VM reset + 4 UI hide), 3 контрольных PASS; `artifacts/repeater-start-date-red.log`.
- Targeted green: 12/12 (6 VM + 6 UI, desktop/узкая ширина); `artifacts/repeater-start-date-green.log`.
- Desktop build: exit 0, 0 errors; `artifacts/repeater-desktop-build.log`.
- Финальный FlaUI flow: 1/1, 34.713 s, persisted reset и повторная установка даты; `artifacts/repeater-flaui-final.log`, `artifacts/ui-evidence/repeater-start-date/final/complete.json` (`Success=true`). Снимки `dated.png`, `cleared.png`, `restored-date.png` просмотрены: секция видна с датой, исчезает после очистки, возвращается без выбранного режима после установки даты. Интерфейс на русском, синтетическая задача.
- Полный Unlimotion.Test: 916/916, 0 skipped, exit 0, 22m 11.147s; `artifacts/repeater-full-unit.log`, `.exit`. Включает новые VM/UI cases и прежние status/layout tests.
- Полный UiTests.Headless: 38/38, 0 skipped, exit 0, 2m 13.368s; `artifacts/repeater-full-headless.log`. Запущен после отдельного restore/build с `--no-dependencies`, затем `dotnet test --no-build`, чтобы не перезаписывать зависимости активного основного test runner. Это проверка корректности, не измерение производительности.

Применён разрешённый fallback вместо видео: desktop-region recorder (`ffmpeg -f gdigrab`) оказался небезопасен при перекрытии окна другим приложением, а координатный UI input зависел от DPI/focus. Неудачные media captures удалены и не являются evidence. Сценарий переведён на UIA/keyboard focus и безопасный `PrintWindow`, не читающий пиксели чужого окна. Next-best evidence — expected red logs, автоматизированные headless и FlaUI assertions, сохранённый JSON и просмотренные снимки конкретного HWND. Все три финальных снимка содержат нужную область планирования. UI test run завершён успешно; usable before/after MP4 нет.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | test fixture | Full-card layout tests предполагают видимость повторителя | Задать дату в fixture, сохранить assertions | fixed, full suite PASS |
| MEDIUM | UI evidence | Desktop-region recording включает перекрывающее окно | Удалить небезопасные media; UIA/keyboard + HWND capture | fixed, safe screenshots inspected, FlaUI PASS |
| LOW | regression strength | Equality после hydration не отличает сохранение объекта от воссоздания | Дополнить identity assertion | fixed, full suite PASS |

- Final checks: `git diff --check` без whitespace errors; новые файлы также проверены `git diff --no-index --check`. SHA-256 production DLL совпадают в основном и Headless output, то есть UI suite проверяла те же сборки.
- Остаточные ограничения: нет миграции старых inconsistent записей; локальные результаты не являются CI/deployment evidence; видео заменено явно описанными безопасными снимками. Других незавершённых проверок или блокирующих findings нет.
- Needs human: нет для завершения согласованного EXEC. Коммит, push, merge и release не выполнялись.

### Актуализация после ребейза на `main` 01.09.2026
- В `origin/main` смержен PR #288 (`edf83000`, `fix(status): preserve edits during status transitions`), который устраняет отдельно обнаруженную гонку между throttled autosave и сменой статуса. Он сохраняет актуальные editor fields и создаёт следующий экземпляр повторяющейся задачи при немедленном завершении.
- Эта spec и PR #287 остаются самостоятельным UI-правилом: блок повторения скрыт без даты начала, а очистка даты сбрасывает `Repeater`. Генератор повторов и lifecycle смены статуса здесь не дублируются.
- Локальная черновая spec `2026-09-01-flush-local-edits-before-status-transition.md` удалена как полностью покрытая canonical spec `2026-08-31-status-transition-latency.md` из PR #288.
- После ребейза проверены 12/12 regression/UI tests этого изменения, file-backed сценарий `ImmediateRepeatingCompletion_FlushesEditorFieldsAndCreatesNextOccurrence` (1/1) и Headless `TaskStatusPicker_ImmediateRepeatingCompletion_PreservesEditorFieldsAndCreatesNextTask` (1/1). Все проверки прошли.
- Приведённые выше полные результаты 916/916 и 38/38 относятся к исходному Post-EXEC прогону PR #287. Актуальный `main` перед merge PR #288 прошёл 932/932 и 38/38; новый CI PR #287 должен проверить итоговую комбинацию после force-push.

## Approval
Получено подтверждение пользователя: «Спеку подтверждаю». Разрешён EXEC этой редакции; Git delivery не запрошен.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Инструкции/preflight/локализация | 0.98 | Дата в примере | Проследить UI command/cache | Нет | Нет | Найден guard даты | Чтение source/instructions |
| SPEC | Первая гипотеза | 0.90 | Предпочтение пользователя | Baseline и первоначальная spec | Да | Вопрос о дате | Предлагалось создавать повтор без даты; теперь отменено | Только эта spec |
| SPEC | Baseline/первый review | 0.95 | Approval | Запросить подтверждение | Да | Approval был запрошен, не получен | 18/18 старых status tests | Эта spec, runner output |
| SPEC | Уточнение пользователя | 1.00 | Нет продуктовых вопросов | Переписать TO-BE и AC | Нет | Пользователь: скрыть без даты, сбросить повтор при очистке | Дата остаётся обязательной | Эта spec |
| SPEC | Проверка UI/VM и повторный review | 0.98 | Approval | Запросить подтверждение новой редакции | Да | Будет запрошено в итоговом ответе | Общий date property, hydration/initial guards, save/reopen tests | Эта spec; код только прочитан |
| EXEC | Approval и preflight | 1.00 | Red/green evidence | Добавить regression/UI tests | Нет | Пользователь: «Спеку подтверждаю» | Подтверждён scope видимости/сброса; исходный код ещё не изменён | Эта spec |
| EXEC | Reproducing tests и минимальный fix | 0.99 | Green и UI evidence | Запустить targeted после baseline video | Нет | Нет | 7 ожидаемых падений (3 VM + 4 UI), 3 контрольных PASS; добавлены guards initial/model и IsVisible внешней секции | VM, XAML, новые tests, artifacts/repeater-start-date-red.log |
| EXEC | Targeted green / desktop UI / fallback evidence | 0.99 | Full suites и final screenshots | Завершить полную валидацию | Нет | Сообщено о небезопасной записи перекрываемого окна и безопасном fallback | 12/12 focused, 1/1 native UI, build PASS; генератор повтора не менялся | Targeted/build/FlaUI logs, эта spec |
| EXEC | Полная валидация и Post-EXEC review | 1.00 | Нет | Передать локальный результат пользователю | Нет | Сообщено об успешном завершении полного набора | 916/916 основной, 38/38 Headless, 1/1 native; снимки просмотрены, source scope и whitespace проверены | Эта spec, full logs, final PNG/complete.json |
