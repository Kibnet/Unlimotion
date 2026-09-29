# `apply` в установленном `unlimotion.cli` 1.31.1

Читай эту справку только для `apply` с подтверждённым установленным пакетом 1.31.1. Контракт сверен с tag `v1.31.1`: `src/Unlimotion.Cli/TaskApplicationJson.cs`, `src/Unlimotion.TaskTreeManager/TaskApplicationCommandService.cs` и `src/Unlimotion.Test/UnlimotionCliIntegrationTests.cs`. Более новый checkout может описывать поведение, отсутствующее в установленной версии. Для иной версии проверь её matching source/schema; если такой проверки нет, остановись перед `apply`.

CLI принимает JSON через `apply --request <path|-> [--dry-run] --format json`. Минимальный запрос для полной замены пользовательского текста описания одной существующей задачи:

```json
{
  "schemaVersion": 1,
  "applicationId": "unique-stable-id-for-this-request",
  "proposalRefs": [{ "id": "user-request-id", "revision": 1 }],
  "author": "Codex",
  "reason": "Согласованное изменение описания",
  "preconditions": [{ "taskId": "TASK_ID", "etag": "ETAG_FROM_TASK_JSON" }],
  "operations": [{
    "operationId": "set-description",
    "kind": "setField",
    "taskId": "TASK_ID",
    "field": "descriptionUserText",
    "value": "ПОЛНЫЙ НОВЫЙ ПОЛЬЗОВАТЕЛЬСКИЙ ТЕКСТ"
  }]
}
```

`proposalRefs` — требуемое поле request, не доказательство отдельного одобрения. Для разового поручения создай локальный ID именно этого согласованного изменения; не выдавай его за внешнее approval. `applicationId` и `operationId` должны быть непустыми и стабильными для данного request; `applicationId` не длиннее 128 символов. `etag` бери из `unlimotion-cli task --id TASK_ID --include details --format json` перед формированием запроса. В `value` передай весь итоговый пользовательский текст, сохраняя исходное содержимое при добавлении фрагмента. Не включай служебные маркеры `AgentExecution`.

Перед записью выполни `unlimotion-cli apply --request <path> --dry-run --format json`. Успешный preview имеет `success=true`, `mode=preview`, `didMutate=false` и ожидаемый `changedTaskIds`/`operationResults`; только затем отправь тот же request без `--dry-run` один раз. При `preconditionFailed` перечитай состояние и сформируй новый request только в порученном scope. При `validationFailed` с `didMutate=false`, включая несогласованные даты другой задачи, остановись; не исправляй постороннюю карточку ради этой операции и не редактируй файлы задач напрямую. При `outcomeUnknown` или потерянном выводе сначала установи состояние чтением, без автоматического повтора.

Для `setField` в 1.31.1 допустимы только `title`, `descriptionUserText`, `plannedDuration`, `plannedBeginDateTime`, `plannedEndDateTime`; остальные операции/поля требуют проверки matching source и бизнес-правил. `apply` может пересчитать производную доступность, статус и timestamps связанных карточек; после записи проверь все `changedTaskIds`, а не только указанную задачу.

Для порученной связи в той же версии доступны `addRelation` и `removeRelation` с направлением `fromTaskId` → `toTaskId` и `relation` равным `contains` или `blocks`. Оба существующих endpoint должны иметь актуальные ETag preconditions; `contains` означает «from содержит to», `blocks` — «from блокирует to». Пример добавления связи:

```json
{
  "schemaVersion": 1,
  "applicationId": "unique-stable-id-for-relation-request",
  "proposalRefs": [{ "id": "user-request-id", "revision": 1 }],
  "author": "Codex",
  "reason": "Согласованная связь задач",
  "preconditions": [
    { "taskId": "FROM_ID", "etag": "FROM_ETAG_FROM_TASK_JSON" },
    { "taskId": "TO_ID", "etag": "TO_ETAG_FROM_TASK_JSON" }
  ],
  "operations": [{
    "operationId": "add-edge",
    "kind": "addRelation",
    "relation": "contains",
    "fromTaskId": "FROM_ID",
    "toTaskId": "TO_ID"
  }]
}
```

Перед записью проверь обе задачи и `apply --dry-run`. После записи прочитай обе: CLI поддерживает симметричные `contains`/`parent` и `blocks`/`blockedBy` ссылки. Для удаления измени только `kind` на `removeRelation` и используй новый `applicationId` и свежие ETag. Самосвязи, циклы и некорректный итоговый граф отклоняются.
