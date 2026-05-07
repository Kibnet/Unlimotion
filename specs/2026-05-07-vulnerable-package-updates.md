# Обновление уязвимых NuGet-зависимостей

## 0. Метаданные
- Тип (профиль): delivery-task; core stack: `model-behavior-baseline`, `quest-governance`, `quest-mode`, `collaboration-baseline`, `testing-baseline`, `testing-dotnet`; профиль: `dotnet-desktop-client`; локальный override: UI tests only if UI behavior changes.
- Владелец: Codex / Kibnet.
- Масштаб: medium.
- Целевая модель: gpt-5.5.
- Целевой релиз / ветка: `fix/remove-warnings`.
- Ограничения:
  - До подтверждения спеки менять только этот spec-файл.
  - Не обновлять все зависимости подряд; менять только пакеты, которые дают NU190x vulnerabilities, и минимально нужный код.
  - Учитывать release notes и breaking changes перед выбором версии.
  - Не менять публичные API и UI-поведение без отдельного согласования.
- Связанные ссылки:
  - AutoMapper advisory: https://github.com/advisories/GHSA-rvv3-g6hj-g44x
  - AutoMapper 15.0 upgrade guide: https://docs.automapper.io/en/stable/15.0-Upgrade-Guide.html
  - AutoMapper 15.1.3 release: https://github.com/LuckyPennySoftware/AutoMapper/releases/tag/v15.1.3
  - Tmds.DBus.Protocol advisory/release: https://github.com/tmds/Tmds.DBus/security/advisories/GHSA-xrw6-gwf8-vvr9, https://github.com/tmds/Tmds.DBus/releases/tag/rel%2F0.21.3
  - System.Drawing.Common advisory: https://github.com/advisories/GHSA-rxg9-xrhp-64gj
  - System.Drawing.Common platform note: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only
  - System.Drawing.Common 5.0.3 package: https://www.nuget.org/packages/System.Drawing.Common/5.0.3

Если секция не применима, явно укажите `Не применимо` и короткую причину, вместо заполнения нерелевантными деталями.

## 1. Overview / Цель
Обновить NuGet-зависимости, которые сейчас дают warning-и `NU1903` / `NU1904` при сборке, и внести только необходимые совместимые правки кода.

Outcome contract:
- Success means:
  - `dotnet list ... package --vulnerable --include-transitive` больше не показывает найденные уязвимые пакеты для затронутых проектов.
  - Build `src/Unlimotion.Test/Unlimotion.Test.csproj` проходит без `NU1903` / `NU1904` по `AutoMapper`, `System.Drawing.Common`, `Tmds.DBus.Protocol`.
  - Изменения совместимости после package upgrade учтены в коде и проверены тестами.
- Итоговый артефакт / output:
  - Обновленные package versions / package references.
  - Минимальные code changes для AutoMapper 15.x.
  - Обновленный журнал спеки и итоговый отчет с командами проверки.
- Stop rules:
  - Остановиться и запросить решение пользователя, если безопасное обновление требует смены архитектуры, лицензии/коммерческого решения или публичного API.
  - Остановиться, если release notes указывают на breaking change, который нельзя проверить локально.
  - После EXEC остановиться только после build, vulnerability audit и релевантных тестов либо явного отчета, почему проверка недоступна.

## 2. Текущее состояние (AS-IS)
- Централизованные версии живут в `src/Directory.Packages.props`.
- `src/Unlimotion.Test/Unlimotion.Test.csproj` собирает основной граф через project references на desktop/app/server-service-interface.
- Существующий warning log `artifacts/build-warnings-after-null-defaults.log` показывает:
  - `AutoMapper 13.0.1`: `NU1903`, high, advisory `GHSA-rvv3-g6hj-g44x`.
  - `System.Drawing.Common 5.0.2`: `NU1904`, critical, advisory `GHSA-rxg9-xrhp-64gj`, транзитивно через `ServiceStack.Server 6.4.0` -> `ServiceStack` -> `System.Drawing.Common`.
  - `Tmds.DBus.Protocol 0.21.2`: `NU1903`, high, advisory `GHSA-xrw6-gwf8-vvr9`, транзитивно через `Avalonia.FreeDesktop 11.3.7`.
- `dotnet list --no-restore` подтвердил:
  - `src/Unlimotion.Test`: vulnerable `AutoMapper 13.0.1`, `Tmds.DBus.Protocol 0.21.2`.
  - `src/Unlimotion.Desktop`: vulnerable `AutoMapper 13.0.1`, `Tmds.DBus.Protocol 0.21.2`.
  - `src/Unlimotion.Server.ServiceInterface`: direct vulnerable `AutoMapper 13.0.1`, transitive vulnerable `System.Drawing.Common 5.0.2`.
  - `src/Unlimotion`: direct vulnerable `AutoMapper 13.0.1`.
- AutoMapper is used in:
  - `src/Unlimotion/AppModelMapping.cs`
  - `src/Unlimotion.Server/AppModelMapping.cs`
  - services and storage classes through injected `IMapper`.
- Direct `new MapperConfiguration(cfg)` appears only in the two `AppModelMapping.cs` files.
- `System.Drawing` source usage is only in `tests/Unlimotion.ReadmeMedia/Program.cs`, which targets `net10.0-windows7.0`; the vulnerable `System.Drawing.Common 5.0.2` warning in the main build is transitive from ServiceStack, not direct source usage.
- `Tmds.DBus.Protocol` has no direct source usage; it is an Avalonia FreeDesktop runtime dependency.

## 3. Проблема
Сборка продолжает выдавать security warnings из-за трех уязвимых NuGet-пакетов, при этом простое "latest everything" может принести лишние breaking changes в UI/runtime/server граф.

## 4. Цели дизайна
- Разделение ответственности: package version changes stay in central package management; direct transitive overrides are explicit in the projects that need them.
- Повторное использование: keep existing AutoMapper profiles and DI shape; update only initialization API.
- Тестируемость: verify restore/build, vulnerability audit, and existing test suites covering mappings/storage/UI flows.
- Консистентность: keep Avalonia package family unchanged unless needed for the vulnerability.
- Обратная совместимость: avoid broad ServiceStack/Avalonia major upgrades for this task; prefer focused vulnerable-package remediation.

## 5. Non-Goals (чего НЕ делаем)
- Не обновляем все NuGet-пакеты до latest.
- Не мигрируем с AutoMapper на ручной mapping.
- Не обновляем ServiceStack/Avalonia major/minor ради общего housekeeping, если vulnerability можно закрыть точечным package override.
- Не меняем UI-поведение, selectors, routes, API contracts или persisted data.
- Не исправляем оставшиеся non-security warnings (`EXEC`, obsolete API, general compiler warnings), кроме побочных изменений, необходимых для security updates.

## 6. Предлагаемое решение (TO-BE)
### 6.1 Распределение ответственности
- `src/Directory.Packages.props` -> централизовать версии:
  - `AutoMapper` -> `15.1.3` (latest 15.x patch with security fix, avoiding an extra 16.x major jump).
  - `Tmds.DBus.Protocol` -> `0.21.3` (security release in same 0.21 line).
  - `System.Drawing.Common` -> `5.0.3` (patched version from the same 5.0 line, avoiding the modern Windows-only behavior change).
- `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` -> add explicit `Tmds.DBus.Protocol` reference so Avalonia FreeDesktop resolves the patched version.
- `src/Unlimotion.Server.ServiceInterface/Unlimotion.Server.ServiceInterface.csproj` -> add explicit `System.Drawing.Common` reference so ServiceStack's vulnerable transitive 5.0.2 is overridden.
- `src/Unlimotion/AppModelMapping.cs` and `src/Unlimotion.Server/AppModelMapping.cs` -> pass `ILoggerFactory` to `MapperConfiguration`.

### 6.2 Детальный дизайн
- AutoMapper:
  - Keep `MapperConfigurationExpression`.
  - Replace `new MapperConfiguration(cfg)` with `new MapperConfiguration(cfg, NullLoggerFactory.Instance)` if 15.x requires the logger factory overload.
  - Add `using Microsoft.Extensions.Logging.Abstractions;` if needed.
  - Do not configure license key unless build/runtime proves it is required; AutoMapper 15 release notes introduce license enforcement/logging, so this remains an explicit risk to verify.
- Tmds.DBus.Protocol:
  - Use `0.21.3` instead of upgrading to later unrelated API lines, because release `rel/0.21.3` is specifically the security fix for advisory `GHSA-xrw6-gwf8-vvr9`.
  - Add direct package reference in the desktop project where Avalonia FreeDesktop dependency is resolved.
- System.Drawing.Common:
  - Use direct package reference to override transitive vulnerable `5.0.2`.
  - Prefer `5.0.3` because the advisory lists it as patched and it keeps the previous 5.0 package-line behavior.
  - Verify no vulnerable `System.Drawing.Common 5.0.2` remains in the resolved graph.
- output/evidence rules:
  - Evidence must include release/advisory links, changed files, version table, build/audit/test commands and results.
- границы сохранения поведения:
  - Mapping configuration stays logically identical.
  - Package graph changes are allowed only to remediate the identified vulnerabilities.
- обработка ошибок:
  - If restore resolves incompatible versions or build fails due package APIs, first prefer minimum code adaptation; if it implies product/API changes, stop for user decision.
- производительность:
  - No expected performance change; no performance benchmark required.

## 7. Бизнес-правила / Алгоритмы (если есть)
Не применимо: задача про dependency remediation, а не доменный алгоритм.

## 8. Точки интеграции и триггеры
- Package restore/build resolves versions from `src/Directory.Packages.props`.
- `AppModelMapping` runs during app/server startup to initialize AutoMapper mappings.
- `Unlimotion.Desktop` package graph pulls Avalonia FreeDesktop dependencies.
- `Unlimotion.Server.ServiceInterface` package graph pulls ServiceStack dependencies.

## 9. Изменения модели данных / состояния
- Новых persisted fields нет.
- Миграций данных нет.
- Runtime state changes are limited to dependency versions and AutoMapper configuration construction.

## 10. Миграция / Rollout / Rollback
- Rollout:
  - Update package versions and direct references.
  - Build and test locally.
  - Update existing PR branch with new commit(s).
- Rollback:
  - Revert the dependency-update commit if build/runtime checks reveal incompatible behavior.
  - Since no data schema changes are planned, rollback is source-only.
- Совместимость:
  - AutoMapper 15.x requires constructor adaptation and may log/enforce license behavior; verify in tests/build.
  - System.Drawing.Common 6+ is Windows-only for supported cross-platform use, so the selected remediation uses patched 5.0.3 instead of jumping to the modern package line.

## 11. Тестирование и критерии приёмки
- Acceptance Criteria:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false -v:minimal` succeeds.
  - `dotnet list src\Unlimotion.Test\Unlimotion.Test.csproj package --vulnerable --include-transitive` reports no vulnerable packages for the test graph, or only unrelated packages not in scope with explicit explanation.
  - `dotnet list src\Unlimotion.Desktop\Unlimotion.Desktop.csproj package --vulnerable --include-transitive` reports no vulnerable packages in scope.
  - `dotnet list src\Unlimotion.Server.ServiceInterface\Unlimotion.Server.ServiceInterface.csproj package --vulnerable --include-transitive` reports no vulnerable packages in scope.
  - Existing mapping initialization compiles and tests pass.
- Какие тесты добавить/изменить:
  - No new test expected unless AutoMapper upgrade changes mapping behavior.
  - If a mapping regression is found, add/update targeted mapping test before fixing.
- Characterization tests / contract checks:
  - Existing `MainWindowViewModelTests`, task availability/repeater tests, and UI command tests serve as regression coverage for app flows touched indirectly by mappings/storage.
- Команды для проверки:
  - `dotnet build src\Unlimotion.Test\Unlimotion.Test.csproj -m:1 /nr:false -v:minimal`
  - `dotnet list src\Unlimotion.Test\Unlimotion.Test.csproj package --vulnerable --include-transitive`
  - `dotnet list src\Unlimotion.Desktop\Unlimotion.Desktop.csproj package --vulnerable --include-transitive`
  - `dotnet list src\Unlimotion.Server.ServiceInterface\Unlimotion.Server.ServiceInterface.csproj package --vulnerable --include-transitive`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainWindowViewModelTests/*"`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlTreeCommandsUiTests/*"`
  - `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj --no-build -- --treenode-filter "/*/*/MainControlAvailabilityUiTests/*"`
  - Full test run: `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj --no-build`
- Stop rules для test/retrieval/tool/validation loops:
  - Retry restore/build once after clearing stale assets only if stale assets are the likely cause.
  - Do not keep iterating through unrelated outdated package warnings.
  - If full test run hangs again, stop after a bounded timeout, kill child processes, and report targeted coverage plus hang.

## 12. Риски и edge cases
- AutoMapper 15 license behavior:
  - Risk: runtime logs or enforcement may require a license key.
  - Mitigation: use 15.1.3 as latest fixed 15.x; verify startup/mapping tests; if license blocks runtime, stop for user decision.
- System.Drawing.Common platform behavior:
  - Risk: jumping to modern System.Drawing.Common would introduce Windows-only behavior.
  - Mitigation: use patched 5.0.3 from the same package line as the vulnerable transitive dependency.
- Transitive override:
  - Risk: direct PackageReference may not affect all project graphs.
  - Mitigation: run vulnerability audit on Test/Desktop/Server.ServiceInterface after restore.
- Package source:
  - Risk: local `artifacts/nuget-local` source or preview SDK may affect resolution.
  - Mitigation: use standard restore/build output as evidence.

## 13. План выполнения
1. После `Спеку подтверждаю`, update `src/Directory.Packages.props`.
2. Add direct `Tmds.DBus.Protocol` reference to `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`.
3. Add direct `System.Drawing.Common` reference to `src/Unlimotion.Server.ServiceInterface/Unlimotion.Server.ServiceInterface.csproj`.
4. Update AutoMapper configuration constructors in both `AppModelMapping.cs` files if build requires the 15.x overload.
5. Run restore/build and vulnerability audits.
6. Run targeted tests, then full test run if feasible.
7. Run post-EXEC review, fix high-confidence issues, update spec journal.
8. Commit changes and update the existing PR branch.

## 14. Открытые вопросы
Нет блокирующих вопросов. Предложенный вариант выбирает минимальные safe-line updates вместо broad dependency modernization.

## 15. Соответствие профилю
- Профиль: `dotnet-desktop-client` plus `testing-dotnet`; backend service interface is touched only for package graph remediation, without API/endpoint behavior changes.
- Выполненные требования профиля:
  - No planned UI thread/blocking changes.
  - No planned UI behavior or automation-id changes.
  - Build and tests are explicit acceptance criteria.
  - Local `AGENTS.override.md` UI-test requirement is not triggered by planned UI behavior changes; existing UI tests remain part of regression validation because app graph changes affect desktop startup.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Directory.Packages.props` | Update/add central package versions for vulnerable packages | Central package management |
| `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` | Add direct `Tmds.DBus.Protocol` reference | Override vulnerable Avalonia transitive |
| `src/Unlimotion.Server.ServiceInterface/Unlimotion.Server.ServiceInterface.csproj` | Add direct `System.Drawing.Common` reference | Override vulnerable ServiceStack transitive |
| `src/Unlimotion/AppModelMapping.cs` | Adapt AutoMapper configuration constructor | AutoMapper 15 compatibility |
| `src/Unlimotion.Server/AppModelMapping.cs` | Adapt AutoMapper configuration constructor | AutoMapper 15 compatibility |
| `specs/2026-05-07-vulnerable-package-updates.md` | Update journal and EXEC result | QUEST audit trail |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| AutoMapper | `13.0.1`, vulnerable | `15.1.3`, patched 15.x line |
| Tmds.DBus.Protocol | transitive `0.21.2`, vulnerable | direct-resolved `0.21.3`, patched same line |
| System.Drawing.Common | transitive `5.0.2`, critical vulnerable | direct-resolved `5.0.3`, patched same major line |
| Mapping initialization | `new MapperConfiguration(cfg)` | logger-factory overload if required by AutoMapper 15 |

## 18. Альтернативы и компромиссы
- Вариант: Update all root packages including Avalonia and ServiceStack.
  - Плюсы: more globally current dependency graph.
  - Минусы: much larger compatibility surface, UI/runtime risk, unrelated changes.
  - Почему не выбран: user asked vulnerable warning libraries; broad modernization is outside scope.
- Вариант: Add only `NoWarn` suppressions.
  - Плюсы: minimal diff.
  - Минусы: leaves known vulnerabilities unresolved.
  - Почему не выбран: contradicts request to update vulnerable libraries.
- Вариант: Use `System.Drawing.Common 5.0.3`.
  - Плюсы: smallest major-version delta for the vulnerable package; avoids the Windows-only behavior change introduced in modern `System.Drawing.Common`.
  - Минусы: old package line in a `net10.0` project.
  - Почему выбран: the advisory lists `5.0.3` as patched, and preserving platform behavior is more important here than aligning to the latest package line.
- Вариант: Update AutoMapper to latest 16.x.
  - Плюсы: latest major line.
  - Минусы: unnecessary additional major-version risk.
  - Почему не выбран: 15.1.3 is sufficient to clear the advisory while minimizing breaking-change surface.

## 19. Результат quality gate и review
### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, цели и Non-Goals зафиксированы. |
| B. Качество дизайна | 6-10 | PASS | Ответственность, интеграция, compatibility и rollback описаны. |
| C. Безопасность изменений | 11-13 | PASS | Данные не меняются; риски AutoMapper/System.Drawing/transitive override выделены. |
| D. Проверяемость | 14-16 | PASS | Acceptance criteria и команды build/audit/test перечислены. |
| E. Готовность к автономной реализации | 17-19 | PASS | План и tradeoffs есть; блокирующих вопросов нет. |
| F. Соответствие профилю | 20 | PASS | .NET desktop/testing требования учтены; UI override объяснен. |

Итог: ГОТОВО

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | Scope ограничен vulnerable warnings, Non-Goals явные. |
| 2. Понимание текущего состояния | 5 | Пакеты, цепочки и файлы использования перечислены. |
| 3. Конкретность целевого дизайна | 5 | Версии, direct references и code adaptations заданы. |
| 4. Безопасность (миграция, откат) | 5 | Rollout/rollback и breaking-change risks описаны. |
| 5. Тестируемость | 5 | Есть build, audit, targeted и full test commands. |
| 6. Готовность к автономной реализации | 5 | Нет блокирующих вопросов; stop rules заданы. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению

### Post-SPEC Review
- Статус: PASS
- Что исправлено:
  - Добавлен явный tradeoff между `System.Drawing.Common 5.0.3` и `10.0.7`.
  - Зафиксировано, что AutoMapper 16.x не нужен для текущего security goal.
  - Уточнены stop rules для license/runtime blockers.
- Что осталось на решение пользователя:
  - Только подтверждение перехода в EXEC фразой `Спеку подтверждаю`.

### Post-EXEC Review
- Статус: PASS
- Что исправлено до завершения:
  - Заменен первоначальный `System.Drawing.Common 10.0.7` на patched `5.0.3`, потому что это закрывает advisory без Windows-only breaking change modern `System.Drawing.Common`.
  - Повторены build, vulnerability audit и key targeted tests после смены версии.
- Что проверено дополнительно:
  - `AutoMapper 15.1.3` требует `ILoggerFactory` overload; оба `AppModelMapping` используют `NullLoggerFactory.Instance`.
  - Direct overrides для `Tmds.DBus.Protocol 0.21.3` и `System.Drawing.Common 5.0.3` действительно очищают affected package graphs.
- Остаточные риски / follow-ups:
  - Полный тестовый прогон `dotnet run --project src\Unlimotion.Test\Unlimotion.Test.csproj --no-build` снова не завершился за 20 минут; процесс был остановлен. Targeted regression coverage прошла.

## Approval
Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента
Заполняется инкрементально после каждого значимого блока работ. Одна строка = один завершённый значимый блок.

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Инвентаризация | 0.95 | Нет | Сформировать решение и quality gate | Нет | Нет | Логи и `dotnet list --no-restore` указывают на три vulnerable packages. | `artifacts/build-warnings-after-null-defaults.log`, `src/Directory.Packages.props`, project assets |
| SPEC | Release notes / breaking changes | 0.85 | Runtime license behavior AutoMapper нужно проверить EXEC-тестами | Создать spec | Нет | Нет | Первичный план выбрал AutoMapper 15.x, Tmds 0.21.3, System.Drawing 10.0.7; post-EXEC review уточнил System.Drawing до patched 5.0.3. | GitHub advisories, release notes, NuGet pages |
| SPEC | Quality gate | 0.90 | Нет | Запросить подтверждение спеки | Да | Да, ожидается фраза `Спеку подтверждаю` | Quest-mode запрещает менять код до подтверждения. | `specs/2026-05-07-vulnerable-package-updates.md` |
| EXEC | Dependency/code update | 0.85 | Нужны restore/build результаты | Запустить build и vulnerability audit | Нет | Нет | Внесены только утвержденные package overrides и AutoMapper constructor adaptation. | `src/Directory.Packages.props`, `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj`, `src/Unlimotion.Server.ServiceInterface/Unlimotion.Server.ServiceInterface.csproj`, `src/Unlimotion/AppModelMapping.cs`, `src/Unlimotion.Server/AppModelMapping.cs` |
| EXEC | Post-EXEC review fix | 0.95 | Нужны повторные проверки | Повторить build/audit/tests | Нет | Нет | `System.Drawing.Common` заменен с `10.0.7` на patched `5.0.3`, чтобы закрыть advisory без Windows-only breaking change. | `src/Directory.Packages.props`, `specs/2026-05-07-vulnerable-package-updates.md` |
| EXEC | Build/audit validation | 0.95 | Нет | Запустить targeted tests | Нет | Нет | Build `Unlimotion.Test.csproj` успешен; audits для `Unlimotion.Test`, `Unlimotion`, `Unlimotion.Desktop`, `Unlimotion.Server.ServiceInterface` не показывают vulnerable packages. | build output, `dotnet list package --vulnerable` |
| EXEC | Test validation | 0.85 | Полный прогон не завершился за 20 минут | Подготовить коммит и обновить PR | Нет | Нет | Targeted UI/ViewModel/domain tests passed; full run timed out and was stopped. | TUnit targeted runs |
