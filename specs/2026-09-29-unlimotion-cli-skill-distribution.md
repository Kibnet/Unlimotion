# Распространять скилл Unlimotion CLI из репозитория Unlimotion

Expanded SPEC: публичный агентский workflow и GitHub-доставка требуют review по quest-governance, quest-mode и review-loops. До нового exact approval эта SPEC — единственный изменяемый файл в репозитории Unlimotion.

## 0. Метаданные

- Тип и профиль: delivery-task, product-system-design; task-specific skills skill-creator, openai-docs и skill-installer; tool-heavy overlay.
- Владелец результата: пользователь. Исполнитель: Codex.
- Поверхность: Codex, repo-scoped и personal skills. Фактические model ID/reasoning/client version не раскрыты; не выводить их из baseline каталога.
- База: Unlimotion origin/main и tag v1.32.0 = d1e6702fa37382f57e5a8210313f88b1f8c59d92 (проверено 2026-09-29); установленный global Unlimotion.Cli = 1.31.1.
- Источник уже проверенного содержимого: три файла скилла из закрытого ошибочного PR в Agents.md. SHA-256: SKILL.md = 6B92084F351FE4733ACC18EA41FED540D0C03B11BE6F2B9558955FE7CECE16C1; agents/openai.yaml = F733D9D8CD5F13688F717D25674D161044EB888A7DB261880F9D631A84B9FF1A; references/apply-v1.31.1.md = 79DB19F9677DF8D5AB417C2642370F4B0E0835F1153F191E51EFEC248C00EA0B.
- Неверная поставка: Agents.md PR #1 закрыт 2026-09-29, в main Agents.md скилл не попал. Его ветка и личная копия не удаляются этой задачей.
- Публикация: после approval создать ветку по GitHub policy от свежего Unlimotion origin/main и открыть PR именно в Kibnet/Unlimotion; merge/release не разрешены.

## 1. Overview / цель

Исходный запрос: создать скилл для Unlimotion CLI, положить его в репозиторий и описать установку; уточнение пользователя: имелся в виду репозиторий Unlimotion, потому что CLI находится там. Предыдущая поставка в Agents.md не достигает этого результата.

Успех: один и тот же проверенный операционный скилл лежит в корневом .agents/skills/unlimotion-cli/ репозитория Unlimotion; пользователь видит, как он работает в клоне и как установить его из Kibnet/Unlimotion для других проектов; PR открыт в нужном репозитории от актуального main. CLI, задачи и релиз не изменены.

Stop rules: не менять CLI/task-space и не устанавливать пакет; при semantic drift между источником скилла и текущим main вернуться к SPEC до копирования; не называть pending CI зелёным; PR не сливать.

## 2. AS-IS и проблема

- В Unlimotion нет .agents/skills. README.md и README.RU.md имеют разделы CLI, а src/Unlimotion.Cli/README.md — подробный контракт и установку самого dotnet tool. Установки скилла там нет. Краткие верхнеуровневые README всё ещё описывают выбор пространства без появившегося в main приоритета UNLIMOTION_TASKS.
- Код/документация CLI на main поддерживают version/help/context/search/apply schema/example/inspect и расширенный task snapshot; skill уже ограничивает эти возможности фактически запущенным 1.32.0 и сохраняет fallback 1.31.1.
- По официальной документации OpenAI Codex ищет repo skills в .agents/skills от рабочего каталога до корня репозитория; личная установка может быть отдельной. Если имена совпадают, копии не сливаются и могут обе отображаться.
- В Unlimotion main есть отдельный untracked файл specs/2026-09-28-task-importance-value-visibility.md. Он не относится к задаче и не должен попасть в PR.
- Корневая проблема — неверное расположение источника распространения: PR в каталоге инструкций не даёт скилл из репозитория продукта и CLI.

## 3. Цели и Non-Goals

- Распространять skill из Unlimotion без смысловой переработки уже проверенных инструкций.
- Дать проверяемую, не перезаписывающую действующую установку инструкцию для Codex и явное различие между установкой скилла и CLI.
- Сохранить двуязычность верхнего уровня и основной русский CLI README.
- Не менять исходники CLI, схемы, task-space, глобальный dotnet tool, личную копию скилла, версии, release, Agents.md main или постороннюю SPEC.
- Не удалять ошибочную remote-ветку Agents.md без отдельного решения; PR там уже закрыт. Не выполнять merge нового PR.

## 4. TO-BE и распределение ответственности

| Артефакт | Изменение | Инвариант |
| --- | --- | --- |
| .agents/skills/unlimotion-cli/SKILL.md | Побайтово перенести проверенный текст | SHA-256 совпадает с источником; capability gate 1.32.0 / 1.31.1 сохраняется |
| .agents/skills/unlimotion-cli/agents/openai.yaml | Перенести существующий UI metadata | Не добавлять политику/зависимости без запроса |
| .agents/skills/unlimotion-cli/references/apply-v1.31.1.md | Перенести legacy reference | Ссылка из SKILL.md разрешается |
| src/Unlimotion.Cli/README.md | Раздел «Скилл Codex»: repo discovery, установка из GitHub, no-overwrite, CLI prerequisite | Не выдавать скилл за сам CLI |
| README.RU.md и README.md | Короткие соответствующие разделы со ссылкой на подробную инструкцию и точечное исправление приоритета UNLIMOTION_TASKS | Обе локализации согласованы; не переписывать остальные разделы |
| Эта SPEC | Разрешения, acceptance, review, delivery journal | Не переносить большие личные исторические SPEC из Agents.md |

Рекомендуемый путь вне клона: вызвать skill-installer для GitHub repo Kibnet/Unlimotion и path .agents/skills/unlimotion-cli. Официальная документация указывает USER path $HOME/.agents/skills, тогда как локальный helper по умолчанию пишет в $CODEX_HOME/skills (обычно ~/.codex/skills); эти пути не считать одним и тем же. В копируемой команде PowerShell явно задать --dest для документированного USER path, не перезаписывая существующую папку. Существующая personal copy в другом каталоге может дать дубликат имени. После merge default main станет доступен без branch pin. Проверка GitHub-инсталляции возможна только после push: использовать --ref имени опубликованной ветки и изолированный --dest, не личный каталог; успешный smoke обязателен до открытия ready PR. Этот smoke подтверждает скачивание и содержимое, не реальное обнаружение личной копии на компьютере пользователя.

Скилл не требует новой упаковки как plugin: пользователь выбрал репозиторий продукта, а официальная repo-discovery поддерживает этот сценарий. Дубликаты с существующей personal/Agents branch копией объяснить, но не удалять автоматически.

### User-Observable Scenarios

| Сценарий | Триггер | Ожидаемый результат | Evidence | AC |
| --- | --- | --- | --- | --- |
| Работа в клоне | Codex запущен в Unlimotion | Скилл доступен из repo path, ссылка на reference существует | file inventory, validator, fresh Codex discovery smoke | AC1 |
| Другой проект | Пользователь следует инструкции GitHub install | Установщик скачивает все три файла в явно выбранный personal destination без перезаписи; фактическое обнаружение вне клона пользователь проверяет в своей новой сессии Codex | temp install из PR branch, negative occupied test и официальный USER path; без claim о чужой активной среде | AC2 |
| Разные версии CLI | У пользователя 1.31.1 или подтверждённый 1.32.0 | Скилл выбирает возможности запущенного бинарника, не меняет задачи без поручения | hash identity, prior/focused synthetic smoke | AC3 |
| Review | Пользователь открывает новый PR | PR в Kibnet/Unlimotion, только согласованные файлы, fresh-main ancestry | PR API, remote diff, CI status | AC4 |

### State / interaction

| Состояние | Действие | Результат | Негативный случай |
| --- | --- | --- | --- |
| Repo checkout | Codex ищет skills | Читает .agents/skills/unlimotion-cli | Вне repo требуется personal install |
| Personal destination свободен | installer получает GitHub path | Копирует три файла | Недоступный ref/path — ошибка, не утверждать установку |
| Personal destination занят | повтор install | Отказ без overwrite | Старую копию не удалять автоматически |
| CLI 1.31.1 | выполнение skill | versioned fallback | Не звать новые команды без capability evidence |

### Decision Ledger

| Решение | Owner | Выбор | Confidence | Риск | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Репозиторий | пользователь | Kibnet/Unlimotion | 1.0 | прежний ошибочный PR | Нет: пользователь уточнил |
| Путь | agent | корневой .agents/skills/unlimotion-cli | 0.99 | не обнаружится в подпапках | Нет: официальный контракт |
| Контент | agent | exact copy трёх файлов | 0.98 | смысловой drift | Нет при hash/source check |
| Документация | agent | подробная CLI README + короткие ссылки EN/RU | 0.9 | недоступная установка | Нет: сохраняет оба сценария |
| Git delivery | пользователь | ветка + PR от fresh main, без merge | 1.0 | публикация не туда | Нет: прежний запрос PR + коррекция repo |

### Runtime / data

| Контракт | Source of truth | Изменение | Совместимость / проверка |
| --- | --- | --- | --- |
| CLI capabilities | Unlimotion main@d1e6702f и фактически запущенный бинарник | нет | source/hash + skill capability gate |
| Skill discovery | официальная документация OpenAI | новый repo folder | path/relative reference check |
| Installed CLI | dotnet tool list --global = 1.31.1 | нет | не заявлять 1.32.0 установленной |
| Task files | пользовательское локальное пространство | нет | не обращаться к live data |
| Git delivery | origin/main и GitHub PR | новая ветка/PR | fetch/ancestry, diff allowlist, PR read-back |

## 5. Совместимость, rollout и rollback

Добавление repo skill не меняет CLI binary и локальные задачи. Personal installation — отдельное действие пользователя; инструкция не удаляет и не перезаписывает существующую копию. Дубликаты имени возможны до очистки старой personal/Agents branch копии. Для rollback PR достаточно revert новых файлов/документации; личная копия вне репозитория не затрагивается. Ошибочный Agents.md PR уже закрыт, но его ветку сохраняем для аудита.

## 6. Acceptance Criteria и проверки

| AC | Проверка | Негативный случай / stop |
| --- | --- | --- |
| AC1: skill в Unlimotion repo, корректен и идентичен источнику | inventory, SHA-256 трёх файлов, PYTHONUTF8=1 quick_validate.py, relative link check; свежий Codex CLI read-only smoke из checkout проверяет repo discovery | mismatch или отсутствие repo discovery — не публиковать |
| AC2: установка описана и скачивает skill из правильного GitHub repo | до push — review трёх README, включая явный USER --dest и одинаковый порядок --tasks → UNLIMOTION_TASKS → desktop; после push, до PR — temp installer с --repo Kibnet/Unlimotion --path .agents/skills/unlimotion-cli --ref опубликованная ветка --dest temp; повтор на занятом destination | partial/overwrite, не скачиваемый ref или противоречие CLI README — NEEDS-FIX, ready PR не открывать; temp dest не доказывает личную Codex discovery |
| AC3: main/legacy contract не изменён | main/tag SHA, installed version, hash equality, focused frozen synthetic scenarios; никакого live task-space | новые команды для 1.31.1 или слепая запись — FAIL |
| AC4: PR от актуального Unlimotion main и без постороннего diff | git fetch/rebase, git diff --check, status/name allowlist, PR API read-back, CI status | wrong repo/base, unrelated SPEC, failed mandatory check — не объявлять готовым |
| AC5: исправлена прежняя ошибочная поставка | Agents.md PR #1 закрыт, main Agents.md без skill; новый PR прикреплён к задаче | не стирать пользовательскую personal copy/историю |

Набор проверок: быстрый skill validator, repo discovery, link/installer smoke и staged Git diff — обязательны; тесты .NET CLI/UI не запускаются для byte-identical skill + Markdown без изменения кода, если не выявлен semantic drift или иной обязательный repo gate. Проверка personal discovery на чужом компьютере не проводится: документация задаёт целевой путь по официальному контракту, а пользователь после установки проверяет наличие skill в новой сессии. После PR различать локальный PASS, pending CI и merge. UI visual/video не применимы: интерфейс приложения не меняется. Performance не применима: runtime-код не меняется.

## 7. Риски, objections, план

| Вероятное замечание | Почему | Митигирование | Статус |
| --- | --- | --- | --- |
| «Опять не тот репозиторий» | уже был ошибочный PR | remote/PR owner Kibnet/Unlimotion проверять до и после публикации | mitigated |
| «Как установить вне клона?» | repo discovery ограничен checkout | GitHub skill-installer команда и temp install smoke | mitigated |
| «У меня всё ещё 1.31.1» | исходники main новее global tool | явно разделить skill и CLI; оставить legacy reference | mitigated |
| «Почему две записи skill?» | personal и repo копии могут сосуществовать | объяснить без удаления данных | mitigated |

Порядок после exact approval: свежий fetch и branch от origin/main → exact copy skill в Unlimotion → docs EN/RU/CLI → local static/hash/synthetic checks и scoped review → commit/push → remote temp install из опубликованной ветки и negative occupied-destination check → только после PASS открыть ready PR и выполнить read-back. Если remote install не проходит, исправить ветку, повторить затронутые проверки и push, не выдавая незавершённое за PASS. При drift origin/main rebase и повтор затронутых проверок. Открытых решений пользователя до EXEC нет.

## 8. Профиль, альтернативы, файлы

Product-system-design: граница системы — инструкционный skill поверх CLI, не изменение CLI API; security — no blind retry/no task writes в skill и no-overwrite в установке; конфигурация — repo/personal scope разделены. Skill-creator требует discriminating frontmatter, минимальные resources и validation; existing три файла этим требованиям соответствуют.

Альтернативы: оставить в Agents.md (противоречит уточнённой цели); положить в src/Unlimotion.Cli (не даёт корневой repo discovery); выпускать plugin (другая модель распространения, не запрошена). Выбран root repo skill + GitHub installer.

## 9. SPEC Linter, Rubric и post-SPEC review

| № | Result | Evidence |
| --- | --- | --- |
| 1 | PASS | исходное поручение и исправленный repo outcome |
| 2 | PASS | inventory Unlimotion, Agents PR, installed CLI и docs |
| 3 | PASS | root cause — неверный repo distribution |
| 4 | PASS | exact skill, install guide, correct PR |
| 5 | PASS | CLI/task/personal/merge и unrelated SPEC исключены |
| 6 | PASS | ответственность по шести целевым файлам |
| 7 | PASS | Codex discovery и GitHub installer |
| 8 | PASS | hashes, capability gate, no-overwrite |
| 9 | PASS | missing ref, occupied destination, version mismatch |
| 10 | PASS | performance неприменима: runtime-код не меняется |
| 11 | PASS | только repo skill/docs/SPEC и Git branch/PR |
| 12 | PASS | legacy 1.31.1 и personal copy сохраняются |
| 13 | PASS | revert PR, без personal deletion |
| 14 | PASS | AC1–AC5 наблюдаемы |
| 15 | PASS | AC→hash/validator/install/Git evidence |
| 16 | PASS | команды, stop при drift/failure |
| 17 | PASS | fresh-main → copy → docs → checks → PR |
| 18 | PASS | ledger, нет открытых решений |
| 19 | PASS | expanded из-за публичного agent behavior/PR |
| 20 | PASS | product-system-design и skill-creator |

Итог linter: ГОТОВО, без FAIL/PARTIAL. Rubric: цель/границы 5, AS-IS 5, дизайн 5, безопасность 5, проверяемость 5, автономность 5 = 30/30; это не заменяет exact approval.

| Role | Applicability | Review question | Verdict |
| --- | --- | --- | --- |
| Domain workflow | да | Не выдаёт ли skill код/PR за выполненную задачу? | PASS: capability/read-back boundaries сохраняются |
| UX/copy | да | Пользователь найдёт установку и различит skill/CLI? | PASS: подробный и короткий пути |
| Tester | да | Покрыты ли source, occupied destination и wrong repo? | PASS: AC1–AC5 |
| Developer/architect | да | Не вводится ли новый CLI contract без source? | PASS: exact copy и source gate |
| Delivery/security | да | Верны ли repo/base, scope, rollback и no-overwrite? | PASS: проверка до/после PR |

### Post-SPEC Review

- Status / stop: PASS после targeted re-review; можно запросить новую точную фразу approval, EXEC ещё не начат.
- Scope/Evidence: прочитаны central QUEST/review/Git/commit/testing owners, product-system-design, skill-creator/openai-docs/skill-installer; Unlimotion status/main/tag/README/CLI README, Agents skill hashes и закрытый ошибочный PR; официальная документация OpenAI о repo skills; planned diff и отдельная untracked SPEC.
- Contract: правильный repo и GitHub installer закрывают уточнение пользователя; previous Agents PR закрыт. Никаких CLI/task/personal changes и merge. AC1–AC5 проверяют видимый результат и отрицательные случаи.
- Adversarial risk: возможны wrong repo, stale main, неразрешённый relative reference, занятая personal установка, разные CLI версии и unrelated untracked SPEC. Для каждого есть проверка/stop. GitHub installer не может проверить неопубликованную ветку, поэтому remote smoke стоит после push и до ready PR. Temp destination доказывает копирование, но не personal discovery: документация явно выбирает официальный USER path, а fresh Codex smoke отдельно проверяет repo discovery. Контент skill переносится byte-identical, поэтому semantic drift не подразумевается; после копирования подтверждать hash.
- Role-Based: таблица выше. Никакого UI-поведения приложения; video evidence не требуется. Отдельный reviewer не менял файлы, но его effective sandbox = danger-full-access, поэтому это writable adversarial fallback, не технически read-only independent review.
- Depth checklist: scope — шесть целевых файлов + SPEC, unrelated 2026-09-28 исключена; acceptance — AC1–AC5; evidence — source/hash/install/PR read-back; unsupported claims — CI/personal install/v1.32.0 не выдаются за выполненные; regression — wrong path/duplicate/version; docs — EN/RU/detail; hidden contract — не менять skill semantics; manual challenge — команда installer должна реально работать с public PR branch, не только с локальными файлами.
- Fix and re-review: reviewer нашёл HIGH по неверному порядку remote smoke и MEDIUM по смешению USER path с helper default. После исправления он перечитал затронутые разделы и подтвердил оба закрытыми; отдельно исправлен stale review/journal wording. Текущий риск: фактический GitHub install smoke доступен лишь после push, а personal discovery вне чужого клона не проверяется здесь; не объявлять AC2 закрытым раньше копирования.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | validation sequence | GitHub installer не видит ветку до push | Локальный gate → push → remote smoke → ready PR | fixed, targeted re-review PASS |
| MEDIUM | destination/discovery | Temp --dest доказывает копирование, не личную Codex discovery; helper default отличается от официального USER path | Явный USER --dest, ограниченный claim, отдельный fresh repo discovery smoke | fixed, targeted re-review PASS |

- No-findings justification после исправлений: незакрытых BLOCKER/HIGH/MEDIUM не осталось; AC2 не притворяется проверкой личной среды, AC1 требует свежий repo discovery, remote smoke стоит после push. Все пять ролей выше сверены с этим изменённым порядком. Отдельный review был writable fallback; это остаточное ограничение evidence, не ложная метка read-only.
- Post-EXEC Review: локальная часть PASS: файлы скилла совпали с источником по трём SHA-256; `quick_validate.py` PASS; ссылки разрешаются; fresh `codex exec` обнаружил repo copy и отдельную personal copy. Синтетический сценарий 1.31.1/1.32.0/потерянный create ответил согласно контракту. Read-only режим для сценария столкнулся с `helper_sandbox_lock_failed` ещё до чтения файла, поэтому повтор выполнен как writable fallback с явным запретом записей и последующей проверкой `git status`; это не технически read-only review. После push installer скачал 3/3 файла из опубликованного коммита `3390e777` с теми же SHA-256; повтор на занятом destination отказал без изменения SKILL.md. PR [Kibnet/Unlimotion#310](https://github.com/Kibnet/Unlimotion/pull/310) открыт от `main`, ready, diff содержит только семь согласованных файлов; CI на момент read-back pending. Не считать pending CI успешным и не утверждать personal discovery вне этого клона. Временная папка smoke в `%TEMP%` осталась: политика среды отклонила рекурсивное удаление точного проверенного пути; она не находится в репозитории и не является личной установкой.

## Approval

Новая точная фраза «Спеку подтверждаю» получена 2026-09-29 после этой post-SPEC review. EXEC разрешён в пределах разделов 3–7; это не разрешение на merge или release.

## 10. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC / исправление цели | Пользователь уточнил, что source of distribution — Unlimotion, не Agents.md | Ошибочный Agents.md PR #1 закрыт; Unlimotion main@d1e6702f, global CLI 1.31.1; unrelated untracked SPEC сохранена | завершить SPEC и запросить approval | уточнение получено, exact approval новой SPEC ещё нет | эта SPEC |
| SPEC / design and review | Выбран exact-copy repo skill с GitHub installer и двуязычной ссылкой | официальная repo discovery, installer help, CLI docs и hashes сверены; AC1–AC5 составлены | получить отдельный review | ожидается | эта SPEC |
| SPEC / review closure | Исправлены HIGH по порядку remote smoke и MEDIUM по personal destination/discovery; targeted re-review PASS | reviewer effective danger-full-access, только writable adversarial fallback; full post-SPEC PASS после исправлений | ждать «Спеку подтверждаю» для EXEC | ожидается | эта SPEC |
| EXEC / approval | Пользователь повторно подтвердил SPEC именно для Unlimotion | exact phrase получена 2026-09-29; origin/main = d1e6702f после fetch | перенести скилл, документацию и проверить AC1–AC5 | «Спеку подтверждаю» | эта SPEC |
| EXEC / local validation | Скилл перенесён побайтово; README EN/RU/CLI обновлены в согласованном scope | 3/3 SHA-256, `quick_validate.py`, links, fresh repo discovery и frozen scenario PASS; read-only sandbox error потребовал writable fallback для сценария, без фактических записей | staged review, commit/push, remote installer smoke | не требовалось | skill, README EN/RU/CLI, эта SPEC |
| EXEC / remote install | Опубликованный commit `3390e777` установлен штатным helper в изолированный `--dest` | 3/3 hash PASS; occupied destination отклонён без overwrite; временная папка сохранена из-за политики удаления | открыть PR и прочитать repo/base/files/checks | не требовалось | remote branch, temp smoke |
| EXEC / PR delivery | Открыт ready PR #310 в `Kibnet/Unlimotion`, база `main`; merge не выполнялся | GitHub read-back: 7 scoped files, head `3390e777`, checks queued/in progress | зафиксировать evidence; сообщить пользователю pending CI | не требовалось | PR #310, эта SPEC |
