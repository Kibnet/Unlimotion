# Диагностика загрузки и поиска Unlimotion на Samsung Z Fold 6

## 0. Метаданные
- Владелец: Павел. Подготовка диагностического скоупа, форма Expanded, масштаб medium: два связанных симптома производительности, Android lifecycle, Git и неизвестная причина. Эта SPEC определяет read-only диагностику; изменение поведения приложения в неё не входит. По applicability quest-governance read-only анализ не требует exact approval; gate относится к будущей реализации.
- Профили: performance-optimization; dotnet-desktop-client для общего Avalonia клиента. ui-automation-testing и локальный AGENTS.override.md определяют обязательные проверки будущего исправления; сейчас UI/код не меняются.
- Central stack: creator-vibe-lens, model-behavior-baseline, collaboration-baseline, tool-execution-baseline, quest-governance/mode, spec-linter/rubric/review-loops, unlimotion-task-context и skill unlimotion-cli.
- Canonical template: `C:/Users/Kibnet/.codex/agents/templates/specs/_template.md`. Поверхность Codex; модель/effort не являются переменными немодельной диагностики. Model eval: не применимо.
- Локальная база исходников: main `e987023d`. Релизная база: [v1.33.0](https://github.com/Kibnet/Unlimotion/releases/tag/v1.33.0), commit `4ca32268b10817601a48f1e38a6540959e61a458`. Различие ревизий сохранено; local main не выдается за исходники последнего релиза.
- Основные карточки: `e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6` — загрузка; `32c49f34-d90a-4dd4-ada6-f961556420ba` — поиск. Сценарий фона: `ec747dfc-2963-40ac-bd41-ffa1cbb26d47`.
- Source tracker: `C:/Projects/Education/Unlimotion Space/Tasks`, CLI `C:/Users/Kibnet/.dotnet/tools/unlimotion-cli.exe`, версия `1.32.1-local.20261007.importance.60a9af2e`. Прочитаны карточки и все родительские ветви до корней.
- Пользователь подтвердил: установленные релизы Windows/Android, Samsung Z Fold 6, Git-хранилище, агенты используют CLI; Android долго грузится, поиск долго реагирует/зависает. «Последний релиз» — сообщение пользователя; фактический package version телефона пока не считан.
- Оборудование: ADB найден, `adb devices -l` вернул пустой список. На телефоне ничего не запускалось и не изменялось. Файлы двух прежних untracked SPEC сохранены.

## 0а. Поручение на последовательное выполнение — 12.10.2026
Пользователь разрешил записи статуса/результата через CLI, отдельные ветки/worktrees и commit/push/PR/merge каждой успешно принятой задачи перед следующей; SPEC подтверждается автоматически после успешного review и исправлений. Это актуальное разрешение заменяет требование ручного exact approval для этого scope. Публикация релиза и замена установленного приложения на личном телефоне не поручены. Обратимая установка отдельного диагностического пакета с собственной копией данных используется как необходимая проверка порученной Android-задачи; она не заменяет установленный релиз.
Первой выполняется e74c5d1a; остальные карточки не claim до результата/merge первой. Branch perf/android-e74c5d1a-task-loading, managed worktree C:/Users/Kibnet/.codex/worktrees/android-task-loading/Unlimotion, fresh origin/main fa1bfc03043f58fc9d6d626c1dedb27767bd0103. Claim/read-back подтвердили InProgress, agent codex-android-scope-20261012.
Для первичной Android-локализации доступен существующий AVD unlimotion_loading_20260914, запущенный -read-only -no-window -no-snapshot-save. Изменения этой сессии не сохраняются в исходном AVD. Допускается использовать официальный release APK 1.33.0 и отдельную копию task-space как disposable test fixture в этой сессии: без копирования Git credentials/remotes, без изменения личного телефона и исходных задач. AVD-результат не выдается за Fold 6 acceptance; исходные 5/1/5 и актуальность на Fold остаются gates основной карточки.
Scope e74c5d1a сейчас — подтвердить source/runtime bottleneck и выбрать минимальную оптимизацию; production code пока не меняется. Решение о следующем code delta добавляется сюда с regression/mandatory gates и review перед автоматически подтвержденным EXEC. Независимый reviewer подключается при multi-module/storage/public behavior delta по central review-loops.
Self-review этого дополнения: выбор AVD соответствует разрешенной тестовой подготовке и не заменяет Fold6; no original-data writes/credentials/install user device. Автоматически подтвержден для diagnostic/test-fixture EXEC по прямому поручению пользователя; кодовая оптимизация еще не выбрана.

## 1. Overview / Цель
Подготовить доказательное основание ближайшего исправления исходного сценария: открыть задачи на Fold 6 и найти нужную задачу без длительного ожидания/зависания.

Outcome contract:
- Success этой фазы: сохранены воспроизводимые baseline, условия, измерения и traces; установлена причина либо ограниченный набор гипотез с конкретной следующей проверкой; сформирован отдельный скоуп исправления с before/after acceptance.
- Output: локальный отчет, таблица замеров, минимальный сценарий поиска, fingerprint APK/dataset и карта bottlenecks. Чувствительные данные остаются локально; публичный отчет содержит агрегаты и обезличенные признаки.
- Диагностика не считается ускорением приложения. Будущий результат загрузки: ранее принятые 5/1/5 секунд; будущий результат поиска: ожидаемые результаты, отзывчивый ввод и отсутствие ANR в заданном сценарии.
- Stop: нет устройства — нет утверждения о замерах Fold 6; нет идентифицируемой версии/dataset — нет сравнения; неожиданные task writes/небезопасная profiling-команда — остановить зависимый эксперимент.

## 2. Текущее состояние (AS-IS)
- Карточка e74c5d1a уже требует baseline до выбора кеша, median/worst и freshness. Указанные там v1.30.x/v1.31.0 являются историческим планом, а новый baseline берется с фактически установленного релиза.
- Карточка поиска Prepared, описание пустое. Заголовок содержит ANR; пользователь подтвердил задержки или зависание, но конкретный текст Android ANR сейчас не наблюдался.
- Релиз 1.33.0 проверен через live GitHub release metadata. Через GitHub contents API прочитаны два файла именно `ref=v1.33.0`: MainWindowViewModel.cs blob `8478c27c9eaba020647ac73ea137fa6637643bb2`, MainActivity.cs blob `2db12037d93f0421e31ef74d757fdfbcd1f4e515`.
- Поиск MainWindowViewModel.cs:1025–1072 строит predicate по title, description, parent emoji и ID, выполняет NormalizeText и при включении fuzzy — FuzzyMatcher. Throttle использует MainThreadScheduler. Это область исследования, не доказанная причина ANR и не доказательство места исполнения всей downstream цепочки.
- В прочитанном MainActivity.OnResume вызываются base.OnResume и завершение запроса разрешения storage. Повторная полная загрузка именно из этого override не установлена; необходимо различать resume, recreation и process death.
- Есть Windows `TaskLoadingPerformanceFlaUiTests`: загрузка проверяется до реального действия на копиях task-space. `tests/Unlimotion.Performance/Program.cs` измеряет построение Roadmap, а не Android startup/search. Эти harness не дают acceptance Fold 6.

## 3. Проблема
Есть подтвержденный владельцем ущерб ежедневному Android-сценарию, но нет измерений, позволяющих выбрать минимальное исправление. Общий «кеш» или отключение fuzzy заранее могут изменить семантику и не устранить причину.

## 4. Цели дизайна
Одинаковые условия сравнения; разделение lifecycle, disk/storage, graph/UI, search и Git/network; сохранение данных и поисковой семантики; измеряемый результат; минимальные затраты времени владельца.

## 5. Non-Goals
До отдельного reviewed code delta: код приложения, instrumentation patches, новые production scripts, cache subsystem, алгоритм поиска и автосинхронизация. На личном устройстве: установка/удаление APK, clear app data. Также вне scope: обновление CLI, release, изменение оригинальных задач для эксперимента и массовая обработка backlog. Разрешение на task lifecycle и commit/push/PR/merge успешно принятой работы зафиксировано в §0а. Будущие исправления проходят отдельную SPEC после evidence этой диагностики.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Ответственности
Агент готовит сценарии, читает metadata, получает безопасно доступные traces и анализирует их. Владелец предоставляет доступ к Fold 6 и подтверждает, что выбранные примеры воспроизводят его проблему. Нельзя перекладывать на владельца ручной анализ логов.

### 6.2 Дизайн эксперимента
- Считать package `com.Kibnet.Unlimotion`, versionName/versionCode, Android build, ABI; сопоставить APK с release asset, насколько это доступно без доступа к приватному каталогу. Не требовать root/run-as у release APK и не обходить отказ.
- Описать Git-backed dataset: task count, description bytes/размеры, multi-parent/blocking links, выбранную вкладку и активные фильтры. Телефонный dataset не приравнивать автоматически к desktop snapshot из 3094 задач.
- Сначала минимум 5 baseline прогонов каждого lifecycle: cold start; resume после 30 секунд; resume после 5 минут; restart после подтвержденного process death. Для folded/unfolded зафиксировать состояние экрана. Не смешивать recreation со сменой устройства.
- Cold start измерять от запуска до пригодного для работы списка и успешного открытия существующей задачи. Resume — от возврата до того же действия. Process-death restart относится к cold/recovery, не к цели 1 секунда живого процесса.
- Git/network observer не запускает дополнительных fetch/pull/push. Сначала измеряется обычный настроенный online flow, затем существующий offline flow с явной пометкой. Смена connectivity/синхронизация на личном телефоне не выполняются скрыто: при необходимости используется отдельно изолированный эксперимент.
- Поиск: выбрать один конкретный медленный запрос из существующего локального текста, затем no-match, пустой запрос/очистка, быстрое последовательное изменение текста и обычный/fuzzy режимы. Набор ожидаемых ID фиксируется до эксперимента; текст для общего отчета обезличивается.
- Измерять input→visible feedback, input→final results, interval UI stall, ANR, frame timing, CPU/GC/allocations там, где доступно. Для недоступной метрики писать unavailable, не 0. Изменение запроса до завершения предыдущего проверяет сохранение результатов последнего запроса, а не добавляет новый дефект в scope.
- Traces: package-scoped logcat/Android profiling, screen-only local recording, временная шкала lifecycle/UI/Git. Не выгружать весь logcat, credentials, remote URLs или личные тексты в публичные артефакты. Release APK может ограничить profiling; в этом случае ограничения и next-best evidence фиксируются отдельно.
- Freshness сначала проверяется наблюдением уже доступных локально изменений. Для контролируемой новой правки используется изолированная копия; production tasks через CLI ради эксперимента не изменяются. Время сети/Git-доставки хранится отдельно от цели 5 секунд локального отражения.
- Visual planning artifact: изменение layout не применяется. Текстовый storyboard диагностики: `launch/resume → existing loading state → usable list → open task`; `type → input feedback → results → open match`. Нынешние состояния записываются как baseline, не как новый дизайн.
- UI video: при безопасной доступности recorder сохраняется локальный baseline нужного окна. Если недоступен, объективная причина, команда проверки и trace/screenshots/timestamps; fallback не называется native video. Passing after video относится к будущей fix-SPEC.

### 6.3 User-Observable Scenarios
| Scenario | Trigger | Наблюдение этой фазы | Evidence | AC |
| --- | --- | --- | --- | --- |
| S1 | Запустить закрытое приложение | Время до пригодного списка/открытия карточки | 5 повторов, lifecycle и recording/trace | AC2 |
| S2 | Вернуться после короткого/долгого фона | Resume отделен от recreation/death | PID/lifecycle, readiness | AC2 |
| S3 | Вводить медленный запрос | Задержка/зависание и результат привязаны к одному запросу | query IDs, input/results/ANR trace | AC3 |
| S4 | Очистить/быстро заменить запрос; fuzzy on/off | Последний ввод и корректность результата сохранены | baseline expected IDs, timeline | AC3 |
| S5 | Folded/unfolded и уже локальные внешние изменения | Экран/версия/данные учтены, latency и freshness различены | run metadata, isolated data | AC1, AC2, AC4 |

### 6.4 State / Interaction Matrix
| State | Trigger | Что фиксировать | Failure case |
| --- | --- | --- | --- |
| Process absent | Launch | version/PID, lifecycle, usable list | timeout/crash записывается как failed run |
| Background, PID alive | Resume | пауза, PID, ready action | новая PID/recreation переклассифицирует прогон |
| Search idle/running | New text | input feedback, final query/results | устаревшие results/ANR фиксируются отдельно |
| Git work in flight | Search/start | overlap и lock/I/O evidence | без trace не объявлять Git причиной |
| Device absent/profiling denied | Observe | точный environment blocker | только source analysis, без device PASS |

### 6.5 Decision Ledger
| Decision | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | --- | --- | --- |
| Главная проблема | user | Android loading/search, Fold 6, release, Git | high, прямо сообщено | не подменять Markdown-пакетом | Нет |
| Warm/cold неизвестны | agent | покрыть оба и process death | high | дополнительный benchmark, ограниченный 5 повторами | Нет |
| Baseline version | agent | фактически установленная, latest сейчас 1.33.0 | установленная пока unknown | auto-update может изменить baseline | Нет, metadata preflight |
| Кеш/индекс/другой scheduler | agent после evidence | решения сейчас нет | unknown | преждевременное изменение семантики | Не относится к этому EXEC |
| Device access | environment/user | ADB device должен быть доступен | сейчас отсутствует | реальные замеры недоступны | Это техническое условие, не approval scope |
| Tracker writes | user | только предложение ниже, без apply | high | изменение графа/статуса вне поручения | Отдельное решение после конкретного preview |

### 6.6 Runtime / Config / Data Contract Matrix
| Area | Source | Изменение | Проверка |
| --- | --- | --- | --- |
| Android install | package metadata устройства | нет | version/ABI/device |
| Release source | v1.33.0 + commit | нет | GH contents/blob IDs |
| Task data | phone Git space / isolated copy | исходник неизменен | run dataset identity/count |
| Git transport | существующая конфигурация | нет | timeline отдельно от local-read |
| Evidence | локальные отчеты/trace | диагностические артефакты в разрешенной read-only работе | run IDs и integrity |

## 7. Правила / инварианты
Read-only source analysis не доказывает runtime bottleneck. Benchmark отладочной/специальной сборки не переназначается установленному release. Mean/median/worst рассчитываются раздельно для одного устройства/APK/dataset/mode. Прогоны с известными помехами сохраняются и объясняются, а не выборочно удаляются ради порога. Нельзя force-stop с потенциально несохраненным вводом; первоначальные измерения делаются без редактирования задач и после подтвержденного сохранения текущих правок.

## 8. Точки интеграции и триггеры
Lifecycle Activity/process; storage read и создание TaskItemViewModel; фильтрация/проекции DynamicData; graph/tree refresh; Git overlap. Это точки наблюдения/гипотез, новые hooks не добавляются в этой фазе.

## 9. Модель данных / состояние
Схемы задач/settings не меняются. Run record содержит device/APK/dataset/mode/query IDs, lifecycle, timestamps, outcomes и warnings. Никаких новых полей production task JSON.

## 10. Rollout / rollback
Публикации и установки нет. Production data не очищается и не сбрасывается. Завершить временные observer/recorder процессы штатно; отчеты оставить для review. Не удалять lock/journal. Если нужен instrumentation APK, отдельная fix/diagnostic SPEC и отдельный установочный scope.

## 11. Приёмка и проверки
AC этой фазы — проверка готового диагностического артефакта, а не обещание уже ускоренного приложения.
| AC | Проверка | Evidence | Если недоступно |
| --- | --- | --- | --- |
| AC1: baseline однозначно идентифицирован | metadata/version/ABI/dataset identity | run metadata | устройство отсутствует → execution incomplete |
| AC2: cold/resume/death различены, есть повторяемые числа | минимум 5 валидных повторов/сценарий, median/worst | таблица + lifecycle/readiness trace | desktop/AVD не заменяют Fold6 |
| AC3: пользовательский search symptom воспроизведен либо проверенный non-repro | конкретный slow query, обычный/fuzzy, clear/rapid text, expected IDs | input/results/frame/ANR timeline | неизвестный пример добывается на месте без ручного анализа пользователя |
| AC4: data/Git источники сохранены | original data untouched; controlled freshness on isolated copy | before/after fingerprints где доступно, operations trace | недоступная integrity отмечается, эксперимент на оригинале не запускается |
| AC5: отчет позволяет выбрать следующий минимальный fix | evidence→hypothesis→decision, отдельные latency/freshness | bottleneck report и proposed fix scope | неизвестная причина → назвать одну следующую проверку; не объявлять цель достигнутой |

Preflight commands (план, сейчас доступны только device-list и release/source reads):
```powershell
& 'C:/Users/Kibnet/AppData/Local/Android/Sdk/platform-tools/adb.exe' devices -l
# SERIAL брать из выбранного Fold 6; при нескольких устройствах всегда -s.
adb -s SERIAL shell dumpsys package com.Kibnet.Unlimotion
adb -s SERIAL shell pidof com.Kibnet.Unlimotion
adb -s SERIAL shell dumpsys gfxinfo com.Kibnet.Unlimotion
```
Profiling/logcat commands выбираются по Android/API и permissions после preflight; broad system log dump не является fallback. Build/tests сейчас не применимы: план не меняет код/поведение. В будущей fix-SPEC обязательны relevant UI regression, targeted→full-run и сопоставимый device before/after по performance profile; TUnit-команды берутся из текущего runner/help, не из VSTest примера.

## 12. Риски / Expected User Review Objections
| Возражение | Мера | Status |
| --- | --- | --- |
| «Получим отчет, а телефон останется медленным» | Отчет — ограниченный prerequisite; AC5 требует следующего cause-specific fix scope; не называть диагностикой выполненное исправление | mitigated |
| «Опять кеш без выяснения причин» | Техническое решение не выбрано до baseline/trace | mitigated |
| «На Windows быстро, на Fold всё плохо» | Android acceptance требует именно Fold6 release; desktop только локализация hot path | mitigated |
| «Вмешательство испортит задачи/несохраненный текст» | No clear data/install/original writes; readiness без редактирования; no force-stop dirty input | mitigated |
| «Нужно помнить точный запрос и собирать логи вручную» | Агент готовит recorder/query probes; от владельца требуется лишь доступ и подтверждение сходства симптома | mitigated |

## 13. План
Baseline metadata → scoped device observations → воспроизведение и раздельные измерения → адресный source/trace анализ → report + минимальная fix-SPEC. При отсутствии устройства продолжается source preparation, физическая acceptance остается невыполненной. Одна profiling-проблема не разрешает установку иной сборки/очистку данных.

## 14. Открытые вопросы
Блокирующих продуктовых решений для определения диагностики нет. Фактическая версия телефона, dataset и точный slow query добываются preflight. Доступ к устройству — текущий environment prerequisite; без него нельзя завершить AC1–3. Перед изменением кода потребуется причина и отдельное подтверждение fix-SPEC.

## 15. Соответствие профилю
Performance: baseline/одинаковая конфигурация и hypotheses/evidence; optimization commits/test-after здесь не применимы, изменений нет. Desktop/UI: исходный обычный flow включен в storyboard; будущие UI changes требуют tests/visual evidence; нет фиктивного Android PASS из build/Headless.

## 16. Изменения файлов
| Файл | Изменение | Причина |
| --- | --- | --- |
| Эта SPEC | план и предложение tracker | конкретный reviewable scope |
В EXEC этого плана допустимы только локальные diagnostic artifacts вне task root. Product/test/script файлы не меняются.

## 17. Предложение учета: было → стало (НЕ применено)
Пока сохраняются title/status/importance/dates/duration/criteria/все relations каждого target. Предлагается только добавить в конец пользовательского описания следующие блоки. Exact request/preview по свежим ETags готовится при отдельном поручении применить пакет; эта SPEC не является CLI full witness и не разрешает apply.

### e74c5d1a — дополнение описания
```text
Ближайший цикл — 12.10.2026, приоритет П1 по решению владельца о главной боли.
Целевое устройство: Samsung Z Fold 6; установленная релизная версия, Git-хранилище. Владелец сообщает о долгой загрузке. Проверить cold start, resume и process death на фактически установленном релизе; актуальный опубликованный релиз на дату уточнения — 1.33.0. Старое сравнение v1.30.x/v1.31.0 остается историческим контекстом, а не новым baseline.
Сохраняются цели 5/1/5 секунд и обязательное измерение до выбора кеша. Связанный resume-сценарий: ec747dfc-2963-40ac-bd41-ffa1cbb26d47. План: specs/2026-10-12-android-startup-search-diagnostics.md. Это уточнение плана, ускорение еще не выполнено.
```
### 32c49f34 — описание из пустого в следующий текст
```text
Ближайший цикл — 12.10.2026, приоритет П1. Samsung Z Fold 6, установленный релиз Unlimotion, Git-хранилище. Владелец подтвердил: поиск долго реагирует или зависает. Устройство/версия и конкретный медленный запрос фиксируются перед baseline; актуальный опубликованный релиз сейчас 1.33.0, установленный номер еще не считан.
Воспроизвести исходный симптом, различить задержку вычисления/обновления UI и ANR. Проверить обычный/fuzzy поиск, быстрый ввод, очистку запроса, folded/unfolded; сохранить ожидаемые ID и раздельно измерить feedback/final-results/UI stall. Корректность результатов и остальные фильтры сохранить. План: specs/2026-10-12-android-startup-search-diagnostics.md. Изменения алгоритма после cause-specific SPEC; исправление еще не выполнено.
```
### ec747dfc — дополнение описания
```text
Сценарий возврата из фона включен в ближайший цикл диагностики e74c5d1a-c7a0-44f9-b7ee-37ca8f3318c6 на Samsung Z Fold 6 с релизной версией и Git. Отличить живой процесс, recreation и process death. Отдельная параллельная реализация кеша/сохранения состояния до этих измерений не планируется. Карточка не архивирована, содержимое и связи сохранены.
```
«Приоритет П1» здесь — предложенная метка плана; числовая Importance не меняется. Две основные карточки вместо новой дублирующей «Android оптимизация». Статусы проверенных пользователем прошлых функций оформляются отдельным пакетом: фраза «вроде всё проверил» сохранена как предварительное подтверждение, automatic Completed не предлагается.

## 18. Альтернативы
Немедленно писать кеш — низкая уверенность, freshness риск. Немедленно убрать fuzzy — меняет возможности поиска без trace. Только desktop benchmark — быстрее, но не отвечает исходному устройству. Выбран короткий device baseline с адресными гипотезами и границей диагностика/исправление.

## 19. Quality gate / review
### SPEC Linter
| № | Status | Evidence / применимость |
| --- | --- | --- |
| 1 | PASS | исходный Fold6 loading/search и outcome диагностики §1 |
| 2 | PASS | CLI карточки, release source blobs, device absence §2 |
| 3 | PASS | причина неизвестна, выбран evidence prerequisite §3 |
| 4 | PASS | разделение измерений и сохранение семантики §4 |
| 5 | PASS | diagnosis only, no product/tracker mutation §5 |
| 6 | PASS | агент/device owner и ограниченное внимание §6.1 |
| 7 | PASS | lifecycle/search/Git точки наблюдения §8 |
| 8 | PASS | повторность/readiness/state identity §6.2, §7 |
| 9 | PASS | denied profiling/device absence/non-repro §6.4, §11 |
| 10 | PASS | latency/freshness/frame/CPU и unavailable §6.2 |
| 11 | PASS | run record, production schema неизменна §9 |
| 12 | PASS | no migration/install, exact release identity §10 |
| 13 | PASS | observers cleanup, original untouched §10 |
| 14 | PASS | diagnostic AC1–5, fix 5/1/5 отдельно §11 |
| 15 | PASS | AC/evidence matrix + rapid/no-match/death §11 |
| 16 | PASS | actual adb path, -s, no broad log dump, stop §11 |
| 17 | PASS | metadata→baseline→report→fix §13 |
| 18 | PASS | user-owned решения и environment разделены §6.5, §14 |
| 19 | PASS | Expanded medium, неизвестный root cause §0 |
| 20 | PASS | perf/UI phase applicability §15 |

### Rubric
Цель/границы 5; AS-IS 2 (device не наблюдался, это предмет EXEC); дизайн диагностики 5; безопасность/rollback 5; проверяемость 5; автономность решений 5. Итого 27/30 — готов план диагностики, не реализация оптимизации.

### Role-Based Review Result (self-review)
Workflow PASS: ближайшая боль Android сохранена. UX PASS для плана: baseline обычного launch/search и Fold states; изменения layout отсутствуют. Tester PASS для плана: версия/PID/dataset, повторы, non-repro/ANR и unavailable distinction. Developer PASS: не выбран premature cache/index, release и checkout различены. Operations PASS: no install/clear data/original writes/extra Git network; private traces local.

### Post-SPEC Review
- Scope reviewed: эта SPEC; central owners quest/governance/review/linter/rubric, performance/UI profiles; CLI карточки и все parents; MainActivity/MainWindowViewModel release blobs; existing Windows loading harness и Roadmap benchmark. Planned product changed files: отсутствуют.
- Scope/Evidence pass: сообщения владельца, actual CLI/package/task reads, release/source refs и пустой adb device-list сопоставлены с заявленным AS-IS.
- Contract pass: AC этой фазы относятся к диагностическому output; ускорение и UI fix не заявлены. Exact tracker witness/authority отдельно от SPEC.
- Adversarial pass: latest не равно installed; resume не равно process death; desktop dataset не равен phone; Git network не входит в local freshness; throttle scheduler не доказывает ANR cause; schema lock и new clean-copy state не гарантируют безопасность force-stop dirty input.
- Role-Based pass: пять прикладных ролей выше; independent review не требуется для medium diagnostic-only без config/security/migration/public behavior изменений. Это самостоятельное review, не independent.
- Fix and re-review: уточнены отсутствие code/instrumentation в scope, опасность несохраненного текста, идентичность релизных исходников, критерий non-repro и отдельная authority tracker. После сохранения исправлен ошибочный diagnostic approval gate: quest-governance исключает read-only анализ. Re-review §5–7/§11/§17/Approval подтвердил границы.
- Findings: MEDIUM release/local-source mismatch — CLOSED проверкой release blobs; MEDIUM installed latest assumption — CLOSED preflight contract; MEDIUM original task writes через freshness — CLOSED isolated-only contract. Нет открытых findings плана.
- Depth checklist: scope/unrelated SPEC сохранены; AC→evidence заполнены; scenarios/ledger/objections имеются; UI/device validation будущая, не PASS; unsupported cause excluded; lifecycle/rapid query/dirty input negatives учтены; docs/changelog не меняются; hidden runtime/authorization changes исключены; manual challenge «быстрый desktop выдан за Fold» закрыт явным device gate.
- No-findings justification: устранены перечисленные контрпримеры, решение и output ограничены наблюдением; никакая неизвестная cause/platform permission не спрятана под фиктивный PASS.
- Stop decision: PASS для готовности этой diagnostic SPEC. Фактическая диагностика не выполнена, no device acceptance. Остаток: технический доступ к Fold6; перед fix-EXEC новая cause-specific SPEC.

### Post-EXEC Review
Не выполнен. AC1–5 не проверены на устройстве. После диагностики проверить actual report/commands/device identity/privacy, original data и соответствие исходному symptom; отсутствие устройства не заменяет обязательный AC.

## Актуальное подтверждение и границы
Прямое поручение пользователя в §0а разрешает последовательное выполнение, статусы/result через CLI, commit/push/PR/merge успешно принятой задачи и автоматическое подтверждение SPEC после review/исправлений. Ручная фраза «Спеку подтверждаю» повторно не требуется. После установления причины code delta оформляется и проходит применимое review, затем автоматически подтверждается в этом scope. Lease-bound CLI result/question выполнены по установленному контракту; ordinary apply для иных полей требует актуального full preview, но не нового разрешения уже порученного эффекта. Публикация релиза и установка на личное устройство остаются вне поручения. Технический blocker — отсутствие Fold6 и падение обеих AVD попыток; это не отсутствие разрешения.

## 20. Журнал действий агента
| Фаза / событие | Решение / основание | Evidence / остаток | Следующее действие | Решение человека | Артефакты |
| --- | --- | --- | --- | --- | --- |
| Scope 12.10 | Android loading/search назначены ближайшим направлением; Git/Fold6/release известны | CLI task/parents, release1.33, source blobs; device-list пуст | Продолжать разрешенные source checks; получить device baseline при доступе к телефону | Владелец сообщил slow/hang, latest release, Git; read-only analysis разрешен, fix approval пока неприменим | Эта SPEC; tracker не менялся |
| EXEC diagnostics 12.10 | Разрешенное последовательное выполнение начато с e74c5d1a; auto approval reviewed диагностического дополнения §0а | Claim/read-back InProgress; fresh origin/main fa1bfc03, отдельная ветка/worktree. Baseline UI4/4; копия3094 valid. Windows Init4.57s/~406MB, traced3.41s/~402MB — различные условия, не before/after. Fold6 отсутствует. AVD официальный1.33 не дал valid ready+action: исправлены собственные fixture permissions, QEMU host crash0xc0000005, software GPU попытка также потеряла ADB | Зафиксировать промежуточный result и техническую потребность в Fold6; не завершать/сливать первую как готовое исправление и не начинать следующую | User разрешил task writes/commit/push/merge после готовности и auto SPEC review; согласование повторно не запрашивается | Local-only D:/CodexArtifacts/android-task-loading-20261012/REPORT.txt, trace/logs/TRX; personal phone и original tasks для экспериментов не менялись |
| Tracker read-back | Сохранены промежуточный result, local artifact links и вопрос о техническом доступе | CLI success/didMutate, task status InProgress, execution AwaitingInput, одна question и точный owner/lease; live validate3094/isValid=true | После доступности Fold6 ответить в execution и продолжить ту же первую задачу; не claim/search task раньше acceptance/merge | Повторный scope/merge approval не нужен | D:/CodexArtifacts/android-task-loading-20261012/task-read-back.json, live-validation-after.json |

Текущее post-EXEC decision: NEEDS-FIX / неполное device evidence. Это не product fix PASS. SPEC дополнение готово, диагностические artifacts проверены, UI baseline4/4 и copied graph valid; обязательный исходный Fold6 scenario не измерен. Код приложения не изменялся; pipeline и cache hypothesis пока не выбраны. Технический доступ к телефону является необходимым следующим входом, не новым разрешением на уже порученную работу.

## 21. Возобновление: Fold6 доступен, измерение стоимости стадий
- mDNS/ADB Wi-Fi connection подтвержден: SM_F956B, Android16, arm64, installed1.33.0/621. Execution question отвечен через CLI, state Active, тот же owner/lease.
- Scoped release video показывает loading overlay примерно до 25s; Activity cold TotalTime2.804s не является ready metric. UIAutomator иногда возвращает null root и сохраняет предыдущий XML: старые timing observations не принимаются как точный ready/action PASS без проверки успешности dump. Видео и exit-info inspected: Android завершил PID9028 с EXCESSIVE CPU USAGE в фоне; причинность с текущим startup еще не установлена.
- Копия phone task files без .git/credentials получена в local artifact root; CLI validate копии:3094/isValid=true, ошибок/duplicates/reference/availability issues нет. Копия не является глобальным атомарным snapshot при внешних writers.
- Для cost decomposition создается disposable package org.unlimotion.loadingprobe: исходники только D:/CodexArtifacts/android-task-loading-20261012/android-probe, собственная directory /storage/emulated/0/UnlimotionFold6Diagnostic20261012/Tasks. Guard hardcoded path, no Git repository/remotes, no release app settings or original file writes. MANAGE_EXTERNAL_STORAGE app-op только этого диагностического package. Режимы read-graph/init/connect/preinit возвращают агрегаты time/CPU/allocations/taskCount; нативный TextView probe не является rendered Avalonia acceptance и не сравнивается как candidate installed release.
- Review дополнения: scope/evidence — source/trace/file copy и inspected video; contract — product code не меняется, benchmark isolation сохраняет оригинал; adversarial — scheduler/rendering/JIT/config отличаются от release и прямо отмечены, копия не доказывает original freshness; tester — expected task count3094, errors visible, fixed package/path; operations — own package, no INTERNET permission, secret-free config, отдельная копия. In-scope находка stale XML исправляется в recorder перед использованием результата. PASS для диагностического harness; не для ускорения. Independent reviewer для этого temporary measurement-only harness не требуется. Automatic approval применяется по §0а.
- Предыдущий аппаратный blocker устранен. Обязательные performance/freshness/UI acceptance первой задачи все еще не завершены; следующая задача не начата.

## 22. Продолжение на эмуляторе: lifecycle и локальная freshness (15Oct)

Пользователь поручил использовать эмулятор вместо недоступного телефона. Это разрешает продолжать native диагностику на собственной AVD; результаты остаются AVD evidence. Уточнение не заменяет целевые cold5/resume1/local-freshness5 секунд и не разрешает объявлять phone acceptance.

Reader experiment завершён отказом от retention: профильные gates пройдены, conservative plain whole-flow gates не пройдены. Product reader восстановлен в563ed4f5, targeted read12/UI7PASS, полный CI38746292374 идёт. Следующая диагностика выполняется обычной baseline APK C334E208, без observer, с тем же проверенным исходным reader и уже существующим UTF-8 migration fast path. Эта APK построена из0b4, product-код которого совпадает с563ed4f5; последующие изменения — tests/docs и отменённый эксперимент. Проверка final payload/SDK401/двух ABI сохранена.

Проверяем вопрос: действительно ли возврат живого процесса повторяет cold initialization, и отражаются ли доступные локально изменения. Нового product поведения в этой фазе нет. Публичный output — таблица cold/resume/freshness с median/worst, границами и оговорками; приватный — native PNG/video, PID/config/hash/timestamps и cleanup proof. Task1 остаётся активной до полного результата; task2/3 не начинаются.

Protocol до captures:

- Собственная AVD API34/x64/engine37.2.12/GLES-host/CPU3/RAM2560/1080x2400, свежий boot, затем одна continuous QEMU identity. Watchdog45m/минимум2GiB, точные package/path/AVD guards. Boot завершён и live focus не содержит системного ANR; старые historical ANR snapshots не используются. Никакого ввода при чужом окне или keyguard. Git/updates выключены в собственной копии; credentials исключены; original task-space untouched, приватная копия3094 содержит реальное пользовательское содержимое и не публикуется.
- Сначала один ordinary cold baseline с fresh PID до root list и открытия существующей Unlimotion карточки; новый boot N1 и прежняя warm-OS серия N4 (warm-up+3pairs) отчитаются раздельно. Не объединять их median/worst и не считать это пятью повторениями одного сценария; полный homogeneous cold N5 этим не закрывается. Разные cohorts не объединяются для A/B gain. Capture deadline165s; нет ready — failure/incomplete по факту. До выбора нового исправления cold≤5s остаётся FAIL по уже наблюдавшимся нижним границам.
- Live-process resume: пять возвратов после30s и пять после300s. Перед каждым уходом сохранить usable list с выбранной Unlimotion и закрытой pane. HOME только из strict own window, затем проверить, что app PID остаётся тем же. Фоновые интервалы не записываются180s recorder; новый bounded≤60s native recording начинается перед возвратом. Возврат — явный am start собственной activity; launch request использует host monotonic clock. Не смешивать process death/recreation с live resume: PID change, root lost, keyguard/systemANR/focus uncertainty делают attempt incomplete для live-process gate и фиксируются отдельно, без автозапуска новой cold загрузки под видом resume.
- После возврата немедленно получать native PNG через bounded adb exec-out screencap -p (без отдельной записи+pull), хранить host captureBegin/End. Повторять до пригодного root, максимум10s. Root detector только guard; вручную проверить выбранную задачу, отсутствие loader и корректность списка. Затем strict ownership guard, открыть карточку и подтвердить её native PNG с правильным ID/24children/раскладкой. Root-ready latency и action-to-card latency записывать отдельно. Root≤1s плюс eventual action не выдавать за подтверждение всего action flow≤1s. Для полного resume gate нужны границы полного action flow≤1s; иначе результат partial/inconclusive/FAIL по измеренным bounds, без смены требования.
- Точное сохранение selection/expanded/card state описать как наблюдение baseline; task2 state-preservation implementation сюда не включать. Host command/PNG overhead включён, interval uncertainty сохраняется; am-start return/Activity launch time не является ready metric. Нельзя выдавать frame gaps за непрерывную отзывчивость.
- Freshness: пять controlled title updates только файла root Unlimotion в собственной копии fixture; ID/relationships/status/остальные поля сохраняются. До каждой правки сохранить исходные bytes/SHA; имя файла выбрать из проверенного manifest по exact task ID, shell path строго bound к own fixture. Подготовить JSON со свежим ASCII marker в Title, publish через temporary non-task file + atomic rename в том же каталоге, сохранить host writeBegin/End. Первый PNG с ожидаемым новым заголовком после ручной проверки даёт upper bound от writeBegin; старый/неправильный текст не считается успехом. Capture максимум10s, freshness gate≤5s. Старый UI frame/неуспешный dump не использовать. В этой части выполнить ровно5 foreground updates при открытой карточке и same live PID. Для каждого clock только writeBegin→первый вручную подтверждённый ожидаемый заголовок, gate≤5s. Background-after-local-edit case в эти5 не входит и откладывается в отдельный диагностический protocol до parent completion; он остаётся явно непроверенным, не закрывать freshness-after-resume этими данными. Network/Git delivery не входит в local freshness.
- После каждого controlled update остановить только собственный package, восстановить точные исходные bytes через собственный atomic path, проверить original SHA и весь manifest3094. При uncertain write/cleanup — no acceptance, не повторять мутацию вслепую; сначала authoritative own-file hash/AVD identity. После restoration следующий trial имеет новый cold PID и это явно учитывается; не смешивать его с live-process resume series. На исходные пользовательские задачи mutations не распространяются.
- Все попытки и причины исключения сохраняются; retry допустим только вместо incomplete transport/environment attempt, не вместо валидного плохого времени. Не расширять gates задним числом. Нет полного набора/нельзя доказать deadline — неполная проверка, не завершение задачи. Для исключения host contention не запускать собственные builds/tests во время измеряемых foreground intervals.

Visual storyboard: `usable selected root → HOME30s/300s → samePID return → native usable root → open same card`; `original title → own atomic title update → expected marker visible → stop/restore/hash`. Layout не меняется. Главные objections: PID death не равен resume; static visible image не доказывает действие; baseline cache already in memory не оправдывает новую persisted cache subsystem; available local change не равно network sync.

Root preflight: scope/evidence/contract/adversarial/clarity — дизайн сохраняет исходные requirements и явно ограничивает claims; helper review обязателен до captures. Автоподтверждение amendment после independent review/fixes по действующему поручению. Сейчас pending review; новой product реализации не начато.

15Oct17:00 review corrections before captures: cold newbootN1/oldwarmOSN4 statistics strictly separated, no homogeneousN5 claim. Foreground freshness exactly5updates/writeBeginclock; background-after-edit explicitly pending separate protocol beforeparentcompletion. Private copy contains real3094taskcontent, originals untouched/credentials excluded. Resume action confirmation uses two card-positive native captures, samefixedcrop/RGBtolerance3/stablefraction99.9%, spacing>=250ms (separate diagnostic action clock, not the original1000ms plain qualification rule); preserve allfirstappearanceframes/manualcorrectlayout required. Cold collector uses direct binary native PNG, no new product code. Helper implementation review pending; nocaptures yet.

15Oct17:05 targeted design re-review PASS after N1/N4/casecounts/privacy corrections; automatic approval underuserinstruction. Focused helpers initial2MEDIUM (confirmedbackgroundclock/non-nullfocus and explicitbootcompleted) fixed; targetedhelperre-reviewPASS. Cold savesbootCompleted==1; HOMErequest separate from confirmedbackground/PIDclock, full30/300afterconfirmation; strictguards/seriescleanup preserved. Python staticcompilePASS; captures nowauthorized. Freshnesshelper remainsunimplemented/notapproved, no mutation permitteduntilitsreview. Parentacceptancepending.

Freshness-only helper preparation clarification before any freshness capture: exactly5foreground updates may use a separate fresh own-AVD boot/cohort with the same declared configuration, to avoid the bounded45m resume controller expiring. Each update follows its own fullyloaded ordinarybaselinecold initialization (newPID pertrial after exactrestoration); these preparatorycoldruns are not merged into priorcoldN1/N4 statistics. Within each write→visible measurement PID/QEMU remainsconstant; allfivefreshnesscases share their own continuousQEMU identity. Resume captures alreadyrunning onattempt13 are unchanged. Freshness helpers stillpendingreview; no fixturemutations yet.

15Oct17:16 freshnesshelper targeted review PASS after observationloopMEDIUMfixed: genericpixelcandidate cannotend10s collection; allobservations retained, deadline-limitedbinarycapture/lateframesexcluded, expectedTitle endpointmanualonly. No marker/noearlyframe alone isnot automaticdeadlineFAIL. Exactownatomicrestoreandcohortdesign reviewedPASS. Automaticapproval underuserinstruction; 5foregroundcasesauthorized, background-after-editstillpending. Resume originalseries stopped onshort4 blacksurface/noROOTwithin10s, threequalifiedshorts only; longcasesnotrun. Globalseriescompletefalse/ownappforceStopped, rawfailurepreserved; logs4424EGL_BAD_SURFACE/BufferQueueabandoned beforeourcleanup, causeappvsengineunresolved. Do not replacefailurebyrerunorclaimresumePASS.

15Oct17:48 baseline lifecycle/freshness audit: newboot cold root upper53.893s (N1, not merged with warm-OS series); three30s resumes root conservative upper1.227/1.897/2.944s and full return+action3.908/5.492/6.095s; fourth valid black retained, no300s cases. Root/card/native-last inspected for1–3, black native-last4 inspected. Foreground freshness5/5 expectedTitle/full card verified with exact3094 SHA restored; intervals ms [644,4016]/[1813,5243]/[2125,4202]/[761,2807]/[2199,4512]. Four conservative uppers≤5s; case2 spans5s, so not confirmed within5s and not proven actual lateness. Background-after-edit not measured. Attempt13 ownAVD stopped after collection. Private summary `lifecycle-foreground-freshness-baseline-summary.json`; raw metadata/title-review/PNG/video retained.

Rollback HEAD563ed4f5 CI audit: Tests38746292374 Main1540/1540, Headless66/66, zero skips, rootTRX counters/stage exit0/invocation IDs verified. CI merge-checkout tree83e2d60a equals HEAD tree; AndroidPkg38746292353 and CodeQL38746292371 success. This remains prior-head compatibility evidence, not acceptance of the following dependency migration.

Next bounded fix: [Android EGL resume SPEC](2026-10-15-android-egl-resume.md) proposes coherent Avalonia12.1.1, minimal released upstream21816fix. Original12.0.3 source has stale cached EGL handle; matching failure mechanism is strong hypothesis, candidate native proof pending. Independent advisory review/fixes/re-review PASS, autoapproved by standing user instruction; ownpackage APK built/decoded6payloads with exact12.1.1 OpenGL on bothABIs, no observers. UI hide/show5/5 and IconGenerator build passed. Full migration checks/native10cases pending; parent remains InProgress, no merge/next task.

## 23. Отражение внешнего изменения после фона: prospective protocol

Protocol reviewed/fixed/re-reviewed PASS и автоматически подтверждён по действующему поручению; implementation helpers отдельно reviewed PASS. Текущие результаты/неполнота серии указаны в журнале ниже. Product code не меняется. Это продолжение task1 freshness scenario, не начало task2/3.

- Ровно5 отдельных cases ordinary no-observer APK текущего проверенного framework candidate, собственный3094task-space, Git/updates/credentials выключены. Exact APK SHA/payload/source/runtime guards как§22; одна continuous QEMU identity для пяти cases. Каждый case начинается после отдельного fullyloaded cold preparation с новым PID, затем PID неизменен от HOME до конца проверки. Эти cold preparations не смешиваются с homogeneous cold timing acceptance.
- Before: проверить все3094 original SHA, сохранить original task bytes и original own root/card PNG. Выбрана задача a5ef0164-8def-417b-9b7d-d235516dd8c7, card закрыта, root видим. Changed bytes меняют только Title (`♾️Unlimotion BG-FRESH-N`), остальные JSON значения равны; stage скрытого nonce.tmp до начала timed clock.
- HOME из strict own window; подтвердить non-null чужой live focus без ANR/keyguard и прежний PID. Минимум30s после подтверждения background. Старт bounded60s screenrecord и все stage/preflight checks до timed write.
- Atomic rename staged file → target: сохранить writeBegin/writeEnd host monotonic clocks, digest опубликованного файла. Затем сразу guarded am start собственной activity с отдельными returnBegin/returnEnd clocks. Это foreground transition после изменения во время фона; не ждать дополнительные30s после write, чтобы сохранялась возможность проверить исходные5s от local availability.
- Весь10s observation window от writeBegin сохраняет bounded binary native PNG, late frames помечаются/не являются endpoint; generic pixel/ROOT heuristic не заканчивает collection. Снимать exact expected Title в root row (native1080×2400, ожидаемая область определяется before frame, не OCR guess). Manual review выбирает first qualified expected positive и последний preceding negative. Positive квалифицируется только при подтверждённом actual own non-starting MainActivity window/current app rendering; Android starting/transition/snapshot frames сохраняются, но исключаются из endpoint, даже если содержат marker. Изменённый BG-FRESH Title отсутствовал при HOME: это исключает прежний pre-write snapshot, но само по себе не доказывает foreground rendering.
- Два независимых interval: write→fresh root (lower=max(0,last post-write negative.captureBegin−writeEnd), upper=first expected.captureEnd−writeBegin); return→fresh root (lower=max(0,last post-return negative.captureBegin−returnBegin), upper=first expected.captureEnd−returnBegin). При отсутствии post-trigger negative lower0. Missing expected → interval unavailable, не0. Parent freshness≤5s проверяется по write clock; return interval отражает current draw и отдельно сопоставляется с resume target, но не доказывает interactive readiness один лишь cached/transition frame.
- Deadline verdict отдельно от observation/functional result: upper≤5s — PASS; доказательный lower>5s — FAIL; пересечение5s, missing qualified endpoint или недостаточное наблюдение — inconclusive/no acceptance. Отсутствие expected Title в полном окне сохраняется как observation/functional failure, но само по себе не доказывает latency>5s. Raw evidence сохраняется, повтор ради улучшения запрещён; технически валидная неопределённая попытка не заменяется новой.
- После10s открыть ту же card обычным UI действием и получить2settled native frames с правильным ID/новым Title/24children. Это дополнительный functional proof, отдельный action clock. Затем own force-stop ДО reconciliation, сохранить authoritative current hash/bytes, exact atomic restore original, проверить все3094 SHA/temp absence. При timeout/outcomeUnknown сначала читать current hash, не повторять mutation вслепую.
- Technical incomplete/identity/PID/ANR/restore failure останавливает series; все attempts сохраняются. Manual title/latency adjudication обязательна, collectionComplete не равен acceptance. No build/test load на host во время cohort. Собственная AVD новый boot/cohort разрешён, конфигурация та же; original apps/task-space не трогаются.
- Output:5case clocks/intervals/median/worst или explicit missing/incomplete, native root/card/video+PID/config/hash/restore proof. Cold external-change scenario и homogeneous coldN5 до parent completion остаются отдельными открытыми checks; background5 не закрывает их.

15Oct18:36 §23 design targeted re-review PASS after two MEDIUM fixes: qualified current draw requires actual own non-starting window; deadline verdict separates PASS/FAIL/inconclusive from missing observation. Automatic approval by standing user instruction. Background helpers remain pending independent implementation review; no background mutations/captures yet. Foreground helper review PASS and six isolated fake-device negative controls PASS; actual full-manifest proof remains required per case.

15Oct18:50 current evidence: candidate12.1.1 native resume10/10 collected on one PID4252/QEMU111696, five30s and five300s pauses. All22 saved guest epoch markers unique/ordered, matching-own-PID HOME→card windows contain no EGL_BAD_SURFACE/0x300d/BufferQueue abandoned;3094SHA before/after equal. Root/card/native video final frames inspected for all10 plus cold; correct task ID/24children, baseline black not reproduced. Root appearance sometimes cached transition, no interactive≤1s claim. Separate cold newbootN1 upper78.241s, original5s unmet. Candidate foreground5/5 manually expectedTitle/fullcard qualified; intervals ms [459,2797]/[549,2269]/[1641,3310]/[1625,3796]/[1635,3370], median upper3310/worst3796; all3094 original hashes restored, own continuous QEMU92912. Raw/summary paths in private DROOT; FG native video postprocessing pending after quiet AVD stop. These checks do not cover background or cold external changes, nor physical Fold6.

§23 helper implementation reviewed/fixed/re-reviewed PASS: explicit foreign non-null live focus/samePID/keyguard/ANR checks immediately before publish and return; post-write guard overhead preserved in write→fresh clock. Ten isolated fake-device recovery/process/focus controls PASS. Background5 series started on existing own AVD15 after FG collection complete; same configuration/continuous QEMU92912, separate cohort identity, fresh PID for each preparation. No build/test load during captures. Manual current-draw/expectedTitle/latency and full3094 restoration still required; collectionComplete alone is not acceptance.

Fresh preimplementation profile preparation reviewed PASS after publisher ownership/child-cleanup fixes: new aligned12.1.1 target, frozen observer/exact-restorable overlays, SDK401/default twoABI/AOT, explicit clean, six actual packaged payload PE proofs with expected observer1/1/0. Build forbidden while own measurement AVD alive; no profile build/runtime yet. Lazy command product implementation remains unstarted. Task1 InProgress/Active, parent5/1/5 and final current-head compatibility gates open; no merge/next-task claim.

15Oct19:06 update: exact testedHEAD47b9f6c1 Tests38759452817 PASS Main1541/1541 and Headless66/66, zero skips, rootTRX/stageexit0/invocation verified; mergecheckout8c907d29 treeabb41c8d equals HEAD tree. AndroidPkg38759452796/CodeQL38759452882 success. Independent raw/visual audit EGL10+FG5 PASS. FG5 and BG3 native videos postprocessed/lastframes inspected; sparse VFR PTS are not wall-clock latency evidence. Separate `egl1211-freshness-native-video-review.json` supersedes earlier pending notes.

Background cohort incomplete3/5: cases1–3 expectedTitle/current own non-starting BASE_APPLICATION/HAS_DRAWN/visible window bracket and correct fullcard/native-last inspected; write→fresh intervals ms [2383,4597]/[1931,3840]/[0,3824]. Case4 staged upload failed ADB connection to127.0.0.1:5037 before HOME/timed write; no writeBeginClock, original task hash remained exact. Collector cleanup and independent driver recovery both verified3094 originals; cleanupUncertain=false. No case5/no replacement retry; raw failure preserved, seriescollectionComplete=false. Independent audit PASS for partial evidence/disposition, not BG5 acceptance or proof of product regression. OwnAVD15 stopped after cleanup. Original phone/task-space untouched.

Baseline12.1.1 selected for the next bounded experiment using CI+EGL10+FG5, independently reviewed as justified. Parent BG5/cold external-change/absolute5-1-5 remain open. Reviewed diagnostic profile publisher started only after ownQEMU exit; clean successful, SDK401/twoABI/AOT profile build running. New profile collector reviewed/fixed/re-reviewed PASS (including whitespace-only manifest tolerance with all nonempty strict checks retained). No profile runtime or lazy command implementation yet. First tracker task remains InProgress/Active, PR draft, no merge/no next task.

15Oct19:32 checkpoint supersedes preceding in-progress notes: fresh12.1.1 profile APK built and six final packaged PEs proved; diagnostic PID4296 profile completed with3094 originals,49 batches, successful nonexpired/nondropped footers. Models17.740s/251225224bytes, wholeVM81.948s/1051806072bytes; root/card/native final frame reviewed. One diagnostic only, not paired evidence/warm-up. OwnAVD16 stopped before product edits.

Reviewed/autoapproved lazy Save/Reload experiment now implemented with disposal admission safeguard; source-switch36/36, models/storage/lifecycle124/124, existing12 card UI cases and new real first-edit/save/Reload UI case passed. Rendered PNGs and persisted results inspected. Independent implementation review PASS, no findings. Detailed execution/test history in lazy SPEC§21–22; failed intermediate tests preserved. New exact-head full CI and candidate profile/plain provenance/paired retention gates pending. Parent5/1/5, complete BG5 and cold external-change still unmet; task1 remains Active/InProgress, no merge/no task2/3.
