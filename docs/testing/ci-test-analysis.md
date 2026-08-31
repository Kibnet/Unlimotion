# Анализ тестовых запусков

Метаданные исполнения фиксируются **до** запуска test host в `invocation-test.json`; `stage-test.json` связывает завершение с непустым invocation ID. Повторный разбор берёт SHA/tree/runtime из manifest, а не с машины анализа. Без manifest или корректного массива аргументов данные считаются неполными и не объединяются между запусками. Полнота требует завершённого invocation, согласованных TRX counters и непустого HTML. Частичные результаты при отмене сохраняются, но не становятся успешным запуском; аварийный exit классифицируется как runner failure даже при наличии Failed в TRX. Обычный test failure соответствует exit 2 и фактическим Failed records.

Stage helper передаёт каталог в форме `--results-directory=<path>`. В локальном SDK 10.0.400 отдельный аргумент с уже существующим каталогом результатов был ошибочно принят CLI за позиционный каталог и test host не запускался. Каталог требуется создать заранее для frozen manifest, поэтому используется attached form; fingerprint нормализует обе формы одинаково.

`Unlimotion Tests / All tests` сохраняет прежнее имя check, Windows runner и 30-минутный timeout. Restore и build обоих проектов предшествуют tests. Ошибка одного проекта не блокирует другой, если собственный build успешен и job не отменён. Отмена/timeout общей машины всё ещё прерывает оба.

После Git delivery и успешной проверки upload в GitHub ожидаются отдельные artifacts `tests-<run>-<attempt>-main` и `...-headless`, retention 14 дней. Их allowlist содержит TRX, TUnit HTML, `run.json`, `tests.json`, summary и созданные diagnostic traces; workspace, конфигурация и RavenDB не загружаются. Workflow не использует PAT, runtime token в тестах или write permissions.

`run.json` schemaVersion=1 связывает step exit codes, test args, SHA/tree, SDK/runtime/TUnit, runner image и completeness. Длительности отдельных тестов берутся из TRX; их сумма не равна wall-clock. `tests.json` сохраняет исходный executionId отдельно от logical identity. Неоднозначные identities не объединяются в историю. Nested BDD cases остаются trace дочерними операциями, не дополнительными TUnit executions.

```powershell
pwsh -File scripts/ci/Test-TestReport.ps1
pwsh -File scripts/ci/Test-TestOrchestration.ps1
pwsh -File scripts/ci/Test-TargetedTestSeries.ps1
pwsh -File scripts/ci/Write-TestReport.ps1 -ResultsRoot artifacts/test-results/<run-attempt> -OutputRoot artifacts/test-analysis/<run-attempt>
pwsh -File scripts/ci/Write-TestReport.ps1 -ResultsRoot artifacts/test-results/<run-attempt> -OutputRoot artifacts/test-analysis/<run-attempt> -HistoryRoot artifacts/downloaded-test-history
```

В history указываются распакованные каталоги артефактов. Повторные копии одного execution не увеличивают denominator; attempts различаются. Выбираются последние десять логических runs **с доступной metadata**, не обещается восстановление отсутствующих/истёкших GitHub artifacts. Группы разделены по дереву и environment fingerprint. `failed / observed` и смена fail/pass — диагностические признаки, не доказанный flaky root cause. При отсутствии данных вывод ограничен фактической выборкой; p95 не выдумывается.

Серия отдельных процессов запускается после build и проверки одиночного фильтра:

```powershell
pwsh -File scripts/ci/Invoke-TargetedTestSeries.ps1 -Project src/Unlimotion.Test/Unlimotion.Test.csproj -TreeNodeFilter "/*/*/StormEmojiFilterExecutableSpecTests/*" -Repeat 5 -ExpectedTests 1 -OutputRoot artifacts/emoji-series-new
```

Серия требует ровно `ExpectedTests` результатов Passed и прекращается на nonzero exit, skipped, malformed TRX или count mismatch; не превращает flaky test в green через retry. Старый output directory не перезаписывается.

`diagnostics-<pid>.jsonl` содержит lifecycle, UI resource leases, independent subcases и именованные fault cases. `test-lifecycle` включает hooks и не является точным `test body`; последний анализируется по TUnit HTML spans. Resource overlap анализировать только внутри одного process/ресурса; два независимых процесса имеют разные Avalonia globals.

Локальные проверки скриптов и stub runner не подменяют GitHub evaluation `if`/upload. Live GitHub verification требует отдельной delivery authorization и конкретного run URL. До этого изменения не считаются проверенными на GitHub.

## Статус текущей реализации

Карта shared resources находится в `ci-test-resources.md`. `scheduler-scope` отмечает весь интервал test hooks для объявленных constraint keys и limiters, включая тесты с прямыми Headless sessions. Lease `AvaloniaHeadless` охватывает только Safe-сессии, `AvaloniaDispatcher` — callback общего dispatch helper; сами по себе эти два lease не доказывают полный охват. Ошибки записи trace откладываются до окончания cleanup и сообщаются через test/session hook, а не прерывают `finally`.

Локальный EXEC по `specs/2026-08-31-ci-test-observability-performance-stability.md` проверен: Main3×906 и Headless3×38 — 2832Passed; controlled emoji RED/GREEN и100процессов описаны с границами сборок. Targeted ускорение подтверждено, full performance **INCONCLUSIVE** из-за неполной пригодной парной серии; вместимость неизменного CI30m budget и GitHub upload ещё не проверены. Полные результаты, старые failures и ограничения — в `ci-test-implementation-evidence.md`. Raw artifacts: `artifacts/ci-implementation-2026-08-31` (local-only, ignored).
