# STORM Stories

Сгенерировано: 2026-06-17
Команда: `/storm:cover CV-0005 ST-0015 story sync`

## Story Update

| Story | Status | Coverage | Notes |
| --- | --- | --- | --- |
| ST-0015: Собирать, обновлять и проверять cross-platform application shells | implemented | TS-0011, TS-0015, TS-0024 | Desktop remains release-supported; Android/browser/iOS are covered as project-contract supported surfaces without runtime release claim. |

## ST-0015 Acceptance Criteria

| AC | Coverage | Tests | Scenarios | Notes |
| --- | --- | --- | --- | --- |
| AC-0041 | critical | TS-0011, TS-0015 | SC-0015-001 | Desktop shell build/update/startup evidence preserved. |
| AC-0042 | critical | TS-0015, TS-0024 | SC-0015-002 | Android/browser/iOS project contracts covered 3/3; runtime release maturity remains out of scope. |
| AC-0043 | critical | TS-0011, TS-0015 | SC-0015-003 | CI and README media automation evidence preserved. |

## Updated Scenario

| Scenario | Status | Test |
| --- | --- | --- |
| SC-0015-002: Android, browser и iOS shell projects сохраняют общий UI contract без заявления runtime release support. | passing | TS-0015, TS-0024 |

## Residual Story Gaps

| Story / область | Gap | Следующее действие |
| --- | --- | --- |
| ST-0014 / AC-0040 | Git timer/conflict-safety остаётся draft/gap. | Отдельная `/storm:bdd-implement ST-0014` SPEC, если поведение поддерживается продуктом. |
| Platform runtime | Android/browser/iOS runtime launch/release pipeline evidence не покрывались. | Отдельная platform validation SPEC при необходимости release support claims. |
| CV-0007 | Attachment workflow не подтверждён как актуальная продуктовая поверхность. | Следующий `/storm:cover` candidate. |
