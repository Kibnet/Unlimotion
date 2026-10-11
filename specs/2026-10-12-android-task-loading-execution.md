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
Пользователь разрешил записи статуса/результата через CLI, отдельные ветки/worktrees и commit/push/PR/merge каждой успешно принятой задачи перед следующей; SPEC подтверждается автоматически после успешного review и исправлений. Это актуальное разрешение заменяет требование ручного exact approval для этого scope. Публикация релиза и установка на личный телефон не поручены.
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
