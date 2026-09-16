# Лента: безопасное восстановление и выравнивание блоков

## Цель и границы

- Metadata: UI bugfix; `feat/daily-feed`; профили `dotnet-desktop-client` + `ui-automation-testing`; short SPEC допустима: один локальный обратимый outcome без миграции, конфигурации, публичного API или внешнего side effect.
- AS-IS: при редактировании блока `MarkdownBlockPatch.ApplyTo` корректно отвергает несовпавший диапазон, но `QueueSaveDraft` вызывает `CreatePatch` без обработки ошибки. Пользователь получает необработанный `InvalidOperationException` вместо сохранения текста и понятного состояния восстановления.
- Цель: ни при каком несовпадении cached блока и текущего document snapshot набор текста не останавливает приложение и не теряется; в дневной заметке блоки образуют одну ровную reading column, а task checkbox полностью помещается в выделенную для него область.
- Non-goals: не ослаблять проверку целостности patch, не применять текст по приблизительному совпадению, не менять Markdown-файлы вне успешного revision-checked commit, не менять UX обычного сохранения, не менять ширину reading column между днями по содержимому.
- Effective runtime: .NET 10 / Avalonia desktop; Visual Studio остановилась на `MarkdownLivePreviewEditorViewModel.cs:46`; доступ к live IDE automation недоступен, поэтому точные locals не являются частью claim.

## Результат, решения и проверки

| Observable scenario / решение (owner) | AC / ожидаемый результат | Команда / evidence |
| --- | --- | --- |
| Snapshot меняется так, что cached `Start/Length/OriginalRaw` перестаёт совпадать; редактор пытается сохранить черновик (ViewModel) | Исключение не выходит в UI dispatcher; активный текст остаётся в editor, появляется локализованная recoverable ошибка, durable draft хранит исходный editor text без вычисленного patched document. | Новый воспроизводящий TUnit test сначала red; затем `MarkdownLivePreviewEditorTests` и `MarkdownLivePreviewEditorUiTests` serial. |
| Пользователь повторно редактирует блок после этой ошибки (UI) | Нормальный commit не повреждает документ; при revision conflict остаётся существующий recovery flow, а не silent overwrite. | Headless UI assertion: block editing/error state и последующий accepted commit. |
| Обычный autosave с корректным patch (regression) | Существующие autosave/revision/BOM contracts не меняются. | Целевые классы; полный `Unlimotion.Test` перед завершением. |
| Один день содержит узкие и широкие блоки, несколько дней видимы в ленте (layout) | Каждый block row использует одну и ту же ширину reading column; content выровнен по её левому краю и не центрируется/не сжимается от собственного содержимого. | Headless bounds test для короткого и длинного блока в двух днях; native screenshot fallback, если test harness не записывает видео. |
| Task checkbox в preview/edit block (layout) | Visible bounds checkbox не обрезаны справа или снизу; visual glyph масштабируется до заданного 16 DIP, hit target остаётся доступен и baseline строки не скачет. | Headless bounds/clip assertion и существующий toggle UI test. |

### Решение

- Оставить `MarkdownBlockPatch.ApplyTo` fail-closed.
- В draft-persistence пути защищённо обрабатывать невозможность сформировать patch: записывать `FeedDraft` с `EditorText`, snapshot revision/path и отсутствующим/безопасным editor-document representation согласно существующему контракту; присваивать error активному блоку и не бросать exception из UI event.
- Для явного commit/autosave преобразовать тот же технический сбой в уже существующее recoverable error state, сохранив editor text и draft. Не пытаться вычислить новый selection или автоматически перезаписывать файл.
- Visual planning artifact: отдельный макет не нужен — новый state использует уже существующее inline `ErrorMessage` на сохраняющемся редакторе; добавляется только локализованная пользовательская формулировка вместо debug English exception.
- Закрепить row контейнера каждого Markdown блока на доступной ширине reading column (`HorizontalAlignment=Stretch`/общий layout width), а не на intrinsic width дочернего preview/editor. Сохранять текущие max width и левый край колонки дня.
- Задать размер checkbox через его layout/control template так, чтобы glyph и clip bounds соответствовали 16 DIP; не использовать только внешние `Width/Height`, если template сохраняет меньший content presenter.

### Изменяемые файлы и риски

- Основные owners: `src/Unlimotion.ViewModel/Feed/MarkdownLivePreviewEditorViewModel.cs`, `src/Unlimotion/Views/MarkdownBlockLivePreviewEditor.axaml` и `MarkdownBlockPreviewControl.cs`.
- Тесты: `src/Unlimotion.Test/MarkdownLivePreviewEditorTests.cs` и `MarkdownLivePreviewEditorUiTests.cs` для error state, widths и checkbox bounds.
- Ресурсы: `Strings.resx`/`Strings.ru.resx`, только если существующий текст ошибки не подходит.
- Риск: заглушить реальный дефект и потерять черновик. Mitigation: fail-closed patch остаётся, новый test читает recovery draft и проверяет исходный editor text; никакого write в Markdown при mismatch.
- Expected objection: «падения нет, но текст всё равно исчез». Mitigation: test доказывает, что editor остаётся активным, а recovery draft сохраняется до явного решения пользователя.
- Риск: stretch row меняет отступы marker/text либо ломает drag/drop. Mitigation: проверять левый край content и существующие selection/drag tests; менять только row width contract.
- Expected objection: «чекбокс нужного размера, но выглядит обрезанным». Mitigation: test проверяет отсутствие visual clipping по bounds и доступность hit target.
- Rollback: один обратимый commit; удаление change возвращает старое fail-fast поведение, данные vault не мигрируются.
- Открытые вопросы: нет.

## Quality gate и review

- Linter 1–20: PASS по short форме — один outcome, AS-IS/TO-BE, non-goals, решения, AC→tests, риск/rollback, owner, approval и журнал заполнены.
- Rubric: outcome 5/5, scope 5/5, evidence 5/5, risk 5/5, reviewability 5/5, UX 5/5; критичных gate нет.
- Post-SPEC review: PASS.
  - Scope/Evidence: рассмотрены screenshot exception, `MarkdownBlockPatch.ApplyTo`, `QueueSaveDraft`, autosave, current reading-column layout и checkbox construction; изменяемые файлы ограничены ViewModel/views/resources/tests.
  - Contract: fail-closed запись и revision checking сохраняются; ширина row не зависит от intrinsic content; checkbox не выходит за собственные bounds.
  - Adversarial: patch mismatch должен оставаться отказом, не fallback search-and-replace; draft обязан пережить error; layout fix не должен сдвинуть marker column или сузить hit target.
  - Role-based: UX PASS (вместо stop — inline recoverable state, стабильная колонка и целый checkbox); Tester PASS (red-first regression + state/draft/bounds assertions); Developer PASS (одна граница обработки, без скрытого mutation); Delivery N/A (без push/release/config); Business workflow N/A (нет доменной логики).
  - Findings/fixes: добавлен явный запрет на approximate patch и AC на текст черновика, row width и clipping checkbox.
  - Re-review: PASS; stop decision — можно запрашивать approval.

## Approval

Ожидается точная фраза: `Спеку подтверждаю`.

## Журнал действий агента

| Фаза / блок | Решение / уверенность | Evidence / что неизвестно | Следующий шаг | Передача человеку / фактическое решение |
| --- | --- | --- | --- | --- |
| SPEC | 0.94 — причина в unguarded draft-persistence path, а не в повреждённом файле | Screenshot указывает на `ApplyTo:46`; код подтверждает `QueueSaveDraft → CreatePatch` без catch. Точные locals/stack из IDE недоступны из-за сбоя automation. | Ждать approval, затем red tests и реализация. | Ожидается `Спеку подтверждаю`. |
| SPEC дополнение | 0.98 — единая ширина row и отсутствие clipping checkbox являются частью того же UX outcome | Пользователь наблюдал content-dependent horizontal scatter и обрезанный checkbox после предыдущей size correction. | Включить bounds/layout regression tests в ту же реализацию. | Решение пользователя получено: включить оба требования. |
| EXEC | 0.90 — fail-closed patch сохранён, mismatch обрабатывается как recoverable draft; row stretch и Viewbox scaling добавлены | Добавлены red-first tests stale snapshot, row width и checkbox host. `dotnet test` заблокирован: `global.json` требует 10.0.400, а каталог SDK пустой и host его не видит. | Восстановить SDK, затем запустить target/full suite и post-EXEC review. | Пользователь подтвердил spec; runtime validation ожидает восстановления среды. |
