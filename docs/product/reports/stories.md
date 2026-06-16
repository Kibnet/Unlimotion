# STORM Stories

Сгенерировано: 2026-06-17
Команда: `/storm:cover ST-0014 CV-0004 story sync`

## Story Update

| Story | Status | Coverage | Notes |
| --- | --- | --- | --- |
| ST-0014: Получать доступ к задачам через Telegram bot | partial | TS-0022, TS-0023 | Command/auth and callback subset covered. Git timer/conflict-safety remains gap. |

## ST-0014 Acceptance Criteria

| AC | Coverage | Tests | Scenarios | Notes |
| --- | --- | --- | --- | --- |
| AC-0039 | full | TS-0022 | SC-0014-001 | Allowed-user gate and /start /help /search /task /root covered. |
| AC-0040 | partial | TS-0023 | SC-0014-002, SC-0014-003 | Callback subset covered; timer part remains draft/gap. |

## New Scenario

| Scenario | Status | Test |
| --- | --- | --- |
| SC-0014-003: Callback-действия открывают задачу, меняют статус, удаляют и показывают отношения. | passing | TS-0023 |
