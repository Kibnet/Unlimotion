# Настраиваемый формат имени ежедневной заметки

## 0. Метаданные

- Тип (профиль): `dotnet-desktop-client` + `ui-automation-testing`.
- Владелец: Unlimotion / режим «Лента».
- Масштаб: medium.
- Целевое семейство / behavior baseline: реализованный local-first режим «Лента» из `specs/2026-08-24-daily-feed-mode.md`; действующий filename contract — `Ежедневные/yyyy-MM-dd.md`.
- Поверхность: Codex / desktop-клиент Avalonia; внешнее действие пользователя — настройка в UI.
- Effective runtime: .NET 10 / Avalonia; model/runtime eval не применим, так как изменение не связано с model/prompt behavior.
- Eval baseline / evidence: сценарии `yyyy-MM-dd` и `yyyy.MM.dd`, persisted sidecar, unit + Headless/AppAutomation UI flow и screenshot настройки. До/после model eval — не применимо.
- Целевой релиз / ветка: текущая рабочая ветка `feat/daily-feed`; не создаёт commit, push, PR-ready, merge или release без отдельного разрешения.
- Ограничения:
  - До точной фразы пользователя `Спеку подтверждаю` изменяется только эта SPEC.
  - Дневные Markdown-файлы остаются источником пользовательских данных; формат задаёт только имя файла в фиксированной папке `Ежедневные/`.
  - Значение формата принадлежит выбранному vault и синхронизируется в его `.unlimotion/`; это не глобальная настройка, которая случайно переезжает на другой vault.
  - Никакого автоматического rename/migration Markdown-файлов.
  - Для UI-поведения обязательны автоматические UI-тесты; при отсутствии доступного desktop video используется документированный screenshot + Headless/FlaUI fallback.
- Связанные ссылки:
  - `specs/2026-08-24-daily-feed-mode.md` — эта SPEC заменяет только её жёсткое правило `Ежедневные/YYYY-MM-DD.md` на один настраиваемый активный layout.
  - `src/Unlimotion.Notes/Daily/DailyNoteService.cs`.
  - `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs`.
  - `src/Unlimotion/Views/SettingsControl.axaml`.

## 1. Overview / Цель

Пользователь задаёт в настройках формат *основы имени* дневного файла. После применения `yyyy.MM.dd` Лента читает, создаёт и дописывает `Ежедневные/2026.08.25.md`, а не `Ежедневные/2026-08-25.md`.

Outcome contract:

- Success means:
  - стандартная установка без настройки продолжает работать с `yyyy-MM-dd`;
  - `yyyy.MM.dd` можно ввести, проверить по preview и применить без изменения кода;
  - один и тот же активный контракт используется во всех путях Ленты: timeline, quick capture, search, review, first-connect bootstrap, move-to-today и ссылки;
  - применение не переименовывает, не удаляет и не перезаписывает существующие Markdown-файлы;
  - настройка переносится вместе с vault, а не наследуется другим выбранным vault на том же устройстве;
  - ошибочный или небезопасный ввод не меняет действующий контракт.
- Итоговый артефакт / output: настройка «Формат имени ежедневной заметки», portable sidecar с layout, централизованный `DailyNoteNaming` и подтверждённые тесты/visual evidence.
- Stop rules:
  - не принимать произвольный DateTime-format, который может создать путь, неоднозначное имя или коллизию дат;
  - не применять новый layout во время незавершённой операции Ленты;
  - не продолжать EXEC при красном обязательном test/UI evidence без объективного blocker и next-best check;
  - не делать автоматическое переименование файлов как скрытый эффект Apply.

## 2. Текущее состояние (AS-IS)

- `DailyNoteService` генерирует, открывает, сканирует и дописывает только `Ежедневные/{date:yyyy-MM-dd}.md`.
- `FeedSearchIndex`, `FeedReviewQueue`, `FirstConnectBootstrapService`, `FeedMoveToTodayService` и `FeedLinkSerializer.MovedBlock` отдельно повторяют или предполагают тот же hardcode.
- Поэтому файл пользователя `Ежедневные/2026.08.25.md` не становится day card, в поиске классифицируется как обычная заметка, а quick capture создаёт параллельный `2026-08-25.md`.
- `SettingsViewModel` уже содержит Note Vault settings, но они хранятся в одном global app-config (`RootPath`, boundary, enable) и не подходят для формата, который описывает содержимое конкретного vault.
- `App.WireNoteVaultFeed` умеет безопасно переинициализировать Ленту при смене root/enable, а boundary меняет live; отдельного controlled rebind для filename layout нет.
- `FirstConnectBootstrapService` принимает только canonical 13-character `yyyy-MM-dd.md` path; старый valid bootstrap не должен стать «повреждённым» только из-за нового layout.
- Имеющийся `FeedVaultWatchRuntime` наблюдает `vault.json`, areas и review sidecars; нового portable layout sidecar ещё нет.

## 3. Проблема

Договорённость Ленты о дате жёстко зашита в нескольких независимых компонентах. Пользователь с существующим Obsidian vault, где дневные файлы называются `2026.08.25.md`, не может подключить историю и безопасно писать в неё без изменения исходного кода; точечная замена одного hardcode сломает поиск, review, bootstrap или recovery.

## 4. Цели дизайна

- Ввести один immutable domain contract, который является единственным владельцем daily relative path и его обратного разбора.
- Дать понятный UI: editable draft, live preview, validation и явный Apply, а не перезапуск vault на каждом символе.
- Сделать формат portable свойством vault и сохранить compatibility с vault без нового sidecar.
- Сохранить текущую дату-ориентированную сортировку и безопасный recovery journal.
- Не терять или менять пользовательские файлы при смене настройки.
- Покрыть happy path, default, invalid input, restart/reconnect, bootstrap и pending move recovery.

## 5. Non-Goals (чего НЕ делаем)

- Не добавляем несколько одновременно активных форматов и не объединяем два файла одной даты.
- Не сканируем/не угадываем форматы по именам файлов.
- Не поддерживаем произвольные локализованные month/day names, custom literals, time fields или вложенные папки в format input.
- Не меняем фиксированную папку `Ежедневные/` и расширение `.md`.
- Не переименовываем существующие файлы, не создаём migration wizard и не исправляем старые Obsidian links.
- Не меняем task storage, области, статус задач, full-text source for thematic notes или поддержку external vault на Browser/iOS/Android.
- Не переводим уже созданные операции/recovery journal на новый путь: их journalled destination остаётся authoritative.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности

| Компонент / файл | Ответственность |
| --- | --- |
| Новый `src/Unlimotion.Notes/Daily/DailyNoteNaming.cs` | Immutable, validated daily filename contract: active stem format, fixed folder/extension, format, parse и resolved path helpers. |
| Новый `src/Unlimotion.Notes/Daily/DailyNoteSettingsStore.cs` | Load/save `.unlimotion/daily-note-settings.json`, schema validation, optimistic revision и preservation unknown JSON fields. |
| `DailyNoteService` | Получает `DailyNoteNaming`; использует его для list/open/append и поиска already-existing active daily path. |
| `FeedSearchIndex` / `FeedReviewQueue` | Получают тот же naming и определяют type/date через него, без локального `TryParseExact`. |
| `FirstConnectBootstrapService` | Получает naming, accepts only layout-matching schema-v1 bootstrap paths and skips other-layout manifests without calling them corrupt. |
| `FeedMoveToTodayService` / `FeedLinkSerializer` | Новый move вычисляет destination через naming; serialiser получает уже resolved relative path. Resume использует journalled path, а не current setting. |
| `FeedViewModel` | Загружает vault setting до создания dependent services; serializes Apply/rebind through session/operation gate; emits applied/external-setting result only for the still-current vault/session. |
| `SettingsViewModel` | Держит editable draft, applied value, dirty-draft/external-change state, preview, validation message и Apply/Reload command; не пишет format в global application config. |
| `App.WireNoteVaultFeed` | Связывает settings command/status с `FeedViewModel`, routes applied/external setting results back to Settings VM and refuses stale-root publication. |
| `SettingsControl.axaml` + resources | Поле, hint, preview, error/status, Apply и stable automation ids. |
| `FeedVaultWatchRuntime` contracts | Распознаёт daily-settings sidecar и invokes a dedicated Feed/settings reload route safely. |

### 6.2 Детальный дизайн

#### 6.2.1 Domain contract `DailyNoteNaming`

`DailyNoteNaming` — immutable value object, созданный только через validation. Его public contract:

```csharp
public sealed class DailyNoteNaming
{
    public const string DefaultFileNameFormat = "yyyy-MM-dd";
    public string FileNameFormat { get; }
    public string FormatStem(DateOnly date);
    public string GetRelativePath(DateOnly date);
    public bool TryParseRelativePath(string relativePath, out DateOnly date);
    public bool IsDailyRelativePath(string relativePath);
}
```

Rules:

1. Input is a filename *stem* only. `Ежедневные/` and `.md` are added by the contract; the user never enters either.
2. Supported grammar contains exactly one of each numeric component `yyyy`, `MM`, `dd`, in any order. Components may be adjacent or separated only by `-`, `.` or `_`. Examples: `yyyy-MM-dd`, `yyyy.MM.dd`, `dd.MM.yyyy`, `yyyyMMdd`.
3. Whitespace, literals, format components outside the three named tokens, path separators, control characters, filename-invalid symbols, `#`, `[`/`]`, and a trailing dot/space are rejected.
4. The implementation also formats and `DateOnly.TryParseExact`s sentinel dates with `InvariantCulture` (including leap day and distinct year/month/day values); every round trip must return the original date. This is the final guard against non-injective or non-parsable formats.
5. `TryParseRelativePath` accepts only one direct child of `Ежедневные/`, an exact `.md` extension and a stem that round-trips using the active format. It does not treat `Ежедневные/archive/…` as daily.

For `yyyy.MM.dd`, `GetRelativePath(new DateOnly(2026, 8, 25))` is exactly `Ежедневные/2026.08.25.md`.

#### 6.2.2 Portable persistence and compatibility

The applied value is stored in the selected vault, not application-global configuration:

```json
{
  "schemaVersion": 1,
  "dailyFileNameFormat": "yyyy.MM.dd"
}
```

Path: `.unlimotion/daily-note-settings.json`.

- Missing file means `yyyy-MM-dd`; this preserves all existing installations and vaults.
- The sidecar is created only after a successful explicit Apply. A blank/unopened vault therefore has no hidden configuration mutation.
- The store validates schema and format before returning an applied value. Invalid/corrupted remote sidecar does not change the in-memory active naming; Feed shows a non-destructive recovery/error state and keeps the previous valid session until user reloads or fixes the file.
- Unknown JSON fields are retained on save. Writes use expected revision; conflict during Apply leaves the draft uncommitted and exposes a reload/retry message.
- The setting is a single active convention for the vault. Files in a different convention remain untouched and discoverable in the general Files/thematic-note pipeline, but are not daily cards, date-filtered search results or quick-capture targets. The UI warns about this before Apply.

#### 6.2.3 Settings interaction and visual plan

The vault section receives a labelled TextBox with a draft, not direct persistence. It appears after the folder chooser and before day-boundary control. The fixed extension and folder are shown in copy so `yyyy.MM.dd` is the natural input.

```text
Настройки › База заметок
┌──────────────────────────────────────────────────────────────┐
│ Папка базы заметок       [ C:\Obsidian\kibnet            ] [Обзор] │
│                                                              │
│ Формат имени ежедневной заметки                              │
│ [ yyyy.MM.dd                                            ]    │
│ Пример: Ежедневные/2026.08.25.md                             │
│ Формат — только основа имени; «Ежедневные/» и .md добавятся. │
│ [Применить]                                                  │
│ Применяется… / Формат изменён в другой копии базы. [Загрузить]│
│                                                              │
│ При смене формата файлы не переименовываются. Для истории    │
│ выберите формат, который соответствует её именам.            │
└──────────────────────────────────────────────────────────────┘

Invalid draft:
│ [ yyyy/MM/dd                                            ]    │
│ ! Используйте yyyy, MM и dd; разрешены только -, . или _.   │
│ [Применить] disabled                                         │
```

- Stable automation IDs: `NoteDailyFileNameFormatTextBox`, `NoteDailyFileNameFormatPreviewText`, `NoteDailyFileNameFormatValidationText`, `ApplyNoteDailyFileNameFormatButton`, `NoteDailyFileNameFormatStatusText`, `ReloadExternalNoteDailyFileNameFormatButton`.
- The status text uses `AutomationProperties.LiveSetting="Polite"` for applying, success, retryable error and external-change state. Tests must use IDs rather than localized status prose.
- `SettingsViewModel` retains `NoteDailyFileNameFormatDraft`, `AppliedNoteDailyFileNameFormat`, `HasUnappliedNoteDailyFileNameFormatDraft`, preview and inline validation separately. Intermediate typing such as `yyyy.` is allowed in the TextBox but cannot be applied.
- Apply is enabled only when: external vault is supported, a root is selected and initialized, Feed has no active mutating operation/recovery conflict, no Apply is already in progress, and draft validates.
- `IsApplyingNoteDailyFileNameFormat` begins synchronously with the command. While it is true, the field, Apply, vault folder picker, Feed enable switch and boundary control are disabled; a short visible applying status is shown. Repeated click is impossible. A root/enable/session replacement cancels the stale Apply; it may not publish an applied value/status for another vault.
- On success the UI shows the applied preview/status, then refreshes day cards/search/review under a new immutable session. It does not create a daily file merely by applying a format. A sidecar write or rebind failure restores interactive controls, keeps the last applied format/session and exposes a retryable inline error.
- On unsupported platforms, the field and preview stay visible, the TextBox is read-only, Apply is disabled, and the existing desktop-only explanation is shown. No fake persisted success is possible.
- A valid external sidecar change always updates Feed's applied naming and rebinds the current session. If the Settings draft is clean, it is refreshed to that applied value. If it is dirty, the draft text is retained untouched, `HasExternalNoteDailyFileNameFormatChange` and a conflict hint appear, and a `Reload external value` action explicitly replaces the draft; Apply remains an intentional overwrite attempt against the latest sidecar revision.

#### 6.2.4 Controlled lifecycle and all consumers

1. `FeedViewModel.InitializeVaultAsync(root)` creates the vault and identity, loads `DailyNoteSettingsStore`, creates one `DailyNoteNaming`, and only then creates `DailyNoteService`, search, review, bootstrap and move consumers.
2. A successful Apply uses a new serialized vault-reconfigure flow. Both public root/enable initialization and Apply call one `RunVaultReconfigureAsync` owner; it acquires `operationGate` **before** loading/saving the sidecar, `ReplaceSession()` or `DisposeVaultSessionAsync()`, then invokes private `ReconfigureVaultCoreAsync` without trying to reacquire the gate. Apply never calls public `InitializeVaultAsync` while holding the gate. A reconfigure request queues behind an active move/recovery instead of cancelling it mid-transaction.
3. `SidecarArtifactKind.DailyNoteSettings` and `IFeedVaultWatchRuntimeSink.ReloadDailyNoteSettingsAsync` form the new watcher route; rescan routes it too after identity verification. A valid external change replaces the session and updates applied settings UI. If the UI draft is dirty it is preserved with an explicit external-change/reload action; invalid/corrupt external data leaves the last valid session and applied value intact with diagnostic state.
4. `DailyNoteService` resolves the active relative path for open/append. If it does not exist, append creates exactly that active path. It never falls back to a similarly dated different-format file, preventing ambiguous silent merges.
5. `FeedSearchIndex` classifies active-format paths as `Daily` and gives them a `DateOnly`; all other Markdown remains a normal note.
6. `ReviewQueueBuildRequest` captures the immutable `DailyNoteNaming` from the active Feed session alongside its documents/state. `BuildReviewQueueSnapshotAsync` constructs `FeedReviewQueue(parser, state, request.Naming)`, so the asynchronous queue cannot fall back to default naming after an Apply/rebind.
7. `FirstConnectBootstrapService` validates each path through the naming contract. Bootstrap JSON stays schema v1: a layout-specific operation ID contains a stable hash of the active filename format, while the unchanged legacy operation ID is retained for default `yyyy-MM-dd`. A manifest is applicable only when every entry parses and formats back to the active layout; a valid manifest for another layout is skipped, not labelled corrupt. New code never writes an empty bootstrap manifest. This permits a first dot-layout bootstrap to baseline ordinary historic text and leave unfinished checkboxes pending, without a breaking manifest schema.
8. A new `Move to today` resolves destination with current naming and passes its final relative path to `FeedLinkSerializer.MovedBlock`. Its link target is therefore `Ежедневные/2026.08.25#^…` for dotted layout. A pending/completed journal resumes with its already stored `DestinationPath`, even if the user later changes the setting.

#### 6.2.5 Error handling and performance

- Validation and preview are in-memory and constant-time.
- Apply never changes `Applied…` until the sidecar write succeeds and the same-root reconfigure completes; write conflict/error keeps the current session and gives a retryable message.
- Reconfiguration acquires the operation gate before cancelling the old Feed session. It does not overwrite dirty Markdown or interrupt a move/recovery; existing conflict/recovery UI remains the authority.
- No full-vault rename or content rewrite is performed. Normal reindexing cost is the existing root-reconnect/index cost; show existing busy/index progress rather than reporting premature success.
- Duplicate files for the same calendar date in different conventions are deliberately not merged. Only the active convention is a daily file; changing formats requires an explicit user choice and warning.

### 6.3 User-Observable Scenarios

| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Existing dotted vault | Select vault with `Ежедневные/2026.08.25.md`, enter `yyyy.MM.dd`, Apply | Preview shows dotted path; existing file appears as 25 Aug day card; quick capture appends there and creates no hyphen duplicate | Unit + Feed UI integration + inspected screenshot | AC-02, AC-05, AC-09 |
| Legacy default | Open vault without sidecar | Applied value and preview use `yyyy-MM-dd`; existing hyphen files behave exactly as before | Unit + characterization tests | AC-01, AC-03 |
| Invalid draft | Enter `yyyy/MM/dd` or incomplete `yyyy.` | Inline error; Apply disabled; applied format/session/files remain unchanged | ViewModel + Headless UI test | AC-04, AC-09 |
| Restart / second device | Reopen same root after successful Apply | Dotted format is loaded from its `.unlimotion` sidecar, not inherited from another vault/global app config | Store persistence + reconnect test | AC-06 |
| Search and review | Search/filter or start review in dotted vault | Dotted file is Daily with correct date/order, not ordinary thematic note | Index/queue tests | AC-07 |
| Move and recovery | Move a block to today using dotted layout, then resume after setting changes | New link points to dotted destination; recovery follows journalled original destination, never recomputes a new-format duplicate | Move/recovery tests | AC-08 |
| Intentional format switch | Apply a different valid convention | Warning states no files renamed; old-convention files remain unchanged and exit daily timeline until user returns/renames | UI + storage assertion | AC-05, AC-09 |

### 6.4 State / Interaction Matrix

| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| No selected/initialized vault | User edits field | Draft can be inspected but Apply disabled with root-required hint | No sidecar write | Prevents a global phantom setting |
| Valid active naming | User types valid draft | Preview changes, active naming does not | No reindex until Apply | Draft/active separation |
| Invalid draft | User types unsupported separator/token | Inline validation error and disabled Apply | Existing timeline/capture unchanged | Intermediate typing permitted |
| Valid draft, idle Feed | Apply | Enter `IsApplying…`; sidecar write → controlled reinitialize → cards/index/review reflect new active naming | Write failure restores controls and preserves old session | No Markdown mutation |
| Apply in progress | Repeated click or edit | Command/input/root/boundary/feed controls remain disabled | Repeated Apply cannot start a second write | Visible applying status |
| Apply in progress | Root/enable switch or session replacement | Cancel/stale completion cannot publish against a different vault | Current root replacement remains authoritative | Test explicit race |
| Feed mutating/recovering | Attempt Apply | UI disables Apply; programmatic/racing request queues behind operation before replacing session | No half-old/half-new graph or cancelled journal | Operation gate is authority |
| Sidecar changed externally, clean draft | Watcher event | Reload layout/current session; applied value and draft refresh | Existing document conflict policy applies | Portable sync |
| Sidecar changed externally, dirty draft | Watcher event | Reload layout/current session; applied value changes but typed draft remains with `Reload external value` action | No silent loss of local typing | Explicit draft conflict |
| Sidecar invalid/conflicts | Watcher/Apply | Preserve last valid naming and show recovery/retry status | No destructive fallback | Error is observable |
| Legacy bootstrap v1, active default | Initialize | Treat as `yyyy-MM-dd` and use it | No migration | Backward compatibility |
| Bootstrap from different layout | Initialize after switch | Ignore as layout-inapplicable and bootstrap current active daily paths | Not a corruption/error | Avoids hiding dotted history |
| Pending move journal | Apply/switch then recover | Resume journalled destination path | No reformat/recompute | Atomic recovery continuity |

### 6.5 Decision Ledger

| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Scope of input | agent | Restricted numeric date-stem grammar with `yyyy`, `MM`, `dd`; supports `yyyy.MM.dd` | 0.96 | A user may later want textual/custom literal date names | Нет; constrained grammar is safer and meets request |
| Storage ownership | agent | Portable `.unlimotion/daily-note-settings.json`, not global app config | 0.91 | Adds sidecar/watcher work, but avoids wrong format when switching roots/devices | Нет; objectively better match for vault-owned filenames |
| Existing different-format files | agent | Keep untouched; single active layout only; warn instead of guessing/merging | 0.94 | User must intentionally rename or switch back for a mixed archive | Нет; avoids hidden merge/data-loss semantics |
| Default without sidecar | agent | `yyyy-MM-dd` | 1.00 | None; preserves current contract | Нет |
| Applying format | agent | Explicit Apply after validation, then controlled rebind | 0.99 | One extra click | Нет; avoids reinitialization on incomplete typing |
| Bootstrap compatibility | agent | Keep JSON schema v1; namespace a non-default bootstrap operation by active layout and select only exact layout-matching paths | 0.92 | Requires legacy-operation/default and custom-layout rollback tests | Нет; avoids forward-schema rollback breakage |
| Automatic migration | agent | Do not implement | 1.00 | Existing mixed names need manual user decision | Нет; user did not ask to rename data |

### 6.6 Runtime / Config / Data Contract Matrix

| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Daily filename layout | Hardcoded `yyyy-MM-dd` in multiple code paths | `DailyNoteNaming` injected into all daily consumers | Missing sidecar → legacy default | Contract unit tests |
| Persisted vault setting | No setting | `.unlimotion/daily-note-settings.json`, schema v1 | Absent file default; preserve unknown fields; no Markdown migration | Store/revision tests |
| Settings UI state | `SettingsViewModel` global config fields | Draft + applied/dirty/external-change state, command bridge to Feed/store | Not written to app global config; dirty draft survives external applied change | ViewModel/Headless tests |
| Bootstrap manifests | Schema v1 assumes hyphen paths | Schema v1 remains; active layout qualifies non-default operation ID and validates path round-trip | Default legacy operation retained; mismatched layout skipped, not corrupted | Default/custom/downgrade bootstrap tests |
| Search/review | Local hardcoded parser | Shared naming parser | Dotted paths get daily date | Index/queue tests |
| Move journal | Destination now recomputed from hardcode | New operations use layout; recovery trusts persisted path | Existing records keep original destination | Recovery test across layout change |
| Watcher | VaultIdentity/Areas/Review sidecars | Daily settings sidecar event | Invalid external write never changes active layout | Runtime watcher test |

## 7. Бизнес-правила / Алгоритмы

### Filename validation

```text
Validate(draft):
  trim? No: whitespace is part of invalid input; user sees exact error.
  reject blank
  tokenize draft into yyyy / MM / dd and separators - . _
  reject if token sequence does not contain each component exactly once
  reject all other characters, forbidden filename chars, #, [, ], trailing dot/space
  for each sentinel date {2001-02-03, 2024-02-29, 2098-11-30}:
      stem = date.ToString(draft, InvariantCulture)
      require DateOnly.TryParseExact(stem, draft, InvariantCulture, None) == date
      require stem is a safe non-empty single file stem
  return DailyNoteNaming(draft)
```

### Apply flow

```text
Apply(draft):
  require an initialized external vault and Feed not mutating/recovering
  naming = Validate(draft); if invalid => keep applied naming + show error
  enter serialized ReconfigureVaultAsync for (current root, current session version)
  acquire operationGate before any sidecar/session mutation
  revalidate root/session identity; stale request exits without publishing
  load current sidecar revision; Save(settings(naming), expectedRevision)
  if conflict/error => keep applied naming + retry message; release gate
  replace/dispose old session and rebuild it under the saved naming
  only after successful same-root load publish AppliedFormat and success status
```

### Daily path and recovery invariants

- A given active `DateOnly` maps to exactly one relative path.
- A raw Markdown file can be a daily file only if `DailyNoteNaming.TryParseRelativePath` succeeds.
- A new quick capture never creates a same-date alternate-format duplicate.
- Journalled `DestinationPath` wins over present-day naming during pending/completed move recovery.
- Sidecar Apply never reads, writes, renames or deletes a user daily Markdown file by itself.

## 8. Точки интеграции и триггеры

- App startup/root selection → Feed loads sidecar and constructs naming before snapshot/bootstrap/index.
- Settings draft change → validation/preview only.
- Settings Apply → optimistic sidecar save → Feed controlled reinitialization.
- Daily settings sidecar watcher → load/validate → safe Feed rebind and Settings UI refresh.
- Quick capture/open/list → `DailyNoteService` naming.
- Build/refresh search index → injected naming.
- Build review queue → injected naming.
- First connect/bootstrap refresh → injected naming, layout-qualified operation ID and layout-aware manifest selection.
- Move-to-today/new journal → injected naming; recovery → stored destination only.

## 9. Изменения модели данных / состояния

- New portable model: `DailyNoteSettings(schemaVersion: 1, dailyFileNameFormat: string, extension data)`.
- New computed/immutable model: `DailyNoteNaming`.
- `SettingsViewModel`: draft, applied format, preview, validation/status, `CanApply…`; draft is UI state and is not source of truth.
- Bootstrap JSON remains schema v1. The default legacy operation ID is retained for `yyyy-MM-dd`; any non-default operation ID includes a stable format hash. A manifest applies only if every entry exact-round-trips through the active naming; batch remains path-specific.
- No changes to user Markdown model, TaskItem, area catalog, task storage or existing review locator model.

## 10. Миграция / Rollout / Rollback

### Rollout

1. Add shared naming and characterize legacy default paths.
2. Add portable settings store plus schema-v1 layout-qualified bootstrap compatibility and watcher routing.
3. Refactor vault reconfiguration to acquire its gate before replacing a session; then add the settings Apply bridge with atomic save, busy/error handling and journal-safe move recovery.
4. Add core, Headless/AppAutomation/FlaUI evidence and inspect screenshot.

### First run / existing vault

- No sidecar: default remains `yyyy-MM-dd`.
- User with `2026.08.25.md` enters `yyyy.MM.dd` and Applies. The app writes only the setting sidecar, reloads and then baselines current dotted daily paths normally; a legacy/default bootstrap is layout-inapplicable and must not suppress them.
- Existing Markdown stays byte-for-byte unchanged by Apply.

### Rollback

- Restore prior binary: `.unlimotion/daily-note-settings.json` is ignored by older versions; existing files remain usable through their actual names. The prior binary does not understand dotted cards, which is expected, but must not modify their Markdown or the custom-layout bootstrap operation.
- To return in new binary, apply `yyyy-MM-dd`. No files are renamed by either route.
- Bootstrap JSON schema is unchanged. Custom-layout bootstrap uses a layout-qualified operation ID, disjoint from the prior binary's legacy default operation ID; its global safe scan ignores a noncanonical dotted manifest rather than attempting recovery at that operation path. A downgrade characterization test proves no custom manifest/Markdown write.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria

- **AC-01 — Legacy default.** A vault with no daily-settings sidecar uses `yyyy-MM-dd`; list/open/append preserve existing hyphen behavior.
- **AC-02 — Dotted existing vault.** After applying `yyyy.MM.dd`, `Ежедневные/2026.08.25.md` is a day card and capture appends it without creating a hyphen sibling.
- **AC-03 — Persistence.** A successful applied value survives restart/reconnect and is tied to its vault, not inherited by another root.
- **AC-04 — Validation safety.** Empty, incomplete, unsafe or unsupported formats are visibly rejected and cannot change sidecar/active naming.
- **AC-05 — No hidden migration.** Apply does not rename/delete/rewrite Markdown; incompatible-convention files are visibly warned about and not silently merged.
- **AC-06 — Portable sidecar.** Settings store preserves unknown fields and rejects revision conflict/corrupt values without changing active session; a valid external change rebinds applied naming without silently erasing a dirty UI draft.
- **AC-07 — All read/query flows.** Dotted files are daily in timeline, chronological review and date-filtered search.
- **AC-08 — Move/recovery.** New move link targets the active dotted path; recovery after a layout change uses its persisted destination and remains idempotent.
- **AC-09 — UI behavior.** Settings field has stable automation ids, works on supported desktop with draft/preview/error/applying/retry states, is visible/read-only with disabled Apply on unsupported platforms, and cannot double-apply or publish to a switched root.
- **AC-10 — Bootstrap compatibility.** Schema-v1 default legacy manifest remains usable under default; a valid manifest for another naming is skipped as inapplicable, custom naming uses a disjoint layout-qualified operation ID, and current dotted files bootstrap safely.

### Planned tests and checks

- New `DailyNoteNamingTests`: accepted/rejected grammar, sentinel round trips, safe paths, exact dot result, no nested/alternate parse.
- `DailyMarkdownServiceTests.Dotted_daily_files_are_listed_and_capture_reuses_their_path`: default characterization and dotted list/order/open/append/no-hyphen-duplicate.
- New `DailyNoteSettingsStoreTests`: missing default, valid save/reload, unknown fields, revision conflict, corrupt payload.
- `FeedSearchIndexTests` and `FeedReviewQueueTests`: dotted classification/date order.
- `FeedMoveToTodayTests`: dotted destination/link plus resume after switched current naming.
- `FeedFirstConnectBaselineTests` and bootstrap sync tests: v1 default + custom dotted/mismatch selection, no-empty-manifest behavior and downgrade/no-write characterization.
- `FeedVaultWatchRuntimeTests`: daily-settings sidecar valid/invalid change routing, including rescan and the dedicated sink signal.
- `SettingsViewModelTests`: draft/preview/validation/bridge result, clean-vs-dirty external applied-change handling and reload action.
- `SettingsControlResponsiveUiTests.Daily_note_filename_format_has_supported_unsupported_and_applying_states`: visible supported control, IDs, validation/Apply/in-flight state, explicit unsupported read-only/disabled contract.
- `FeedShellUiTests` or closest existing wiring test: settings Apply → rebind → dotted day + quick capture target; programmatic Apply racing a move/recovery queues safely rather than cancelling it.
- The move/recovery race test additionally proves the sidecar still contains the old format while the competing operation owns `operationGate`, then changes only after that operation reaches its durable completion/recovery checkpoint.
- Headless/AppAutomation: `MainWindowHeadlessTests.Daily_note_filename_format_settings` changes the input through automation IDs, waits until Apply/rebind ends, then asserts preview/error/disabled states and dotted quick-capture target. It covers repeated Apply and root-switch cancellation in the ViewModel/Headless layer.
- Add all six field/status/action selectors to `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs`; direct `SettingsControlResponsiveUiTests` alone is not accepted as end-to-end proof. The Headless/FlaUI scenario invokes `ReloadExternal…` and asserts applying/error/external state through its stable ID.
- Visual evidence sequence: first attempt a focused recorder capture of the deterministic FlaUI flow using the repository-supported recorder/harness. On success retain `chat-artifacts/daily-note-filename-format/before-settings.mp4` and `chat-artifacts/daily-note-filename-format/after-settings-dot-format.mp4`. If capture cannot be produced, record the concrete technical blocker (for example unavailable interactive desktop, recorder failure or SDK/test-host blocker), the attempted command and its output, then use the screenshot/automation fallback below; "not proportionate" alone is not a valid reason.
- FlaUI screenshot fallback: extend `FeedScenariosBase` with a dedicated settings scenario that starts TestHost with the existing pre-launch `feedVaultPrepared` hook, opens Settings, applies `yyyy.MM.dd`, waits for the rebind and captures the Note Vault settings surface. `MainWindowFlaUiTests.DailyNoteFilenameFormatSettings` invokes it. It uses opt-in `UNLIMOTION_DAILY_NOTE_FORMAT_SCREENSHOT_PATH` and writes `chat-artifacts/daily-note-filename-format/after-settings-dot-format.png`. Run it with `$env:UNLIMOTION_DAILY_NOTE_FORMAT_SCREENSHOT_PATH = "$PWD\\chat-artifacts\\daily-note-filename-format\\after-settings-dot-format.png"; dotnet test --project tests/Unlimotion.UiTests.FlaUI`.

### Acceptance-to-Test Matrix

| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| AC-01 | Naming + DailyMarkdown legacy tests | Inspect no diff in default files | TUnit output | — |
| AC-02 | Daily service + Feed wiring UI test | Inspect dotted day/capture result | temp-vault assertion + screenshot | — |
| AC-03 | Store/reconnect two-root test | Inspect sidecar path/value | test logs | — |
| AC-04 | ViewModel + Headless invalid input | Inspect inline copy/disabled button | UI assertion | — |
| AC-05 | Storage behavior test | Inspect original file hash/name | file hash/test log | — |
| AC-06 | Store + watcher + Settings dirty-draft tests | Inspect retry/external-change/reload status | test log | — |
| AC-07 | Search, direct queue and real async `ReviewQueueBuildRequest` test | Inspect chronological result/date filter | TUnit output | — |
| AC-08 | Move/recovery test | Inspect wiki target and journal path | golden Markdown/journal log | — |
| AC-09 | Responsive + dedicated Headless/AppAutomation + `MainWindowFlaUiTests.DailyNoteFilenameFormatSettings` | Compare supported/unsupported/in-flight wireframe states; inspect focused before/after video or objective fallback screenshot | `chat-artifacts/daily-note-filename-format/before-settings.mp4`, `chat-artifacts/daily-note-filename-format/after-settings-dot-format.mp4`; fallback PNG | If recorder fails, record exact blocker/command/output and retain screenshot plus UI automation trace |
| AC-10 | Bootstrap default/custom/downgrade test | Inspect no "corrupt" treatment, no custom-manifest/Markdown write on downgrade | TUnit output | — |

### Validation commands (EXEC only)

```powershell
dotnet test --project src/Unlimotion.Test -- --treenode-filter "/*/*/DailyNoteNamingTests/*"
dotnet test --project src/Unlimotion.Test -- --treenode-filter "/*/*/DailyNoteSettingsStoreTests/*"
dotnet test --project tests/Unlimotion.UiTests.Headless -- --treenode-filter "/*/*/MainWindowHeadlessTests/Daily_note_filename_format_settings"
dotnet test --project tests/Unlimotion.UiTests.FlaUI -- --treenode-filter "/*/*/MainWindowFlaUiTests/Daily_note_filename_format_settings"
dotnet test --project src/Unlimotion.Test
dotnet test --project tests/Unlimotion.UiTests.Headless
dotnet test --project tests/Unlimotion.UiTests.FlaUI
git diff --check
```

The exact focused filters follow the repository's current `/*/*/<fixture>/<test>` TUnit convention; after the test names are created, discovery output must confirm them before the first run. During EXEC, run the affected nodes first, then the listed related projects. If local SDK `10.0.400` remains unavailable, report it as an environment blocker, run all independently available checks, and use final green repository CI only when it finishes successfully; do not alter `global.json` to bypass it.

## 12. Риски и edge cases

| Risk / edge case | Mitigation |
| --- | --- |
| Typing an incomplete value would repeatedly reinitialize vault | Draft/Apply separation; validation does not change active naming |
| Unsafe format becomes path traversal or odd filenames | Narrow grammar, fixed folder/extension, invariant parse/round-trip and filename checks |
| Format only patched in capture but missed in query/recovery | One injected naming owner; explicit consumer inventory and tests |
| Format is accidentally global and wrong after switching vault | Portable sidecar tied to root/vault; two-root test |
| Existing files appear lost after convention switch | No file mutation, explicit warning; single active convention documented |
| Bootstrap v1 mismatches dotted layout | Treat v1 as default and mismatched manifests as inapplicable, not corrupt |
| Pending move starts in old layout then setting changes | Persisted journal destination overrides current naming |
| External device writes bad sidecar | Keep last valid active session, visible diagnostic/retry |
| Sidecar conflicts with sync | Optimistic revision / reload-retry, never last-writer-wins silently |
| Double click/root switch races Apply | `IsApplying…`, disabled controls, session/root identity check before publishing, ViewModel/Headless race test |
| External sidecar arrives while user is typing | Rebind applied naming but retain dirty draft, show explicit external-change/reload action and test it |
| New layout breaks rollback bootstrap reader | Keep schema v1, use a disjoint layout-qualified operation ID, and run downgrade/no-write characterization |
| UI screenshot cannot be captured locally | Report SDK/tooling blocker and retain Headless/AppAutomation evidence plus exact next command |

### Expected User Review Objections

| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| «Я ввёл `yyyy.MM.dd`, но Лента всё ещё пишет через дефис» | Several components previously had independent hardcodes | One naming contract is injected into capture, search, review, bootstrap and move; full integration AC | mitigated |
| «Я не хочу, чтобы приложение переименовало мои заметки» | Format change touches filenames conceptually | Apply writes only sidecar and shows warning; hash/name assertion | mitigated |
| «На другом устройстве снова нужен код/ручная правка» | Global app setting would cause this | Portable vault sidecar is authoritative | mitigated |
| «Поле ломает Ленту, пока я набираю формат» | Text input is transiently invalid | Explicit draft/Apply and disabled invalid action | mitigated |
| «Другой компьютер перезаписал то, что я ещё печатал» | Sidecar sync can race a local settings draft | Applied value reloads but dirty draft is retained with an explicit reload/overwrite choice | mitigated |
| «Что будет с прежней историей в другом формате?» | Single active layout avoids ambiguous duplicate dates | Files stay untouched and warning describes switch/manual choice | accepted-risk |

### Rework Prevention Checklist

- [x] Spec names what the user sees and operates: field, preview, Apply, error, warning.
- [x] Every user-visible scenario maps to test/check/evidence.
- [x] Agent-owned decisions and their risks are in the Decision Ledger.
- [x] Likely objections are predicted and handled or called out as accepted risk.
- [x] Role-based review is required below before approval.
- [x] Acceptance criteria are verifiable outcomes, not preparation steps.
- [x] EXEC has an explicit path to prove core/visual scenarios.

## 13. План выполнения

1. Add/characterize `DailyNoteNaming` and migrate all path/parser consumers to it without changing legacy default behavior.
2. Add portable settings store plus schema-v1 layout-qualified bootstrap compatibility and watcher routing.
3. Add Feed lifecycle/Apply bridge with atomic save, busy/error handling and journal-safe move recovery.
4. Add localized Settings UI with draft/preview/error/Apply and automation ids.
5. Add core + UI tests, then run targeted and broader relevant suites.
6. Capture/inspect screenshot or document objective blocker/fallback; run post-EXEC full review.

## 14. Открытые вопросы

Нет блокирующих вопросов. Ограниченный numeric format grammar и portable per-vault persistence выбраны как безопасные defaults; пользователь может подтвердить или скорректировать их вместе со SPEC.

## 15. Соответствие профилю

- Профиль: `dotnet-desktop-client`, `ui-automation-testing`, repository TUnit practice.
- Выполненные требования профиля:
  - Avalonia desktop UI is described with visual artifact, responsive/unsupported states and automation IDs.
  - State mutation/rebind boundaries, persistence, cancellation and errors are defined.
  - UI behavior has Headless/AppAutomation/FlaUI validation plan and screenshot fallback.
  - .NET/TUnit commands use `--treenode-filter`; SDK blocker policy is explicit.

## 16. Таблица изменений файлов

| Файл | Изменения | Причина |
| --- | --- | --- |
| `src/Unlimotion.Notes/Daily/DailyNoteNaming.cs` | New immutable naming contract | Remove scattered hardcodes |
| `src/Unlimotion.Notes/Daily/DailyNoteSettingsStore.cs` | New portable config store | Per-vault persisted setting |
| `src/Unlimotion.Notes/Daily/DailyNoteService.cs` | Inject/use naming | List/open/append correct file |
| `src/Unlimotion.Notes/Search/FeedSearchIndex.cs` | Inject/use naming | Correct daily classification/date |
| `src/Unlimotion.Notes/Review/FeedReviewQueue.cs` | Inject/use naming | Correct chronology |
| `src/Unlimotion.Notes/Identity/FirstConnectBootstrapService.cs` | Layout-aware schema-v1 bootstrap selection | Safe initial history/reconnect and downgrade |
| `src/Unlimotion.Notes/Operations/FeedMoveToTodayService.cs` | Inject naming, journal-safe destination | Correct link/recovery |
| `src/Unlimotion.Notes/Markdown/MarkdownMutationService.cs` | Serialize move link from resolved path | No hidden hardcode |
| `src/Unlimotion.Notes/Watching/*` | New daily-settings sidecar change kind | External sync/rebind |
| `src/Unlimotion.ViewModel/Feed/FeedViewModel.cs` | Load/store/rebind/consumer injection | One session-consistent layout |
| `src/Unlimotion.ViewModel/SettingsViewModel.cs` | Draft/validation/status/command bridge | Usable setting UI |
| `src/Unlimotion/App.axaml.cs` | Wire Apply/load between settings and Feed | Controlled runtime integration |
| `src/Unlimotion/Views/SettingsControl.axaml` | TextBox/preview/error/Apply | User-visible interaction |
| `src/Unlimotion.ViewModel/Resources/Strings*.resx` | Localized copy | RU/EN UI consistency |
| `tests/Unlimotion.UiTests.Authoring/Pages/MainWindowPage.cs` | Six daily-format input/status/action automation selectors | End-to-end UI addressing |
| `tests/Unlimotion.UiTests.Authoring/Tests/FeedScenariosBase.cs`, `tests/Unlimotion.UiTests.FlaUI/Tests/MainWindowFlaUiTests.cs` | Dedicated settings flow and opt-in screenshot | Executable visual evidence |
| Relevant `src/Unlimotion.Test/*`, `tests/Unlimotion.UiTests.*/*` | Core/UI coverage and selectors | Verify acceptance criteria |
| `specs/2026-08-25-daily-note-filename-format.md` | This SPEC | Approved contract |

## 17. Таблица соответствий (было → стало)

| Область | Было | Стало |
| --- | --- | --- |
| Daily filename | Hardcoded `Ежедневные/yyyy-MM-dd.md` | Per-vault active numeric naming, default unchanged |
| User configuration | Requires source-code change | Settings draft + preview + explicit Apply |
| Persistence | No filename format state | Portable `.unlimotion/daily-note-settings.json` |
| Parsing | Each subsystem parses hyphen independently | One `DailyNoteNaming` contract |
| Bootstrap | Assumes only hyphen 13-char paths | Schema-v1 layout-aware path validation + layout-qualified operation ID |
| Switching | Implicit impossible/manual code change | Explicit warning, rebind, no file mutation |

## 18. Альтернативы и компромиссы

### Chosen: portable one-active-layout setting with constrained free-text format

- Плюсы: works for `yyyy.MM.dd`, travels with vault, eliminates global-root leakage, safely validates parsing and keeps one unambiguous target per day.
- Минусы: does not simultaneously surface mixed historical conventions; requires sidecar/watcher/bootstrap work.
- Почему выбран: it protects user data and the standalone vault contract while keeping the requested setting understandable.

### Alternative: global `NoteVault:DailyFileNameFormat`

- Плюсы: fewer code changes and easy immediate binding.
- Минусы: switching RootPath silently applies the prior vault’s convention; another device loses the setting; violates vault-owned semantics.
- Почему не выбран: filename layout describes external vault contents, not a device preference.

### Alternative: arbitrary .NET custom date-format strings

- Плюсы: maximum apparent flexibility.
- Минусы: literals/localization/time tokens/unsafe characters/non-injective strings create incompatible or ambiguous filenames and hard-to-test parser behavior.
- Почему не выбран: numeric grammar provides the needed `yyyy.MM.dd` safely.

### Alternative: recognize all historical conventions automatically

- Плюсы: mixed archive can remain in timeline.
- Минусы: two files can map to one date; capture/move/review semantics become ambiguous and require migration/conflict UI.
- Почему не выбран: out of scope and unsafe without an explicit product policy.

## 19. Результат quality gate и review

### SPEC Linter Result

| Блок | Пункты | Статус | Комментарий |
| --- | --- | --- | --- |
| A. Полнота спеки | 1-5 | PASS | Цель, AS-IS, проблема, границы и non-goals конкретны. |
| B. Качество дизайна | 6-10 | PASS | Owner contract, data flow, error/lifecycle/perf и alternatives определены. |
| C. Безопасность изменений | 11-13 | PASS | Sidecar revision, no rename, legacy/bootstrap/recovery/rollback contracts есть. |
| D. Проверяемость | 14-16 | PASS | AC, matrix, UI evidence, test classes and validation commands определены. |
| E. Готовность к автономной реализации | 17-19 | PASS | Two adversarial reviews, UX re-review and fix/re-review closed all findings; no user decision remains. |
| F. Соответствие профилю | 20 | PASS | Desktop/Avalonia + UI automation requirements accounted for. |

Итог: ГОТОВО.

### SPEC Rubric Result

| Критерий | Балл (0/2/5) | Обоснование |
| --- | ---:| --- |
| 1. Ясность цели и границ | 5 | User outcome and explicit non-goals are concrete. |
| 2. Понимание текущего состояния | 5 | All production hardcodes and runtime wiring are identified. |
| 3. Конкретность целевого дизайна | 5 | Naming/store/UI/bootstrap/recovery contracts are named. |
| 4. Безопасность (миграция, откат) | 5 | No mutation, schema-v1 compatibility, conflict and rollback plan are defined. |
| 5. Тестируемость | 5 | Every AC maps to automated/visual evidence. |
| 6. Готовность к автономной реализации | 5 | Full post-SPEC review, fix/re-review and explicit validation/evidence path are complete. |

Итоговый балл: 30 / 30
Зона: готово к автономному выполнению.

### Role-Based Review Result

| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does the setting solve an existing dotted-vault workflow without altering notes? | PASS | One active layout, no migration and exact dotted outcome are explicit. |
| UX / designer | applicable | Is draft/preview/Apply/warning understandable and safe? | PASS | UX review confirmed supported/unsupported/in-flight/external-change states, IDs and wireframe. |
| Tester / validation | applicable | Does each scenario have positive/negative evidence? | PASS | AC matrix includes unit, real async queue, Headless/AppAutomation, FlaUI and recorder fallback. |
| Developer / architect | applicable | Are naming, sidecar, bootstrap, journal boundaries coherent? | PASS | Reviewer closed gate ordering, naming propagation, schema-v1 rollback and recovery concerns. |
| Delivery / operations / security | applicable | Are config/sync/rollback and SDK evidence risks handled? | PASS | Per-vault sidecar, revision conflict, downgrade/no-write test and SDK/video blocker rules are defined. |

### Post-SPEC Review

- Статус: PASS.
- Scope reviewed: `specs/2026-08-25-daily-note-filename-format.md`; central QUEST/template/linter/rubric/review-loop instructions; profiles `dotnet-desktop-client` and `ui-automation-testing`; current daily naming, Feed initialization, settings, watcher and UI-test code; no open product questions; planned files in section 16.
- Decision: можно запрашивать подтверждение SPEC.
- Review passes:
  - Scope/Evidence pass: inspected hardcode inventory in `DailyNoteService`, search, review, bootstrap, move/link serialiser and Feed construction; settings/app wiring, SettingsControl, watcher contracts and existing tests. `git diff --check` is clean for this SPEC; only pre-existing untracked user artifacts are outside scope.
  - Contract pass: verified user-visible `yyyy.MM.dd` flow, default compatibility, per-vault ownership, no rename, one active layout, all daily consumers, portable sidecar, failure/retry and unsupported-platform behavior against the approved feed baseline.
  - Adversarial risk pass: challenged rebind during move/recovery, legacy/bootstrap rollback, asynchronous queue capture, external sidecar versus dirty draft, duplicate Apply/root switch, automation evidence and mixed layouts.
  - Role-Based pass: business, UX, testing, architecture and delivery rows above are all PASS.
  - Re-review after fixes / Fix and re-review: separate adversarial reviewer found and rechecked two HIGH, four MEDIUM and two LOW issues; UX reviewer independently rechecked all UI findings. The technical reviewer sandbox was unrestricted, so this is an adversarial read-only-practice fallback rather than a technically sandbox-enforced independent review. No reviewer changed files.
  - Stop decision: PASS; remaining step is user approval, not a missing design decision.
- Evidence inspected: current source locations named in sections 2/6/8; current test patterns; current `SettingsControl` unsupported behavior; `git diff --check`; two reviewer reports and their re-reviews.
- Depth checklist:
  - Scope drift / unrelated changes: only the new SPEC was authored; pre-existing untracked `.codex-remote-attachments/`, `chat-artifacts/` and `output/` were not touched.
  - Acceptance criteria: AC-01..10 each map to a test/check/evidence row.
  - User-observable scenarios / Decision ledger / Expected objections: all populated; no decision requires user input before EXEC.
  - Validation evidence: no implementation tests are claimed during SPEC; exact future test/evidence commands and SDK blocker policy are stated.
  - Unsupported claims: removed schema-v2/old-binary claim; custom layout uses schema-v1 and a disjoint operation ID.
  - Regression / edge case: gate-before-sidecar mutation, journalled destination, bootstrap layout filtering, watcher dirty draft, invalid sidecar and no automatic migration are covered.
  - Comments/docs/changelog: localized UI copy and automation IDs are planned; no changelog/release claim is made before EXEC.
  - Hidden contract change: old strict filename contract is superseded only by the explicitly documented single active setting; folder/extension and user Markdown semantics remain fixed.
  - Manual-review challenge: a reviewer would try applying during a move, downgrading after dotted bootstrap, changing sidecar remotely while typing, and exercising settings automation; each now has a test/evidence contract.
- No-findings justification: after the final fix/re-review, reviewers report no BLOCKER/HIGH/MEDIUM/LOW findings; static consistency scans no longer contain obsolete schema-v2 or ambiguous unsupported-platform wording.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | concurrency | Early session replacement could cancel a live move/recovery; Apply save ordering was ambiguous | One gate-owning `RunVaultReconfigureAsync`, non-reentrant core, and durable race assertion | fixed + re-reviewed |
| HIGH | rollback | Proposed schema-v2 bootstrap could trigger legacy recovery behavior | Keep schema v1; use layout-qualified operation ID and downgrade/no-write test | fixed + re-reviewed |
| MEDIUM | async review | `ReviewQueueBuildRequest` could lose naming | Capture immutable naming in request and pass it to real queue build | fixed + re-reviewed |
| MEDIUM | external settings | Watcher/draft behavior was underspecified | Dedicated sidecar sink, clean/dirty draft contract and reload action | fixed + re-reviewed |
| MEDIUM | visual evidence | Screenshot plan lacked executable scenario and objective video fallback | Dedicated selectors/scenario, recorder-first contract and screenshot fallback | fixed + re-reviewed |
| MEDIUM | accessibility/automation | Applying/external status had no stable selector | Six IDs plus polite live status and page-object coverage | fixed + re-reviewed |
| LOW | test filtering | Focused TUnit nodes were not named | Named fixtures/tests and repository-style filters | fixed + re-reviewed |
| LOW | spec consistency | Old schema-v2 wording remained | Replaced with schema-v1 layout-qualified wording | fixed + re-reviewed |

- Fixed before continuing: all findings in the table.
- Checks rerun: `git diff --check`; schema/wording scan; full relevant-spec re-review; UX re-review.
- Needs human: exact phrase `Спеку подтверждаю` to start EXEC.
- Residual risks / follow-ups: single-active-layout is an explicit accepted product boundary; actual recorder availability and local SDK are validation environment conditions, not design blockers.

### Post-EXEC Review

- Статус: PASS для реализованного scope; один нестабильный Windows teardown в полном core-прогоне зафиксирован отдельно ниже.
- Scope reviewed: portable daily filename naming/store, Feed rebind and watcher path, Settings/App bridge, SettingsControl, Headless/FlaUI automation and this SPEC.
- Decision: `yyyy.MM.dd` готов к использованию как vault-owned active layout; отдельный commit/PR не создавался.
- Review passes:
  - Реализация: naming/store подключены ко всем daily consumers; переключение сохраняется в `.unlimotion/daily-note-settings.json`, не переименовывает Markdown и корректно перестраивает текущую сессию.
  - Concurrency: исправлены stale busy mirror между Feed и Settings, а также completion race, когда availability успевает прийти до первого published state. Успешный Apply подтверждается только для того же root/context и живой session.
  - UI evidence: FlaUI ждёт видимое terminal state (поле, preview, disabled Apply и status), закрывает существующую панель карточки задачи и только затем снимает Settings.
  - Независимый re-review после исправлений: BLOCKER/HIGH/MEDIUM/LOW не найдено.
- Evidence inspected:
  - `dotnet build src/Unlimotion.Test/Unlimotion.Test.csproj -c Debug --no-restore -nr:false -m:1` — PASS, 0 errors.
  - Focused TUnit: `SettingsViewModelTests` — 98/98; `FeedDailyNoteFileNameFormatTests` — 17/17; `DailyNoteNamingTests` — 15/15; `DailyNoteSettingsStoreTests` — 7/7.
  - `dotnet build tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj -c Debug --no-restore -nr:false -m:1` — PASS; exact Headless daily settings flow — 1/1.
  - `dotnet build tests/Unlimotion.UiTests.FlaUI/Unlimotion.UiTests.FlaUI.csproj -c Debug --no-restore -nr:false -m:1` — PASS; exact real-window daily settings flow — 1/1, including invalid input, Apply/Reload, external sidecar change and dotted quick capture.
  - Visual evidence: [`after-settings-dot-format.png`](../chat-artifacts/daily-note-filename-format/after-settings-dot-format.png) visibly shows `yyyy.MM.dd`, `Ежедневные/2026.08.25.md` preview and applied status.
  - `git diff --check` — PASS (only existing LF-to-CRLF warnings).
- Full core validation note: serial run finished 1246/1247. The only error was `TrailingSeparatorReinitializeDoesNotCreateRegistryConflictBeforeApplyingDailyFormat` while `TempNotesDirectory.Dispose` deleted a temporary `2026-08-25.md` still locked by Windows. Its assertions had completed; immediate exact rerun passed 1/1, and the focused owning fixture had passed 17/17 before the full run. This is recorded as a teardown-lock flake rather than a passing full-suite claim.
- Depth checklist:
  - Product behavior: default `yyyy-MM-dd` remains compatible; dotted format produces only `Ежедневные/YYYY.MM.DD.md` for new capture, without migration or rename.
  - Settings lifecycle: invalid drafts cannot Apply; preview, explicit Reload, external sidecar conflict, state rebind and unavailable/applying controls are covered.
  - Async safety: root/context/session guards reject stale and foreign results without discarding the user's dirty draft.
  - Visual/automation: stable IDs cover input, preview, validation, Apply, Reload and status; real desktop screenshot was inspected after the final run.
  - Scope hygiene: no unrelated user artifacts were staged or changed; screenshot remains an untracked chat artifact.
- No-findings justification: the independent post-fix review found no actionable product or test-contract issue. The only non-green validation signal is the separately reproducible-as-clean teardown lock described above, not an unmet acceptance assertion.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| MEDIUM | Settings async lifecycle | Apply result could be lost if availability arrived before the first Feed setting state or if a same-root rebind replaced the session | Preserve root/context/external/reload guards; accept only a confirmed live-session result and add completion-before-passive-state regression | fixed + re-reviewed |
| MEDIUM | FlaUI evidence | Screenshot could be taken after sidecar persistence but before visible Apply completion, or from an occluded Settings template | Wait for terminal UI state; close the details pane, check viewport and capture the visible window | fixed + re-reviewed |

- Fixed before final report: обе MEDIUM выше; stale busy bridge, completion ordering and visual capture were rechecked by focused tests and independent review.
- Checks rerun: core and UI builds; focused naming/store/Settings/Feed tests; exact Headless and FlaUI scenario; exact retry of the one teardown-locked full-suite test; `git diff --check`.
- Validation evidence: commands and results above; final inspected screenshot.
- Unrelated changes: pre-existing untracked `.codex-remote-attachments/`, `output/` and other workspace work remain out of scope; screenshot is intentionally not a repository asset.
- Needs human: Нет.
- Residual risks / follow-ups: a future test-infrastructure hardening may add retry/diagnostics for Windows directory cleanup if the recorded teardown lock recurs; no product behavior is blocked.

## Approval

Ожидается фраза: "Спеку подтверждаю"

## 20. Журнал действий агента

| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SPEC | Исследование current hardcodes/settings/tests | 0.96 | Formal post-SPEC review | Create/review this SPEC | Нет | User supplied `2026.08.25.md` use case | Format is cross-cutting, not a one-line setting | Existing code inventory only |
| SPEC | Storage/naming design | 0.91 | Reviewer challenge | Run full post-SPEC review | Нет | No new user decision requested | Portable per-vault sidecar avoids global configuration leakage | This SPEC |
| SPEC | Full post-SPEC review and re-review | 0.97 | User approval only | Request `Спеку подтверждаю` | Да | No new choice; approval gate is mandatory | All adversarial and UX findings were fixed and re-reviewed; technical independent sandbox was unavailable, so read-only-practice fallback is documented | This SPEC |
| EXEC | Implement portable daily filename format and harden rebind lifecycle | 0.97 | Full core suite had one non-reproducing Windows teardown lock; no product decision is missing | Deliver validated result with the recorded test-flake note | Нет | User approved SPEC; then closed the running app for UI validation | The requested `yyyy.MM.dd` workflow is implemented with durable sidecar state; review-led fixes prevent stale busy/completion rebinds and the final desktop screenshot shows the applied setting | Daily naming/store, Feed/Settings/UI, tests, screenshot artifact, this SPEC |
