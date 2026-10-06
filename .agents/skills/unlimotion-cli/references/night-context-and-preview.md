# Снимки контекста, content search и согласованный preview

Применяй эту памятку только после capability probe установленного закреплённого executable. Эти команды добавляются локальной разработкой; наличие скилла и версия `1.32.0` не доказывают их поддержку. Для нужной функции проверь её help/schema. Отсутствующий `--diff full` или `--expect-preview` не разрешает переходить к plain preview/apply. Не устанавливай CLI и не запускай helpers вместо отсутствующего контракта без отдельного поручения.

## Захват и чтение

У snapshot разделены выбор targets и включение контекста. Получи `snapshot schema --kind selection --format json`; используй синтетический пример как форму, заменив IDs:

```json
{
  "schemaVersion": 1,
  "select": { "mode": "unlocked", "rootIds": ["goal-a", "goal-b"], "statuses": [] },
  "context": "night-v1",
  "missingSelection": "report",
  "include": ["details", "criteria", "history", "execution"]
}
```

- `mode=all` не фильтрует доступность, `unlocked` использует вычисленный CanStart; `ids` требует `taskIds` и не принимает root/status filters. Пустые roots в all/unlocked означают всё пространство. Root union включает корни и descendants.
- `missingSelection=report` сохраняет отсутствие выбранных IDs в результате; отсутствующий root не превращается в выбор всего пространства. Default `error` отказывает при missing selection. Ошибка настоящей графовой ссылки фатальна при обеих политиках.
- `night-v1` включает все родительские ветви и нужных upstream blockers без отбрасывания Completed/Archived. Храни/показывай DAG: каждый node один раз, все parent edges сохранены; общий узел можно свернуть ссылкой, родительскую связь скрывать нельзя. Первый `ancestorPaths[0]` или один «главный» parent не даёт полного контекста.
- `details`/`criteria` обязательны для night-v1; `history`/`execution` включай, если нужны для анализа. История — текущая `StatusHistory`, не полная история правок или Git commits. Незапрошенная секция — `notRequested`, execution audit может быть усечён по собственному контракту.

```powershell
& $cliExe snapshot capture --tasks $tasksPath --selection selection.json --output C:\NightRuns\run.snapshot.json --format json
& $cliExe snapshot read --snapshot C:\NightRuns\run.snapshot.json --view targets --page-size 100 --format json
& $cliExe snapshot read --snapshot C:\NightRuns\run.snapshot.json --view context --page-size 100 --format json
```

Output должен быть новым файлом вне task root; его родитель уже должен существовать. Не сохраняй snapshot/preview/внешние run records среди task JSON. Capture требует явный закреплённый `--tasks`; read/diff/schema и snapshot search offline, без `--tasks` и desktop settings. Пройди каждую нужную view до `nextCursor=null` с неизменными artifact/view/page-size; не редактируй cursor.

Артефакт неизменяемый, имеет snapshotId/artifactHash, не имеет TTL и пригоден для offline read после перезапуска. Старые ETag и availability не являются текущими; время оценки — `evaluatedAt`. Не меняй содержимое сохранённого файла под прежним ID и не скрывай checksum failure новым live read.

Capture держит общий lock штатных writers и проверяет имена/исходные bytes вторым проходом; `atomic=false` честно исключает обещание атомарности для внешних writers. Pending journal → `recoveryRequired`, без task/receipt/journal writes; coordination lock metadata разрешены. Zero-byte, corrupt, duplicate/missing references и превышение лимитов не дают «полный» частичный результат. Лимиты и диагностику бери из help/schema. Сужение selection уменьшает payload, но не полный scan источника. Не удаляй lock/journal и не исправляй legacy автоматически.

## Delta и продолжение после перезапуска

```powershell
& $cliExe snapshot diff --before previous.snapshot.json --after run.snapshot.json --format json
```

Сравнение требует совместимых source/scope/contract versions и сохраняет все страницы изменений. Это endpoint state difference, не журнал событий: промежуточное изменение с возвратом или создание/удаление между captures не обнаруживаются. Собственная правка, новая связь, изменение родителя/блокера или clock-only availability могут инвалидировать target. `deleted` означает отсутствие в наблюдаемом namespace; выход из unlocked/root/status selection — membership change. Не удаляй внешние материалы и решения при выходе target из selection.

При `baselineUnavailable`, `snapshotNotFound`, `snapshotInvalid` или `deltaIncompatible` не сообщай «изменений нет». Сохрани внешние decisions/materials/queues, захвати новый полный snapshot желаемого scope и отметь `baselineReset` в run record. Перепроверь весь набор targets на актуальность, рассматривая прежние результаты как candidates reuse. Baseline становится обработанным только после сохранения queue checkpoint с after artifact hash; после перезапуска сравнивай с последним обработанным baseline. Свежесть Obsidian и внешних источников проверяется отдельно.

## Поиск по содержимому

```powershell
& $cliExe search --snapshot run.snapshot.json --query 'проверка решения' --fields title,description,criteria --limit 20 --format json
```

Snapshot search покрывает только payload; проверь `coverage`, `searchedNodeCount`, `catalogCount`, `searchComplete`. Для поиска во всём пространстве сначала захвати `mode=all`, `context=none`, `include=["details","criteria"]`, включая terminal статусы. `sectionNotCaptured`/`scopeNotCaptured` и `descriptionUnavailable` не означают отсутствия решения. Последнее предупреждение даёт `searchComplete=false`.

`--fields` — unique subset `id,title,description,criteria`; default id,title. Description исключает protected execution markers, criteria ищутся по Text всех критериев. Query — одна буквальная подстрока, NFC + OrdinalIgnoreCase, без regex/wildcards/морфологии; `ё`/`е` и похожие латинские/кириллические символы различаются. Empty query перечисляет всё. Snippets и occurrence ranges могут быть усечены с флагами; ranges — UTF-16 offsets в нормализованном snippet. Не принимай snippet за полное содержимое карточки. Snapshot cursor привязан к artifact/filters; live поиск с `--fields` остаётся `liveUnpinned` между процессами.

## Exact предложение и применение

1. Во внешнем workflow сохрани proposal ID/revision, материалы, exact request/application ID и смысл before → after. Незавершённая часть работы через contains блокирует родителя; prerequisite через blocks, последующий шаг и альтернативный вариант не взаимозаменяемы. Approval lifecycle остаётся снаружи CLI.
2. Прочитай свежие explicit targets и ETag закреплённым CLI; не выдавай snapshot ETag за текущий. Создай request по `apply schema`/`apply example`, не меняя protected execution state. Выполни полный preview:

```powershell
& $cliExe apply --tasks $tasksPath --request request.json --dry-run --diff full --format json > preview.json
```

3. Проверь `success=true`, `mode=preview`, `didMutate=false`, `preview.complete=true`; покажи `preview.changes` с явными/производными/системными изменениями и `preview.affectedTaskIds`. Generated timestamps имеют typed after placeholders, не обещание точного времени commit. Неполный или превышающий лимит diff не имеет executable guard и не подходит для согласования.
4. Получи однозначное решение пользователя на этот proposal revision и exact effect. Учитывай уже данное разрешение; receipt и само существование preview разрешением не являются. Отказ означает no apply; новый замысел получает новую revision/application. Если данные успели измениться, новый preview может потребовать нового решения.
5. Для разрешённого пакета выполни исходный request с исходным witness:

```powershell
& $cliExe apply --tasks $tasksPath --request request.json --expect-preview preview.json --format json
& $cliExe apply inspect --tasks $tasksPath --request request.json --format json
```

Guard проверяет decoded request hash, source identity, полный raw manifest имён/bytes и semantic effect; даже посторонняя byte-only правка может дать `previewStale`. Не редактируй request whitespace или body/witness после согласования. `previewInvalid`, `previewStale`, `unstablePrecondition` и `unstableSource` требуют сверки/нового preview либо отдельного scope ремонта, без снятия guard и без fallback на plain apply. Неизменённая посторонняя legacy задача сама по себе не означает отказ.

6. Выполни read-back всех changed/created/affected IDs с details/relations/criteria/history/execution, сверив и производные изменения. Сохрани внешний application result, очередь и baseline checkpoint. Изменение задачи и фактическое продвижение работы — разные результаты.

При прерывании после task commit, но до Obsidian, продолжи по сохранённым application/request: inspect → read-back → reconciliation → внешний checkpoint. `receiptMatched` не доказывает сохранности текущего состояния; `desiredStatePresent` без receipt не доказывает авторства прежней записи. Не отправляй новый application по памяти и не повторяй неизвестный исход автоматически. Pending journal блокирует observation; разрешённый write path может восстановить старую транзакцию до нового guard. CLI не гарантирует cross-store transaction с Obsidian и не подтверждает внешнюю очередь автоматически.

Защита от обнаруженного bypass writer при Save/commit и условный откат действуют в текущей попытке guarded apply. Persisted journal/recovery сохраняют прежний контракт: после аварии recovery может записать сохранённые образы поверх внешних изменений в обход lock. Не приписывай этой границе универсальную атомарность. Если безопасный откат не удался и journal остался, сообщи неизвестный исход и необходимость сверки; не удаляй журнал и не повторяй мутацию автоматически.
