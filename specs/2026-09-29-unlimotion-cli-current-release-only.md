# Скилл Unlimotion CLI только для актуального контракта

Expanded SPEC: меняется опубликованное поведение агента и состав уже открытого PR; форма выбрана по `quest-governance`, review — по `review-loops`. До нового exact approval разрешена только эта рабочая SPEC.

## 0. Метаданные

- Тип/профиль: `delivery-task`, `product-system-design`; tool-heavy overlay, `skill-creator` и `openai-docs`.
- Владелец результата: пользователь. Исполнитель: Codex. Масштаб: medium, public instruction behavior.
- Поверхность: repo-scoped Codex skill в Unlimotion; personal copies и другие хосты не меняются. Фактические model ID/reasoning основного агента не раскрыты; behavioral smoke должен использовать одинаковую доступную конфигурацию до/после и записать её.
- База: `Kibnet/Unlimotion` `origin/main` = release `v1.32.0` = `d1e6702fa37382f57e5a8210313f88b1f8c59d92`, проверено по remote и GitHub release 2026-09-29. Последнюю версию перепроверить перед EXEC/push.
- Delivery: открытый [PR #310](https://github.com/Kibnet/Unlimotion/pull/310), ветка `feat/cli-operational-skill`, head `2472761d`; текущие checks green на момент SPEC. Не открывать дубль и не выполнять merge/release.
- Связь: эта SPEC заменит [первоначальную SPEC распространения в коммите `2472761d`](https://github.com/Kibnet/Unlimotion/blob/2472761d6f507ce63f4d1a890bc04557ca990b8e/specs/2026-09-29-unlimotion-cli-skill-distribution.md) в итоговом diff PR, потому что её утверждённый ранее multi-version контракт отменён новым запросом пользователя. Ссылка на сохранённый коммит останется рабочей после удаления файла из ветки.

## 1. Цель и исходный сценарий

Пользователь просит убрать упоминание прежней версии и сразу ориентировать скилл на последнюю. В контексте текущего PR это означает распространять из Unlimotion один операционный скилл для актуального контракта CLI, без старого маршрута, файла-справки и объяснений по нему. Установка скилла из репозитория и PR в правильном репозитории остаются исходной целью.

Success means: в финальном дереве PR скилл описывает проверенный `v1.32.0`/current-main contract, при неподходящем фактически запущенном CLI останавливается до изменения задач и рекомендует установить совместимый инструмент, а не переключается на устаревший алгоритм. README объясняет это без упоминания удаляемой версии. Skill install, repo discovery и task-space safety сохраняются.

Stop rules: не менять CLI-код, локальные задачи, глобально установленный CLI или personal skill; не считать новый файл скилла доказательством обновлённого бинарника; не сливать PR. При изменении remote main/release до EXEC/push сначала обновить контракт SPEC, а не молча назвать старый контракт «последним».

## 2. AS-IS и корневая проблема

- В открытом PR скилл имеет ветку для устаревшего CLI и отдельный versioned reference; `src/Unlimotion.Cli/README.md` обещает обе ветки. Первоначальная SPEC также закрепляет этот дизайн. Верхние README описывают repo skill, но их вводные абзацы приводят устаревший пример отличия demo от latest stable. Исторические строки таблицы релизов и замеры не являются операционными инструкциями и остаются.
- Фактический `origin/main` и последний GitHub release — `v1.32.0`; CLI предоставляет `version/help/context/search/apply schema/example/inspect`, `task --include` и приоритет `--tasks` → непустой `UNLIMOTION_TASKS` → desktop settings. Проверять бинарник пользователя всё равно необходимо.
- На этом компьютере глобальный CLI отстаёт от текущего релиза. Его обновление не входит в просьбу: скилл должен честно отказаться от несовместимой операции, а не объявлять местную установку актуальной.
- В checkout есть несвязанный untracked `specs/2026-09-28-task-importance-value-visibility.md`; его не включать в PR.
- Корневая проблема: multi-version fallback в распространяемом скилле противоречит новому запросу «только последняя версия» и оставляет ненужный риск применения старых правил.

## 3. Цели и Non-Goals

- Упростить `SKILL.md` до одного текущего маршрута, оставив safety-инварианты: identity фактического бинарника, закреплённый task-space, preview, однократная запись, read-back и reconciliation.
- Удалить versioned reference и устаревшую ветку; убрать её из CLI README и итогового diff PR, включая прежнюю SPEC. В верхних README точечно убрать устаревший пример расхождения demo/current release, не стирая таблицы исторических релизов. Пример local package version в CLI README обновить под текущий baseline. `agents/openai.yaml` оставить.
- Сохранить repo/personal installation instructions, no-overwrite, ссылки и PR #310.
- Не обновлять установленный dotnet tool, не синхронизировать личную копию, не писать в task-space, не менять код/пакет/релиз, не трогать чужую SPEC, не делать merge.

## 4. TO-BE: ответственность и контракты

| Артефакт | Изменение | Инвариант |
| --- | --- | --- |
| `.agents/skills/unlimotion-cli/SKILL.md` | Один current-release маршрут; убрать все legacy-version развилки и ссылки | Все мутации только при проверенном identity/capability; те же preview/read-back/recovery |
| Versioned reference в `.agents/skills/unlimotion-cli/references/` | Удалить из итогового PR | Нет битых ссылок и неиспользуемой справки |
| `src/Unlimotion.Cli/README.md` | Сказать, что скилл предназначен для актуального CLI; при несовместимом бинарнике обновить его отдельно; local package example привести к текущему baseline | Скилл ≠ установка .NET tool; инструкция GitHub install прежняя |
| `README.md`, `README.RU.md` | Убрать недостоверный пример отличия demo от latest stable во вводном абзаце | Исторические release rows/замеры остаются |
| Прежняя SPEC распространения | Убрать из итогового diff PR после approval | История Git и ссылка на PR остаются для аудита |
| Эта SPEC | Добавить в PR после approval как новый актуальный contract/evidence | До approval единственный изменяемый файл |

### Runtime / safety contract

1. Перед вызовами агент разрешает и закрепляет абсолютный путь CLI, сверяет `version --format json`, `help` и нужные `apply schema/example` у того же бинарника. Текущий проверенный baseline — `v1.32.0`; версия `main` сама по себе не доказывает версию установленного инструмента. Если нужный контракт отсутствует или identity неясна, до записи остановиться и предложить обновление CLI; не угадывать JSON и не включать fallback.
2. `context` с явным пользовательским `--tasks`, если он дан, возвращает абсолютный `tasksPath`. Его передавать во все связанные чтения, preview, запись, inspect и read-back. Непустой `--tasks` приоритетнее непустого `UNLIMOTION_TASKS`, затем desktop settings; не подменять ошибочный путь fallback.
3. Для `apply` получать форму из `apply schema/example` и ограничения из `help apply`; выполнить dry-run, отправить ровно тот же request один раз, прочитать все изменённые ID. При потерянном ответе использовать `apply inspect` и read-back, не слепой повтор. Не утверждать завершение задачи без evidence.
4. Более новая неизвестная версия не считается автоматически совместимой: проверить её фактическую справку/схему и matching source; при неясной семантике мутацию остановить. Это future-proofing safety, а не возврат старой ветки.

UI visual/video: не применимо — интерфейс приложения не меняется. Performance: не применимо — runtime-код CLI не меняется. Data/storage: не меняются; задачи в live task-space не трогать.

### User-Observable Scenarios

| Scenario | Trigger | Expected visible result | Evidence | AC |
| --- | --- | --- | --- | --- |
| Работа с актуальным CLI | Агент в клоне/с установленным repo skill получает задачу пользователя | Выбирает одно текущее `context`/schema/help поведение, закрепляет task-space и безопасно работает | before/after synthetic smoke, source/readme comparison | AC1, AC2 |
| Несовместимый CLI | Пользователь имеет старый исполняемый файл | Агент сообщает о несовместимости и необходимости отдельного обновления, без legacy-команд и записи | negative smoke без live tasks | AC2 |
| Установка из Unlimotion | Пользователь клонирует repo либо устанавливает skill из GitHub | Skill обнаруживается/скачивается, README не обещает обновить binary | validator, repo discovery, installer temp smoke | AC3 |
| Review PR | Пользователь открывает #310 | В итоговом diff нет старой ветки/reference/прежней SPEC и unrelated файлов; вводные README не содержат устаревшего примера, исторические release rows сохранены; checks green | GitHub read-back | AC4 |

### State / Interaction Matrix

| State | Trigger | Result | Negative case |
| --- | --- | --- | --- |
| CLI current + identity confirmed | read/modify request | current commands, pinned tasksPath, preview/read-back | отсутствие schema/help или сомнение в identity → stop |
| CLI incompatible/unknown | skill invocation | объяснение обновления, no mutation | не включать старый `apply` маршрут |
| Outcome unknown after mutation | timeout/lost output | inspect + authoritative read-back | no blind retry, lock не удалять |
| Personal skill already exists | GitHub install | installer refuses overwrite | не удалять личную копию автоматически |

### Decision Ledger

| Decision | Owner | Choice | Confidence | Risk | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Только актуальный контракт | пользователь | current release, без fallback | 1.0 | существующий binary может не подходить | Нет: прямой запрос |
| Публичная безопасность | agent | mismatch → stop before mutation | 0.99 | неполная работа на старом бинарнике | Нет: безопасное следствие |
| Scope доставки | agent | обновить существующий PR #310 | 0.99 | дубль PR/не тот repo | Нет: исходный запрос PR сохраняется |
| Историческая SPEC | agent | заменить в final diff актуальной SPEC, history сохранить | 0.9 | audit clarity | Нет: старый дизайн в PR противоречит запросу |

## 5. Миграция, rollout и rollback

До approval менять только эту SPEC. После approval обновить скилл и README, убрать reference и прежнюю SPEC из PR, выполнить проверки, commit/push в ту же ветку, обновить PR body под один current-release contract и проверить GitHub diff/CI. Никакой автоматической миграции personal skill или установленного CLI. Rollback — revert нового коммита в ветке PR; до merge `main` не меняется. Нельзя объявить уже установленную личную копию синхронизированной по одному PR.

## 6. Acceptance Criteria / validation

Обязательный набор: static inventory/links/quick_validate; representative before/after behavioral smoke на одной effective Codex-модели, reasoning и sandbox с frozen prompts про current CLI, incompatible CLI, lost-create outcome; проверка отсутствия старой ветки/reference в операционном скилле и README, включая вводные абзацы верхних README и local package example, но без стирания исторической таблицы релизов; temp GitHub install из финального head и no-overwrite; Git diff/checks/read-back. .NET/UI tests локально не запускать для instruction/docs-only delta без изменения CLI/runtime; GitHub mandatory checks должны быть green перед объявлением готовности PR. Если read-only sandbox снова блокирует файл до чтения, использовать отдельно помеченный writable fallback с запретом записей и status/read-back; не называть его read-only.

| AC | Test/check | Evidence | Negative/stop |
| --- | --- | --- | --- |
| AC1: текущие операционные инструкции без устаревших упоминаний | `rg` по skill, CLI README, верхним README до секции релизов и новой SPEC; `quick_validate.py`; relative links | final diff + validator; исторические release rows отдельно сохранены | old reference/path, stale intro/local example или битая ссылка → FAIL |
| AC2: безопасный current/incompatible/recovery behavior | frozen before/after synthetic smoke в одном runtime; source cross-check | raw scenario outputs + decision matrix в SPEC | fallback, guessed apply или blind retry → FAIL |
| AC3: установка и discovery | fresh Codex repo discovery; temp installer из опубликованного final head, hash/read-back и occupied destination | CLI outputs | installer не скачал/перезаписал → FAIL |
| AC4: PR #310 правильный и чистый | `git diff --check`, allowlist, GitHub base/head/files/checks | PR read-back, CI | wrong repo/base, unrelated SPEC, red/pending mandatory check → не объявлять green |

### Acceptance-to-Test Matrix

| Acceptance | Automated test | Manual/log check | Artifact | If not tested |
| --- | --- | --- | --- | --- |
| AC1 | `quick_validate.py`, `rg` и path inventory | scoped diff | validator output, final tree | — |
| AC2 | fresh frozen Codex eval before/after | сравнение с `main` source/help contract | scenario responses в журнале SPEC | live mutation не нужна и не разрешена |
| AC3 | GitHub installer helper | repo discovery и occupied-dest read-back | temp hashes и command output | personal discovery на чужой машине не проверяется |
| AC4 | `git diff --check` | `gh pr view/checks` | PR URL/CI state | — |

## 7. Риски и Expected User Review Objections

| Objection | Why | Mitigation | Status |
| --- | --- | --- | --- |
| «Скилл не работает с моим установленным CLI» | binary может отставать | честная ошибка и отдельная инструкция обновить CLI; не обещать автообновление | mitigated |
| «Старый номер всё ещё в PR» | прежняя SPEC/reference и текущие пояснения README входят в PR | убрать оба артефакта, поправить вводные README и local example; исторические release rows сохранить как историю | mitigated |
| «У меня две копии скилла» | personal и repo scope сосуществуют | не трогать personal copy, объяснить дубликат в README | mitigated |
| «Версия снова устареет» | current release движется | сверить remote release/main перед EXEC/push; mismatch стоп до правки SPEC | mitigated |

## 8. План, альтернативы и профиль

Порядок-инвариант: fresh release/main/PR preflight → approval → frozen baseline до изменения скилла → edit/validate candidate → same-runtime after smoke → post-EXEC review → commit/push → temp install из финального GitHub head → PR read-back/CI. При post-push исправлении повторить затронутые проверки. Не выполнять живые операции с задачами. Открытых user-owned решений нет.

Альтернатива оставить multi-version fallback быстрее, но противоречит запросу. Заменить его жёстким сравнением строки версии проще, но лишает local build и compatible help/schema безопасного пути; выбран capability/identity gate с baseline текущего release и stop при неопределённости. Профиль `product-system-design`: subsystem boundary — инструкции поверх CLI, публичный CLI API и data не меняются; security — no guessed mutation; config — task-space precedence и personal/repo scope.

## 9. SPEC linter, rubric и post-SPEC review

| № | Result | Evidence |
| --- | --- | --- |
| 1 | PASS | исходный запрос и наблюдаемый сценарий |
| 2 | PASS | PR/current skill/remote release и installed mismatch |
| 3 | PASS | устаревший fallback как корневая проблема |
| 4 | PASS | один current маршрут с identity/capability gate |
| 5 | PASS | CLI/task/personal/merge исключены |
| 6 | PASS | ответственность по skill/reference/docs/SPEC |
| 7 | PASS | Codex discovery, installer, PR |
| 8 | PASS | pinned task-space, dry-run, read-back, inspect |
| 9 | PASS | mismatch, unknown outcome, occupied destination |
| 10 | PASS | нет runtime-кода; performance неприменима |
| 11 | PASS | task files не меняются |
| 12 | PASS | несовместимость намеренная; пользователь получает update instruction |
| 13 | PASS | revert PR, без personal deletion |
| 14 | PASS | AC1–AC4 наблюдаемы |
| 15 | PASS | матрица AC→test/evidence выше |
| 16 | PASS | команды и stop rules в разделе 6 |
| 17 | PASS | baseline → edit → smoke → push → remote validation |
| 18 | PASS | Decision Ledger, вопросов нет |
| 19 | PASS | expanded: public instruction behavior |
| 20 | PASS | product-system-design границы и безопасность |

Linter: ГОТОВО, без FAIL/PARTIAL.

| Rubric criterion | Score | Basis |
| --- | ---: | --- |
| Цель и границы | 5 | исходный запрос, latest-only outcome, no CLI/task/personal/merge |
| AS-IS | 5 | проверены текущий PR, live release/main, skill/reference и все три README |
| Дизайн | 5 | один runtime route, identity/capability gate, explicit task-space и file responsibility |
| Безопасность/rollback | 5 | mismatch и unknown outcome останавливают мутацию; revert PR, personal data не трогать |
| Проверяемость | 5 | AC1–AC4 и матрица; frozen before/after smoke, remote installer, PR read-back |
| Автономность | 5 | Decision Ledger без открытых user-owned решений; точный порядок и stop rules |

Rubric: 30/30, готово к автономному EXEC только после exact approval.

### Role-Based Review Result

| Role | Applicability | Question | Verdict |
| --- | --- | --- | --- |
| Domain workflow | да | Не потеряны ли authorization/read-back границы? | PASS |
| UX/copy | да | Понятно ли различие между скиллом и binary/update? | PASS |
| Tester | да | Покрыты ли current, mismatch, recovery и installer? | PASS |
| Developer/architect | да | Не превращён ли release в слепую гарантию runtime? | PASS |
| Delivery/security | да | Обновляется ли тот же PR без unrelated изменений? | PASS |

### Post-SPEC Review

- Status / stop: PASS после targeted re-review. Первый reviewer pass дал NEEDS-FIX; оба findings исправлены в SPEC и повторно проверены. Можно запросить новое exact approval; EXEC ещё не начат.
- Scope reviewed: эта SPEC, central QUEST/routing/review/testing owners, `product-system-design`, `skill-creator`, официальная документация OpenAI о repo skills; `origin/main`/release, PR #310, текущие skill/README/reference и unrelated status.
- Contract pass: исходная поставка из Unlimotion сохранена; новый результат — latest-only без auto-update или live task mutation. AC1–AC4 покрывают обычный и негативный пути.
- Adversarial risk pass: выявлены stale current-release примеры в трёх README и отсутствие оснований rubric; после исправлений проверены границы исторических release rows, сохранность `create` reconciliation, раздельность установленного бинарника и репозитория, repo/personal copies.
- Role-based: таблица выше; visual planning artifact не применим, UI приложения не меняется.
- Fix and re-review: reviewer обнаружил устаревшие текущие абзацы обоих верхних README, local package example в CLI README и необоснованную rubric; план и AC1 расширены, rubric записана по шести критериям. Проверены реальные строки README/release и затронутые разделы SPEC; targeted re-review подтвердил закрытие обоих findings. При финальной проверке обнаружена относительная ссылка на SPEC, которую планируется удалить из ветки; заменена постоянной ссылкой на уже опубликованный commit `2472761d`, target проверен через Git и GitHub API.
- Depth checklist: scope/unrelated — отдельная untracked SPEC исключена; evidence — remote SHA и checks нужны после EXEC; unsupported claims — версия binary не выводится из release; regression — no blind retry; docs/hidden contract — убрать старые операционные примеры и оба артефакта, сохранить исторические release rows; manual challenge — установленный binary не совместим после удаления fallback.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | docs/scope | Вводные README ложно описывали различие demo и latest release; CLI README содержал старый local-package example, но SPEC не планировала их обновление | Включить точечные правки обоих README и local example, уточнить AC1 и исключение исторических release rows | fixed, targeted re-review PASS |
| MEDIUM | rubric | 30/30 без основания шести критериев | Записать отдельные проверяемые основания | fixed, targeted re-review PASS |
| LOW | audit link | Относительная ссылка на предыдущую SPEC стала бы битой после её удаления из ветки | Ссылаться на файл в сохранённом commit `2472761d` | fixed, targeted link check PASS |

- No-findings justification после исправлений: reviewer сверил SPEC:24,33,43–44,64,90,94 с обоими верхними README, CLI README и release `v1.32.0`, а rubric:150–159 с шестью критериями; противоречий в затронутых разделах не осталось. Self-review нашёл и исправил битую после rollout ссылку на прежнюю SPEC; её target проверен в опубликованном commit. Final stop decision: PASS для SPEC, запросить exact approval. Effective reviewer sandbox — `danger-full-access`, не read-only; проход учитывается как writable adversarial fallback, файлы reviewer не менял.
- Post-EXEC Review: локальный contract/adversarial pass и targeted AC2 re-review PASS. Первый проход нашёл HIGH: не было raw responses/decision matrix; добавлен адресуемый evidence artifact, finding закрыт targeted re-review. Reviewer сверил A–C, README, links и scope; safety regression нет. Effective sandbox `danger-full-access`/unrestricted — writable adversarial fallback, reviewer только читал. Общий delivery verdict пока pending: remote install и CI проверяются после push.

### EXEC validation evidence

- Raw frozen prompt, verbatim baseline replay/candidate responses, invocation/config и decision matrix: [behavior evidence](evidence/2026-09-29-cli-skill-behavior.md). Это audit artifact, а не операционная инструкция. Replay baseline из неизменного Git HEAD повторно подтвердил прежний legacy route; candidate A–C соответствует новому контракту.
- Static: `quick_validate.py` → `Skill is valid!`; `git diff --check` PASS. В операционном skill, CLI README и этой SPEC нет удалённой версии/ссылки; historical release rows верхних README не изменены. Metadata `agents/openai.yaml` сохранена.
- Frozen before/after: Codex CLI `0.154.0`, `gpt-5.5`, reasoning `low`, sandbox `read-only`, `--ephemeral`; одинаковые synthetic prompts A=current CLI + explicit/environment task-space, B=incompatible CLI, C=lost apply create/ordinary create result. Только supplied skill text, без tool calls или live task-space. Оба запуска exit 0.
- A до/после: explicit `C:\SyntheticA` выше environment `C:\SyntheticB`; pinned binary/tasksPath, schema/example/help, dry-run, тот же request один раз и read-back всех ID. PASS без регрессии.
- B до: narrow legacy commands допускались при наличии безопасного пути. После: stop before editing, no legacy fallback, no auto-update; предложить отдельное обновление CLI. PASS нового контракта.
- C до/после: no blind retry, lock wait, exact request `apply inspect`, receipt/state различаются, read-back ID; ordinary create с потерянным ID остаётся unknown без однозначного read-back. PASS без регрессии.
- Fresh Codex discovery: тот же runtime, без tools; repo skill найден по абсолютному пути `.agents/skills/unlimotion-cli/SKILL.md`, exit 0.
- Remote install/no-overwrite и CI: ожидаются после публикации ветки; до их результата AC3/AC4 не объявляются завершёнными.

## Approval

Exact approval получено 2026-09-29: «Спеку подтверждаю». EXEC разрешён в пределах этой SPEC.

## 10. Журнал действий агента

| Фаза / событие | Решение и основание | Evidence / остаток | Следующее действие | Решение человека | Артефакт |
| --- | --- | --- | --- | --- | --- |
| SPEC / новый запрос | Пользователь заменил multi-version результат на latest-only; это существенный scope change после предыдущего approval | PR #310 открыт и green; main/release = v1.32.0; текущий skill содержит старую ветку | закончить expanded SPEC и review | новый запрос получен; exact approval этой SPEC ещё нет | эта SPEC |
| SPEC / review rework | Reviewer нашёл HIGH по stale README и MEDIUM по rubric; оба исправлены в плане | upper README intro и CLI local example включены в AC1; rubric 6/6 обоснована; review writable fallback | получить targeted re-review | не требовалось | эта SPEC |
| SPEC / review closure | Targeted re-review подтвердил оба finding закрытыми, новых не нашёл | full post-SPEC PASS по содержанию; reviewer writable adversarial fallback, не технически read-only | запросить новое «Спеку подтверждаю» | ожидается | эта SPEC |
| EXEC / approval и preflight | Exact approval получено; main/release повторно сверены, drift нет | v1.32.0 / d1e6702f; чужая SPEC сохранена | реализовать один current route | «Спеку подтверждаю» | skill, README, эта SPEC |
| EXEC / implementation и smoke | Удалены legacy route/reference/прежняя SPEC; README актуализированы | static, одинаковые before/after A–C и fresh discovery PASS; live tasks/personal skill/tool не менялись | post-EXEC review, commit/push, remote install, CI | в рамках approval | эта SPEC |
| EXEC / review closure | HIGH evidence gap закрыт raw artifact и матрицей A–C | targeted re-review AC2 PASS; artifact явно включается в commit; общий delivery pending | commit/push, remote install, CI | не требовалось | эта SPEC, behavior evidence |
