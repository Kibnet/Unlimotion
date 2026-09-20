# Произвольный текст и Markdown в описании задачи через CLI

## 0. Метаданные
- Тип (профиль): `delivery-task`; публичный CLI-контракт; `.NET/TUnit` validation через `testing-dotnet`.
- Форма SPEC: expanded — меняется публичный контракт сразу трёх write-path CLI (`create`, `apply/createTask`, `apply/setField`).
- Владелец: Unlimotion CLI / TaskTreeManager.
- Масштаб: medium.
- Целевое семейство / behavior baseline: GPT-6 Astra baseline из central stack; на продуктовый runtime не влияет.
- Поверхность: Work / Codex.
- Effective runtime: среда текущего Codex-сеанса; модель и reasoning не являются частью изменяемого контракта.
- Eval baseline / evidence: актуальный `origin/main` на `a3a12e31`; воспроизводимый отказ `create --description` для текста с переводом строки; существующие `UnlimotionCliIntegrationTests`.
- Целевой релиз / ветка: `fix/cli-markdown-description`, релиз не выполняется в рамках этой задачи.
- Ограничения: до точной фразы пользователя `Спеку подтверждаю` меняется только этот SPEC.
- Связанные ссылки: задача Unlimotion `304eb3d1-84fc-4d3b-96c4-b0ffdf5a4c56` про Markdown и ссылки в описании.

## 1. Overview / Цель
Разрешить через `unlimotion-cli` сохранять в пользовательской части описания задачи произвольный текст, включая многострочный Markdown, без нормализации и потери символов.

Outcome contract:
- Исходное поручение / симптом и точка применения результата: `unlimotion-cli create --description` отклоняет описание с переводами строк, потому что `char.IsControl` считает `CR`, `LF` и табуляцию управляющими символами; тот же запрет есть в `apply`.
- Success means: `create` и оба description-path команды `apply` принимают и дословно сохраняют доступную входной поверхности строку с Markdown, переводами строк, табуляцией, Unicode, кавычками, обратными слешами, URL и локальными путями.
- Итоговый артефакт / output: обновлённый CLI-контракт, regression-тесты и документация с примером многострочного описания.
- Stop rules: не менять title-validation, execution question/result validation, формат служебного блока исполнения, рендер Markdown в UI или правила открытия ссылок; остановиться при выявлении необходимости менять persisted schema.

## 2. Текущее состояние (AS-IS)
- `TaskGraphCommandService.TryCreateTaskCoreAsync` отклоняет `Description`, если любой символ удовлетворяет `char.IsControl`.
- `TaskApplicationCommandService.CreateTask` применяет тот же запрет к `DescriptionUserText` в операции `createTask`.
- `TaskApplicationCommandService.SetField` применяет тот же запрет к полю `descriptionUserText`.
- Поэтому не проходят обычные для Markdown `\r\n`, `\n`, `\r` и `\t`; при этом storage уже хранит описание как JSON-строку и способен экранировать такие символы.
- Описание ограничено 100 000 символов и не может содержать служебные маркеры `AgentExecutionDescriptionRenderer`; эти ограничения защищают размер данных и целостность проекции `AgentExecution`.
- Заголовки и тексты execution lifecycle имеют отдельные контракты и в эту задачу не входят.
- `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs` содержит обратный текущему требованию тест `Create_ControlCharacterInDescriptionDoesNotWrite`.

## 3. Проблема
Валидация описания ошибочно приравнивает форматирующие и сериализуемые символы текста к повреждённому вводу, из-за чего CLI нельзя использовать для хранения Markdown и полноценного контекста задачи.

## 4. Цели дизайна
- Один смысловой контракт пользовательского описания для всех write-path CLI.
- Дословный round-trip без `Trim`, смены line endings или другой нормализации.
- Совместимость с существующим JSON storage и чтением через `task --include details`.
- Сохранение лимита размера и защиты служебных маркеров.
- Наблюдаемые end-to-end тесты через реальный CLI entry point.

## 5. Non-Goals (чего НЕ делаем)
- Не реализуем Markdown-renderer в desktop UI.
- Не делаем ссылки кликабельными и не определяем allowlist URI-схем.
- Не добавляем новый способ передачи файла в `create`; PowerShell here-string уже может быть одним значением `--description`, а для машинных сценариев есть `apply --request <path|->`.
- Не меняем лимит 100 000 символов.
- Не разрешаем пользовательскому тексту имитировать внутренние маркеры блока исполнения.
- Не меняем валидацию title, agent id, question/answer/result/complete/release text и ссылок execution lifecycle.
- Не публикуем NuGet, GitHub Release и не обновляем глобально установленный CLI.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `TaskGraphCommandService`: разрешить полный строковый payload описания в `create`, сохранив length/marker/parent/title checks.
- `TaskApplicationCommandService`: применить тот же контракт в `createTask.DescriptionUserText` и `setField(descriptionUserText)`.
- `AgentExecutionDescriptionRenderer`: остаётся владельцем распознавания зарезервированных маркеров; содержимое marker block не меняется, а синтетический перевод строки перед ним устраняется, чтобы render/remove сохранял пользовательский текст дословно.
- `UnlimotionCliIntegrationTests`: доказать поведение через CLI для всех трёх write-path и отрицательных границ.
- `src/Unlimotion.Cli/README.md`: описать многострочный Markdown и пример безопасной передачи из PowerShell.

### 6.2 Детальный дизайн
- Удалить blanket-проверку `Any(char.IsControl)` только для пользовательского описания.
- Принимать любую строку, которую уже передала входная поверхность CLI/JSON, если длина не больше 100 000 и строка не содержит зарезервированный execution marker.
- Не нормализовать `CRLF`, `LF`, `CR`, табуляцию, пробелы в начале/конце, Unicode или Markdown-пунктуацию.
- `null` в `create` по-прежнему означает пустое описание; `setField(descriptionUserText)` по-прежнему требует ненулевое значение.
- JSON output/read-back должен экранировать непечатаемые символы средствами штатного serializer; отдельное ручное экранирование не добавляется.
- Проекция `AgentExecution` не должна добавлять символ-разделитель к пользовательскому тексту: служебный marker block присоединяется непосредственно и удаляется без изменения исходной строки.
- Сообщения ошибок `apply` должны точно говорить о превышении лимита или конфликте служебного маркера и больше не упоминать запрещённые control characters для description.
- Visual planning artifact: не применимо — UI и визуальное отображение не меняются.
- UI test video evidence: не применимо — изменение ограничено CLI/storage-контрактом.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Создание задачи с Markdown | Передать PowerShell here-string в `create --description` | exit code 0; `task --include details --format json` возвращает исходный текст дословно | integration test + package smoke | AC1, AC4 |
| Создание через manifest | Выполнить `apply` с `createTask.descriptionUserText` | задача создана, Markdown сохранён дословно | integration test | AC2, AC4 |
| Обогащение существующей задачи | Выполнить `apply` с `setField(descriptionUserText)` | пользовательский текст обновлён без повреждения execution projection | integration test | AC3, AC4 |
| Защитные границы | Передать слишком длинное описание или reserved marker | операция отклонена без записи | negative integration tests | AC5 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| Задачи нет | `create` с многострочным description | Prepared-задача создана; description точен | missing parent и unsafe graph работают как раньше | транзакционность не меняется |
| Задачи нет | `apply/createTask` | staged task содержит точный user text | manifest/etag rules работают как раньше | schema v1 не меняется |
| Задача без execution | `apply/setField descriptionUserText` | Description равен входной строке | null, length, marker отклоняются | без нормализации |
| Задача с execution | `apply/setField descriptionUserText` | user text заменён; один корректный marker block сохранён | malformed marker отклоняется без записи | structured execution остаётся source of truth |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Охватить `create` и оба description-path `apply` | agent | единый публичный контракт | 0.99 | частичная поддержка Markdown | Нет |
| Разрешить все символы входной строки, а не whitelist Markdown | user + agent | убрать `char.IsControl` для description | 0.98 | редкие control chars сохранятся в данных; JSON их экранирует | Нет |
| Сохранить reserved markers | agent | маркеры остаются запрещены | 0.99 | иначе пользовательский текст может повредить execution projection | Нет |
| Сохранить лимит 100 000 | agent | без изменения | 0.99 | увеличение размера не требуется исходным сценарием | Нет |
| Не менять execution text | agent | отдельный follow-up при необходимости | 0.95 | summary по-прежнему однострочный | Нет |

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| `create --description` | `TaskGraphCommandService` | убрать запрет control chars | обратно совместимо | CLI integration |
| `apply/createTask.descriptionUserText` | `TaskApplicationCommandService` | тот же контракт | schema v1 без изменений | CLI integration |
| `apply/setField(descriptionUserText)` | `TaskApplicationCommandService` | тот же контракт | existing ETag/marker behavior сохраняется | CLI integration |
| Persisted task JSON | `FileTaskStorage` / serializer | schema без изменений; новые строки экранируются штатно | миграция не нужна | save/load/read-back |

## 7. Бизнес-правила / Алгоритмы
1. `DescriptionUserText` допустим, если его длина `<= 100_000` и он не содержит зарезервированный execution marker.
2. Для `create` отсутствие description преобразуется в пустую строку.
3. Для `apply/setField` отсутствие `value` остаётся ошибкой контракта.
4. Значение сохраняется побайтно-эквивалентно на уровне .NET-строки: line endings и пробельные символы не нормализуются.
5. Правила title и execution lifecycle не зависят от этого контракта.

## 8. Точки интеграции и триггеры
- `unlimotion-cli create` → `TaskGraphCommandService.TryCreateTaskAsync`.
- `unlimotion-cli apply`, operation `createTask` → `TaskApplicationCommandService.CreateTask`.
- `unlimotion-cli apply`, operation `setField`, field `descriptionUserText` → `TaskApplicationCommandService.SetField`.
- `unlimotion-cli task --include details` используется как публичный read-back.

## 9. Изменения модели данных / состояния
- Новых полей и версии schema нет.
- Меняется только admissible input set существующего `Description`.
- Существующие задачи не переписываются.

## 10. Миграция / Rollout / Rollback
- Миграция не требуется.
- Rollout: обычная поставка новой версии CLI вместе с будущим релизом Unlimotion; публикация не входит в этот EXEC.
- Rollback: revert validation change; уже сохранённые строки остаются валидным JSON и читаются текущим storage, поэтому data rollback не нужен.

## 11. Тестирование и критерии приёмки
### Acceptance Criteria
- AC1. `create --description` принимает строку с `CRLF`, `LF`, `CR`, табуляцией, Unicode, Markdown code fence, URL и Windows path; persisted `Description` и CLI read-back совпадают с входом.
- AC2. `apply/createTask.descriptionUserText` принимает и дословно сохраняет тот же класс текста.
- AC3. `apply/setField(descriptionUserText)` принимает и дословно сохраняет многострочный текст, в том числе для задачи с действующим structured execution block без дублирования/потери marker block.
- AC4. После save/load и `task --include details --format json` `descriptionUserText` точно совпадает с входной .NET-строкой.
- AC5. Лимит 100 000, reserved marker conflict, title validation, missing parent, graph safety и atomic no-write behavior остаются прежними.
- AC6. README содержит PowerShell-пример here-string и уточняет, что line endings и Markdown сохраняются, а reserved marker остаётся служебным.
- AC7. Целевые тесты, Release build CLI и полный последовательный TUnit suite зелёные.

Обязательный набор проверок: полный TUnit обязателен, потому что меняется публичный CLI-контракт и общая mutation validation; UI suites не обязательны, поскольку UI behavior не меняется.

Characterization / RED:
- изменить/добавить integration test, который на текущем коде падает только из-за `char.IsControl` для многострочного description;
- подтвердить, что это product red, а не quoting/runner failure.

Команды EXEC:
```powershell
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release -- --treenode-filter "/*/*/UnlimotionCliIntegrationTests/*" --maximum-parallel-tests 1 --output Detailed
dotnet build src\Unlimotion.Cli\Unlimotion.Cli.csproj -c Release --no-restore
dotnet test src\Unlimotion.Test\Unlimotion.Test.csproj -c Release --no-build -- --maximum-parallel-tests 1 --output Normal
```

Дополнительный package smoke после green:
- собрать локальный tool package с явным `PackageVersion`;
- установить во временный tool-path;
- создать во временном task-space задачу с PowerShell here-string;
- прочитать её через `task --include details --format json` и сравнить точный текст;
- глобальную установку пользователя не менять.

Stop rules: после полного green и package smoke не расширять тестирование без нового failure или изменения; при full-suite failure классифицировать product regression, flaky test, lock или environment blocker до retry.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC1 | `UnlimotionCliIntegrationTests`: create multiline round-trip | package smoke | test log + smoke output | — |
| AC2 | apply createTask multiline round-trip | — | test log | — |
| AC3 | apply setField + execution marker preservation | — | test log | — |
| AC4 | assertions on persisted model and task JSON | package smoke comparison | test/smoke output | — |
| AC5 | existing negative tests + marker/limit regression | inspect no-write assertions | test log | — |
| AC6 | documentation diff review | command example inspection | git diff | — |
| AC7 | targeted + build + full suite | process exit/status | command logs | — |

## 12. Риски и edge cases
- Shell quoting differs by platform; contract begins after the shell has produced one argument. README documents PowerShell, while `apply --request` remains the robust cross-shell machine path.
- Arbitrary control characters can be visually confusing in a terminal; JSON serializer must remain the escaping boundary, and no raw command execution is introduced.
- Mixed `CRLF`/`LF` must not be normalized during persistence or read-back.
- Exact reserved marker text remains unavailable as user content until execution projection is redesigned; this is an explicit integrity boundary, not accidental text filtering.
- Description with active execution state must keep exactly one valid marker block after `setField`.
- 100 000-character boundary must be tested at limit and over limit without expensive broad allocations beyond existing contract.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Починили только create, а apply всё ещё ломает Markdown» | запрет продублирован в трёх местах | все три write-path входят в AC1–AC3 | mitigated |
| «CLI изменил переносы строк или пробелы» | многие системы нормализуют text input | exact round-trip и смешанные endings проверяются | mitigated |
| «Под видом любого текста всё ещё запрещён Markdown» | старый blanket filter слишком широк | whitelist не вводится; остаются только length и exact internal marker | mitigated |
| «Исправление сломало журнал агента» | Description содержит служебную проекцию execution | отдельный тест обновления user text при active execution | mitigated |
| «Из-за мелкой правки запустили бессмысленные UI-тесты» | репозиторий большой | UI не меняется; targeted CLI + build + full core TUnit | mitigated |

### Rework Prevention Checklist
- [x] Назван точный пользовательский сценарий и три write-path.
- [x] Каждый сценарий имеет automated evidence.
- [x] Явно сохранены marker и size boundaries.
- [x] Учтено точное сохранение line endings и active execution projection.
- [x] UI/рендер Markdown отделены как non-goal.
- [x] Полный regression gate установлен публичным контрактом.

## 13. План выполнения
1. Добавить RED integration coverage для прямого `create` и двух `apply` paths.
2. Убедиться, что RED вызван только запретом `char.IsControl`.
3. Согласовать predicates описания между `TaskGraphCommandService` и `TaskApplicationCommandService`, не меняя остальные текстовые контракты.
4. Уточнить user-facing error messages и README.
5. Выполнить targeted tests, CLI Release build, полный последовательный TUnit и isolated package smoke.
6. Выполнить post-EXEC review и обновить журнал SPEC.

## 14. Открытые вопросы
Нет блокирующих вопросов. Формулировка «любой текст» трактуется как отсутствие общего запрета `char.IsControl` для description после того, как строка уже принята CLI/JSON поверхностью; exact internal marker и лимит размера остаются контрактными исключениями.

## 15. Соответствие профилю
- Профиль: `delivery-task` + `testing-dotnet`.
- Выполненные требования профиля: SPEC-first; публичный контракт; TDD regression; targeted/build/full staged validation; exact read-back; rollback и no-write negatives.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.TaskTreeManager/TaskGraphCommandService.cs` | description validation без blanket control-char ban | прямой `create` |
| `src/Unlimotion.TaskTreeManager/TaskApplicationCommandService.cs` | тот же контракт в createTask/setField; точные ошибки | manifest workflow |
| `src/Unlimotion.TaskTreeManager/AgentExecutionDescriptionRenderer.cs` | обратимая без потерь граница user text / marker block | exact round-trip при active execution |
| `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs` | RED/GREEN round-trip и negative coverage | публичное evidence |
| `src/Unlimotion.Test/AgentExecutionDescriptionRendererTests.cs` | exact render/remove для разных line endings и control chars | regression evidence проекции |
| `src/Unlimotion.Cli/README.md` | multiline Markdown contract и PowerShell example | discoverability |
| `specs/2026-09-20-cli-markdown-description-text.md` | решения, проверки, журнал | QUEST evidence |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| `create --description` | любой control char отклоняет всю команду | любая принятая строка до 100 000 без reserved marker |
| `apply/createTask` | многострочный text отклонён | точный multiline round-trip |
| `apply/setField descriptionUserText` | многострочный text отклонён | точный multiline round-trip с сохранением execution block |
| Markdown | только фактически однострочный | многострочный source хранится без нормализации |

## 18. Альтернативы и компромиссы
- Вариант: разрешить только `CR`, `LF`, `TAB`. Плюс: уже. Минус: сохраняет искусственный blacklist и противоречит требованию «любой текст»; новые Unicode/control cases снова потребуют правок.
- Вариант: добавить отдельный `--description-file`. Плюс: проще shell quoting. Минус: не исправляет `apply` и не нужен для основного контракта; может быть отдельным UX-улучшением.
- Выбрано: убрать blanket control-char ban только с user description во всех write-path, сохранив marker/length protection.

## 19. Результат quality gate и review
### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | outcome, границы, три path и non-goals определены |
| B. Качество дизайна | 6-10 | PASS | контракт единообразен, schema не меняется |
| C. Безопасность изменений | 11-13 | PASS | marker/length/no-write/rollback сохранены |
| D. Проверяемость | 14-16 | PASS | AC сопоставлены с CLI integration и full suite |
| E. Готовность к автономной реализации | 17-19 | PASS | нет открытых решений; этапы и stop rules заданы |
| F. Соответствие профилю | 20 | PASS | testing-dotnet и QUEST применены |

Итог: ГОТОВО.

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | исходный отказ и non-goals конкретны |
| 2. Понимание текущего состояния | 5 | найдены все три validators и execution marker dependency |
| 3. Конкретность целевого дизайна | 5 | единый predicate и exact round-trip |
| 4. Безопасность | 5 | storage schema, marker, limit и rollback определены |
| 5. Тестируемость | 5 | end-to-end AC matrix и package smoke |
| 6. Готовность к автономному выполнению | 5 | блокирующих вопросов нет |

Итоговый балл: 30 / 30. Зона: готово к автономному выполнению.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Хранит ли CLI полный контекст задачи? | PASS | охватить create и apply |
| UX / designer | not applicable | UI не меняется | PASS | не требуется |
| Tester / validation | applicable | Покрыты ли exact round-trip и отрицательные границы? | PASS | matrix добавлена |
| Developer / architect | applicable | Согласованы ли контракты и execution projection? | PASS | active execution scenario добавлен |
| Delivery / operations / security | applicable | Нет ли migration/publish/command execution риска? | PASS | publish исключён, serializer остаётся boundary |

### Post-SPEC Review
- Статус / stop decision: PASS; можно запрашивать exact approval.
- Scope/Evidence pass: прочитаны актуальные `TaskGraphCommandService`, `TaskApplicationCommandService`, `AgentExecutionDescriptionRenderer`, CLI usage, integration tests, README и central QUEST/testing owners.
- Contract pass: исходный сценарий, три write-path, marker/length boundaries, exact round-trip и non-goals согласованы.
- Adversarial risk / Role-Based pass: проверены mixed line endings, arbitrary control chars, active execution marker, too-long input, shell boundary и отсутствие schema migration.
- Findings: MEDIUM — первоначально можно было ограничиться `create`, оставив `apply` сломанным; исправлено расширением AC2/AC3. MEDIUM — «любой текст» мог случайно разрешить подделку marker block; закрыто явным integrity exception и negative test. LOW — `--description-file` удобнее для shell; оставлено follow-up, поскольку не требуется для исправления контракта.
- Fix and re-review: после исправлений матрица и file scope перечитаны; открытых HIGH/MEDIUM нет.
- Manual-review challenge: наиболее вероятная скрытая регрессия — `setField` повреждает marker block активного исполнения; для неё добавлен отдельный обязательный сценарий.
- Остаточный риск: конкретная shell может не уметь передать отдельные символы; contract начинается после успешного получения строки процессом, а `apply --request` покрывает machine input.
- Needs human: только exact approval по QUEST.

### Post-EXEC Review
- Статус / stop decision: PASS WITH KNOWN FLAKY INFRA по изменённому контракту; публикация, глобальная установка и commit не выполнялись. Формальный AC7 для единого полностью зелёного запуска не достигнут из-за несвязанной headless Avalonia-гонки, но product tests и оба isolated retries зелёные.
- Scope/Evidence pass: изменены только три write-path description, обратимость execution projection, тесты и README; title/execution text/storage schema не менялись.
- Contract pass: `create`, `apply/createTask` и `apply/setField` сохраняют исходную строку; 100 000 и reserved marker остаются границами.
- Adversarial risk / Role-Based pass: проверены mixed `CRLF/LF/CR`, tab, Unicode, Markdown, Windows path, URL, `NUL` через JSON и active execution marker.
- Findings: MEDIUM — после снятия validation `setField` записывал данные, но возвращал `outcomeUnknown`, потому что прежний renderer добавлял синтетический `LF`; исправлено обратимым соединением user text и marker block и отдельными unit-тестами. LOW — два полных последовательных suite получили один и тот же headless Avalonia race `Collection was modified`, но в разных несвязанных UI-сценариях; оба изолированных повтора прошли 1/1. Это классифицировано как существующая нестабильность UI-инфраструктуры, не product regression данной задачи.
- Manual-review challenge: проверено, что отсутствие разделительного `LF` не меняет строки marker block, не создаёт второй block и исправляет повторяющуюся occurrence без хвостового символа.
- Остаточный риск: оболочка командной строки не способна передать `NUL`; такой текст доступен через документированный `apply --request <path|->`, что доказано packaged smoke.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
| Фаза / событие | Решение и основание | Evidence / остаток работы | Следующее действие | Фактическое решение человека, если требовалось | Затронутые артефакты |
| --- | --- | --- | --- | --- | --- |
| SPEC / старт | Выбрана expanded SPEC из-за публичного CLI-контракта | пользовательский отказ воспроизведён ранее; main checkout отставал | создать worktree от актуального origin/main | пользователь попросил исправить CLI | этот SPEC |
| SPEC / discovery | Охват расширен с `create` до трёх write-path description | `char.IsControl` найден в TaskGraphCommandService и двух ветках TaskApplicationCommandService | определить единый контракт и AC | не требовалось | этот SPEC |
| SPEC / решение | Разрешить все символы принятой строки, сохранить 100k и reserved marker | JSON storage уже сериализует строку; migration не нужна | post-SPEC review | не требовалось | этот SPEC |
| SPEC / review | PASS после закрытия partial-fix и marker-integrity рисков | 30/30; открытых HIGH/MEDIUM и вопросов нет | запросить `Спеку подтверждаю` | ожидается | этот SPEC |
| EXEC / approval | Получено точное подтверждение, scope SPEC переведён в реализацию | пользователь написал `Спеку подтверждаю`; код ещё не менялся | добавить RED regression tests | Спека подтверждена | этот SPEC |
| EXEC / RED | Три end-to-end regression-теста добавлены до исправления | `UnlimotionCliIntegrationTests`: 35 total, 32 PASS, 3 expected FAIL; каждый отказ возвращён прежней description validation | изменить только predicates пользовательского описания | не требовалось | `UnlimotionCliIntegrationTests.cs`, test report |
| EXEC / GREEN | Удалён blanket `char.IsControl` только для description; renderer сделан обратимым без синтетического `LF` | CLI integration 35/35; renderer 7/7; CLI Release build 0 warnings / 0 errors | выполнить полный suite и package smoke | не требовалось | TaskTreeManager, tests, README |
| EXEC / full regression | Первый последовательный прогон: 1080/1081; единственный headless Avalonia race вне изменённой поверхности | isolated retry `TaskPlanningWantedImportanceScenario_ExecutesFeatureSteps`: 1/1 PASS | повторить полный suite как финальный gate | не требовалось | TUnit logs/report |
| EXEC / full regression retry | Второй последовательный прогон: 1080/1081; та же Avalonia-гонка возникла уже в другом UI-тесте | isolated retry `RoadmapGraph_SelectedNodesDragDrop_AppliesBatchOperation`: 1/1 PASS; product tests 35/35 + 7/7 | классифицировать gate как flaky infra и не расширять scope | не требовалось | TUnit logs/report |
| EXEC / package smoke | Локальный пакет `1.31.1-local.9201` установлен только во временный tool-path | packaged `create` exact round-trip=true; packaged `apply` exact round-trip=true, `NUL` сохранён | финальный full suite и рабочая копия | не требовалось | local nupkg, temp task-space |
