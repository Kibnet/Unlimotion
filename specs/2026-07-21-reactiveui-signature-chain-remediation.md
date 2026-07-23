# Восстановление доверенной цепочки подписей ReactiveUI/Splat

## 0. Метаданные
- Тип: `delivery-task`; context: `testing-dotnet`; stack profile: `dotnet-desktop-client`; доменная область: dependency security / CI reliability / delivery prerequisite.
- Владелец: Kibnet; реализация и evidence — Codex после отдельного approval.
- Масштаб: medium.
- Целевое семейство / behavior baseline: `origin/main@e11cae9a086ddd4fd97105f00b67bedf05f92700`, .NET SDK из `global.json`, Avalonia `12.0.3`.
- Поверхность: GitHub Actions, NuGet restore, build/test dependency graph; UI-контракт не меняется.
- Effective runtime: GitHub-hosted Ubuntu/Windows runners и локальный Windows .NET SDK; model/runtime Не применимо.
- Eval baseline / evidence: GitHub Actions run `29792038710`, job `88516063131`; fresh isolated restore, resolved assets, signature verification, full Unit и Headless regression.
- Целевой релиз / ветка: prerequisite branch `fix/reactiveui-resigned-packages`, затем rebase `docs/distribution-support-contract` / PR #280.
- Ограничения: реализация запрещена до отдельной фразы `Спеку подтверждаю`; exact allowlist; никаких обходов revocation/signature verification.
- Текущий статус: `POST-SPEC PASS (user-authorized adversarial fallback)`. Technical amendments закрыли recorded-commit, bounded child I/O, command-file, worker, Android/Debian trust and exact-context findings. Независимый read-only sandbox недоступен в текущей среде; пользователь 2026-07-23 явно разрешил принять adversarial fallback. Implementation всё ещё запрещена до отдельной фразы `Спеку подтверждаю` именно для этой dependency/security spec, затем потребуется отдельный ASK-HUMAN по полному protected-merge contract на `main`.
- Связанные ссылки:
  - Stage 3 PR: https://github.com/Kibnet/Unlimotion/pull/280
  - failing Linux job: https://github.com/Kibnet/Unlimotion/actions/runs/29792038710/job/88516063131
  - ReactiveUI 23.2.28: https://github.com/reactiveui/ReactiveUI/releases/tag/23.2.28
  - Splat 19.4.1: https://github.com/reactiveui/splat/releases/tag/v19.4.1
  - NU3012: https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu3012

## 1. Overview / Цель
Заменить package chain, подписанную отозванным author certificate, минимальным согласованным набором переподписанных ReactiveUI/Splat packages и вернуть fail-closed проверку NuGet signatures в Linux workflows.

Outcome contract:
- Success means: fresh Ubuntu restore с включённой проверкой подписи разрешает только целевое affected signed subset без постороннего graph drift, fingerprint-bound `dotnet nuget verify` проходит, build/Unit/Headless regression и точный Android check-run, observed on candidate SHA, зелёные, prerequisite PR reviewed и merged.
- Итоговый артефакт / output: merged prerequisite commit с двумя direct pin updates, CI guard и auditable validation evidence. Rebase PR #280 и новый exact-SHA Stage 3 gate — обязательный downstream handoff, но не часть completion contract уже merged prerequisite PR.
- Stop rules:
  - любое изменение runtime/UI/API кода, Avalonia, public behavior, target framework, SDK или package sources;
  - необходимость прямых `Splat*` pins либо иной версии, чем указано ниже;
  - невозможность получить exact affected signed subset при полном отсутствии unrelated resolved-graph drift;
  - restore требует отключить signature/revocation verification;
  - build/test выявляет несовместимость, не устранимую exact allowlist.

## 2. Текущее состояние (AS-IS)
- `src/Directory.Packages.props` фиксирует `ReactiveUI.Avalonia 12.0.1` и `ReactiveUI 23.2.27`.
- Их dependency resolution затрагивает `ReactiveUI 23.2.1/23.2.27` и `Splat`, `Splat.Builder`, `Splat.Core`, `Splat.Logging 19.3.1`.
- Эти пакеты подписаны certificate SHA-256 `09702DACA40821B9E2F12DF12FB32479AD60F6C5C73A69E3EB35E06C9C3F898B`; Linux restore в job `88516063131` завершился `NU3012` с `Revoked: certificate revoked`.
- Baseline workflows `.github/workflows/android-packaging.yml` и `.github/workflows/deb_packaging.yml` явно задают `DOTNET_NUGET_SIGNATURE_VERIFICATION: "false"`.
- Незавершённый Stage 3 workflow `.github/workflows/distribution-validation.yml` также задаёт `"false"` в `android_build`; это должно быть исправлено после merge prerequisite и rebase PR #280.
- Windows-only `tests.yml` с кэшом NuGet не доказывает Linux chain validation и не является достаточным GREEN для этого дефекта.
- Live audit 2026-07-21: у `main` `required_status_checks=null`, `enforce_admins=false`, а ruleset `Main` disabled; новый job без отдельной branch-rule операции был бы только diagnostic, а не permanent merge gate.
- `ReactiveUI.Avalonia 12.0.2` требует `Avalonia >=12.0.1`, `ReactiveUI 23.2.28` и `Splat 19.4.1`; текущая Avalonia `12.0.3` совместима. Версия `12.0.3` не выбирается, потому что потребовала бы Avalonia `>=12.0.4` и расширила scope.
- Upstream называет ReactiveUI `23.2.28` переподписанной заменой с тем же кодом, что `23.2.27`; Splat `19.4.1` содержит также реальные изменения, поэтому package update требует runtime/UI regression, а не только restore check.

## 3. Проблема
Свежая безопасная Linux-среда не может восстановить baseline dependency graph из-за отозванного certificate, а часть production packaging workflows маскирует тот же класс риска явным отключением signature verification.

## 4. Цели дизайна
- Минимально обновить только два прямых package pins.
- Оставить Splat транзитивной зависимостью и проверить exact affected signed subset вместе с сохранением полного остального graph.
- Удалить известные Linux verification bypasses без изменения package sources.
- Добавить независимый fresh-cache Ubuntu gate, который воспроизводит реальную security boundary.
- Проверить отсутствие runtime/UI regression существующими Unit и Headless suites.
- Зафиксировать downstream handoff: Stage 3 зависит от merged prerequisite и полностью сбрасывает старое evidence, но это не блокирует завершение child-spec после её merge.

## 5. Non-Goals (чего НЕ делаем)
- Не обновляем Avalonia `12.0.3`, .NET SDK, target frameworks или другие packages.
- Не добавляем прямые `Splat*` pins и package lock files.
- Не меняем runtime, UI, storage, API, README, release version, `global.json`, `nuget.config` или package sources.
- Не используем `NUGET_CERT_REVOCATION_MODE=offline`, `DOTNET_NUGET_SIGNATURE_VERIFICATION=false/0`, `--no-restore` как замену authoritative fresh verified restore, vendoring старых nupkg или trust для отозванного certificate. `--no-restore` допустим только после успешного fresh restore того же exact SHA с тем же isolated `NUGET_PACKAGES` внутри одного attempt.
- Не включаем signature verification принудительно на macOS: authoritative dynamic check этой спеки выполняется на Ubuntu.
- Не настраиваем signing/notarization приложений; речь только о NuGet package signatures.

## 6. Предлагаемое решение (TO-BE)

### 6.1 Распределение ответственности
- `src/Directory.Packages.props` — два direct pins: `ReactiveUI.Avalonia 12.0.2`, `ReactiveUI 23.2.28`.
- `scripts/Test-NuGetSignatureChain.ps1` — producer/orchestrator: guarded `GenerateBaseline`, raw-input/receipt-capable `RunAttempt`, closed external `Worker` kinds for verification/sanitizers/finalizer and `SelfTest`. There are no public `Verify` or `WriteAttemptReceipt` CLI modes: acceptance verification is reachable only as `WorkerKind=SignatureVerify` through framed stdin, while receipt creation remains an internal `RunAttempt` responsibility. Workflow parent passes framed canonical lane/SHA/attempt; local gate uses `Full`.
- `distribution/fixtures/reactiveui-signature-chain-baseline.json` — immutable parent-SHA full-graph snapshot.
- `scripts/Test-NuGetEvidencePublication.ps1` — отдельный минимальный read-only gate: trusted workflow разрешает `EXPECTED_SOURCE_SHA` как exact commit, получает validator mode/object только из его raw commit tree, binary-safe извлекает exact blob через trusted `git cat-file blob`, независимо пересчитывает Git object id и исполняет только эту runner-temp copy; workspace/HEAD alias/index bytes/filters/config никогда не являются executable input. Validator повторно проверяет raw JSON/XML/HTML/log schemas, canonical paths, receipt/manifest cross-links, hashes, link count/file identity и expected attempt identity; producer/sanitizer/write modes отсутствуют.
- `.github/workflows/tests.yml` — новый `Signature` job on `ubuntu-24.04` делает full-history credential-free checkout, immutable local-feed proof and unique empty packages root; `Regression` on `windows-2022` keeps exact two-project scope. One trusted inline wrapper step owns two sibling process domains: it raw-extracts/runs producer `RunAttempt`, captures its frame/exit, then independently resolves/extracts/runs the validator itself. Producer cannot launch or attest the validator. Parent retains one pre-scrub seed snapshot and passes it separately to both children; no cross-step persistence. Wrapper alone owns command-file outputs and requires marker + independent validation + exact receipt/native-exit binding. It exits success only after safe authorization, for either a successful or failed attempt; pinned uploader accepts outputs only from that successful wrapper, and a separate constant `always()` enforcement step makes a validated failed attempt/job red after evidence upload.
- `src/Unlimotion.Test/CiReadmeMediaContract.cs` — assertions удалённых legacy step names заменяются exact `Regression` owner/serial Unit+Headless x2/evidence-gate contract.
- `.github/workflows/android-packaging.yml`, `.github/workflows/deb_packaging.yml` — явное `DOTNET_NUGET_SIGNATURE_VERIFICATION: "true"`.
- Stage 3 `.github/workflows/distribution-validation.yml` остаётся downstream handoff вне approval scope этой child-spec: после merge prerequisite его изменение, static contract и evidence разрешаются и завершаются только Stage-3 spec.
- GitHub main branch protection/ruleset — separate user-owned operation after technical PASS: required set фиксируется только из exact observed current-candidate check-runs `{Signature, Regression, AndroidPkg}` с их exact `{context, GitHub Actions app_id}`; до observation ни alias `android-build`, ни category `security contexts` не являются допустимым required context. Уже существующие required checks сохраняются, CodeQL не добавляется. `strict=true`; admins/force-push/ruleset bypass closed.

### 6.2 Детальный дизайн
Prerequisite exact allowlist:

1. `specs/2026-07-21-reactiveui-signature-chain-remediation.md`.
2. `src/Directory.Packages.props` — ровно две version replacements.
3. `.github/workflows/android-packaging.yml` — verification `false -> true`; remove workflow-level `contents: write`/global token, pin both Android jobs to `ubuntu-24.04`, set build job `contents: read`, split release-only handoff into `android-release` with exact `actions: read, contents: write`, and replace every external action reachable from build/release with the exact reviewed immutable 40-hex SHA table below. Floating tag/branch is forbidden.
4. `.github/workflows/deb_packaging.yml` — `"false" -> "true"`, same immutable external-action SHA table and least-privilege token split as Android; release-only write authority is isolated from verified restore/build.
5. `.github/workflows/tests.yml` — добавить `Signature` on `ubuntu-24.04` с `fetch-depth: 0`, `persist-credentials:false`, verified HEAD-bound local feed и unique empty cache; в Windows создать exact `Regression` on `windows-2022`, replacing current cache/two restores/Unit/Headless steps with one framed trusted wrapper while preserving exact two projects and Unit once/Headless twice. Both jobs use `timeout-minutes:120`, verification=true before attempt, in-step independent gate, wrapper-success-only pinned fail-path upload and a final `always()` verdict-enforcement step; duplicate orchestration/full solution build forbidden.
6. `scripts/Test-NuGetSignatureChain.ps1` — `GenerateBaseline`/raw-input `RunAttempt`/closed-stdin `Worker`/`SelfTest`, internal receipt bootstrap, candidate sanitizer, publication finalizer и fail-closed orchestration. Legacy top-level `Verify`/`WriteAttemptReceipt` entry points запрещены, включая diagnostic aliases: acceptance path не должен получать verification payload через binder/`PSBoundParameters`.
7. `scripts/Test-NuGetEvidencePublication.ps1` — отдельный narrow read-only validator, исполняемый только из trusted runner-temp raw-blob extraction exact `EXPECTED_SOURCE_SHA` commit.
8. `distribution/fixtures/reactiveui-signature-chain-baseline.json` — immutable normalized full-graph snapshot, anchored to `origin/main@e11cae9a086ddd4fd97105f00b67bedf05f92700`.
9. `src/Unlimotion.Test/CiReadmeMediaContract.cs` — новый exact workflow/lane/serial/gate contract.
10. GitHub main branch protection/ruleset — только после отдельного user decision: exact observed app-bound required checks, `required_status_checks.strict=true`, `enforce_admins=true`, `allow_force_pushes=false`, enabled `Main` ruleset без bypass actors, merge-queue compatibility и сохранение/усиление остальных settings.

Reviewed external-action pins are closed and normative; comments in YAML preserve the upstream release label, while execution uses only the SHA:

| Action | Intended upstream release | Required commit |
| --- | --- | --- |
| `actions/checkout` | `v4.4.0` | `11d5960a326750d5838078e36cf38b85af677262` |
| `actions/setup-dotnet` | `v4.3.1` | `67a3573c9a986a3f9c594539f4ab511d57bb3ce9` |
| `actions/setup-java` | `v4.8.0` | `c1e323688fd81a25caa38c78aa6df2d33d3e20d9` |
| `android-actions/setup-android` | `v3.2.2` | `9fc6c4e9069bf8d3d10b2204b1fb8f6ef7065407` |
| `actions/cache` | `v4.3.0` | `0057852bfaa89a56745cba8c7296529d2fc39830` |
| `actions/upload-artifact` | `v4.6.2` | `ea165f8d65b6e75b540449e92b4886f43607fa02` |
| `softprops/action-gh-release` | `v2.6.2` | `3bb12739c298aeb8a4eeaf626c5b8d85266b0e65` |

The pins are not inferred from moving major tags during implementation. Author validation resolved each exact release ref to the listed 40-hex commit and inspected its committed `action.yml`: upload `v4.6.2` exposes `artifact-id` and `artifact-digest`; release `v2.6.2` accepts exact `files`, explicit `token`, `fail_on_unmatched_files` and `overwrite_files`. `actions/download-artifact` is deliberately not used: its digest mismatch is warning-only. Release obtains and hashes the exact REST archive by numeric artifact id instead.

Downstream Stage 3 handoff после prerequisite merge (не авторизуется approval этой spec и не входит в её file table):

1. `.github/workflows/distribution-validation.yml` — ровно Android `"false" -> "true"`.
2. `scripts/test-distribution-contract.ps1` — static prohibition of disabled verification и negative fixture.
3. `specs/2026-07-18-distribution-support-contract.md` — approval/evidence journal.
4. `specs/2026-07-17-readme-reliability-roadmap.md` — prerequisite/Stage 3 status.

Целевое net10.0 affected signed subset (это не полная package closure):

| Package | Current | Target | Pin type |
| --- | --- | --- | --- |
| `ReactiveUI.Avalonia` | `12.0.1` | `12.0.2` | direct central |
| `ReactiveUI` | `23.2.27` и промежуточное разрешение `23.2.1` | `23.2.28` | direct central + transitive requirement |
| `Splat` | `19.3.1` | `19.4.1` | transitive only |
| `Splat.Builder` | `19.3.1` | `19.4.1` | transitive only |
| `Splat.Core` | `19.3.1` | `19.4.1` | transitive only |
| `Splat.Logging` | `19.3.1` | `19.4.1` | transitive only |

Для каждого проверяемого проекта verifier нормализует полный `project.assets.json` до множества `package id/version` и сравнивает before/after. Допустимый version drift ограничен шестью строками affected signed subset; добавление/удаление любого другого package id либо изменение его версии — stop/re-approval. В частности, должны сохраниться `DynamicData 9.4.31`, `System.Reactive 6.1.0`, `Avalonia 12.0.3` и остальные разрешённые Avalonia package versions baseline.

Baseline never runs in Ubuntu acceptance or from parent/dirty workspace. First candidate-tooling commit fixes generator/verifier/self-tests; trusted launcher raw-extracts/re-hashes exact committed generator and runs only temp file with mandatory canonical `-RepositoryRoot`, `-ExpectedParentSha`, `-PackagesRoot`, `-OutputPath` and `-DotNetExecutable <absolute-dotnet-from-pinned-setup>`. Cwd, `$PSScriptRoot`, implementation checkout, PATH and aliases are not inputs; roots are non-link/non-overlapping.

`RepositoryRoot` has exact committed parent HEAD, stage-0 non-sparse index, no hidden flags and binary clean state. Clean adapter uses trusted Git with replace/fsmonitor/untracked-cache disabled; security-surface checks prevent hiding. `DotNetExecutable` is canonical absolute leaf from pinned setup, never PATH lookup; selected version must match closed `^10\.0\.[0-9]+(?:-[0-9A-Za-z.-]+)?$`, equal global.json resolver result and remain stable during attempt. `latestFeature` selection is recorded rather than falsely forced to 10.0.100. Packages/output remain isolated/non-link/absent.

Каждый baseline restore использует exact `--configfile <repository-root>/src/nuget.config --force --no-http-cache -p:DisableImplicitLibraryPacksFolder=true -p:DisableImplicitNuGetFallbackFolder=true -p:RestoreFallbackFolders=`. Простого empty `RestoreAdditionalProjectSources` недостаточно: SDK повторно добавляет library packs как `TreatAsLocalProperty`. Exact switches удаляют Windows `C:\Program Files\dotnet\library-packs` и Visual Studio Shared `NuGetPackages` fallback; before restore `dotnet msbuild -getProperty:RestoreAdditionalProjectSources,RestoreAdditionalProjectFallbackFolders,RestoreFallbackFolders` под теми же global properties обязан вернуть все три empty. Если installed SDK нарушает closed set, generation останавливается как environment blocker — silently расширять allowlist нельзя. После каждого sequential restore root identity, one `packageFolders` key = canonical isolated packages root, `project.restore.sources` exact `{<repository-root>/artifacts/nuget-local, https://api.nuget.org/v3/index.json}`, absent/empty fallback folders и actual six target nupkg only under isolated root проверяются; root `project.assets.json` копируется без overwrite до следующего restore. После independent candidate validation temp bytes переносятся в allowlisted `distribution/fixtures/reactiveui-signature-chain-baseline.json`; existing fixture не перезаписывается.

Preseeded cache is transport only, never provenance, and unrelated/private global-cache entries are never enumerated. A preliminary parent restore may produce three untrusted `project.assets.json` files used only as id/version hints; their exact union is bounded by graph/cardinality limits and selects raw nupkg candidates from a dedicated copy. Extra cache entries are ignored before trusted rebuild and cannot enter evidence. There is no NuGet flat-container `.nupkg.sha512` HTTP endpoint and global-cache `.sha512` is never trusted as provenance. For every selected NuGet.org id/version, generator starts at exact `https://api.nuget.org/v3/index.json`, selects one closed `RegistrationsBaseUrl/3.6.0` resource, resolves the exact lowercase normalized-id/version registration leaf, and requires that leaf's HTTPS `catalogEntry` URL plus `packageContent` exact to the service-index-derived flat-container id/version URI. It fetches the bound catalog entry and requires catalog `id`/normalized `version`, `packageHashAlgorithm=SHA512`, canonical Base64 `packageHash` and positive canonical `packageSize`; catalog need not repeat `packageContent`. It downloads the exact leaf packageContent, accepts only HTTPS redirects among `api.nuget.org` and `globalcdn.nuget.org` with unchanged path semantics/no userinfo/query/fragment and max five hops, then requires raw byte length=`packageSize` and SHA-512=`packageHash`. Wrong/missing/duplicate leaf, leaf catalogEntry/packageContent, catalog id/version/hash/algorithm/size or redirect fails. Repo-local packages map to exact regular tracked `artifacts/nuget-local` parent blob. Existing extracted nuspec/content, `.sha512` and `.nupkg.metadata` are discarded. A new empty cache is safely extracted only from catalog-verified nupkg bytes (no traversal, links, duplicate/casefold paths, unsupported compression or size overflow); nuspec/dependencies come only from these bytes and deterministic metadata is regenerated. Authoritative restore runs against this closed cache with package-content network fetch denied, must reproduce all three graphs exactly, use every selected hint and require no missing/extra package. Every resolved package records source/raw `nupkgSha512`; unchanged target packages must match committed byte manifest. Missing hint/package/hash, unused/extra selected seed, nonexistent HTTP sidecar mistakenly treated as provenance, tampered nupkg/nuspec/.sha512/.metadata or extraction ambiguity stops generation. This covers full graph, not only six targets.

Baseline network/archive limits are exact and checked while streaming: at most 2048 unique id/version hints; service-index/registration/catalog response 16 MiB each; connect timeout 30 seconds, per-response deadline 120 seconds, whole generation deadline 30 minutes; max five redirects; one nupkg 512 MiB and all selected nupkg bytes 8 GiB. A ZIP has at most 65536 entries, UTF-8 entry path at most 512 bytes/32 segments, one uncompressed entry at most 256 MiB, uncompressed package total 2 GiB, global extracted total 16 GiB and compression ratio at most 200:1 for every nonempty entry and archive aggregate. Exact-boundary fixtures pass; `+1`, unknown sizes, premature EOF, content-length/stream mismatch and timeout fail before partial cache becomes authoritative.

Two SHA-512 domains are never conflated. Catalog `packageHash` and regenerated global-cache `<id>.<version>.nupkg.sha512` represent the raw `.nupkg` SHA-512 as canonical 88-character Base64; evidence `nupkgSha512` represents those same 64 digest bytes as lowercase 128-hex. Conversion is exact decode/re-encode, not a second hash. NuGet assets `libraries[].sha512` and `.nupkg.metadata.contentHash` use NuGet's distinct logical package-content hash, computed only by exact selected-SDK API `NuGet.Packaging.PackageArchiveReader.GetContentHash(CancellationToken, Func<string>)`; generator loads its dependency closure only from the same selected SDK, records assembly informational versions/raw SHA-256 values and rejects a missing/wrong method shape or loader path. It writes `.nupkg.sha512` as exact 88 ASCII Base64 bytes, no BOM/whitespace/final newline. Metadata-v2 canonical JSON is UTF-8 no BOM/final LF and exact `{"version":2,"contentHash":"<logical>","source":"<source>"}`; `<source>` is `https://api.nuget.org/v3/index.json` for NuGet.org and the canonical absolute parent `<RepositoryRoot>/artifacts/nuget-local` URI/path form emitted by NuGet for repo-local packages. Source classification must agree with the fixture's `source` field; the tracked local package is never labeled NuGet.org. Restore assets equal logical value; catalog/raw/cache-sidecar/evidence verification equals the raw digest under its exact encoding. Positive fixtures prove a real signed package where raw and logical hashes differ plus both source branches; wrong Base64/hex encoding, swapping either domain/source, trusting pre-existing metadata, or regenerating with an unrecorded API/assembly fails.

Package-resolution input manifest строится детерминированно непосредственно из recorded commit: trusted Git с `GIT_NO_REPLACE_OBJECTS=1` и `--no-replace-objects` выполняет `git ls-tree -rz --full-tree <sha>`; stage-0 materialized index сравнивается через `git ls-files -z --stage -v`, но не является byte source. Оба потока читаются как raw NUL-delimited records с обязательным разбором mode/object/stage/flag/path без line splitting. Parser доказывает well-formed tree/index view, stage `0`, отсутствие sparse-directory records и tags assume-unchanged/skip-worktree для selected entries, затем применяет ordinal-sorted фильтр по exact `global.json`, case-insensitive basename `nuget.config`, всем tracked `*.csproj`, `*.fsproj`, `*.vbproj`, `*.props` и `*.targets` во всём repo. Только selected package-resolution inputs обязаны быть regular blobs `100644|100755`; selected symlink/gitlink, malformed/control path, case collision или tree/index disagreement отклоняются. Существующий unrelated gitlink `.native/libgit2-src` mode `160000` корректно парсится и исключается после фильтра, а не делает baseline невозможным. Для каждого selected object `sha256` считается над exact raw bytes `git cat-file blob <gitObjectId>`, а не над checkout/smudge/CRLF-transformed file; raw blob length также сверяется с binary-safe `git cat-file -s`. Fixture хранит полный selected path/mode/blob-id/raw-byteLength/raw-blob-SHA-256 set; verifier заново строит тот же set из recorded commit и отклоняет missing/extra/duplicate path, stage/flag/mode/object/size mismatch либо hash drift. Replace refs, repo filters/config, fsmonitor, untracked cache, excludes and checkout bytes не могут подменить raw commit input. Если cache неполон, verification/restore не проходит, root identity не совпадает, source dirty либо exact parent inputs недоступны, EXEC останавливается — bypass для создания fixture запрещён.

Comparison is explicitly two-tree. Fixture parent manifest must reproduce exact parent commit raw bytes. Candidate manifest is rebuilt from exact candidate SHA and must have identical selected path set/mode/object/hash/length for every entry except `src/Directory.Packages.props`. That one raw blob is parsed as closed XML and its semantic plus raw normalized diff must be exactly two approved version replacements (`ReactiveUI.Avalonia 12.0.1 -> 12.0.2`, `ReactiveUI 23.2.27 -> 23.2.28`) with no other element/attribute/order/whitespace/EOL/BOM drift. Extra selected input, second changed props/targets/project/config/global.json, or additional Directory.Packages change fails. The intentional candidate blob is recorded separately in target evidence rather than compared equal to parent object id.

Baseline fixture — closed JSON. Top-level exact properties: `schemaVersion`, `sourceSha`, `gitObjectFormat`, `dotnetSdkVersion`, `inputManifest`, `projects`; fixed schema/source/object format as above and SDK exact selected value matching `^10\.0\.[0-9]+(?:-[0-9A-Za-z.-]+)?$`. `inputManifest` is ordinal-sorted unique array closed entries `{path,mode,gitObjectId,byteLength,sha256}`; mode `100644|100755`, object lowercase 40-hex, byteLength exact raw length and sha256 exact bytes. `projects` is exact ordered Headless/Desktop/Debian closed entries `{projectPath,packageSet,graphSha256}`. Each `packageSet` entry is closed `{id,version,source,nupkgSha512}`; source exact `nuget.org|repo-local`, hash lowercase 128-hex raw nupkg bytes. NuGet ids use ASCII canonical casing from verified nuspec and must match all assets sightings; set uniqueness is both Ordinal and OrdinalIgnoreCase, versions use NuGet normalized form. Sort is OrdinalIgnoreCase id with Ordinal exact id tiebreaker then normalized version; casefold duplicate/casing drift rejected before hashing. `graphSha256` hashes canonical UTF-8 no-BOM lines `<canonical-id>\t<normalized-version>\t<source>\t<nupkgSha512>\n` in that order, literal TAB/LF and final LF; field TAB/CR/LF forbidden. Fixture uses canonical Utf8JsonWriter bytes and byte-identical reserialization. Verifier proves parent commit, selected SDK evidence, complete raw input manifest, exact projects and full package-byte sets. Placeholder/external baseline forbidden.

Ubuntu job вызывает `RunAttempt` с raw lane text `Signature`; Windows `all-tests` — с `Regression`. `Full` разрешён только локально как wrapper двух отдельных child attempts/evidence, а не как один combined phase plan. Workflow устанавливает `DOTNET_NUGET_SIGNATURE_VERIFICATION=true` до вызова; `RunAttempt` не исправляет входное окружение, а проверяет его как precondition. Raw process env читается через `Environment.GetEnvironmentVariable`: verification принимает только ordinal exact `true`; revocation mode — только отсутствующее значение (`null`) либо ordinal exact `online`. Empty/whitespace, `false`, `0`, `True`, `offline` и иные значения отклоняются; тот же контракт действует для `GenerateBaseline`. Внешние lane/SHA/attempt принимаются как nullable strings без binder-level `ValidateSet`, `[int]` или mandatory rejection: semantic validation выполняется после безопасного bootstrap. Raw attempt имеет canonical unsigned decimal grammar `^[1-9][0-9]{0,9}$`, затем `TryParse`/range `1..2147483647`; GitHub third and later reruns therefore legal. Bootstrap создаёт exact root layout `<runner-temp>/nuget-evidence/<signature|regression|full>/<sourceSha>/attempt-<canonical-positive-int32>/<lowercase-32hex-nonce>/`, где final path ровно `final`; каждый component имеет closed ASCII grammar, CR/LF/C0/DEL запрещены до filesystem access и до `GITHUB_OUTPUT`. Work/candidate/receipt/publication/fallback/quarantine/final paths являются разными non-link descendants того же nonce parent. Publication/fallback/final roots дополнительно проходят native same-filesystem identity check; injected cross-volume path отклоняется. Bootstrap нормализует только path-safe SHA/attempt tokens и возвращает allowlisted failure code вместо exception text.

`RunnerTempRoot` is mandatory and canonical rather than implicitly read inside repository code. CI trusted inline bootstrap passes exact canonical `$RUNNER_TEMP`; local launcher passes canonical `[IO.Path]::GetTempPath()` explicitly. The selected root must already exist as a non-link directory, have stable native volume/directory identity before and after attempt, and contain no pre-existing nonce path; caller-supplied relative/non-temp/link/identity-changing roots fail. Local Regression/Full use the same descendant grammar and isolation rules as CI.

Recorded `attempt:preconditions` проверяет lane/platform, canonical positive Int32 attempt, exact process cwd equal canonical `RepositoryRoot`, что `ExpectedSourceSha` является exact commit object with replace objects disabled и `HEAD == ExpectedSourceSha`, а также source state. A native no-follow bootstrap supports both layouts: `.git` is either a real directory or a regular ASCII `gitdir: <path>` file. For a linked worktree it validates canonical non-link worktree gitdir, its `gitdir` back-pointer, bounded `commondir`, common-dir object/config identity and no control/path escape; for a normal clone gitdir=common-dir. It then reads common `config`, optional `config.worktree` and common `info/attributes` through held identities, rejecting include/includeIf, filter.*, core.attributesFile/hooksPath/fsmonitor/untrackedCache, nonempty info attributes and link/identity drift. Expected raw `.gitattributes` plus built-in Git attribute parsing must show no `filter`/`working-tree-encoding`; checkout `.gitattributes` may have platform EOL normalization but semantic built-in-clean comparison must equal expected. System/global config are disabled, `GIT_ATTR_NOSYSTEM=1`, and clean commands set explicit `core.autocrlf=true` on Windows or `false` on Linux so the current clean CRLF checkout is legal without trusting system config. Thus status cannot launch repository-selected external filters. Clean adapter runs absolute trusted Git with sanitized environment, replace objects disabled, fsmonitor/untracked-cache disabled and exact `status --porcelain=v2 -z --untracked-files=all --ignored=no`; stdout is binary-safe/online-capped and command failure/malformed/nonempty/overflow fails. Staged/unstaged and untracked non-ignored drift are rejected; ignored outputs outside security surfaces are allowed. Separate expected raw tree/index checks reject hidden flags/sparse/collisions and ignored/untracked shadows on security/package-input surfaces. Positive fixtures cover normal clone, this linked-worktree layout and Windows CRLF; poisoned gitdir/commondir/back-pointer/config/filter/attributes and actual content mutation fail. Raw `ExpectedSourceSha` blobs remain the only content anchor.

Local acceptance-capable `Regression` and local diagnostic `Full` разрешены only for already committed clean exact SHA; standalone local `Signature` is invalid and Full Signature child remains non-authoritative for NUGET-AC-04/06. Dirty characterization is explicitly non-authoritative pre-commit debug and creates no acceptance receipt. The same small trusted launcher is mandatory in CI and locally: it resolves `scripts/Test-NuGetSignatureChain.ps1` only as a regular raw blob in `EXPECTED_SOURCE_SHA`, verifies normal matching stage-0 index, bounded-binary extracts it with `git cat-file blob`, recomputes the Git object id and executes only a temp copy. Direct local invocation of the workspace script is diagnostic-only and cannot create an acceptance receipt. Workspace producer bytes are never acceptance input; the independent gate rechecks expected tree/index/extracted producer bytes after the child, so workspace-tamper/restore attacks fail. Gate post-run scope is exact: HEAD, bounded clean status, producer/validator tree+index+raw object identities and final evidence. It does not claim to rerun full scans after execution; the trusted producer performs full CI/package-input scans before work and records their hashes. Precondition also checks empty canonical `NUGET_PACKAGES`, exact absolute `<RepositoryRoot>/src/nuget.config`, exact implicit-library/fallback disabling switches plus empty evaluated additional/fallback properties, verification/revocation and path/volume invariants. All recoverable directory creation/orchestration is finalizer-covered. Every known precondition/orchestration failure gets `safe-fallback`, empty manifest and nonzero verdict; inability to prove safe parent/fallback leaves marker/path unset. Signature/Regression failed precondition preserves exact first code and adds all legal lane phases as explicit skipped; invalid lane has no invented lane phases. Full precondition failure creates no child/package/work roots and preserves exact outer code. Every standalone valid lane starts with canonical `attempt:preconditions`.

Signature lane:

1. pinned checkout exact `${{ github.sha }}` uses `fetch-depth: 0`, `persist-credentials: false`, does not materialize submodules and proves the parent through `git cat-file -e $parentRevspec`, where `$parentRevspec` is a single quoted PowerShell string `e11cae9a086ddd4fd97105f00b67bedf05f92700^{commit}`, before setup from `global.json`;
2. assert the existing repo-local `artifacts/nuget-local` is an exact HEAD-bound tree: only tracked top-level regular nupkg blobs, no untracked/nested/link entries, and no job step creates or mutates it;
3. select unique empty `NUGET_PACKAGES` under runner temp;
4. assert that workflow-provided `DOTNET_NUGET_SIGNATURE_VERIFICATION=true` is already present before `RunAttempt`;
5. restore с exact `--force --no-http-cache --configfile src/nuget.config -p:DisableImplicitLibraryPacksFolder=true -p:DisableImplicitNuGetFallbackFolder=true -p:RestoreFallbackFolders=` последовательно выполняется для `tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj`, `src/Unlimotion.Desktop/Unlimotion.Desktop.csproj` и exact Debian publisher project `src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj` в обычные project-local `obj`; `dotnet msbuild -getProperty` under same globals first proves empty additional/fallback properties. Сразу после каждого restore проверяются exact root `project.restore.projectUniqueName`, единственный `packageFolders` key = canonical isolated `NUGET_PACKAGES`, absent/empty fallback folders, `project.restore.sources` exact canonical repo `artifacts/nuget-local` + `https://api.nuget.org/v3/index.json`, отсутствие SDK/VS/ambient sources и расположение target nupkg only under isolated root. Затем root `project.assets.json` копируется без overwrite в отдельный immutable evidence path до следующего restore (Desktop и Debian намеренно делят исходный `src/Unlimotion.Desktop/obj/project.assets.json`, поэтому порядок и немедленное копирование являются частью контракта);
6. run verifier against exact `src/nuget.config`, central props, tracked CI executable surface, committed baseline fixture, три скопированных root-assets evidence files и isolated package root; verifier повторно проверяет exact three project identities, packageFolders/sources, разные copy paths, SHA-256 каждого copied assets file и отсутствие overwrite/collision;
7. parent invokes external closed worker kind `SignatureVerify` with one framed canonical array of exactly three absolute assets-copy paths; no native argv array coercion/caller closure is trusted. For each of six exact nupkg it launches fingerprint-bound `dotnet nuget verify`; only exit 0 accepts. SelfTest rejects 0/1/2/4 paths, reorder/duplicate and caller-local/ambient-function dependence;
8. write schemaVersion=1 phase-dependent Signature evidence и sanitized logs только в `candidateEvidenceRoot` вне upload tree: success variant требует exact three assets/six packages/per-package verification; failure variant содержит failure phase, exact completed safe-prefix evidence и sanitized diagnostics, запрещая недоступные success-only graph/package fields;
9. publication finalizer строит primary receipt вне upload root. `evidenceManifest` хеширует только validated candidate files и никогда не включает `attempt-receipt.json`; primary final tree обязан состоять ровно из receipt плюс entries manifest. Finalizer копирует candidate в same-volume scratch, добавляет receipt, повторно проверяет exact file set/hashes/links и rename-ит отсутствующий root только после полной проверки. Recoverable precondition/orchestration/sanitizer/manifest/receipt/cleanup/copy/hash/rename/final-validation failure приводит к новому fallback-only tree ровно с одним receipt и `evidenceManifest=[]`; inability to prove a clean fallback is catastrophic и оставляет marker/path unset;
10. within the same trusted wrapper step, parent captures producer `RunAttempt` frame/native exit, then independently raw-extracts/runs `Test-NuGetEvidencePublication.ps1` with that root/exit and the same seed snapshot. Validator verifies canonical root/receipt/manifest/allowed prefix, exact attempt verdict and exact binding: success receipt requires producer exit `0`; failure/fallback receipt requires its allowlisted nonzero producer exit. Producer cannot forge this second process result. Parent starts outputs false/empty and only after both frames plus command-file continuity/recovery checks writes ready/verified/root/verdict. The wrapper exits `0` for either validated success or validated failure evidence and nonzero for every authorization/catastrophic failure. Pinned upload under `always()` requires exact wrapper outcome `success`, ready+verified and verdict `success|failure`; a following constant `always()` enforcement step requires successful wrapper+upload and then exits nonzero for verdict `failure`. Thus a failed wrapper can never authorize upload even if runner-visible outputs were forged. Artifact name contains SHA + canonical positive attempt, no overwrite, retention 14 days.

Regression lane сохраняет host-safe scope ровно двух проектов и никогда не восстанавливает/собирает `src/Unlimotion.sln`, Android/iOS/Browser/Docker projects или workloads. Canonical order: targeted `restore --force --no-http-cache --configfile <RepositoryRoot>/src/nuget.config -p:DisableImplicitLibraryPacksFolder=true -p:DisableImplicitNuGetFallbackFolder=true -p:RestoreFallbackFolders=` для exact absolute paths двух проектов. Каждый restore adapter success является compound result: native exit 0 плюс немедленная post-restore проверка root project identity, единственного isolated package folder, exact local+NuGet.org source set, empty fallback properties/folders and target nupkg containment; native 0 с failed identity check возвращает typed failure `restore-evidence-failed`, а build не стартует. Затем targeted `build -c Debug --no-restore -p:UseSharedCompilation=false` for each; Unit once and Headless twice.

Каждый test invocation использует `dotnet run --project <exact-absolute-project> -c Debug --no-restore --no-build -- --maximum-parallel-tests 1 --report-trx --report-trx-filename results.trx --report-html --report-html-filename <absolute-unique-raw-run-root>/results.html --results-directory <absolute-unique-raw-run-root>`. Runner args идут strictly after `--`. Pinned TRX extension rejects a directory component in `--report-trx-filename`, so its value is exact bare `results.trx` and absolute `--results-directory` owns placement; pinned TUnit `1.44.0` HTML reporter uses filename verbatim, so only HTML filename is absolute. Each root is distinct and absent before orchestrator creation; after exit it contains exact one `results.trx` + one `results.html`, without extra/sidecar/attachment or cwd file. Before child launch adapter sets supported exact `TUNIT_DISABLE_GITHUB_REPORTER=true` and removes all `GITHUB_OUTPUT`, `GITHUB_ENV`, `GITHUB_PATH`, `GITHUB_STATE`, `GITHUB_STEP_SUMMARY`, `ACTIONS_RUNTIME_TOKEN`, `ACTIONS_RESULTS_URL`; unsupported invented TUnit toggles are not contract. This forbids GitHub reporter/HTML upload/summary side effects; adapter proves exact root contents and no created report/summary outside root. Unit and Headless branches remain independent after prerequisites. `Invoke-TestCommandAdapter` only runs native process, retains raw reports outside candidate and parses exact named files into typed counts/paths; it never creates sanitized reports. Sanitizer regenerates closed minimal TRX/HTML under `candidateEvidenceRoot/regression` and validates them. Complete receipt records exact SHA/lane/projection, native vs synthetic exit, counts and regenerated report hashes.

Parent evidence `e11cae9a086ddd4fd97105f00b67bedf05f92700`, retained при merge PR #279, зафиксировал Unit `830 total / 0 failed / 0 skipped` и Headless `36 / 0 / 0`. Успешный Unit run требует `discovered >= 830`, `passed = discovered`, `failed = 0`, `skipped = 0`; каждый Headless run требует `discovered >= 36` с теми же invariants, оба Headless runs имеют distinct run ids и одинаковые counts. Missing primary TRX/HTML, extra/sidecar/out-of-root file, count arithmetic mismatch или floor regression переводят соответствующую test phase в `failure` даже при `nativeExitCode=0`: run сохраняет native `0`, а phase tuple получает synthetic `exitCode=2` и `failureCode=test-evidence-failed`.

Windows Regression и Ubuntu Signature jobs имеют `timeout-minutes: 120`; Signature internal deadline 65 минут, Regression — 95, и ни один attempt не начинает новую native/sanitizer operation, если после неё не остаётся зарезервированных 5 минут для sanitizer и 5 минут для finalizer. Regression budget: четыре restore/build phases по 10 минут, Unit 20, два Headless по 10, sanitizer 5, finalizer 5 — максимум 90 минут; оставшиеся 5 минут покрывают orchestration/cleanup. Signature restore — 10 минут каждый, verify — 20, sanitizer/finalizer — по 5, максимум 60 + 5 orchestration.

Native adapter выдаёт heartbeat раз в 60 секунд только с phase id/elapsed. Timeout посылает graceful interrupt, через 10 секунд убивает entire process tree, ждёт/join-ит process и все stdout/stderr writer tasks и только после доказанного termination записывает `nativeExitCode=-1`, phase `exitCode=-1`, `native-command-timeout`; injected cancellation использует те же termination invariants и `native-command-cancelled`. Process-start/adapter exception до native exit перехватывается внутри adapter и возвращает typed `nativeExitCode=-2`, phase `exitCode=-2`, `native-command-threw`; online stdout/stderr limit overflow возвращает exact `nativeExitCode=-3`/`native-output-limit-exceeded` only after proven tree/writer termination. `[int]$null`, отсутствующий property и accidental zero запрещены. Verification/sanitizer/finalizer use an external closed worker mode of the same raw-extracted expected-commit script, never caller scriptblocks/runspace closures or legacy public CLI modes. Worker receives canonical closed JSON payload and the in-memory secret seed set only through redirected stdin, gets no caller locals/functions/profile/command-file/token env, and returns one closed bounded JSON result. Timeout/cancel/kill/join semantics match native adapter. Kill/join failure, living descendant or lingering reader/writer after grace period is catastrophic: candidate quarantined if possible, marker/path/upload unset. Finalizer timeout разрешает только доказанный fallback after proven termination либо catastrophic suppression. External GitHub cancellation never counts as acceptance. Recorded restore/build/test failure may yield validated primary failure tree only when sanitizer/publication fully succeed. Pinned upload publishes validated root as `reactiveui-regression-<sourceSha>-attempt-<runAttempt>` for 14 days only after a successful authorizing wrapper; final enforcement keeps the attempt verdict fail-closed.

Every native/test/worker child gets a closed environment view without every name in the immutable snapshotted secret-seed set, plus fixed `GITHUB_OUTPUT`, `GITHUB_ENV`, `GITHUB_PATH`, `GITHUB_STATE`, `GITHUB_STEP_SUMMARY`, `ACTIONS_RUNTIME_TOKEN`, `ACTIONS_RESULTS_URL`, `ACTIONS_ID_TOKEN_REQUEST_TOKEN`, `ACTIONS_ID_TOKEN_REQUEST_URL` and Git credential/header variables. Removal is ordinal by snapshotted name, so arbitrary matches such as `FOO_APIKEY`, proxy/auth material and `GOOGLE_APPLICATION_CREDENTIALS` cannot remain inherited; seed values reach producer/validator only through their separate framed stdin payloads. stdout/stderr are never emitted verbatim to Actions logs. Exact online limits are 4 MiB per native/test stream, 8 MiB combined per process, 32 MiB raw TRX, 16 MiB raw HTML, 128 MiB aggregate raw attempt tree; expected-commit producer/validator blob max 4 MiB, Git status/tree/index stream max 8 MiB, external validator stdout/stderr 16 KiB each and must both be empty on success. Limits are enforced incrementally before allocation/write exceeds cap. Every child has declared deadline, 10-second entire-tree kill/join grace and joined readers/writers; overflow, timeout or failed join can never be checked only after `ReadToEnd`/`CopyTo`.

Parent trusted inline bootstrap validates canonical containment below a held `RUNNER_TEMP` identity and every ancestor of `GITHUB_ENV`, `GITHUB_PATH`, `GITHUB_OUTPUT` and extraction roots as non-link before opening leaf files with platform-native no-follow handles (`CreateFileW` plus file-id/link-count on Windows; `open(O_NOFOLLOW)` plus `fstat` device/inode/link-count on Linux). It snapshots ENV/PATH bytes and identities and holds the original OUTPUT handle. Normal path: after child, identities and bytes remain unchanged and outputs append only through that held OUTPUT handle after final identity/link checks. Recovery path: any unlink/swap/ancestor drift is catastrophic/non-authorizing, but `finally` must still replace the runner-visible ENV/PATH with original bytes and OUTPUT with only false/empty fields through safe temp+atomic rename under the still-validated parent. Recovery may have a new identity and is never accepted as continuity; it exists only so the runner does not consume poisoned command files. Failure to establish safe recovery forces nonzero wrapper outcome; uploader requires wrapper outcome `success`, so attacker-controlled runner-visible outputs cannot authorize it. Ancestor reparse/root drift, same-byte leaf replacement, OUTPUT swap and forged ready/verified/root/verdict after recovery failure all have fixtures.

Lane-specific deadlines закрыты: standalone/child Signature получает 65 минут (60 declared operations + 5 orchestration), standalone/child Regression — 95 минут (90 + 5). Local Full wrapper получает 175 минут total: maximum 65 + 95 child envelopes и отдельные 10 минут outer aggregation/final validation; wrapper не продлевает deadlines и не принимает partial success. После outer preconditions exact child parents создаются как `<outer-nonce-parent>/children/signature/<lowercase-32hex-nonce>/` и `<outer-nonce-parent>/children/regression/<lowercase-32hex-nonce>/`. Каждый child parent имеет собственные absent-at-start `packages`, `work`, `candidate`, `receipt`, `publication`, `fallback`, `quarantine`, `final` descendants, native filesystem identity checks и hardlink/file-identity namespace; paths/ancestors/identities не пересекаются ни между детьми, ни с outer scratch/final roots. Child runtime context exact `full-child`, authority false и источник/config те же exact committed SHA/`src/nuget.config`. Validated Signature failure не подавляет Regression child. A proven-terminated recoverable child fallback or insufficient remaining 95-minute budget suppresses the next child and permits only a validated one-file outer fallback. Any unproven child process/tree/reader/writer termination is catastrophic and deterministically suppresses outer root/path/marker; it can never create fallback bytes that the living child might mutate. Full outer precondition failure creates zero child parents. Any root overlap/reuse or child fallback is outer failure under this precedence.

Full final root exact layout: outer `attempt-receipt.json`, then directory `signature/**` and `regression/**`, no other entry; каждый child subtree byte-identical corresponding validated child `final` root and includes its own `attempt-receipt.json`. Outer `full-primary` receipt exact properties: `schemaVersion`, `receiptKind`, `sourceSha`, `runAttempt`, `lane`, `runtime`, `outcome`, `failureCode`, `childAttempts`, `evidenceManifest`; `lane=Full`, outer runtime `executionContext=local`/authority false, `receiptKind=full-primary`. `childAttempts` is exact ordered two closed objects `{lane,relativeRoot,receiptSha256,outcome,failureCode}` for `Signature/signature` then `Regression/regression`; relativeRoot has exact one-segment value and receipt hash cross-links exact nested bytes. Outer manifest ordinal-sorted хеширует every child receipt/payload file, but not outer receipt; exact tree equals outer receipt union manifest, every manifest path begins exact `signature/` or `regression/`, and nested child manifests/hashes remain valid independently. Success требует оба child primary success; validated Signature child failure continues Regression and yields outer primary failure with first failed child in Signature/Regression order while retaining both validated subtrees. Any child fallback, catastrophic/missing child, insufficient budget, root/identity overlap, wrong context/lane/SHA/attempt, receipt/manifest/hash mismatch or outer publication defect discards/quarantines all child bytes and produces outer one-file `safe-fallback` or catastrophic suppression. Independent validator with `ExpectedLane=Full` recursively validates outer identity/runtime/manifest, exact two roots and each complete child tree; `ExpectedLane=Signature|Regression` never accepts Full root.

Verifier invariants:

- exactly one direct central pin for each of the two ReactiveUI packages at target versions;
- zero direct central pins for `Splat*`;
- no tracked workflow contains disabled NuGet signature verification or offline revocation bypass;
- assets contain all six target packages and none of their superseded versions; normalized full graph differs от parent snapshot только этими six version transitions;
- nupkg files exist under isolated package root, hashes are recorded, and fingerprint-bound signature verification passes;
- target package set is accepted only by new author SHA-256 `4D2DDD563BC0ECF5C9B438E1CE32E3FCC69DAADAFC2D1BD9CF858FD9E755CFB9`; exact old author `09702DACA40821B9E2F12DF12FB32479AD60F6C5C73A69E3EB35E06C9C3F898B` and repository signer `1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D` each must return native nonzero/`NU3034` against target package, without parsing localized positive output;
- negative fixtures reject downgrade/mixed graph, unrelated package drift, duplicate/direct Splat pin, missing package, disabled verification and wrong fingerprint.

CI scan всегда привязан к raw tree `EXPECTED_SOURCE_SHA`, а не к mutable workspace/index content. Trusted Git with replace objects disabled выполняет `ls-tree -rz --full-tree EXPECTED_SOURCE_SHA -- .github/workflows .github/actions scripts src/Unlimotion.Desktop/ci`; raw NUL parser требует unique canonical paths and regular blobs `100644|100755`, отклоняет symlink/gitlink, malformed/control/non-ASCII paths and Ordinal/OrdinalIgnoreCase collisions. Stage-0 index/mode/object/flags дополнительно обязаны совпасть с expected tree и не иметь assume-unchanged/skip-worktree/sparse records, но scanner bytes читаются только binary-safe `cat-file blob <expected-object-id>` with exact size/hash. Closed extension set: workflow/composite `*.yml|*.yaml` and executable `*.ps1|*.sh|*.cmd|*.bat|*.py`; unsupported reachable executable child extension forbidden.

Scan состоит из двух разных closed passes. Content-bypass pass token/AST-aware parses every tracked raw blob supported extension under all four roots, even if script unreachable, and classifies every occurrence `DOTNET_NUGET_SIGNATURE_VERIFICATION`/`NUGET_CERT_REVOCATION_MODE` as approved read, exact literal assignment or fixture-data constructor. Любая assignment/unset/default form for verification legal only exact `true`; revocation absent or exact `online`. Empty, whitespace, wrong case, `false`, `0`, dynamic interpolation/concatenation, conditional unset, indirect name construction and offline equivalents forbidden; unknown parser syntax fails closed. Fixture-data occurrences legal only in exact SelfTest registry constructors and never executable flow.

Invocation-closure pass starts only from every workflow raw blob and follows literal repo-relative invocations plus local `uses: ./.github/actions/...`; each reachable child and composite `action.yml|action.yaml` must resolve to the same expected-tree regular blob. Dynamic/unresolved invocation in reachable closure is forbidden, while an unreachable utility may use dynamic invocation if its content-bypass pass succeeds; in particular existing dynamic command adapters in `scripts/update-readme-media.ps1` and `scripts/record-status-contract-evidence.ps1` do not fail merely because they are unreachable from workflows. Required sentinels: `.github/workflows/android-packaging.yml`, `.github/workflows/deb_packaging.yml`, `.github/workflows/osx-packaging.yml`, `.github/workflows/tests.yml`, `scripts/Test-NuGetSignatureChain.ps1`, `scripts/Test-NuGetEvidencePublication.ps1`, `src/Unlimotion.Desktop/ci/deb/generate-deb-pkg.sh`, `src/Unlimotion.Desktop/ci/osx/generate-osx-publish.sh`. Negative fixtures cover empty/incomplete surface, raw expected-tree mutation, unsupported/reachable child, local composite action, all extensions, stage/mode/flags/link/path collisions, reachable-dynamic rejection, unreachable-dynamic allowance, unset/empty/dynamic flag constructions and bypass inside Desktop CI children.

Action-pin pass is intentionally scoped to exact required/security-owned `.github/workflows/tests.yml`, `.github/workflows/android-packaging.yml`, `.github/workflows/deb_packaging.yml` and their reachable local composites: every external `uses:` there is literal `owner/repository@<lowercase-40-hex-commit>`; tags/branches/expressions/short SHAs forbidden. Local uses are literal canonical `./.github/actions/...` and enter closure. Debian build must be `contents: read` with no token export; any release-only write authority is a separately pinned no-checkout job. Fixtures include Android and Debian floating-action, write-permission and release-split negatives.

`tests.yml` remains `contents: read`, no token reference, credentials false. Android workflow has no workflow-level permissions/token. `android-build` runs on `ubuntu-24.04` with `contents: read`, pinned checkout exact `${{ github.sha }}`, `persist-credentials:false` and current recursive submodule policy; it never exports `GITHUB_TOKEN`. Candidate artifact name is exact `unlimotion-android-apk-<lowercase-source-sha>-attempt-<canonical-positive-int32>`. Before pinned upload-artifact it creates exact artifact contents: `Unlimotion-<validated-release-tag>-android-arm64.apk`, `Unlimotion-<validated-release-tag>-android-x64.apk` in that order plus canonical `android-artifact-manifest.json`; tag grammar is exact `v?[0-9]+\.[0-9]+\.[0-9]+`, manifest stores the observed tag verbatim, and non-release build uses literal tag token `ci`. Manifest is closed as `{schemaVersion,sourceSha,runId,runAttempt,artifactName,releaseTag,apks}`, with ordinal-sorted exact two-entry `apks` `{file,byteLength,sha256}` and no link/extra. Upload output `artifact-id` must be canonical positive Int64 and `artifact-digest` exact bare lowercase 64-hex (v4.6.2 output); exact name/run id/canonical positive Int32 attempt/source SHA/id/digest become closed job outputs.

Debian verified build follows the same `ubuntu-24.04`/`contents: read`/pinned-checkout/`persist-credentials:false` contract before its fresh signed restore. A Debian release job, if retained, is a distinct pinned no-checkout job with only the minimum `actions: read, contents: write` permissions; PR and push cannot instantiate it. The scanner and AC08 fixture registry reject a Debian floating action, build-token/write permission, inherited workflow write permission, release-with-checkout and any build/release job collapse.

Release-only `android-release` has `needs: android-build`, exact `if: github.event_name == 'release'`, `runs-on: ubuntu-24.04`, no checkout and job permissions exactly `actions: read, contents: write`. GitHub permissions are job-wide: every step can technically access that job token; this spec therefore makes no false «single token-bearing step» claim. Risk is bounded by no repository checkout, exact pinned actions, literal REST endpoints and the minimum job required for reading an Actions artifact and uploading Release assets; PR/push never instantiate this job. A trusted inline PowerShell step uses the job token with bounded `/usr/bin/curl` requests. Metadata response max 1 MiB; archive max 1 GiB; connect timeout 30 seconds, response deadline 180 seconds and whole validation step 10 minutes; max five redirects and 32 KiB response headers. `GET /repos/<exact-owner>/<exact-repo>/actions/artifacts/<exact-decimal-id>` must return metadata `id`, exact attempt-qualified name, `expired=false`, `workflow_run.id`, `workflow_run.head_sha`, and service `digest == 'sha256:' + <bare-upload-output>`. It then downloads only `GET .../artifacts/<same-id>/zip` into an absent runner-temp file, streams exact SHA-256 and requires bare archive hash equal upload output. Redirect host is restricted to the exact documented GitHub artifact service allowlist captured by the implementation fixture; any other redirect stops. Closed `ZipArchive` extraction allows exactly three entries, compressed aggregate <=1 GiB, each APK <=512 MiB, manifest <=64 KiB, uncompressed aggregate <=1 GiB and compression ratio <=200:1; it rejects traversal/rooted/duplicate/casefold/link/special/unsupported-compression entries and requires exact manifest+two APKs. Content-Length/stream mismatch, unknown/oversized length, premature EOF, timeout and every exact-boundary `+1` fail. Manifest run attempt equals build output/current `github.run_attempt`; all names/lengths/hashes are revalidated with no extra bytes.

Only after this validation, pinned `softprops/action-gh-release@3bb12739c298aeb8a4eeaf626c5b8d85266b0e65` receives explicit `token: ${{ github.token }}` and exactly two newline-separated literal APK paths, `fail_on_unmatched_files: true`, `overwrite_files: false`; wildcards are forbidden. Wrong/missing upload outputs, run/id/attempt/source/name/service digest, metadata expiry, redirect, archive/link/extra/hash drift or partial release match fails. A release asset name collision is a stop rather than silent overwrite.

Все persisted evidence/receipt JSON имеют strict closed discriminated variants. Они читаются как UTF-8 без BOM через `System.Text.Json.JsonDocument` с `AllowTrailingCommas=false`, `CommentHandling=Disallow` и bounded depth. До semantic validation validator рекурсивно отклоняет duplicate properties, unknown properties, wrong-case names и Unicode lookalikes; имена сравниваются `StringComparer.Ordinal`. Числа принимаются только при совпадении raw JSON token с canonical signed/unsigned decimal grammar без leading zero, `-0`, fraction либо exponent, а затем проходят соответствующий `TryGetInt32`/`TryGetInt64`; PowerShell object coercion не является частью validator contract. SHA-256 — lowercase `[0-9a-f]{64}`, source SHA — lowercase `[0-9a-f]{40}`, fingerprint — uppercase `[0-9A-F]{64}`. Arrays имеют exact cardinality, canonical order и unique identity.

Closed scalar contract:

| Field | Exact JSON type / range |
| --- | --- |
| `schemaVersion` | Number / Int32, exact `1` |
| `runAttempt` | Number / Int32 `1..2147483647` when non-null, serialized canonical decimal; JSON Null legal only in `safe-fallback` state where raw attempt identity is invalid |
| phase `exitCode` | Null либо Number / Int32; tuple-state rules below; synthetic evidence defect exact `2`, timeout/cancel `-1`, start/throw `-2`, output overflow `-3` |
| Regression `nativeExitCode` | Null либо Number / Int32; null only for `not-attempted`; timeout/cancellation exact `-1`, process-start/adapter throw before native exit `-2`, online output overflow `-3` |
| `verifyExitCode` | Number / Int32; `0` для accepted package, non-zero только в legal Signature failure prefix |
| `discovered`, `passed`, `failed`, `skipped` | Null либо Number / Int32 `0..2147483647`; nullability только по run-state table |
| `durationMs` | Null либо Number / Int64 `0..9223372036854775807`; success requires non-null, failure allows null only when no trustworthy raw duration exists, `not-attempted` requires null |
| `byteLength` | Number / Int64 `0..9223372036854775807`, exact фактическая длина hashed file |
| `nupkgSha512` | JSON String, lowercase `[0-9a-f]{128}` raw SHA-512 exact nupkg bytes; NuGet logical content hash сюда не подставляется |
| `signatureVerification`, `signatureAuthoritative` | JSON Boolean; `signatureVerification=true`; authority legal value exact lane/platform matrix, string/number/null запрещены |
| `revocationMode` | JSON Null для absent process value либо String exact `online`; empty/other case/other mode запрещены |
| paths / enum / identifiers | JSON String; exact case-sensitive enum либо соответствующая canonical ASCII/path/hash grammar; empty string запрещён |
| `sanitizedLogs` | JSON Array exact canonical ordered unique sanitized-file references; object/string/null запрещены |
| `diagnostics` | JSON Array exact canonical ordered closed objects `{phase,code}`; только allowlisted phase/code, без message/exception/free text |

| JSON variant | Exact required properties | Nullable / conditionally legal | Always forbidden |
| --- | --- | --- | --- |
| `signature-success` | `schemaVersion`, `evidenceKind`, `sourceSha`, `runAttempt`, `lane`, `runtime`, `projects`, `packages`, `expectedAuthorFingerprint`, `sanitizedLogs` | нет | `failurePhase`, `completedProjects`, `attemptedPackages`, `diagnostics` |
| `signature-failure` | `schemaVersion`, `evidenceKind`, `sourceSha`, `runAttempt`, `lane`, `runtime`, `failurePhase`, `completedProjects`, `attemptedPackages`, `diagnostics` | completed project/package prefixes только по legal-state table | full `projects`, full `packages`, success outcome |
| `regression-success` | `schemaVersion`, `evidenceKind`, `sourceSha`, `runAttempt`, `lane`, `runtime`, `runs` | нет; все три run records имеют `state=success` | `failurePhase`, `diagnostics` |
| `regression-failure` | `schemaVersion`, `evidenceKind`, `sourceSha`, `runAttempt`, `lane`, `runtime`, `failurePhase`, `runs`, `diagnostics` | nullable run fields только по run-state table | success outcome |
| `primary` receipt | `schemaVersion`, `receiptKind`, `sourceSha`, `runAttempt`, `lane`, `outcome`, `failurePhase`, `failureCode`, `phases`, `evidenceManifest` | `failurePhase` и `failureCode` оба null только при success; оба non-null и связаны с first failed phase при failure | debug/exception/path/env fields |
| `full-primary` receipt | `schemaVersion`, `receiptKind`, `sourceSha`, `runAttempt`, `lane`, `runtime`, `outcome`, `failureCode`, `childAttempts`, `evidenceManifest` | `failureCode=null` only when both child outcomes success; otherwise first child failure in Signature/Regression order | `phases`, `failurePhase`, debug/exception/env/absolute path fields |
| `safe-fallback` receipt | `schemaVersion`, `receiptKind`, `sourceSha`, `runAttempt`, `lane`, `outcome`, `failureCode`, `evidenceManifest` | identity nullability только по следующей таблице | `phases`, `failurePhase`, evidence/debug/exception/path/env fields |

Каждый nested object также closed:

| Nested object | Exact properties |
| --- | --- |
| phase tuple | `name`, `status`, `exitCode`, `failureCode` |
| manifest entry | `path`, `sha256`, `byteLength` |
| runtime | `os`, `architecture`, `dotnetSdkVersion`, `executionContext`, `signatureVerification`, `revocationMode`, `signatureAuthoritative` |
| Signature project | `id`, `projectPath`, `assetsCopyId`, `assetsSha256`, `baselineGraphSha256`, `targetGraphSha256` |
| Signature package | `id`, `version`, `nupkgSha512`, `verifyExitCode`, `verifyLog` |
| Regression run | `runId`, `state`, `projectPath`, `configuration`, `nativeExitCode`, `failureCode`, `discovered`, `passed`, `failed`, `skipped`, `durationMs`, `trx`, `html`, `skipReason` |
| Full child attempt | `lane`, `relativeRoot`, `receiptSha256`, `outcome`, `failureCode` |
| sanitized file reference | `phase`, `path`, `sha256`, `byteLength` |
| diagnostic | `phase`, `code` |

Fallback identity и failure-code precedence имеют closed legal states:

| Condition | `lane` | `sourceSha` | `runAttempt` | Top-level `failureCode` |
| --- | --- | --- | --- | --- |
| corresponding raw field independently valid | normalized non-null | normalized non-null | normalized non-null | first failed canonical precondition |
| corresponding raw field invalid | null iff lane invalid | null iff SHA invalid | null iff attempt invalid | earliest of `invalid-lane`, `invalid-platform`, `invalid-run-attempt`, `invalid-source-sha`, затем later preconditions |
| failure after identity validation | non-null | non-null | non-null | exact later failure |
| CI gate with expected identity | exact expected lane | exact `${{ github.sha }}` | exact `${{ github.run_attempt }}` | null identity forbidden |

Canonical standalone phase plans содержат `attempt:preconditions`, lane phases, `attempt:safe-staging`, `attempt:raw-cleanup`. Signature сохраняет текущий ordered restore/assets plan. Regression plan exact: `regression:restore:unit`, `regression:restore:headless`, `regression:build:unit`, `regression:build:headless`, `regression:test:unit`, `regression:test:headless-1`, `regression:test:headless-2`, `regression:sanitize`. Каждая phase присутствует ровно один раз и в canonical order. `success` требует exact integer exit `0` и null code; `failure` — non-zero integer и phase-allowlisted code; `skipped` — null exit и exact `prerequisite-failed`. Timeout/cancellation use exit `-1` and `native-command-timeout|native-command-cancelled`; process-start/adapter throw uses `-2`/`native-command-threw`; online output overflow uses `-3`/`native-output-limit-exceeded`; post-native restore/report evidence defect uses synthetic `2`. Skip without failed dependency and success after failed dependency are forbidden.

| Failed phase | Required later state |
| --- | --- |
| `attempt:preconditions` | все Signature/Regression lane phases explicit skipped in canonical order; только fallback publication, exact precondition code preserved |
| `signature:restore:X` | matching assets и весь последующий Signature prefix skipped; sanitize attempted |
| `signature:assets:X` | последующий restore/assets/verify prefix skipped; sanitize attempted |
| `signature:verify` | sanitize attempted; primary `signature-failure` legal только если sanitize succeeds |
| `regression:restore:unit` | `build:unit` и `test:unit` skipped; Headless chain продолжается |
| `regression:restore:headless` | `build:headless` и оба Headless tests skipped; Unit chain продолжается |
| `regression:build:unit` | только `test:unit` skipped |
| `regression:build:headless` | оба Headless tests skipped |
| любой Regression test | не подавляет другие reachable tests; `headless-1` failure не разрешает skip `headless-2` |
| любой sanitizer | primary forbidden; fallback либо catastrophic suppression |
| safe-staging/raw-cleanup/publication defect | primary discarded; fallback with `publication-integrity-failed` |

`failurePhase` в Signature/Regression evidence и primary receipt обязан равняться первой `status=failure` phase в canonical receipt projection; later independent failures сохраняются, но не заменяют first-failure identity.

| Evidence state | Legal evidence | Illegal evidence |
| --- | --- | --- |
| все lane phases и sanitizer/staging/cleanup successful | primary success variant | fallback, partial/null success records |
| Signature restore/assets/verify failure + sanitize success | primary `signature-failure`, exact completed project/package prefix | full success graph/packages, unattempted records |
| Regression upstream restore/build failure + sanitize success | primary `regression-failure`, всегда exact three run records; blocked runs `state=not-attempted` | fake counts/reports for blocked run |
| Regression test failure + sanitize success | failed run и каждый later reachable attempted run | silent omission/suppression of independent run |
| sanitizer/publication recoverable failure | exact one-file fallback | candidate/partial primary bytes |
| bootstrap/quarantine/fallback publication catastrophic failure | no root/path/marker | receipt или uploader authorization |
| post-return tamper | existing marker may remain true; gate false and uploader skipped | fallback recovery promise after finalizer return |

| Regression run `state` | Required values |
| --- | --- |
| `success` | `nativeExitCode=0`; nonnegative integer counts/duration; regenerated TRX/HTML non-null; `failureCode=null`; `skipReason=null` |
| `failure` | `nativeExitCode` сохраняет exact native Int32, включая `0` при report/count defect; available nonnegative counts/duration и regenerated reports retained, unavailable fields null; `failureCode` allowlisted; `skipReason=null`; соответствующая phase имеет native non-zero exit либо synthetic `2` для `test-evidence-failed` |
| `not-attempted` | `nativeExitCode`/counts/duration/reports/failureCode null; `skipReason=prerequisite-failed` |

В lane `Full` outer receipt не объединяет child phases: Signature и Regression child receipts/sub-schemas валидируются независимо, затем outer manifest и `childAttempts` связывают их exact hashes/identities.

Upload tree is closed sanitized JSON/log/TRX/HTML; raw/candidate/scratch data never enters it. Secret env-name matcher first segments separators/camel boundaries, then matches exact segments `TOKEN|SECRET|PASSWORD|PASS|KEY|CREDENTIAL|CREDENTIALS|AUTH|PAT|SAS|COOKIE|CONNECTION`; additionally a whole uppercased name/segment may end only in closed suffix `TOKEN|SECRET|PASSWORD|PASSWD|APIKEY|PRIVATEKEY|CREDENTIAL|CREDENTIALS|CONNECTIONSTRING|CONNECTIONSTRINGS`. This covers `GITHUB_TOKEN`, `ACTIONS_RUNTIME_TOKEN`, `APIKEY`, `GOOGLE_APPLICATION_CREDENTIALS`, `ConnectionString`; explicit negatives `PATH`, `PATHEXT`, `PSModulePath`, `HOMEPATH`, `__COMPAT_LAYER` never seed. Every nonempty match is seed; Git credential headers/material also forbidden.

Trusted wrapper parent snapshots one immutable closed seed set before removing token/command-file environment from children. Values remain only in parent memory and are sent separately to producer and sibling validator through length-prefixed canonical stdin frames; never argv/env/temp/evidence/output, and buffers are zeroed in unconditional `finally`. Each child returns only the ordinal-sorted SHA-256 of non-secret seed variable names inside its bounded result channel; parent compares both hashes with its own. Children never receive/write `GITHUB_OUTPUT`, and validator never re-snapshots environment. Missing/extra identity, value/frame divergence, stdin echo, partial read/write, pipe-close/zeroing/cap failure suppresses authorization. Thus token-env removal preserves scanner input without cross-step persistence or child-owned output.

Secret scanning has exact resource bounds: at most 64 seeds, each UTF-8 seed `1..8192` bytes. Canonical encoder order is percent-upper, percent-lower, form-plus, Base64 standard padded/unpadded, Base64url padded/unpadded, UTF-8 byte-hex lowercase/uppercase, JSON-unicode, XML named, XML decimal, XML hex — exactly 13 transforms. Depth four has theoretical node bound `1+13+13^2+13^3+13^4=30941` per seed and `1980224` for 64 seeds. A generated variant is max 1 MiB UTF-8; aggregate stored generated+decoded bytes are max 4 MiB per seed and 64 MiB global, while cumulative decoder input/output accounting is also max 16 MiB per seed/64 MiB global. Length multiplication is checked before allocation/encoding and the aggregate reservation occurs before insert, so the theoretical node count cannot allocate unbounded memory. Candidate file max 4 MiB, aggregate max 32 MiB and at most 65536 maximal encoded-token runs. Scanner builds deterministic closure in stated order and examines maximal runs alphabet `[A-Za-z0-9%+/_=\\;&#xXuU-]` length `1..8192`; any node/byte/token cap may fail earlier, never truncate. Exact-boundary and `+1` variant/aggregate allocation fixtures are mandatory.

Transforms are recognizer-gated, not blindly required for every word. Percent is explicit only when `%` appears, JSON only with `\u`, XML only with `&...;`; malformed explicit escape/surrogate/entity is recognized-invalid and fail-closed. Base64 and plain byte-hex have no marker: each creates an edge only when alphabet/length/padding is legal (hex is even-length), decoded bytes are valid UTF-8 and canonical re-encode equals the input in its exact lower/upper or padded/unpadded form. Invalid alphabet/padding, non-UTF8 bytes or noncanonical re-encode are `not-applicable`, not fatal, so ordinary SHA and identifiers remain legal. Nonmatching transforms add no edge. Each applicable output is Ordinal-deduplicated/rescanned; found variants become fixed placeholders and whole candidate repeats both scans. A recognized token still decodable after level four is rejected. Any cap overflow, recognized-invalid explicit encoding, binary/NUL, missed representation, absolute path, URI user-info, authorization header, link/reparse point or unexpected extension produces fallback; truncation forbidden. Fixtures include raw lower/upper byte-hex and percent/Base64/hex nesting through four levels.

Raw TRX читается adapter XML parser with DTD/entities/network disabled only for typed run metadata; raw HTML never transfers as markup and adapter never writes candidate tree. Sanitizer builds deterministic minimal TRX and standalone HTML only from allowlisted `runId`, test name, outcome, duration and aggregate counts with mandatory escaping; `StdOut`, `StdErr`, stack traces, attachments, collector data, arbitrary attributes/elements/scripts/styles/links and raw fragments forbidden. Generated reports are reparsed by a closed-schema validator: exact root/element/attribute allowlist, counts arithmetic/floors and no free text. Signature logs similarly derive only from allowlisted phase/package identity, integer exit and fixed diagnostic code. Every candidate file repeats secret/path scan before hashing. A valid raw report is not evidence: sanitizer/rebuild failure still fails `regression:sanitize` and forbids primary.

Raw report bounds apply before parsing: TRX max 32 MiB, raw HTML max 16 MiB, exactly two files per run and 128 MiB whole raw attempt tree. TRX uses forward-only `XmlReader` over a limited stream with DTD prohibited, resolver null, entities zero, depth <=32, elements <=250000, attributes <=1000000, cumulative text <=16 MiB and test-result records <=100000; no DOM load occurs. Raw HTML is size/hash accounted then discarded without markup parsing. Regenerated TRX max 4 MiB and HTML max 4 MiB, depth <=16, elements <=100000, attributes <=400000, cumulative text <=8 MiB and exact test-count cross-link. Any limit reached at `+1`, malformed UTF-8/XML, extra file or bounded-read mismatch is `test-evidence-failed` and still enters finalizer only after readers are joined.

Primary candidate exact-set/cross-link contract is closed. Signature success has exactly `signature/evidence.json` plus six `signature/verify/<ordinal-package-id>.log`; `packages[].verifyLog` uniquely references matching log and `sanitizedLogs` is same six references in package order. Signature failure has evidence JSON plus logs only for exact attempted package prefix; attempted-package refs, sanitizedLogs and manifest logs have equal cardinality/order/set. Signature `diagnostics` equals every failed canonical Signature phase in phase order as unique `{phase,code}`; success has none. Regression success/failure always has `regression/evidence.json`; each success/failure run has exact `regression/<runId>/results.trx` and `.html`, each not-attempted run has null refs and no files. `runs[].trx/html` uniquely reference exact paths. Regression failure `diagnostics` equals every failed canonical Regression phase in phase order as unique `{phase,code}`; missing/extra/reordered/duplicate entries illegal, success has none. No other payload is legal. Every reference occurs exactly once in manifest; referenced/manifest hashes and lengths equal bytes; every manifest payload is referenced with no orphan. Gate reparses sanitized JSON/TRX/HTML/log schemas and reapplies secret/path rules rather than trusting hashes.

`evidenceManifest[].path` — canonical forward-slash relative ASCII path длиной `1..240`; каждый segment имеет длину `1..80` и match `^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?:/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)*$`. Rooted path, backslash, empty/`.`/`..` segment, colon/ADS, hidden segment, control, glob metacharacter, non-ASCII/lookalike, trailing dot/space и Windows device name запрещены. Entries ordinal-sorted и unique одновременно Ordinal и OrdinalIgnoreCase. Receipt текущего root `attempt-receipt.json` никогда не входит в собственный manifest; Full outer manifest, напротив, обязан включить exact nested `signature/attempt-receipt.json` и `regression/attempt-receipt.json`, которые не являются outer receipt.

Containment проверяется только через canonical root и `Path.GetRelativePath` с exact round-trip к manifest path; string-prefix checks запрещены. Каждый ancestor — real directory без link/reparse; каждый leaf и receipt — regular file с native link count exact `1`. Native file identity unique во всём final tree и не совпадает с candidate/scratch/quarantine/raw files. Path, link count, identity, exact set и hashes повторно проверяются до hash, после copy, непосредственно до/после rename и independent gate. Symlink, junction, reparse point, hardlink, duplicate identity или link-count drift дают recoverable `publication-integrity-failed`; если invalid/existing final tree нельзя атомарно quarantine либо удалить, failure catastrophic, marker/path остаются unset.

All publication/fallback/final roots являются siblings под проверенным non-link canonical attempt parent и имеют одинаковую native filesystem identity. Full candidate validation предшествует copy; scratch повторно сверяет manifest, receipt и отсутствие extra/link, затем выполняет same-volume rename и final read-only validation внутри finalizer до возврата marker. Primary tree exact-set равен `{attempt-receipt.json} + evidenceManifest`; fallback exact-set равен только `{attempt-receipt.json}`. Recoverable failures до возврата marker дают fallback; corruption/tamper, обнаруженный внешним gate после возврата marker, считается catastrophic post-publication failure: gate оставляет `safe_upload_verified=false`, валит job и подавляет uploader, не обещая восстановить fallback после выхода finalizer.

Visual planning artifact: Не применимо — UI не меняется.

UI test video evidence: Не применимо — нет нового UI flow; existing Headless automation используется как regression evidence.

Behavior preservation boundary: public/runtime/UI behavior должно остаться неизменным. Любая необходимая code adaptation останавливает EXEC и требует обновления и повторного подтверждения spec.

### 6.3 User-Observable Scenarios
| Scenario | User action / trigger | Expected visible result / output | Evidence required | Covered by AC |
| --- | --- | --- | --- | --- |
| Contributor opens PR | GitHub runs tests | Ubuntu fresh restore проходит с signature verification enabled | signature job log/evidence | NUGET-AC-04..06 |
| Maintainer runs Android/Debian packaging | workflow restore | restore не отключает проверку signatures | workflow scan + CI | NUGET-AC-03, NUGET-AC-09 |
| User opens app/tests exercise startup | Unit/Headless launch ReactiveUI | существующее поведение и startup сохраняются | Unit + Headless x2 | NUGET-AC-07 |
| Stage 3 resumes | PR #280 rebased onto merged prerequisite | Linux/Android restore boundaries use trusted chain; macOS остаётся documented platform exception | exact-SHA Stage 3 gate/CI | Stage3-HO-01 |
| Attempt precondition or sanitizer fails | CI/local gate encounters invalid lane/platform/cache/path or unsafe partial output | non-zero result; artifact is fallback-only receipt, or upload is suppressed if even safe fallback cannot be established | fallback receipt + safe marker assertion | NUGET-AC-08 |
| Native restore/build/test fails after valid preconditions | recorded command returns non-zero | attempt fails, but strictly validated phase-dependent primary diagnostics may be uploaded | primary failure receipt/evidence + gate output | NUGET-AC-08 |
| Receipt/copy/rename/final validation fails inside finalizer | recoverable publication defect is injected | partial tree is quarantined; final result is a one-file fallback receipt | fallback fixture tree + failure-code precedence | NUGET-AC-08 |
| Published tree is altered before uploader | in-wrapper independent validator detects mismatch | wrapper keeps `safe_upload_verified=false`; uploader is skipped even if finalizer marker existed | wrapper frame/output + skipped upload | NUGET-AC-08 |

### 6.4 State / Interaction Matrix
| Current state | Trigger | Expected transition/result | Empty/error/disabled/concurrent case | Notes |
| --- | --- | --- | --- | --- |
| old pins + current CRL | fresh Ubuntu restore | deterministic `NU3012` RED | no retry loop | baseline evidence |
| target pins + empty cache | fresh Ubuntu restore | exact affected subset, unchanged remaining graph, verified signatures | missing/network/CRL failure fails closed | no bypass |
| target pins + mixed assets fixture | verifier | reject | old package cannot coexist | negative fixture |
| CI workflow/script flag false/offline | static scan | reject | comments do not authorize bypass | all tracked CI execution surfaces |
| prerequisite merged | Stage 3 rebase | prior Stage 3 receipts invalidated | full rerun mandatory | no evidence reuse |
| RunAttempt precondition invalid | receipt envelope | record allowlisted failure, skip lane phases, publish fallback-only root | malformed binder input is accepted as raw text; no exception text | preflight fixtures |
| Sanitizer writes partial candidate then fails | publication finalizer | quarantine candidate and publish fallback-only root | final root never receives candidate bytes | two-phase fixtures |
| Recorded Signature native/check phase fails | sanitizer receives incomplete safe inputs | produce strict `signature-failure` primary or fallback if sanitization cannot be proven | success-only graph/package fields forbidden | phase-variant fixtures |
| Finalizer sees both attempt failure and publication defect | fallback-code selector | publication-integrity code wins deterministically | no arbitrary exception/message leakage | precedence fixtures |
| Gate sees marker=true but assertion fails | uploader condition evaluation | upload skipped and job failed | `safe_upload_verified` remains false | assertion fixture |

### 6.5 Decision Ledger
| Decision | Owner | Default / chosen option | Confidence | Risk if assumed | Needs user before EXEC |
| --- | --- | --- | ---: | --- | --- |
| Use separate prerequisite PR | agent | yes | 0.99 | Stage 3 scope pollution otherwise | Нет |
| Update exactly two direct pins | user via this spec approval | `12.0.2` + `23.2.28` | 0.98 | different graph/API scope | Нет — это часть единого approval, не отдельный открытый выбор |
| Enable verification in Android/Debian/Stage 3 Linux jobs | user via this spec approval | explicit `true` | 0.99 | restores remain insecure otherwise | Нет — это часть единого approval, не отдельный открытый выбор |
| Add permanent Ubuntu guard | user via this spec approval | separate tests job + verifier | 0.95 | recurrence might reach packaging CI late | Нет — это часть единого approval, не отдельный открытый выбор |
| Keep Avalonia at `12.0.3` | agent | yes | 0.99 | broader upgrade if changed | Нет |
| Treat network/CRL outage as blocker | agent | fail closed | 0.99 | bypass would weaken supply chain | Нет |
| Publish evidence only after full-tree validation | agent | candidate -> validated publication scratch -> same-volume final root; fallback-only on failure | 1.00 | `if: always()` could upload partial unsafe bytes | Нет |
| Keep receipt outside its own evidence manifest | agent | manifest covers candidate files only; tree is receipt plus manifest entries | 1.00 | self-referential hash cannot be constructed or verified | Нет |
| Distinguish recoverable finalizer failure from post-return tamper | agent | fallback before marker; gate suppression after marker | 0.99 | blanket fallback promise is impossible after external mutation | Нет |
| Enforce new checks on `main` | user | require exact current-commit app-bound checks; `strict=true`, admin/force-push enforcement, enabled no-bypass Main ruleset; preserve reviews and prove merge queue absent/disabled before+after | 0.99 | stale/foreign/context-only or bypass path could satisfy claim; enabling queue without merge_group deadlocks/bypasses checks | **Да — ASK-HUMAN after technical PASS and before approval/EXEC** |

Открытых продуктовых решений нет. Единственное оставшееся operational решение — применять ли полный protected-merge contract (app-bound current-commit required checks + `strict=true` + admin/force-push enforcement + merge-queue-compatible enabled ruleset без bypass actors); оно запрашивается только после technical review PASS и до approval/EXEC. До явного ответа implementation/approval gate остаётся закрытым. Если пользователь отклонит хотя бы один элемент, spec должна честно понизить outcome с permanent merge gate до diagnostic job, обновить NUGET-AC-09/rollout и пройти новый security review; агент не может молча сохранить permanent claim.

Перед изменением snapshot includes branch protection, app-bound checks, reviews/restrictions, force/deletion flags and ruleset ordered rules/bypass/merge-queue parameters. Each required check records current SHA/app/event. Snapshot must prove merge queue absent/disabled; after-state preserves that while strengthening exact approved checks, strict/admin/force/ruleset settings. Queue enabled, stale SHA, foreign/missing app, context-only satisfaction, removed rule or later drift stops delivery.

### 6.6 Runtime / Config / Data Contract Matrix
| Contract area | Current source of truth | Expected change | Compatibility / migration | Verification |
| --- | --- | --- | --- | --- |
| Direct packages | `src/Directory.Packages.props` | two exact pins | no code migration expected | XML/static verifier |
| Transitive graph | NuGet nuspec + `project.assets.json` | exact affected subset with no unrelated drift | runtime regression required | before/after normalized assets + tests |
| Signature trust | immutable nupkg + platform trust/CRL | expected new author certificate | fresh Ubuntu authoritative | fingerprint-bound `dotnet nuget verify --all` |
| Workflow flags | tracked YAML | no false/offline bypass | macOS remains unchanged | all-workflow scan |
| Stage 3 | PR #280 workflow/spec | Android verification true | rebase after prerequisite | full exact-SHA rerun |
| Evidence publication | raw/candidate/scratch/final roots + receipt schema | no direct sanitizer writes to final root; safe marker required | no product data migration | adversarial preflight/publication SelfTest |
| Upload authorization | one `nuget_wrapper` step + independent validator process | require ready/verified and exact step-outcome/verdict pair | uploader skipped on wrapper/gate mismatch | workflow expression fixtures |

Lane/platform/runtime contract is closed; `GITHUB_ACTIONS` читается raw process env и принимает только exact `true` либо отсутствие (`null`):

| Lane | OS | Execution context | Signature authority | Legal result |
| --- | --- | --- | --- | --- |
| `Signature` | Linux | `GITHUB_ACTIONS=true` / `github-actions` | `true`, authoritative for NUGET-AC-04/06 | allowed standalone |
| `Regression` | Windows | `GITHUB_ACTIONS=true` / `github-actions` | `false` | authoritative for NUGET-AC-07 |
| `Regression` | Windows | `GITHUB_ACTIONS=null` / `local` | `false` | local committed-clean preflight allowed |
| `Full` | Windows | `GITHUB_ACTIONS=null` / `local` | `false`; Signature child diagnostic only | local wrapper allowed, cannot satisfy NUGET-AC-04/06 |
| Full child `Signature` | Windows | inherited null / `full-child` | `false` | legal only beneath validated Full wrapper |
| Full child `Regression` | Windows | inherited null / `full-child` | `false` | legal only beneath validated Full wrapper |
| any other tuple | any | any | none | `invalid-platform`; `Full` in CI always rejected |

Receipt runtime is closed: os `linux|windows`, architecture `x64|arm64`, context `github-actions|local|full-child`, SDK matching `^10\.0\.[0-9]+(?:-[0-9A-Za-z.-]+)?$` and exact actual selection of passed absolute muxer under candidate global.json, stable within an attempt/Full children. Baseline records its generator SDK; cross-platform SDK strings may differ, but both graph and byte contract must remain equal. Verification/revocation/authority follow exact table; wrong case/mismatch/context/authority rejected.

## 7. Бизнес-правила / Алгоритмы
1. Security failure не переводится в retry, если package bytes/version не менялись.
2. Affected signed subset принимается только целиком; смешение `19.3.1/19.4.1`, старого ReactiveUI или drift любого иного package id/version запрещено.
3. Splat остаётся transitive; прямой pin потребует новой причины и re-approval.
4. Successful cached Windows restore не заменяет fresh Ubuntu signature verification.
5. `false`, `0` или offline revocation в tracked workflow являются fail независимо от комментария.
6. Старое Stage 3 local/CI evidence до merge prerequisite не может использоваться для финального acceptance.
7. Внешние RunAttempt значения проверяются только после safe receipt bootstrap; известный precondition failure не бросает произвольный exception до fallback.
8. Sanitizer, manifest builder и primary receipt writer не получают final upload root. Final root публикуется только из полностью проверенного same-volume scratch; unsafe/catastrophic state не получает safe marker.
9. Phase names/status/failure codes сравниваются case-sensitive. `success` требует exact integer exit `0` и `failureCode=null`; `failure` требует non-zero integer exit и non-empty allowlisted code; `skipped` требует `exitCode=null` и non-empty allowlisted code. Empty string, wrong case и arbitrary code запрещены.
10. Outcome/fallback precedence детерминирован: catastrophic bootstrap/fallback-publication defect не создаёт receipt/root/marker; явный recoverable sanitizer/manifest/receipt/cleanup/copy/hash/rename/final-validation defect создаёт fallback с `publication-integrity-failed`, перекрывая execution failure, потому что primary отвергнут; иначе установленный outer-boundary flag создаёт fallback с `unexpected-orchestration-failure` без требования complete primary phase projection; иначе incomplete/malformed projection при normal primary validation создаёт `publication-integrity-failed`; иначе precondition fallback сохраняет exact code первой failed canonical precondition. Поэтому accidental incomplete projection не маскирует уже пойманный unexpected exception, но отсутствие unexpected flag не позволяет выдать incomplete projection за orchestration failure. Native/build/test failure сам по себе не запрещает primary: top-level primary outcome выводится из первой failed canonical lane phase, а последующие failures остаются в projection и не перезаписывают его. Caller не передаёт top-level code. Никакой exception text не участвует в receipt.
11. Safe marker является только заявлением finalizer. Upload authorization требует второго independent-validator claim from the same wrapper frame plus exact native-step verdict binding; failure любого claim suppresses uploader.

## 8. Точки интеграции и триггеры
- PR/push/manual trigger `.github/workflows/tests.yml` запускает Ubuntu security job.
- Android and Debian packaging restores наследуют explicit `true`.
- Stage 3 `android_build` после rebase наследует explicit `true`.
- Local EXEC запускает verifier после fresh restore, затем build/Unit/Headless.
- Оба CI wrapper steps экспортируют ready/verified/root/verdict только после independent process validation; pinned uploader требует exact ready+verified and matching `(step outcome, attempt_verdict)` pair.

## 9. Изменения модели данных / состояния
Persisted application data не меняются. Меняются только central package metadata, workflow configuration и validation evidence.

## 10. Миграция / Rollout / Rollback
- Child rollout: technical re-review PASS -> explicit full branch-protection decision -> spec approval -> snapshot current branch/ruleset settings -> TDD verifier RED -> exact package/workflow changes -> local gate -> draft PR -> observe exact current-candidate `Signature`, `Regression`, `AndroidPkg` check runs/app ids -> при разрешении пользователя применить those exact app-bound contexts + admin/force-push/ruleset-no-bypass contract without weakening reviews/dismissal/deletion -> compare before/after settings -> review -> merge without bypass. На merge child-spec завершена.
- Downstream handoff: Stage 3 rebase/full reset gate выполняется и закрывается в Stage 3 spec/roadmap, а не дописывается задним числом как child completion.
- При первом restore NuGet получает новые immutable package versions в обычный cache.
- Rollback на старые versions/disabled verification запрещён: он восстанавливает известный security defect.
- Operational rollback при ошибочно названном required context: оставить PR draft, восстановить сохранённый branch-setting snapshot только на время исправления имени/job и затем снова применить точные успешные contexts, `enforce_admins=true`, force-push prohibition и enabled ruleset без bypass до merge. Нельзя ослаблять review/admin enforcement или мержить в окно без security gate.
- При несовместимости rollback означает остановку и новую подтверждённую remediation spec, а не возврат к revoked chain.

## 11. Тестирование и критерии приёмки

### Acceptance Criteria
- **NUGET-AC-01**: implementation начата только после отдельного approval этой spec; prerequisite diff ограничен exact allowlist.
- **NUGET-AC-02**: central pins равны `ReactiveUI.Avalonia 12.0.2` и `ReactiveUI 23.2.28`; Avalonia и все остальные pins неизменны; прямых `Splat*` pins нет.
- **NUGET-AC-03**: Android/Debian workflows используют `DOTNET_NUGET_SIGNATURE_VERIFICATION: "true"`; raw expected-commit scan exact roots `.github/workflows`, `.github/actions`, `scripts`, `src/Unlimotion.Desktop/ci` даёт непустой regular-blob-only content surface and workflow-rooted invocation closure с восемью обязательными sentinels. Raw bytes берутся только из `EXPECTED_SOURCE_SHA`; stage-0 index mode/object/flags совпадают, replace refs/sparse/assume-unchanged/skip-worktree absent. Content surface свободен от false/0/offline bypass, reachable closure — от unresolved/dynamic/unsupported child, path/mode/link ambiguity and case collisions; unreachable dynamic utility fixture разрешён только после successful content pass.
- **NUGET-AC-04**: fresh Ubuntu restore с empty isolated `NUGET_PACKAGES`, exact `--configfile src/nuget.config --force --no-http-cache -p:DisableImplicitLibraryPacksFolder=true -p:DisableImplicitNuGetFallbackFolder=true -p:RestoreFallbackFolders=` and verification=true проходит; evaluated additional/fallback properties are empty, assets contain exactly intended local+nuget.org sources, one package folder and no ambient/fallback source.
- **NUGET-AC-05**: resolved assets содержат exact six-package affected subset, не содержат superseded versions, а full normalized package graph отличается от parent только шестью ожидаемыми transitions; `DynamicData 9.4.31`, `System.Reactive 6.1.0` и Avalonia baseline сохранены. Recorded-commit package-resolution input manifest exact, ordinal-sorted и missing/extra/hash drift-free.
- **NUGET-AC-06**: все шесть target nupkg проходят fingerprint-bound `dotnet nuget verify --all --certificate-fingerprint 4D2DDD...CFB9`; wrong old/repository fingerprints дают non-zero/`NU3034`.
- **NUGET-AC-07**: Regression lane targeted restore/build выполняет only the two exact test projects; full solution/mobile workloads forbidden. Compound restore success proves exact config/source/package-folder/fallback identity. Unit serial once and Headless serial twice pass with distinct absolute results roots, bare TRX filename, absolute HTML filename, reporter disabled, no command-file/token env and no extra/out-of-root files. Unit floor `>=830`, Headless `>=36`, `passed=discovered`, zero failed/skipped and equal Headless counts. Phase/job deadlines, typed `-1/-2/-3` adapter results, process-tree/worker/reader/writer termination proof and catastrophic suppression contract satisfied; no code/UI adaptations.
- **NUGET-AC-08**: `SelfTest` executes exact 647-case `AC08-*` registry and closed result with `caseCount=647`, `passed=647`, zero failed/skipped and exact ordered IDs. It covers raw trust/cache/config/Full/adapter/TUnit/evidence/runtime/secret/Git/CI/Android/protection/worker/runner/typed-publication/number contracts; any missing/extra/duplicate/skipped/schema mismatch fails. Recoverable publication defects yield validated fallback; catastrophic defects suppress root/marker/gate.
- **NUGET-AC-09**: new required set is exactly the observed current-candidate `Signature`, `Regression`, `AndroidPkg` check-runs, each stored as `{context, observed GitHub Actions app_id, workflowPath, jobId, runUrl, headSha}`. Full raw workflow scan proves each context is emitted by exactly one expected workflow/job path and no other workflow/job duplicates its display context; existing required checks are preserved, CodeQL is not newly required. Strict/admin/force/ruleset protections apply. Merge queue is absent/disabled before+after because tracked workflows have no `merge_group`; queue-enabled state fails/requires new spec. Stale/foreign/context-only/duplicate-producer satisfaction rejected; review protections preserved.
- **NUGET-AC-10**: при API/runtime incompatibility, package verification ambiguity или scope drift выполнение остановлено для re-approval.

Downstream handoff, не child acceptance criterion:

- **Stage3-HO-01**: после merge prerequisite PR #280 rebased; Stage 3 Android flag=true, static negative fixture green, полный local gate и вся native matrix повторены на новом exact SHA. Evidence и completion фиксируются в Stage 3 spec/roadmap.

RED evidence:

- GitHub job `88516063131`: `NU3012`, `Revoked: certificate revoked` на текущей цепочке.
- Перед package/workflow edits новый verifier обязан упасть минимум на старых pins и двух baseline workflow flags `false`.
- Windows non-reproduction не отменяет RED; authoritative affected platform — fresh Ubuntu.

`scripts/Test-NuGetSignatureChain.ps1 -Mode RunAttempt` владеет следующим обязательным контуром; workflow вызывает этот parameter set одной декларативной командой, а `SelfTest` подменяет native command adapter для orchestration negative fixtures:

```powershell
# Canonical orchestration skeleton. Mode may use ValidateSet, but RunAttempt-bound
# lane/SHA/attempt inputs remain raw strings until the receipt envelope exists.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

switch ($Mode) {
    'GenerateBaseline' {
        # Trust is established by the external launcher: this file is a raw blob
        # extracted from an already committed candidate-tooling SHA. Parent checkout
        # bytes and $PSScriptRoot are never repository inputs.
        Assert-GenerateBaselinePreconditions `
            -GeneratorPath $PSCommandPath `
            -RepositoryRoot ([string]$RepositoryRoot) `
            -ExpectedParentSha ([string]$ExpectedParentSha) `
            -PackagesRoot ([string]$PackagesRoot) `
            -OutputPath ([string]$OutputPath)
        Invoke-GenerateBaselineCore `
            -RepositoryRoot ([string]$RepositoryRoot) `
            -ExpectedParentSha ([string]$ExpectedParentSha) `
            -PackagesRoot ([string]$PackagesRoot) `
            -OutputPath ([string]$OutputPath) `
            -DotNetExecutable ([string]$DotNetExecutable) `
            -ConfigFile 'src/nuget.config' `
            -RestoreGlobalProperties @(
                '-p:DisableImplicitLibraryPacksFolder=true',
                '-p:DisableImplicitNuGetFallbackFolder=true',
                '-p:RestoreFallbackFolders='
            )
        return
    }
    'SelfTest' { Invoke-SelfTestMode; return }
    'Worker' {
        # Worker reads one length-prefixed closed frame from stdin. WorkerKind,
        # payload and seed values are not accepted through PSBoundParameters.
        Invoke-ClosedWorkerCliMode -StandardInput ([Console]::OpenStandardInput())
        return
    }
    'RunAttempt' { }
    default { throw 'Unsupported mode.' }
}

# RunAttempt-bound values are nullable raw strings. Do not use ValidateSet, [int],
# or mandatory binder rejection: semantic failures must enter the receipt envelope.
$envelope = $null
$publication = $null
$phaseResults = $null
$expectedPhaseNames = $null
$unexpectedFailureCode = $null
$catastrophicSafetyFailure = $false
$attemptDeadlineUtc = $null
$secretSeedSnapshot = $null
$publicationInput = $null
$repositoryRootCanonical = $null
try {
    $envelope = Start-AttemptEnvelope `
        -LaneText ([string]$Lane) `
        -ExpectedSourceShaText ([string]$ExpectedSourceSha) `
        -RunAttemptText ([string]$RunAttemptText) `
        -RunnerTempRootText ([string]$RunnerTempRoot)
} catch {
    # Catastrophic bootstrap: no upload root/path/output exists, and exception text
    # is neither copied to evidence nor emitted as a workflow output.
    throw 'Safe NuGet attempt bootstrap could not be established.'
}

# Full is a local wrapper, never a combined standalone phase plan. The wrapper owns
# outer preconditions/fallback, two isolated child envelopes and recursive validation.
if ($envelope.NormalizedLane -ceq 'Full') {
    try {
        $publication = Invoke-FullAttemptWrapper `
            -Envelope $envelope `
            -RepositoryRoot ([string]$RepositoryRoot) `
            -ExpectedSourceShaText ([string]$ExpectedSourceSha) `
            -RunAttemptText ([string]$RunAttemptText) `
            -RunnerTempRoot ([string]$RunnerTempRoot) `
            -DotNetExecutable ([string]$DotNetExecutable) `
            -ConfigFile 'src/nuget.config' `
            -RestoreGlobalProperties @(
                '-p:DisableImplicitLibraryPacksFolder=true',
                '-p:DisableImplicitNuGetFallbackFolder=true',
                '-p:RestoreFallbackFolders='
            ) `
            -SignatureDeadlineMinutes 65 `
            -RegressionDeadlineMinutes 95 `
            -WrapperDeadlineMinutes 175 `
            -AggregationTimeoutMinutes 10
    } catch {
        $publication = [pscustomobject]@{
            SafeUploadReady = $false; SafeUploadRoot = $null
            ReceiptKind = $null; AttemptOutcome = 'failure'
        }
    }
    Assert-ClosedPublicationResult `
        -Result $publication -Envelope $envelope -ExpectedLane 'Full' `
        -ReparseReceiptAndCrossLink
    Write-ClosedAttemptResultFrame -Publication $publication
    if ($publication.SafeUploadReady -ceq $false) {
        throw 'Safe Full evidence publication could not be established; upload is forbidden.'
    }
    if ($publication.AttemptOutcome -cne 'success') {
        throw 'Full attempt failed after validated evidence publication.'
    }
    return
}

# One outer recovery boundary starts immediately after the envelope exists.
# Only the envelope and predeclared nullable state may be referenced by finally;
# every property/env/path/plan initialization below is therefore recoverable.
try {
$phaseResults = [System.Collections.Generic.List[object]]::new()
$expectedPhaseNames = @()
# The envelope owns one unique canonical non-link parent. Publication/fallback/final
# are absent-or-empty siblings below it and pass a native same-filesystem identity
# check. Callers cannot provide or override publication paths.
$Lane = $envelope.NormalizedLane
$attemptDeadlineMinutes = if ($Lane -ceq 'Signature') { 65 } else { 95 }
$attemptDeadlineUtc = [DateTimeOffset]::UtcNow.AddMinutes($attemptDeadlineMinutes)
$sourceSha = $envelope.NormalizedSourceSha
$runAttempt = $envelope.NormalizedRunAttempt
$publicationParentRoot = $envelope.PublicationParentRoot
$workRoot = $envelope.WorkRoot
$candidateEvidenceRoot = $envelope.CandidateEvidenceRoot
$receiptScratch = $envelope.ReceiptScratch
$publicationScratch = $envelope.PublicationScratch
$fallbackScratch = $envelope.FallbackScratch
$quarantineRoot = $envelope.QuarantineRoot
$evidenceRoot = $envelope.FinalEvidenceRoot
$bootstrapFailureCode = $envelope.BootstrapFailureCode

$signaturePhasePlan = @(
    'signature:restore:headless', 'signature:assets:headless',
    'signature:restore:desktop', 'signature:assets:desktop',
    'signature:restore:debian', 'signature:assets:debian',
    'signature:verify', 'signature:sanitize'
)
$regressionPhasePlan = @(
    'regression:restore:unit', 'regression:restore:headless',
    'regression:build:unit', 'regression:build:headless',
    'regression:test:unit', 'regression:test:headless-1', 'regression:test:headless-2',
    'regression:sanitize'
)
$expectedPhaseNames = @(
    'attempt:preconditions'
    if ($Lane -ceq 'Signature') { $signaturePhasePlan }
    if ($Lane -ceq 'Regression') { $regressionPhasePlan }
    'attempt:safe-staging'
    'attempt:raw-cleanup'
)

$preconditionFailureCodeAllowlist = @(
    'invalid-lane', 'invalid-platform', 'invalid-run-attempt', 'invalid-source-sha',
    'git-command-failed', 'parent-commit-missing', 'ambiguous-head-sha', 'head-sha-mismatch',
    'source-tree-dirty', 'working-directory-mismatch', 'local-feed-invalid', 'config-file-invalid',
    'packages-root-missing', 'packages-root-invalid', 'packages-root-not-empty',
    'restore-isolation-invalid', 'assets-package-folders-invalid', 'assets-sources-invalid',
    'signature-verification-not-enabled', 'unsafe-revocation-mode',
    'unsafe-path-layout', 'path-overlap', 'path-collision', 'path-link-detected',
    'cross-volume-publication', 'existing-final-root'
)
function Assert-AllowedPhaseFailureCode([string]$Name, [string]$FailureCode) {
    $allowed = switch -Regex -CaseSensitive ($Name) {
        '^attempt:preconditions$' { $preconditionFailureCodeAllowlist; break }
        '^(signature:restore:|regression:restore:)' {
            @('native-command-failed', 'native-command-threw',
              'native-command-timeout', 'native-command-cancelled',
              'native-output-limit-exceeded', 'restore-evidence-failed'); break
        }
        '^regression:build:' {
            @('native-command-failed', 'native-command-threw',
              'native-command-timeout', 'native-command-cancelled',
              'native-output-limit-exceeded'); break
        }
        '^regression:test:' {
            @('native-command-failed', 'native-command-threw',
              'native-command-timeout', 'native-command-cancelled',
              'native-output-limit-exceeded', 'test-evidence-failed'); break
        }
        '^signature:assets:' { @('assets-evidence-failed'); break }
        '^signature:verify$' {
            @('signature-verification-failed', 'native-command-threw',
              'native-command-timeout', 'native-command-cancelled',
              'native-output-limit-exceeded'); break
        }
        '^signature:sanitize$' {
            @('signature-sanitization-failed', 'native-command-threw',
              'native-command-timeout', 'native-command-cancelled',
              'native-output-limit-exceeded'); break
        }
        '^regression:sanitize$' {
            @('regression-sanitization-failed', 'native-command-threw',
              'native-command-timeout', 'native-command-cancelled',
              'native-output-limit-exceeded'); break
        }
        '^attempt:safe-staging$' { @('safe-staging-failed'); break }
        '^attempt:raw-cleanup$' { @('raw-cleanup-failed'); break }
        default { throw "No failure-code contract for phase: $Name" }
    }
    if ($FailureCode -cnotin $allowed) { throw "Invalid failure code for phase: $Name" }
}
function Add-PhaseResult(
    [string]$Name,
    [string]$Status,
    [AllowNull()][object]$ExitCode,
    [AllowNull()][object]$FailureCode
) {
    $index = $phaseResults.Count
    if ($index -ge $expectedPhaseNames.Count -or $expectedPhaseNames[$index] -cne $Name) {
        throw "Unexpected or out-of-order phase: $Name"
    }
    if ($Status -cnotin @('success', 'failure', 'skipped')) { throw "Invalid phase status: $Status" }
    $isInt32 = $null -ne $ExitCode -and $ExitCode.GetType() -eq [int]
    $hasFailureCode = $FailureCode -is [string] -and $FailureCode.Length -gt 0
    switch -CaseSensitive ($Status) {
        'success' {
            if (-not $isInt32 -or $ExitCode -ne 0 -or $null -ne $FailureCode) {
                throw "Invalid phase tuple: $Name/$Status"
            }
        }
        'failure' {
            if (-not $isInt32 -or $ExitCode -eq 0 -or -not $hasFailureCode) {
                throw "Invalid phase tuple: $Name/$Status"
            }
            Assert-AllowedPhaseFailureCode -Name $Name -FailureCode $FailureCode
        }
        'skipped' {
            if ($null -ne $ExitCode -or -not $hasFailureCode -or $FailureCode -cne 'prerequisite-failed') {
                throw "Invalid phase tuple: $Name/$Status"
            }
        }
    }
    Assert-LegalPhaseTransition `
        -Lane $Lane `
        -ExistingResults ([object[]]$phaseResults.ToArray()) `
        -NextName $Name -NextStatus $Status -NextFailureCode $FailureCode
    [void]$phaseResults.Add([ordered]@{
        name = $Name; status = $Status; exitCode = $ExitCode; failureCode = $FailureCode
    })
}
function Add-SkippedPhase([string]$Name) {
    Add-PhaseResult -Name $Name -Status 'skipped' -ExitCode $null -FailureCode 'prerequisite-failed'
}
function Invoke-RecordedNative(
    [string]$Name,
    [string]$File,
    [string[]]$Arguments,
    [string]$RawLogPath,
    [int]$TimeoutMinutes,
    [AllowNull()][string]$PostSuccessValidationKind = $null,
    [AllowNull()][System.Collections.IDictionary]$PostSuccessValidation = $null
) {
    # The adapter emits only a typed result. It owns 60-second safe heartbeats,
    # graceful interrupt, the 10-second process-tree kill window and cancellation.
    $result = Invoke-NativeCommandAdapter `
        -File $File -Arguments $Arguments -WorkingDirectory ([string]$RepositoryRoot) `
        -RawLogPath $RawLogPath `
        -RemoveChildEnvironment @(
            'GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_STATE',
            'GITHUB_STEP_SUMMARY', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_RESULTS_URL'
        ) `
        -StdoutByteLimit 4194304 -StderrByteLimit 4194304 `
        -CombinedOutputByteLimit 8388608 `
        -TimeoutMinutes $TimeoutMinutes `
        -AttemptDeadlineUtc $attemptDeadlineUtc `
        -ReserveMinutes 10
    Assert-ClosedNativeAdapterResult -Result $result
    if ($null -eq $result -or $result.NativeExitCode -isnot [int] -or
        $result.Status -cnotin @('success', 'failure') -or
        $result.TerminationProven -isnot [bool]) {
        throw 'Native adapter returned an untyped exit code.'
    }
    if ($result.TerminationProven -isnot [bool] -or -not $result.TerminationProven) {
        throw [EvidenceSafetyCatastrophicException]::new(
            'Native process tree or writer termination was not proven.')
    }
    if ($result.Status -ceq 'success') {
        if ($result.NativeExitCode -ne 0 -or $null -ne $result.FailureCode) {
            throw 'Native success tuple is invalid.'
        }
        if ($null -ne $PostSuccessValidationKind) {
            if ($PostSuccessValidationKind -cne 'RestoreAssetsIdentity' -or
                $null -eq $PostSuccessValidation) {
                throw 'Native post-success validation request is invalid.'
            }
            try {
                Assert-RootAssetsIdentity @PostSuccessValidation
            } catch {
                Add-PhaseResult -Name $Name -Status 'failure' `
                    -ExitCode 2 -FailureCode 'restore-evidence-failed'
                return $false
            }
        }
        Add-PhaseResult -Name $Name -Status 'success' -ExitCode 0 -FailureCode $null
        return $true
    }
    if ($result.NativeExitCode -eq 0 -or $result.FailureCode -isnot [string] -or
        ([string]$result.FailureCode).Length -eq 0) {
        throw 'Native failure tuple is invalid.'
    }
    if (($result.FailureCode -ceq 'native-output-limit-exceeded') -ne
        ($result.NativeExitCode -eq -3)) {
        throw 'Native output-limit tuple is invalid.'
    }
    Add-PhaseResult -Name $Name -Status 'failure' `
        -ExitCode $result.NativeExitCode -FailureCode ([string]$result.FailureCode)
    return $false
}
function Invoke-RecordedTest(
    [string]$Name,
    [string]$Project,
    [string]$RunId,
    [string]$ResultsRoot,
    [int]$MinimumExpectedTests,
    [int]$TimeoutMinutes
) {
    # Adapter runs the native test, retains raw reports only below workRoot and parses
    # typed counts/paths. It never rebuilds reports or writes candidateEvidenceRoot.
    $result = Invoke-TestCommandAdapter `
        -DotNetExecutable $DotNetExecutable `
        -WorkingDirectory ([string]$RepositoryRoot) `
        -Project $Project -RunId $RunId -ResultsRoot $ResultsRoot `
        -TrxFileName 'results.trx' `
        -HtmlFilePath (Join-Path $ResultsRoot 'results.html') `
        -DisableGitHubReporter $true `
        -RemoveChildEnvironment @(
            'GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_STATE',
            'GITHUB_STEP_SUMMARY', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_RESULTS_URL'
        ) `
        -StdoutByteLimit 4194304 -StderrByteLimit 4194304 `
        -CombinedOutputByteLimit 8388608 `
        -TrxByteLimit 33554432 -HtmlByteLimit 16777216 `
        -MinimumExpectedTests $MinimumExpectedTests `
        -TimeoutMinutes $TimeoutMinutes `
        -AttemptDeadlineUtc $attemptDeadlineUtc `
        -ReserveMinutes 10
    Assert-ClosedTestAdapterResult -Result $result
    if ($null -eq $result -or $result.NativeExitCode -isnot [int] -or
        $result.Status -cnotin @('success', 'failure') -or
        $result.TerminationProven -isnot [bool]) {
        throw 'Test adapter returned an untyped native exit code.'
    }
    if ($result.TerminationProven -isnot [bool] -or -not $result.TerminationProven) {
        throw [EvidenceSafetyCatastrophicException]::new(
            'Test process tree or writer termination was not proven.')
    }
    if ($result.Status -ceq 'success') {
        if ($result.NativeExitCode -ne 0 -or $null -ne $result.FailureCode) {
            throw 'Test success tuple is invalid.'
        }
        [void]$testRunResults.Add($result)
        Add-PhaseResult -Name $Name -Status 'success' -ExitCode 0 -FailureCode $null
        return $true
    }
    if ($result.FailureCode -isnot [string] -or ([string]$result.FailureCode).Length -eq 0) {
        throw 'Test failure tuple is invalid.'
    }
    if (($result.FailureCode -ceq 'native-output-limit-exceeded') -ne
        ($result.NativeExitCode -eq -3)) {
        throw 'Test output-limit tuple is invalid.'
    }
    [void]$testRunResults.Add($result)
    $nativeExitCode = $result.NativeExitCode
    $phaseExitCode = if ($nativeExitCode -ne 0) { $nativeExitCode } else { 2 }
    $phaseFailureCode = if ($nativeExitCode -ne 0) {
        [string]$result.FailureCode
    } else { 'test-evidence-failed' }
    Add-PhaseResult -Name $Name -Status 'failure' `
        -ExitCode $phaseExitCode -FailureCode $phaseFailureCode
    return $false
}
function Invoke-RecordedAssetsCheck(
    [string]$Name,
    [string]$AssetsPath,
    [string]$ProjectPath,
    [string]$ProjectReceiptPath,
    [string]$CopyPath
) {
    try {
        Assert-RootAssetsIdentity `
            -AssetsPath $AssetsPath `
            -ExpectedProjectPath $ProjectPath `
            -ExpectedProjectReceiptPath $ProjectReceiptPath `
            -ExpectedPackagesRoot $env:NUGET_PACKAGES `
            -ExpectedConfigFile $configFilePath
        if (Test-Path -LiteralPath $CopyPath) { throw 'Assets copy collision.' }
        Copy-Item -LiteralPath $AssetsPath -Destination $CopyPath
        [void]$assetsPaths.Add($CopyPath)
        Add-PhaseResult -Name $Name -Status 'success' -ExitCode 0 -FailureCode $null
        return $true
    } catch {
        Add-PhaseResult -Name $Name -Status 'failure' -ExitCode 1 `
            -FailureCode 'assets-evidence-failed'
        return $false
    }
}
function Invoke-RecordedWorker(
    [string]$Name,
    [string]$FailureCode,
    [string]$WorkerKind,
    [System.Collections.IDictionary]$Payload,
    [int]$TimeoutMinutes,
    [int]$ReserveMinutes
) {
    # Same raw-extracted expected-commit script executes in a fresh external process.
    # Payload + pre-scrub secret seeds are length-prefixed canonical JSON on stdin;
    # caller locals/functions, argv/env/temp files and profiles are not worker inputs.
    $workerInput = $null
    try {
        $workerInput = New-ClosedWorkerInput `
            -WorkerKind $WorkerKind -Payload $Payload `
            -SecretSeedSnapshot $secretSeedSnapshot
        $result = Invoke-ClosedWorkerProcessAdapter `
            -ScriptPath $PSCommandPath `
            -ExpectedScriptObjectId $envelope.ProducerObjectId `
            -Mode 'Worker' -WorkerKind $WorkerKind `
            -CanonicalStandardInputBytes $workerInput `
            -WorkingDirectory ([string]$RepositoryRoot) `
            -RemoveChildEnvironment @(
                'GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_STATE',
                'GITHUB_STEP_SUMMARY', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_RESULTS_URL'
            ) `
            -StdoutByteLimit 1048576 -StderrByteLimit 16384 `
            -TimeoutMinutes $TimeoutMinutes `
            -AttemptDeadlineUtc $attemptDeadlineUtc `
            -ReserveMinutes $ReserveMinutes
    } finally {
        if ($null -ne $workerInput) { Clear-ByteArray $workerInput }
    }
    Assert-ClosedWorkerAdapterResult -Result $result
    if ($null -eq $result -or $result.ExitCode -isnot [int] -or
        $result.Success -isnot [bool] -or $result.TerminationProven -isnot [bool]) {
        throw 'Worker adapter returned an untyped result.'
    }
    if ($result.TerminationProven -isnot [bool] -or -not $result.TerminationProven) {
        throw [EvidenceSafetyCatastrophicException]::new(
            'Worker process tree or reader/writer termination was not proven.')
    }
    if ($result.Success -ceq $true) {
        if ($result.ExitCode -ne 0 -or $null -ne $result.FailureCode) {
            throw 'Worker success tuple is invalid.'
        }
        Add-PhaseResult -Name $Name -Status 'success' -ExitCode 0 -FailureCode $null
        return $true
    }
    if ($result.ExitCode -eq 0 -or $result.FailureCode -isnot [string] -or
        ([string]$result.FailureCode).Length -eq 0) {
        throw 'Worker failure tuple is invalid.'
    }
    $recordedFailureCode = switch -CaseSensitive ([string]$result.FailureCode) {
        'worker-failed' { $FailureCode; break }
        'native-command-timeout' { 'native-command-timeout'; break }
        'native-command-cancelled' { 'native-command-cancelled'; break }
        'native-command-threw' { 'native-command-threw'; break }
        'native-output-limit-exceeded' { 'native-output-limit-exceeded'; break }
        default { throw 'Worker adapter failure code is invalid.' }
    }
    if ($recordedFailureCode -cne $FailureCode -and
        (($recordedFailureCode -ceq 'native-command-timeout' -and $result.ExitCode -ne -1) -or
         ($recordedFailureCode -ceq 'native-command-cancelled' -and $result.ExitCode -ne -1) -or
         ($recordedFailureCode -ceq 'native-command-threw' -and $result.ExitCode -ne -2) -or
         ($recordedFailureCode -ceq 'native-output-limit-exceeded' -and $result.ExitCode -ne -3))) {
        throw 'Worker adapter exit/code mapping is invalid.'
    }
    Add-PhaseResult -Name $Name -Status 'failure' `
        -ExitCode ([int]$result.ExitCode) -FailureCode $recordedFailureCode
    return $false
}
function Invoke-RecordedPreconditions {
    $result = if ($bootstrapFailureCode) {
        [pscustomobject]@{ Success = $false; FailureCode = $bootstrapFailureCode }
    } else {
        Test-RunAttemptPreconditions `
            -Lane $Lane `
            -RepositoryRoot ([string]$RepositoryRoot) `
            -ExpectedSourceSha $sourceSha `
            -ExpectedParentSha 'e11cae9a086ddd4fd97105f00b67bedf05f92700' `
            -RunAttempt $runAttempt `
            -LocalFeedRoot 'artifacts/nuget-local' `
            -ConfigFile 'src/nuget.config' `
            -DotNetExecutableText ([string]$DotNetExecutable) `
            -RestoreProjectPaths (Get-LaneRestoreProjectPaths -Lane $Lane) `
            -RestoreGlobalProperties @(
                '-p:DisableImplicitLibraryPacksFolder=true',
                '-p:DisableImplicitNuGetFallbackFolder=true',
                '-p:RestoreFallbackFolders='
            ) `
            -RequiredEmptyEvaluatedProperties @(
                'RestoreAdditionalProjectSources',
                'RestoreAdditionalProjectFallbackFolders',
                'RestoreFallbackFolders'
            ) `
            -PackagesRootText $packagesRootText `
            -SignatureVerificationText $signatureVerificationText `
            -RevocationModeText $revocationModeText `
            -PublicationParentRoot $publicationParentRoot `
            -WorkRoot $workRoot `
            -CandidateEvidenceRoot $candidateEvidenceRoot `
            -ReceiptScratch $receiptScratch `
            -PublicationScratch $publicationScratch `
            -FallbackScratch $fallbackScratch `
            -QuarantineRoot $quarantineRoot `
            -FinalEvidenceRoot $evidenceRoot
    }
    Assert-ClosedPreconditionResult -Result $result
    if ($result.Success -ceq $false) {
        Assert-AllowlistedAttemptFailureCode -FailureCode $result.FailureCode
        Add-PhaseResult -Name 'attempt:preconditions' -Status 'failure' -ExitCode 1 -FailureCode $result.FailureCode
        return $false
    }
    if ($result.Success -cne $true -or $null -ne $result.FailureCode -or
        $result.CanonicalPackagesRoot -isnot [string]) {
        throw 'Precondition success result is malformed.'
    }
    $env:NUGET_PACKAGES = $result.CanonicalPackagesRoot
    Add-PhaseResult -Name 'attempt:preconditions' -Status 'success' -ExitCode 0 -FailureCode $null
    return $true
}

$secretSeedSnapshot = Get-ClosedSecretSeedSnapshot -ProcessEnvironment
$packagesRootText = [Environment]::GetEnvironmentVariable('NUGET_PACKAGES', 'Process')
$signatureVerificationText = [Environment]::GetEnvironmentVariable(
    'DOTNET_NUGET_SIGNATURE_VERIFICATION', 'Process')
$revocationModeText = [Environment]::GetEnvironmentVariable(
    'NUGET_CERT_REVOCATION_MODE', 'Process')

$repositoryRootCanonical = [IO.Path]::GetFullPath([string]$RepositoryRoot)
$configFilePath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'src/nuget.config'))
$restorePlan = @(
    @{ Id = 'headless'; ProjectRelative = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'; ProjectPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj')); AssetsPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'tests/Unlimotion.UiTests.Headless/obj/project.assets.json')) },
    @{ Id = 'desktop'; ProjectRelative = 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj'; ProjectPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'src/Unlimotion.Desktop/Unlimotion.Desktop.csproj')); AssetsPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'src/Unlimotion.Desktop/obj/project.assets.json')) },
    @{ Id = 'debian'; ProjectRelative = 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj'; ProjectPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'src/Unlimotion.Desktop/Unlimotion.Desktop.ForDebianBuild.csproj')); AssetsPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'src/Unlimotion.Desktop/obj/project.assets.json')) }
)
$assetsRoot = Join-Path $workRoot 'assets'
$rawLogRoot = Join-Path $workRoot 'raw-logs'
$rawRegressionRoot = Join-Path $workRoot 'regression'
$assetsPaths = [System.Collections.Generic.List[string]]::new()
$testRunResults = [System.Collections.Generic.List[object]]::new()
$preconditionsReady = $false

    $preconditionsReady = Invoke-RecordedPreconditions
    if (-not $preconditionsReady) {
        $lanePlan = switch -CaseSensitive ($Lane) {
            'Signature' { $signaturePhasePlan; break }
            'Regression' { $regressionPhasePlan; break }
            default { @() }
        }
        foreach ($phaseName in $lanePlan) { Add-SkippedPhase $phaseName }
    }
    if ($preconditionsReady) {
        New-Item -ItemType Directory -Path $assetsRoot, $rawLogRoot, $rawRegressionRoot -ErrorAction Stop | Out-Null
    }
    if ($preconditionsReady -and $Lane -ceq 'Signature') {
        $signatureReady = $true
        foreach ($item in $restorePlan) {
            $restorePhase = "signature:restore:$($item.Id)"
            $assetsPhase = "signature:assets:$($item.Id)"
            if (-not $signatureReady) {
                Add-SkippedPhase $restorePhase
                Add-SkippedPhase $assetsPhase
                continue
            }
            $signatureReady = Invoke-RecordedNative $restorePhase $DotNetExecutable `
                @('restore', $item.ProjectPath, '--force', '--no-http-cache',
                  '--configfile', $configFilePath,
                  '-p:DisableImplicitLibraryPacksFolder=true',
                  '-p:DisableImplicitNuGetFallbackFolder=true',
                  '-p:RestoreFallbackFolders=') `
                (Join-Path $rawLogRoot "$($item.Id)-restore.log") 10
            if (-not $signatureReady) { Add-SkippedPhase $assetsPhase; continue }
            $copyPath = Join-Path $assetsRoot "$($item.Id).project.assets.json"
            $signatureReady = Invoke-RecordedAssetsCheck `
                -Name $assetsPhase -AssetsPath $item.AssetsPath `
                -ProjectPath $item.ProjectPath `
                -ProjectReceiptPath $item.ProjectRelative -CopyPath $copyPath
        }
        if ($signatureReady) {
            [void](Invoke-RecordedWorker 'signature:verify' 'signature-verification-failed' `
                'SignatureVerify' ([ordered]@{
                    dotnetExecutable = $DotNetExecutable
                    repositoryRoot = $repositoryRootCanonical
                    assetsPaths = [string[]]$assetsPaths.ToArray()
                    expectedAssetsIdentity = @('headless', 'desktop', 'debian')
                    baselineGraphPath = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'distribution/fixtures/reactiveui-signature-chain-baseline.json'))
                    packagesRoot = $env:NUGET_PACKAGES
                    rawRoot = $workRoot
                }) 20 10)
        } else {
            Add-SkippedPhase 'signature:verify'
        }
        [void](Invoke-RecordedWorker 'signature:sanitize' 'signature-sanitization-failed' `
            'SignatureSanitize' ([ordered]@{
                rawRoot = $workRoot
                candidateEvidenceRoot = $candidateEvidenceRoot
                phaseResults = [object[]]$phaseResults.ToArray()
            }) 5 5)
    }

    if ($preconditionsReady -and $Lane -ceq 'Regression') {
        $unitProjectRelative = 'src/Unlimotion.Test/Unlimotion.Test.csproj'
        $headlessProjectRelative = 'tests/Unlimotion.UiTests.Headless/Unlimotion.UiTests.Headless.csproj'
        $unitProject = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical $unitProjectRelative))
        $headlessProject = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical $headlessProjectRelative))
        $unitAssets = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'src/Unlimotion.Test/obj/project.assets.json'))
        $headlessAssets = [IO.Path]::GetFullPath((Join-Path $repositoryRootCanonical 'tests/Unlimotion.UiTests.Headless/obj/project.assets.json'))

        $unitReady = Invoke-RecordedNative `
            -Name 'regression:restore:unit' -File $DotNetExecutable `
            -Arguments @('restore', $unitProject, '--force', '--no-http-cache',
              '--configfile', $configFilePath,
              '-p:DisableImplicitLibraryPacksFolder=true',
              '-p:DisableImplicitNuGetFallbackFolder=true',
              '-p:RestoreFallbackFolders=') `
            -RawLogPath (Join-Path $rawLogRoot 'unit-restore.log') -TimeoutMinutes 10 `
            -PostSuccessValidationKind 'RestoreAssetsIdentity' `
            -PostSuccessValidation ([ordered]@{
                AssetsPath = $unitAssets
                ExpectedProjectPath = $unitProject
                ExpectedProjectReceiptPath = $unitProjectRelative
                ExpectedPackagesRoot = $env:NUGET_PACKAGES
                ExpectedConfigFile = $configFilePath
            })
        $headlessReady = Invoke-RecordedNative `
            -Name 'regression:restore:headless' -File $DotNetExecutable `
            -Arguments @('restore', $headlessProject, '--force', '--no-http-cache',
              '--configfile', $configFilePath,
              '-p:DisableImplicitLibraryPacksFolder=true',
              '-p:DisableImplicitNuGetFallbackFolder=true',
              '-p:RestoreFallbackFolders=') `
            -RawLogPath (Join-Path $rawLogRoot 'headless-restore.log') -TimeoutMinutes 10 `
            -PostSuccessValidationKind 'RestoreAssetsIdentity' `
            -PostSuccessValidation ([ordered]@{
                AssetsPath = $headlessAssets
                ExpectedProjectPath = $headlessProject
                ExpectedProjectReceiptPath = $headlessProjectRelative
                ExpectedPackagesRoot = $env:NUGET_PACKAGES
                ExpectedConfigFile = $configFilePath
            })

        if ($unitReady) {
            $unitReady = Invoke-RecordedNative 'regression:build:unit' $DotNetExecutable `
                @('build', $unitProject, '-c', 'Debug', '--no-restore',
                  '-p:UseSharedCompilation=false') `
                (Join-Path $rawLogRoot 'unit-build.log') 10
        } else { Add-SkippedPhase 'regression:build:unit' }
        if ($headlessReady) {
            $headlessReady = Invoke-RecordedNative 'regression:build:headless' $DotNetExecutable `
                @('build', $headlessProject, '-c', 'Debug', '--no-restore',
                  '-p:UseSharedCompilation=false') `
                (Join-Path $rawLogRoot 'headless-build.log') 10
        } else { Add-SkippedPhase 'regression:build:headless' }

        if ($unitReady) {
            $unitResults = Join-Path $rawRegressionRoot 'unit'
            New-Item -ItemType Directory -Path $unitResults -ErrorAction Stop | Out-Null
            [void](Invoke-RecordedTest 'regression:test:unit' $unitProject `
                'unit' $unitResults 830 20)
        } else { Add-SkippedPhase 'regression:test:unit' }

        if ($headlessReady) {
            $headless1Results = Join-Path $rawRegressionRoot 'headless-1'
            New-Item -ItemType Directory -Path $headless1Results -ErrorAction Stop | Out-Null
            # Headless-2 remains reachable even when Headless-1 records failure.
            [void](Invoke-RecordedTest 'regression:test:headless-1' $headlessProject `
                'headless-1' $headless1Results 36 10)
        } else { Add-SkippedPhase 'regression:test:headless-1' }
        if ($headlessReady) {
            $headless2Results = Join-Path $rawRegressionRoot 'headless-2'
            New-Item -ItemType Directory -Path $headless2Results -ErrorAction Stop | Out-Null
            [void](Invoke-RecordedTest 'regression:test:headless-2' $headlessProject `
                'headless-2' $headless2Results 36 10)
        } else { Add-SkippedPhase 'regression:test:headless-2' }

        [void](Invoke-RecordedWorker 'regression:sanitize' 'regression-sanitization-failed' `
            'RegressionSanitize' ([ordered]@{
                rawResultsRoot = $rawRegressionRoot
                runIds = @('unit', 'headless-1', 'headless-2')
                runResults = [object[]]$testRunResults.ToArray()
                phaseResults = [object[]]$phaseResults.ToArray()
                candidateEvidenceRoot = $candidateEvidenceRoot
            }) 5 5)
    }
} catch [EvidenceSafetyCatastrophicException] {
    $catastrophicSafetyFailure = $true
} catch {
    $unexpectedFailureCode = 'unexpected-orchestration-failure'
} finally {
    # This is the single post-envelope publication boundary. The finalizer owns
    # candidate validation, raw cleanup, receipt, fallback construction, atomic
    # promotion and final validation. An unexpected flag selects its fallback without
    # requiring a primary projection; only a normal/no-flag incomplete projection
    # selects publication-integrity-failed.
    try {
        $finalizerPhaseNames = if ($null -eq $expectedPhaseNames) {
            $null
        } else { [string[]]$expectedPhaseNames }
        $finalizerPhaseResults = if ($null -eq $phaseResults) {
            $null
        } else { [object[]]$phaseResults.ToArray() }
        try {
            $publicationInput = New-ClosedWorkerInput `
                -WorkerKind 'PublicationFinalize' `
                -Payload ([ordered]@{
                    envelope = $envelope
                    expectedPhaseNames = $finalizerPhaseNames
                    partialPhaseResults = $finalizerPhaseResults
                    unexpectedFailureCode = $unexpectedFailureCode
                    catastrophicSafetyFailure = $catastrophicSafetyFailure
                    attemptDeadlineUtc = $attemptDeadlineUtc
                }) `
                -SecretSeedSnapshot $secretSeedSnapshot
            $publication = Invoke-ClosedPublicationWorker `
                -ScriptPath $PSCommandPath `
                -ExpectedScriptObjectId $envelope.ProducerObjectId `
                -CanonicalStandardInputBytes $publicationInput `
                -WorkingDirectory $repositoryRootCanonical `
                -TimeoutMinutes 5 -KillGraceSeconds 10 `
                -StdoutByteLimit 1048576 -StderrByteLimit 16384 `
                -RemoveChildEnvironment @(
                    'GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH', 'GITHUB_STATE',
                    'GITHUB_STEP_SUMMARY', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_RESULTS_URL'
                )
        } finally {
            if ($null -ne $publicationInput) { Clear-ByteArray $publicationInput }
        }
    } catch {
        # Only catastrophic inability to establish/validate any safe tree reaches
        # this branch. Never expose the exception, candidate path or partial root.
        $publication = [pscustomobject]@{
            SafeUploadReady = $false
            SafeUploadRoot = $null
            ReceiptKind = $null
            AttemptOutcome = 'failure'
        }
    }
}

# PublicationFinalize worker returns ready only after validating an exact primary
# tree ({receipt}+manifest entries) or exact one-receipt fallback tree. It derives
# failure codes with fixed precedence: publication safety; unexpected orchestration
# without projection completion; then no-flag projection validation and first failed
# canonical phase. Receipt JSON is re-read with System.Text.Json so
# Number/TryGetInt32, Null and String types cannot be accepted through PS coercion.
try {
    if ($null -ne $secretSeedSnapshot) { Clear-SecretSeedSnapshot $secretSeedSnapshot }
} catch {
    $publication = [pscustomobject]@{
        SafeUploadReady = $false; SafeUploadRoot = $null
        ReceiptKind = $null; AttemptOutcome = 'failure'
    }
}
Assert-ClosedPublicationResult `
    -Result $publication -Envelope $envelope -ExpectedLane $Lane `
    -ReparseReceiptAndCrossLink
Write-ClosedAttemptResultFrame -Publication $publication
if ($publication.SafeUploadReady -ceq $false) {
    throw 'Safe evidence publication could not be established; upload is forbidden.'
}
if ($publication.AttemptOutcome -cne 'success') {
    throw "NuGet signature-chain attempt failed after validated evidence publication."
}
```

`SelfTest` исполняет тот же dispatcher/functions in-process с подменёнными native/filesystem adapters. Case registry является static source of truth: порядок строк и slugs в таблице canonical, generated full IDs unique, каждый range ниже непрерывен, а перечисленные case slugs соответствуют номерам слева направо.

| Stable ID range | Count | Ordered case slugs |
| --- | ---: | --- |
| `AC08-BOOT-001..026` | 26 | `raw-lane-null`, `raw-lane-empty`, `raw-lane-wrong-case`, `raw-lane-arbitrary`, `attempt-null`, `attempt-empty`, `attempt-nonnumeric`, `attempt-zero`, `attempt-int32-overflow`, `sha-null`, `sha-malformed`, `git-command-failed`, `parent-commit-missing`, `ambiguous-head`, `head-mismatch`, `local-feed-invalid`, `signature-wrong-platform`, `regression-wrong-platform`, `full-in-ci`, `unsafe-path-layout`, `path-overlap`, `path-collision`, `path-link-detected`, `cross-volume-publication`, `existing-final-root`, `bootstrap-catastrophic` |
| `AC08-ENV-001..012` | 12 | `verification-unset`, `verification-false`, `verification-zero`, `verification-wrong-case`, `revocation-empty`, `revocation-offline`, `packages-missing`, `packages-invalid`, `packages-nonempty`, `retry-env-only-ignored`, `nonterminating-write-error`, `post-envelope-init-failure` |
| `AC08-CI-001..018` | 18 | `empty-surface`, `missing-sentinel`, `yaml-extension`, `key-wrong-case`, `block-scalar`, `quoted-false`, `unquoted-zero`, `offline-shell-forms`, `stage-nonzero`, `mode-symlink`, `mode-gitlink`, `control-path`, `nonascii-path`, `casefold-path-collision`, `unscanned-invoked-child`, `desktop-ci-child-bypass`, `powershell-revspec-tokenization`, `submodules-enabled` |
| `AC08-BASE-001..012` | 12 | `malformed-tree-record`, `malformed-index-record`, `selected-stage-nonzero`, `selected-mode-symlink`, `selected-mode-gitlink`, `selected-tree-index-disagreement`, `selected-object-mismatch`, `selected-hash-drift`, `baseline-unknown-property`, `baseline-wrong-type`, `baseline-case-collision`, `unrelated-gitlink-allowed` |
| `AC08-GRAPH-001..009` | 9 | `downgrade`, `mixed-graph`, `unrelated-drift`, `duplicate-reactiveui-pin`, `direct-splat-pin`, `missing-package`, `disabled-verification`, `old-author-fingerprint`, `repository-signer-fingerprint` |
| `AC08-JSON-001..058` | 58 | `duplicate-top-level`, `unknown-top-level`, `wrong-case-top-level`, `unicode-lookalike-top-level`, `duplicate-nested`, `unknown-nested`, `schema-version-string`, `schema-version-float`, `exit-code-string`, `exit-code-float`, `exit-code-out-of-range`, `tuple-nullability`, `status-wrong-case`, `failure-code-wrong-case`, `failure-code-empty`, `failure-code-arbitrary`, `discriminator-mismatch`, `success-with-failure-property`, `failure-with-success-property`, `fallback-with-primary-property`, `fallback-invalid-lane-nullability`, `fallback-invalid-sha-nullability`, `fallback-invalid-attempt-nullability`, `fallback-multi-invalid-precedence`, `exit-code-exponent`, `run-attempt-string`, `run-attempt-float`, `run-attempt-exponent`, `run-attempt-out-of-range`, `byte-length-string`, `byte-length-float`, `byte-length-exponent`, `byte-length-negative`, `byte-length-mismatch`, `byte-length-out-of-range`, `native-exit-code-string`, `native-exit-code-float`, `native-exit-code-exponent`, `native-exit-code-out-of-range`, `count-negative`, `count-exponent`, `count-out-of-range`, `duration-negative`, `duration-exponent`, `duration-out-of-range`, `verify-exit-code-string`, `verify-exit-code-float`, `verify-exit-code-exponent`, `verify-exit-code-out-of-range`, `sanitized-logs-wrong-type`, `sanitized-log-unknown-property`, `diagnostics-wrong-type`, `diagnostics-unknown-property`, `nupkg-sha512-wrong-type`, `nupkg-sha512-wrong-domain`, `signature-verification-string`, `signature-authoritative-null`, `revocation-mode-wrong-type` |
| `AC08-PHASE-001..018` | 18 | `missing-phase`, `extra-phase`, `reordered-phase`, `duplicate-phase`, `success-nonzero`, `success-with-code`, `failure-zero`, `failure-null-code`, `skipped-nonnull-exit`, `skipped-wrong-code`, `signature-success-after-dependency`, `signature-skip-without-failed-dependency`, `signature-failurephase-not-first`, `regression-unit-after-build-failure`, `regression-headless-after-build-failure`, `headless2-skipped-after-headless1-failure`, `regression-failurephase-not-first`, `test-evidence-native-zero-synthetic-two` |
| `AC08-SIG-001..016` | 16 | `assets-count-zero`, `assets-count-one`, `assets-count-two`, `assets-count-four`, `assets-reordered`, `assets-duplicate`, `headless-restore-failure`, `headless-assets-failure`, `desktop-restore-failure`, `desktop-assets-failure`, `debian-restore-failure`, `debian-assets-failure`, `verify-graph-failure`, `verify-package-loop-failure`, `sanitize-fallback`, `happy-success` |
| `AC08-REG-001..021` | 21 | `unit-restore-failure`, `headless-restore-failure`, `unit-build-failure`, `headless-build-failure`, `unit-test-failure-headless-continues`, `headless1-failure-headless2-continues`, `headless2-failure`, `unit-floor-829`, `headless1-floor-35`, `headless2-floor-35`, `failed-count-nonzero`, `skipped-count-nonzero`, `count-arithmetic-mismatch`, `missing-trx`, `missing-html`, `unit-timeout`, `headless-timeout`, `adapter-cancellation`, `native-zero-invalid-report-synthetic-exit`, `adapter-parses-raw-without-rebuild`, `happy-floors-and-equality` |
| `AC08-SAN-001..029` | 29 | `partial-write-then-throw`, `unsafe-raw-file`, `nul-binary`, `absolute-path`, `uri-userinfo`, `authorization-header`, `unexpected-extension`, `short-secret`, `long-secret`, `percent-upper-secret`, `percent-lower-secret`, `percent-form-plus-secret`, `base64-standard-padded-secret`, `base64-standard-unpadded-secret`, `base64url-padded-secret`, `base64url-unpadded-secret`, `json-unicode-secret`, `xml-named-secret`, `xml-decimal-secret`, `xml-hex-secret`, `hex-lower-secret`, `hex-upper-secret`, `double-encoded-secret`, `percent-base64-hex-four-layer`, `four-layer-encoded-secret`, `fifth-layer-decodable-rejected`, `missed-redaction`, `raw-valid-regression-sanitizer-failure`, `happy-transform` |
| `AC08-MAN-001..020` | 20 | `receipt-self-entry`, `missing-payload`, `extra-payload`, `unsorted`, `duplicate-ordinal`, `casefold-collision`, `backslash`, `rooted`, `dot-segment`, `dotdot-segment`, `ads-colon`, `hidden-component`, `control-character`, `unicode-lookalike`, `reserved-device`, `overlength`, `symlink-reparse`, `hardlink-linkcount`, `duplicate-file-identity`, `prefix-sibling-containment` |
| `AC08-PUB-001..015` | 15 | `receipt-write-failure`, `candidate-cleanup-failure`, `raw-cleanup-failure`, `copy-failure`, `hash-mismatch`, `rename-failure`, `final-validation-failure`, `incomplete-projection-without-unexpected`, `execution-plus-publication-precedence`, `unexpected-complete-projection`, `unexpected-with-incomplete-projection`, `quarantine-success-to-fallback`, `quarantine-failure-catastrophic`, `fallback-writer-catastrophic`, `primary-native-failure-valid-tree` |
| `AC08-TIME-001..009` | 9 | `signature-restore-timeout`, `signature-verify-timeout`, `unit-restore-timeout`, `headless-restore-timeout`, `unit-build-timeout`, `headless-build-timeout`, `signature-sanitize-timeout`, `regression-sanitize-timeout`, `finalizer-timeout` |
| `AC08-GATE-001..017` | 17 | `marker-unset`, `root-unset`, `root-noncanonical`, `expected-identity-mismatch`, `validator-expected-blob-mismatch`, `last-exit-code-null`, `last-exit-code-stale-nonzero`, `canonical-raw-root-mismatch`, `post-return-tamper`, `happy-primary`, `happy-fallback`, `validator-tree-nonregular`, `validator-index-nonzero-stage`, `validator-index-expected-mismatch`, `validator-raw-git-object-rehash-mismatch`, `workspace-validator-tamper-ignored`, `quoted-parent-revspec` |
| `AC08-E2E-001..001` | 1 | `local-full-happy` |
| `AC08-CLEAN-001..020` | 20 | `staged-index-drift`, `tracked-unstaged-drift`, `untracked-nonignored`, `ignored-output-allowed`, `git-status-nonzero`, `git-status-malformed-record`, `git-status-nul-malformed`, `fsmonitor-disabled`, `untracked-cache-disabled`, `assume-unchanged-input`, `skip-worktree-input`, `sparse-index-input`, `sparse-checkout-input`, `ignored-security-shadow`, `replace-ref-rejected`, `raw-attempt-int32-max-positive`, `raw-attempt-leading-space`, `raw-attempt-leading-zero`, `raw-attempt-negative-one`, `invalid-lane-exact-fallback-projection` |
| `AC08-BASEX-001..024` | 24 | `generator-parent-input-missing`, `generator-parent-input-wrong`, `generator-raw-commit-extraction`, `generator-object-rehash-mismatch`, `generator-workspace-script-rejected`, `generator-parent-script-absent-allowed`, `generator-source-output-overlap`, `generator-source-packages-overlap`, `generator-output-link`, `generator-parent-dirty`, `raw-blob-length-mismatch`, `fixture-bom`, `fixture-crlf`, `fixture-noncanonical-json`, `graph-order-mismatch`, `graph-hash-mismatch`, `package-id-casefold-duplicate`, `package-id-casing-drift`, `candidate-exact-two-props-diff`, `candidate-second-input-drift`, `candidate-extra-props-drift`, `generator-different-cwd`, `sdk-selection-recorded`, `sdk-selection-drift` |
| `AC08-CACHE-001..020` | 20 | `untrusted-assets-hints-only`, `hint-missing-package`, `hint-extra-unused-package`, `unrelated-cache-entry-ignored`, `cache-sha512-sidecar-mismatch`, `repo-local-blob-mismatch`, `tampered-nupkg`, `tampered-nuspec`, `tampered-dot-sha512`, `tampered-nupkg-metadata`, `zip-traversal`, `zip-link`, `zip-casefold-path`, `zip-duplicate-path`, `zip-size-overflow`, `nuget-redirect-wrong-host`, `authoritative-package-download-blocked`, `rebuilt-cache-graph-mismatch`, `rebuilt-cache-extra-package`, `happy-full-graph-provenance` |
| `AC08-CACHE2-001..024` | 24 | `registration-resource-missing`, `registration-resource-duplicate`, `registration-leaf-missing`, `registration-leaf-duplicate`, `registration-leaf-wrong-id`, `registration-leaf-wrong-version`, `catalog-entry-uri-invalid`, `package-content-uri-invalid`, `catalog-id-mismatch`, `catalog-version-mismatch`, `catalog-hash-algorithm-not-sha512`, `catalog-hash-base64-invalid`, `catalog-package-size-mismatch`, `nuget-redirect-rejected`, `flatcontainer-sidecar-404-not-provenance`, `raw-logical-differ-positive`, `raw-logical-domain-swap`, `cache-sidecar-byte-format`, `metadata-v2-byte-format`, `metadata-nuget-source`, `metadata-local-source`, `nuget-packaging-loader-path`, `content-hash-method-shape`, `network-archive-exact-boundaries` |
| `AC08-CONFIG-001..014` | 14 | `config-missing`, `config-wrong-path`, `ambient-source`, `implicit-library-packs-source`, `implicit-vs-fallback-folder`, `missing-disable-library-packs-switch`, `missing-disable-nuget-fallback-switch`, `nonempty-restore-fallback-folders`, `evaluated-additional-source-nonempty`, `evaluated-fallback-nonempty`, `wrong-package-folder`, `target-package-outside-isolated-root`, `source-set-case-or-uri-drift`, `happy-exact-source-folder-contract` |
| `AC08-FULL-001..020` | 20 | `outer-precondition-no-child-roots`, `signature-child-root-grammar`, `regression-child-root-grammar`, `child-root-overlap`, `child-package-root-reuse`, `child-file-identity-overlap`, `signature-child-deadline-65`, `regression-child-deadline-95`, `outer-deadline-175`, `outer-aggregation-deadline-10`, `signature-failure-regression-continues`, `signature-catastrophic-regression-suppressed`, `insufficient-regression-budget-suppressed`, `child-fallback-forces-outer-fallback`, `child-context-not-full-child`, `child-authority-true`, `child-order-wrong`, `outer-manifest-child-receipt-mismatch`, `outer-relative-root-mismatch`, `happy-recursive-full` |
| `AC08-ADAPTER-001..022` | 22 | `restore-start-throw-minus-two`, `build-start-throw-minus-two`, `test-start-throw-minus-two`, `native-null-exit-rejected`, `adapter-status-arbitrary`, `worker-string-false-rejected`, `adapter-success-with-code`, `adapter-success-nonzero`, `adapter-failure-zero`, `timeout-minus-one`, `cancellation-minus-one`, `process-tree-kill-failure-catastrophic`, `lingering-stdout-writer-catastrophic`, `lingering-stderr-writer-catastrophic`, `worker-join-failure-catastrophic`, `worker-action-failure-mapping`, `worker-timeout-mapping`, `worker-cancel-mapping`, `worker-throw-mapping`, `child-command-files-absent`, `raw-stream-cap-terminates`, `raw-stream-overflow-kill-failure-catastrophic` |
| `AC08-REPORT-001..022` | 22 | `runner-args-before-separator`, `trx-bare-filename-required`, `trx-absolute-filename-rejected`, `html-relative-filename-rejected`, `html-absolute-filename`, `results-root-absent-before-orchestrator`, `results-root-preexisting`, `results-root-nonempty`, `results-root-collision`, `missing-results-trx`, `missing-results-html`, `extra-report-file`, `json-sidecar`, `cwd-report-file`, `github-reporter-not-disabled`, `actions-runtime-token-inherited`, `actions-results-url-inherited`, `github-step-summary-inherited`, `raw-html-copied`, `exact-two-raw-outputs`, `three-runs-distinct-roots`, `happy-bare-trx-absolute-html` |
| `AC08-EVID-001..022` | 22 | `signature-success-log-cardinality`, `signature-failure-prefix-log-cardinality`, `signature-verifylog-crosslink`, `signature-sanitizedlogs-order`, `signature-diagnostic-missing`, `signature-diagnostic-extra`, `signature-diagnostic-reordered`, `signature-diagnostic-duplicate`, `regression-run-report-crosslink`, `notattempted-run-has-report`, `regression-diagnostic-missing`, `regression-diagnostic-extra`, `regression-diagnostic-reordered`, `regression-diagnostic-duplicate`, `manifest-orphan-payload`, `manifest-missing-reference`, `reference-hash-mismatch`, `reference-length-mismatch`, `sanitized-trx-schema-invalid`, `sanitized-html-schema-invalid`, `sanitized-log-schema-invalid`, `happy-content-reparse` |
| `AC08-RUNTIME-001..016` | 16 | `os-wrong-case`, `architecture-unknown`, `execution-context-unknown`, `sdk-version-wrong-grammar`, `sdk-muxer-selection-mismatch`, `sdk-drift-during-attempt`, `sdk-baseline-value-missing`, `signature-verification-nonbool`, `revocation-semantic-mismatch`, `linux-signature-authority-false`, `regression-authority-true`, `local-full-authority-true`, `full-child-authority-true`, `full-child-outside-wrapper`, `local-signature-rejected`, `happy-platform-runtime-tuples` |
| `AC08-SEC2-001..024` | 24 | `path-not-secret-seed`, `pathext-not-secret-seed`, `psmodulepath-not-secret-seed`, `homepath-not-secret-seed`, `compat-layer-not-secret-seed`, `github-token-seed`, `api-key-seed`, `credentials-plural-seed`, `connection-string-seed`, `password-suffix-seed`, `seed-count-overflow`, `variant-node-cap-overflow`, `decoded-byte-cap-overflow`, `candidate-file-cap-overflow`, `candidate-aggregate-cap-overflow`, `token-run-cap-overflow`, `explicit-percent-invalid`, `explicit-json-invalid`, `explicit-xml-invalid`, `ordinary-sha-not-base64-failure`, `ordinary-lane-name-path-positive`, `max-branch-closure-positive`, `hosted-windows-env-positive`, `hosted-ubuntu-env-positive` |
| `AC08-GIT2-001..024` | 24 | `absolute-pwsh-shell-required`, `dotnet-path-shadow-rejected`, `git-replace-object-rejected`, `git-system-config-poison`, `git-global-config-poison`, `git-local-fsmonitor-overridden`, `git-environment-poison`, `command-env-outside-runner-temp`, `command-path-outside-runner-temp`, `command-output-outside-runner-temp`, `command-file-link`, `command-file-identity-drift`, `validator-command-env-absent`, `validator-command-output-absent`, `validator-token-env-absent`, `validator-stdout-nonempty`, `validator-stderr-nonempty`, `validator-output-overflow`, `expected-head-before-mismatch`, `expected-head-after-mismatch`, `tree-recheck-command-failure`, `index-flags-drift`, `raw-validator-object-mismatch`, `byte-sequence-runtime-positive` |
| `AC08-CI2-001..018` | 18 | `raw-expected-tree-workspace-mutation-ignored`, `cmd-parser`, `bat-parser`, `python-parser`, `composite-action-parser`, `reachable-dynamic-child-rejected`, `unreachable-dynamic-utility-allowed`, `required-action-floating-tag`, `required-action-short-sha`, `outofscope-floating-action-allowed`, `local-dynamic-uses-rejected`, `tests-contents-read`, `android-workflow-write-removed`, `android-build-contents-read`, `android-release-write-only`, `release-job-exact-permissions-no-checkout`, `pr-release-job-suppressed`, `happy-current-parent-closure` |
| `AC08-PROTECT-001..016` | 16 | `strict-false`, `required-check-app-missing`, `required-check-foreign-app`, `required-check-stale-sha`, `context-only-satisfaction`, `required-check-current-success`, `admin-enforcement-false`, `force-push-enabled`, `ruleset-bypass-actor`, `review-rule-weakened`, `merge-queue-enabled-rejected`, `merge-queue-disabled-preserved`, `unknown-extra-context`, `settings-drift-before-merge`, `required-android-write-token`, `happy-app-bound-protection` |
| `AC08-ANDROID-001..024` | 24 | `artifact-name-or-pattern-download-rejected`, `upload-artifact-id-missing`, `upload-artifact-id-invalid`, `upload-artifact-digest-missing`, `upload-artifact-digest-invalid`, `artifact-wrong-run-id`, `artifact-wrong-run-attempt`, `artifact-wrong-id`, `artifact-service-digest-mismatch`, `artifact-name-mismatch`, `artifact-source-sha-mismatch`, `artifact-metadata-expired`, `artifact-metadata-head-sha-mismatch`, `archive-redirect-rejected`, `archive-content-length-mismatch`, `archive-size-plus-one`, `artifact-extra-apk`, `artifact-link-entry`, `artifact-apk-hash-mismatch`, `release-path-glob-rejected`, `release-unmatched-file`, `release-overwrite-forbidden`, `pr-release-job-suppressed`, `happy-id-bound-release-handoff` |
| `AC08-WORKER-001..016` | 16 | `producer-raw-expected-blob`, `producer-workspace-tamper-ci`, `producer-workspace-tamper-local`, `worker-closed-stdin-schema`, `caller-local-not-visible`, `ambient-function-not-visible`, `seed-snapshot-before-scrub`, `seed-values-stdin-only`, `seed-echo-rejected`, `seed-identity-drift`, `worker-stdout-cap`, `worker-stderr-nonempty`, `worker-timeout`, `worker-join-failure-catastrophic`, `parent-owned-sibling-validator`, `happy-isolated-workers` |
| `AC08-RUNNER-001..012` | 12 | `signature-ubuntu-24-04`, `regression-windows-2022`, `android-ubuntu-24-04`, `floating-runner-label-rejected`, `job-timeout-120`, `exact-job-display-names`, `duplicate-context-producer`, `single-wrapper-step`, `separate-gate-step-rejected`, `command-ancestor-link`, `command-file-recovery-nonauthorizing`, `held-output-handle` |
| `AC08-TRUST2-001..016` | 16 | `precondition-success-string-false`, `precondition-missing-success`, `precondition-failure-code-null`, `precondition-success-root-null`, `publication-ready-string-false`, `publication-outcome-wrong-case`, `publication-root-mismatch`, `publication-receipt-kind-mismatch`, `publication-receipt-outcome-mismatch`, `fallback-native-exit-zero`, `success-native-exit-nonzero`, `validator-skipped-forged-frame`, `validator-exit-outcome-mismatch`, `producer-seed-hash-mismatch`, `validator-seed-hash-mismatch`, `happy-typed-publication-binding` |
| `AC08-NUM-001..012` | 12 | `json-negative-zero`, `json-leading-zero`, `json-plus-one`, `json-leading-space`, `json-trailing-space`, `json-unsigned-negative`, `raw-attempt-tab`, `raw-attempt-crlf`, `raw-source-sha-space`, `duration-leading-zero`, `byte-length-negative-zero`, `canonical-number-positive` |

Registry contains exact `647` cases. SelfTest closed JSON has exact ordered 647 `{id,status}` objects and requires schema 1, criterion `NUGET-AC-08`, `caseCount=647`, `passed=647`, zero failed/skipped and no ID drift. Negative publication semantics remain fallback/primary/catastrophic as defined; happy lanes have exact projections/trees.

Оба evidence upload steps имеют fail-path semantics. Producer и independent validator являются отдельными bounded processes, но живут под одним trusted wrapper step: одна in-memory seed snapshot и один held command-file set охватывают оба процесса.

Runner/job contract exact:

| Job id | Display/check context | Runner | Job timeout | Lane deadline |
| --- | --- | --- | ---: | ---: |
| `nuget-signature-chain` | `Signature` | `ubuntu-24.04` | 120 min | 65 min |
| `all-tests` | `Regression` | `windows-2022` | 120 min | 95 min |
| observed AndroidPkg check-run | recorded only after candidate CI | `ubuntu-24.04` | bounded current build | current |

Floating labels, missing/wrong job `name`, duplicate context or another runner fail static validation. Across all tracked workflows each new context has one producer only: `.github/workflows/tests.yml:jobs.nuget-signature-chain`, `.github/workflows/tests.yml:jobs.all-tests`, `.github/workflows/android-packaging.yml:jobs.android-build`.

The trusted inline wrapper, not repository workspace code, performs the closed order:

1. write false/empty defaults through the held `GITHUB_OUTPUT` handle; snapshot command-file bytes/identities/ancestors and secret seeds before isolation;
2. validate exact SHA/clean/index/filter/gitdir contract; bounded-extract/re-hash the producer regular blob from `EXPECTED_SOURCE_SHA` to a fresh non-link temp path;
3. launch producer with absolute PowerShell, `-Mode RunAttempt`, one canonical stdin frame, canonical roots/muxer/lane/SHA/attempt and no command-file/token child env; incrementally capture its closed frame and native exit;
4. parent independently resolves/extracts/re-hashes the validator from the same commit and launches it as a sibling process with producer root/exit plus the same immutable seed snapshot; producer has no validator path or authorization role;
5. parent incrementally reads both closed channels (1 MiB producer stdout; validator stdout/stderr 16 KiB and empty on success), applies deadlines/tree kill/join, binds receipt outcome to producer exit, rechecks HEAD/status/objects and restores command files in `finally`;
6. only then append ready/verified/root/`attempt_verdict` through the held output handle; exit 0 for either validated `success` or validated `failure` evidence, and nonzero only for authorization/catastrophic failure.

No wrapper path may call `ReadToEnd*`/`CopyTo`; every Git/blob/child stream uses online caps and kill/join. Catastrophic wrapper/gate/recovery failure leaves false/empty outputs. Validated failure evidence may upload while the job remains red.

```yaml
# Structural projection; each run body is the exact inline wrapper contract 1..6.
jobs:
  nuget-signature-chain:
    name: Signature
    runs-on: ubuntu-24.04
    timeout-minutes: 120
    permissions: { contents: read }
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0
        with: { ref: "${{ github.sha }}", fetch-depth: 0, persist-credentials: false, submodules: false }
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with: { global-json-file: global.json }
      - name: Run and independently validate Signature evidence
        id: nuget_wrapper
        working-directory: ${{ github.workspace }}
        shell: /opt/microsoft/powershell/7/pwsh -NoLogo -NoProfile -NonInteractive -File {0}
        env:
          DOTNET_NUGET_SIGNATURE_VERIFICATION: "true"
          EXPECTED_SOURCE_SHA: ${{ github.sha }}
          EXPECTED_RUN_ATTEMPT: ${{ github.run_attempt }}
          EXPECTED_LANE: Signature
        run: Invoke-TrustedNuGetAttemptWrapper -Lane Signature
      - name: Upload Signature evidence
        if: ${{ always() && steps.nuget_wrapper.outcome == 'success' && steps.nuget_wrapper.outputs.safe_upload_ready == 'true' && steps.nuget_wrapper.outputs.safe_upload_verified == 'true' && (steps.nuget_wrapper.outputs.attempt_verdict == 'success' || steps.nuget_wrapper.outputs.attempt_verdict == 'failure') }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: nuget-signature-chain-${{ github.sha }}-attempt-${{ github.run_attempt }}
          path: ${{ steps.nuget_wrapper.outputs.validated_upload_root }}
          if-no-files-found: error
          overwrite: false
          retention-days: 14
      - name: Enforce Signature attempt verdict
        if: ${{ always() }}
        shell: pwsh
        run: if ('${{ steps.nuget_wrapper.outputs.attempt_verdict }}' -eq 'failure') { exit 1 }; if ('${{ steps.nuget_wrapper.outcome }}' -ne 'success') { exit 1 }

  all-tests:
    name: Regression
    runs-on: windows-2022
    timeout-minutes: 120
    permissions: { contents: read }
    steps:
      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0
        with: { ref: "${{ github.sha }}", fetch-depth: 0, persist-credentials: false, submodules: false }
      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4.3.1
        with: { global-json-file: global.json }
      - name: Run and independently validate Regression evidence
        id: nuget_wrapper
        working-directory: ${{ github.workspace }}
        shell: '"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -NoLogo -NoProfile -NonInteractive -File {0}'
        env:
          DOTNET_NUGET_SIGNATURE_VERIFICATION: "true"
          EXPECTED_SOURCE_SHA: ${{ github.sha }}
          EXPECTED_RUN_ATTEMPT: ${{ github.run_attempt }}
          EXPECTED_LANE: Regression
        run: Invoke-TrustedNuGetAttemptWrapper -Lane Regression
      - name: Upload Regression evidence
        if: ${{ always() && steps.nuget_wrapper.outcome == 'success' && steps.nuget_wrapper.outputs.safe_upload_ready == 'true' && steps.nuget_wrapper.outputs.safe_upload_verified == 'true' && (steps.nuget_wrapper.outputs.attempt_verdict == 'success' || steps.nuget_wrapper.outputs.attempt_verdict == 'failure') }}
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: reactiveui-regression-${{ github.sha }}-attempt-${{ github.run_attempt }}
          path: ${{ steps.nuget_wrapper.outputs.validated_upload_root }}
          if-no-files-found: error
          overwrite: false
          retention-days: 14
      - name: Enforce Regression attempt verdict
        if: ${{ always() }}
        shell: pwsh
        run: if ('${{ steps.nuget_wrapper.outputs.attempt_verdict }}' -eq 'failure') { exit 1 }; if ('${{ steps.nuget_wrapper.outcome }}' -ne 'success') { exit 1 }
```

`Invoke-TrustedNuGetAttemptWrapper` above denotes the inline function defined at the top of each same `run` body, not a PATH/workspace command. Static tests require its full definition and six ordered operations in both expanded YAML bodies; undefined placeholder/function, separate gate step, direct workspace producer execution and path-based `>> GITHUB_OUTPUT` are negative fixtures.

Local trusted launcher uses the same parent-owned raw producer then sibling-validator path. One evidence-based automatic local retry is allowed only after independently proven NuGet/CRL/network outage: explicit attempt 2, new empty cache/root, retain attempt 1. This policy does not constrain identity grammar: CI/manual reruns with canonical `github.run_attempt >= 3` are legal fresh attempts with distinct immutable roots. `NUGET_SIGNATURE_RUN_ATTEMPT` is never read.

Stop rules for validation:

- deterministic package/signature/test failure is not blindly retried;
- one retry is allowed only after independently evidenced NuGet/CRL/network outage, uses a new empty cache and distinct immutable `attempt-2` evidence root while retaining `attempt-1`;
- no result is accepted if worktree SHA, dependency graph or package hashes differ from recorded evidence.

### Acceptance-to-Test Matrix
| Acceptance criterion | Automated test | Manual / visual / log check | Evidence artifact | If not tested, why |
| --- | --- | --- | --- | --- |
| NUGET-AC-01/02 | verifier XML/scope checks | diff/allowlist review | spec Post-EXEC + diff | — |
| NUGET-AC-03 | `git ls-files` result/sentinel guard + workflow scan + negative fixtures | inspect CI env | verifier JSON/log | — |
| NUGET-AC-04 | Ubuntu fresh restore | check empty cache path and exit | Actions log | — |
| NUGET-AC-05 | assets parser + recorded-commit input-manifest comparator | inspect six versions and complete sorted input paths | verifier evidence | — |
| NUGET-AC-06 | fingerprint-bound `dotnet nuget verify --all` | review package/log hashes and exit codes; raw output is transient only | verifier JSON + sanitized `.log` files | — |
| NUGET-AC-07 | targeted two-project restore/build; Unit x1 + Headless x2; report/count floors, dependency transitions and timeout/cancellation fixtures | compare `>=830`/`>=36`, zero failed/skipped, Headless equality, hashes and exit receipts | pinned `reactiveui-regression-<sha>-attempt-<n>` Actions artifact, 14 days | — |
| NUGET-AC-08 | exact 647-case `AC08-*` registry; all closed trust/state/evidence fixtures | inspect primary/fallback/suppression and final exit | `artifacts/nuget-selftest/ac08-results.json` + fixtures | — |
| NUGET-AC-09 | Signature + Windows Regression + exact observed AndroidPkg check-run | inspect exact current-candidate contexts/app ids, `enforce_admins=true`, `allow_force_pushes=false`, enabled no-bypass ruleset and non-weakened review settings | branch/ruleset before/after snapshot + PR URL/merge SHA | — |
| NUGET-AC-10 | stop-rule audit | human decision if reached | journal | conditional |
| Stage3-HO-01 | Stage 3 static/local/native gates | inspect exact SHA | Stage 3 spec/PR #280 runs/bundle | downstream, not child completion |

## 12. Риски и edge cases
- Splat 19.4.1 includes behavior changes (thread-safety/platform targeting); existing tests may expose a real incompatibility.
- NuGet cache can hide old/new package provenance; mandatory empty package root and no HTTP cache mitigate it.
- CRL/NuGet outages can fail a secure build; bypass is forbidden and outage must be independently evidenced.
- A workflow or invoked repo script may reintroduce bypass outside the three known files; verifier scans the complete tracked CI execution surface defined above.
- Signature output formatting may vary by SDK locale/version; acceptance uses only exit code from fingerprint-bound `dotnet nuget verify`; localized raw output is transient outside the upload root, and only its sanitized UTF-8 copy is retained without parsing as an acceptance signal.
- Stage 3 branch can reintroduce its pre-prerequisite `false` during rebase; explicit integration AC and negative fixture prevent it.
- Candidate sanitizer/manifest/receipt/copy/rename can fail after partial writes; final root is never their destination, and only validated primary or fallback-only publication receives the safe marker.
- Existing final/work/scratch path, symlink/reparse point or filesystem cleanup failure can indicate tampering or runner drift; known cases quarantine/fallback, while inability to establish a clean fallback suppresses upload and fails the job.
- A marker cannot protect against mutation after finalizer return; independent gate revalidates exact bytes immediately before upload and its output is mandatory in uploader condition.
- NuGet logical content hashes and raw package hashes are distinct domains; this spec compares each field only to its canonical source and never substitutes one hash representation for another.

### Expected User Review Objections
| Likely objection | Why likely | Mitigation in spec/code plan | Status |
| --- | --- | --- | --- |
| Почему не поменять только две строки package versions? | это кажется минимальным fix | current workflows explicitly disable verification; permanent Ubuntu guard proves security outcome | mitigated |
| Не расширяет ли 12.0.2 upgrade Avalonia? | adjacent package version exists | keep Avalonia 12.0.3; choose RA 12.0.2 whose minimum is 12.0.1 | mitigated |
| Не станет ли CI flaky из-за CRL/network? | online trust checks depend on external state | fail closed, one evidence-based retry, separate job | accepted-risk |
| Почему нужны Headless x2? | no intended UI change | ReactiveUI/Splat initialize UI/test host and include runtime changes | mitigated |
| Почему нельзя вернуть false при outage? | delivery pressure | that recreates the known supply-chain defect | mitigated |
| Зачем менять branch rules, если job уже есть? | workflow может выглядеть permanent без фактического merge enforcement | live audit показывает checks не required, admins не enforced и ruleset disabled; после отдельного user decision применяются exact contexts + admin/force-push/no-bypass settings snapshot | ASK-HUMAN |

### Rework Prevention Checklist
- User-visible workflow/result named: Да.
- Every visible scenario has evidence: Да.
- Assumed decisions listed: Да.
- Likely objections mitigated: Да.
- Role-based review required before approval: Да.
- AC are verifiers, not preparation: Да.
- EXEC can prove all scenarios before final: Да.

## 13. План выполнения
1. Закрыть все findings текущего adversarial audit, повторить author checks и independent tester/architect/security review; запрос approval разрешён только после `PASS`.
2. После technical `PASS` получить user decision по app-bound strict protected-merge contract и доказанно disabled merge queue; синхронизировать permanent-vs-diagnostic outcome.
3. Запросить отдельную фразу `Спеку подтверждаю` именно для финальной версии этой spec.
4. Implement generator/verifier/read-only gate/SelfTest and RED fixtures without fixture/package/workflow outcome changes; run static/unit RED, then commit this tooling-only candidate so generator has an immutable SHA.
5. From that commit raw-extract/re-hash generator externally. Against separate clean exact parent worktree, bounded hint cache and verified full-graph nupkg bytes, generate temp baseline; independently validate canonical bytes before adding fixture.
6. Apply fixture, exactly two pins, Android/Debian flags, tests workflow, Android/Debian least-privilege/action pins and contract tests. Commit the complete candidate before any authoritative RunAttempt.
7. On the committed clean exact candidate SHA run local Regression and diagnostic Full with isolated roots; no tracked write is allowed. Preliminary dirty runs are characterization only.
8. Add only journal/evidence references in a follow-up commit if required; because SHA changes, rerun clean exact-SHA local gate without another tracked edit, push and use final CI artifacts/check-runs for authoritative closure.
9. Open/update draft PR, perform independent implementation security/test/delivery review, observe current-SHA app ids/names, then apply approved saved-snapshot branch/ruleset operation only with queue disabled.
10. Wait for exact final-SHA Signature/Regression/read-only Android/security PASS and required app-bound strict gates; obtain external approval and merge without bypass.
11. Close child-spec through delivery record without mutating the reviewed candidate before its final checks; hand merge SHA/evidence to Stage 3 journal.

Downstream Stage 3 work (не блокирует child completion): rebase PR #280, изменить Android verification flag/static contract, инвалидировать prior evidence и повторить complete exact-SHA local/native gate до завершения Stage 3.

## 14. Открытые вопросы
Открыт один operational вопрос после technical PASS: подтвердит ли пользователь полный protected-merge contract на `main` для exact observed current-candidate Signature, Regression и AndroidPkg contexts/app ids вместе с `enforce_admins=true`, `allow_force_pushes=false`, enabled `Main` ruleset без bypass actors и сохранением остальных review settings. Рекомендуемый ответ — да; частичный или отрицательный ответ требует понизить permanent-gate claim до diagnostic и повторно пройти security review. Если affected subset, preserved full graph или package signature отличается от зафиксированного контракта, это новый блокирующий вопрос, а не право агента выбрать другую версию.

## 15. Соответствие профилю
- Профиль: central `instructions/profiles/dotnet-desktop-client.md`; context `instructions/contexts/testing-dotnet.md`; security/CI требования заданы этой spec как domain contract.
- Выполненные требования профиля: UI-thread/selector/public behavior не меняются; required .NET build/full Unit и два full Headless regression runs сохраняют desktop behavior; trusted primary sources, exact allowlist, fail-closed security boundary, fresh-cache proof, rollback/stop rules и protected delivery покрывают dependency/CI риск.

## 16. Таблица изменений файлов
| Файл | Изменения | Причина |
| --- | --- | --- |
| `specs/2026-07-21-reactiveui-signature-chain-remediation.md` | contract, approval, evidence | auditable prerequisite |
| `src/Directory.Packages.props` | two versions | replace revoked chain |
| `.github/workflows/android-packaging.yml` | verification true | remove bypass |
| `.github/workflows/deb_packaging.yml` | verification true | remove bypass |
| `.github/workflows/tests.yml` | pinned full-history credential-free checkout without submodule materialization; add Ubuntu `Signature`; replace legacy Windows steps with one host-safe `Regression`; verification env, unique empty cache, trusted temp-extracted gate, gate-root pinned upload, 120-minute job / 95-minute attempt budget | permanent fresh-restore guard without duplicate orchestration or unsafe fail-path upload |
| `scripts/Test-NuGetSignatureChain.ps1` | guarded baseline/attempt/workers/finalizer and 647 adversarial self-tests | exact graph/signature/control-flow/evidence contract |
| `scripts/Test-NuGetEvidencePublication.ps1` | read-only validator executed only from raw `EXPECTED_SOURCE_SHA` commit-tree blob after binary extraction/Git-object re-hash; closed JSON/TRX/HTML/log/tree/link/cross-link checks and silent terminating contract | independent authorization without workspace/index/filter execution trust |
| `distribution/fixtures/reactiveui-signature-chain-baseline.json` | parent-SHA normalized graphs and input hashes | reproducible full-graph comparison without insecure parent restore in Ubuntu |
| `src/Unlimotion.Test/CiReadmeMediaContract.cs` | replace legacy step-name assertion with exact Signature/Regression/serial x2/gate contract | keep existing CI executable contract truthful |
| GitHub `main` branch protection/ruleset | after explicit user decision, require exact observed current-candidate Signature, Regression and AndroidPkg contexts/app ids, `enforce_admins=true`, `allow_force_pushes=false`, enabled `Main` ruleset without bypass actors and no weakened review settings | make the new security job a non-bypassable merge gate |

## 17. Таблица соответствий (было -> стало)
| Область | Было | Стало |
| --- | --- | --- |
| ReactiveUI packages | revoked signature chain | exact re-signed target chain |
| Linux packaging flags | verification disabled in known jobs | explicit true; global negative scan |
| CI evidence | cached Windows tests + late Linux failure | dedicated fresh Ubuntu signature gate |
| Fail-path evidence | direct writes to an always-uploaded root | candidate-only writes, full-tree validation, primary/fallback publication and safe marker |
| Stage 3 evidence | based on revoked baseline | fully reset after prerequisite merge |

## 18. Альтернативы и компромиссы
- Только две version replacements: меньше diff, но не устраняет tracked bypasses и не создаёт regression guard; отклонено.
- `ReactiveUI.Avalonia 12.0.3`: новее, но требует Avalonia >=12.0.4; отклонено как scope expansion.
- Direct Splat pins: делает graph явным, но дублирует upstream contract и усложняет future alignment; отклонено.
- Offline/disabled revocation verification: может дать зелёный restore, но скрывает известный revoked certificate; запрещено.
- Выполнить update прямо в PR #280: быстрее, но загрязняет exact Stage 3 scope и evidence; отклонено в пользу prerequisite PR.

## 19. Результат quality gate и review

### SPEC Linter Result
| Блок | Пункты | Статус | Комментарий |
|---|---|---|---|
| A. Полнота спеки | 1-5 | PASS | goal, baseline, scope and non-goals explicit |
| B. Качество дизайна | 6-10 | PASS | exact affected subset, preserved graph and integration defined |
| C. Безопасность изменений | 11-13 | PASS | no bypass; stop/rollback rules explicit |
| D. Проверяемость | 14-16 | PASS | every AC mapped to evidence |
| E. Готовность к автономной реализации | 17-19 | PASS | ordered delivery and exact allowlist |
| F. Соответствие профилю | 20 | PASS | dependency-security checks included |

Итог: structural linter `PASS`; approval readiness временно `NEEDS-FIX` до повторного independent validation/architecture/security review всех adversarial amendments.

### SPEC Rubric Result
| Критерий | Балл (0/2/5) | Обоснование |
|---|---:|---|
| 1. Ясность цели и границ | 5 | one root supply-chain defect, exact non-goals |
| 2. Понимание текущего состояния | 5 | pins, transitive graph, workflows and CI RED traced |
| 3. Конкретность целевого дизайна | 5 | files, versions, job and verifier invariants exact |
| 4. Безопасность (миграция, откат) | 5 | fail closed, no insecure rollback |
| 5. Тестируемость | 5 | fresh restore/signature/build/runtime matrix |
| 6. Готовность к автономной реализации | 5 | no implementation choice remains after approval |

Итоговый балл: 30 / 30 по author rubric. Зона approval readiness будет определена только повторным independent review.

### Role-Based Review Result
| Role | Applicability | Review question | Verdict | Required spec changes |
| --- | --- | --- | --- | --- |
| Business analyst / domain workflow | applicable | Does prerequisite unblock reliable Stage 3 without product scope drift? | PASS | architect/governance review подтвердил child completion и downstream handoff boundary |
| UX / designer | not applicable | No intended UI/copy/layout change | Не применимо | none |
| Tester / validation | applicable | Are graph/signature/runtime regressions proved? | PASS (adversarial fallback) | technical findings closed; user accepted fallback because read-only sandbox unavailable |
| Developer / architect | applicable | Is the affected subset minimal and full graph preserved? | PASS (adversarial fallback) | technical findings closed; user accepted fallback because read-only sandbox unavailable |
| Delivery / operations / security | applicable | Are trust, CI, rollback and merge boundaries fail closed? | PASS (adversarial fallback) | technical findings closed; user accepted fallback because read-only sandbox unavailable |

### Post-SPEC Review
- Статус: `PASS (user-authorized adversarial fallback)`; technical re-review закрыл Stage-3 scope, wrapper/enforcement, 647 registry, observed Android context and Debian trust findings. Reviewer sandbox was unrestricted rather than read-only; пользователь 2026-07-23 явно разрешил этот adversarial fallback. UX = `N/A`, поскольку UI/copy/layout/runtime behavior не меняется.
- Scope reviewed: `specs/2026-07-21-reactiveui-signature-chain-remediation.md`; central `model-behavior-baseline`, `quest-governance`, `collaboration-baseline`, `testing-baseline`, `tool-execution-baseline`, `quest-mode`, `spec-linter`, `spec-rubric`, `review-loops`; context `testing-dotnet`; profile `dotnet-desktop-client`; exact planned files from section 16; no open product choice.
- Decision: Post-SPEC gate закрыт user-authorized adversarial fallback; теперь требуется отдельная фраза `Спеку подтверждаю` именно для этой dependency/security spec. Protected-merge contract остаётся отдельным user decision после technical candidate CI.
- Review passes:
  - Scope/Evidence pass: complete spec, exact child/downstream allowlists, workflow lanes, evidence schema, AC/test mapping and planned file table independently confirmed.
  - Historical Contract/Adversarial passes: прежние lane/native-array/receipt-bootstrap/marker-only findings закрыты; третий pass дополнительно выявил mutable workspace validator trust, incomplete executable surface/baseline cases, insufficient timeout budget, смешение native/report exit, open scalar/encoded-secret variants, bypassable branch settings и ambiguous unexpected/projection precedence.
  - Current Role-Based pass: `PASS (adversarial fallback)` для tester/validation, developer/architect/governance и delivery/security; UX remains N/A.
  - Re-review after fixes / Fix and re-review: raw expected-commit blob extraction/re-hash, closed baseline/cache/CI/schema/deadline/decoder/precedence/protection contracts and expanded registry проверены adversarially; user accepted fallback.
  - Stop decision: запросить отдельное approval этой spec; implementation до него запрещена.
- Evidence inspected: CI RED `29792038710/88516063131`; upstream release/version constraints; complete spec prose/code/YAML blocks; third validation/architecture/security findings; PowerShell/YAML contract blocks; spec-only Git status.
- Depth checklist:
  - Scope drift / unrelated changes: only this untracked spec is owned; production/scripts/workflows remain untouched in SPEC.
  - Acceptance criteria: AC8 now covers binder-safe raw preconditions, baseline/tree/index and executable-surface closure, strict scalar/JSON/native-report variants, lane-specific evidence, bounded operations, iterative secret decoding, same-volume publication, exact manifest closure, outcome precedence, trusted gate extraction and catastrophic upload suppression.
  - User-observable scenarios / Decision ledger / Expected objections: populated; one explicit operational branch-protection decision remains after technical PASS and before approval/EXEC.
  - Validation evidence: parser/fence/diff checks are author evidence only; executable self-tests and CI remain EXEC obligations.
  - Unsupported claims: authoritative links/evidence retained; code block is explicitly a canonical skeleton, not claimed as executed implementation.
  - Regression / edge case: all three tests after prerequisites, local Full, every Signature/Regression/timeout phase, native-zero report defect, raw-valid sanitizer failure, preflight/env/path/baseline/CI failures, four encoded layers, manifest self-reference, invalid receipt, cleanup/publication failures and post-return tamper addressed in draft; independent verdict pending.
  - Comments/docs/changelog: spec only; README/changelog excluded.
  - Hidden contract change: no product/UI/API change; workflow/security behavior is explicit scope.
  - Manual-review challenge: verify mutable workspace validator bytes are never executed, a failed gate can never authorize upload, raw scalar/native values cannot be coerced, encoded secrets cannot survive four layers, unexpected failure cannot be misclassified as projection corruption, and merge enforcement has no admin/force-push/ruleset bypass.
- Open review state: technical findings закрыты adversarial fallback; residual governance risk — review sandbox был unrestricted, и пользователь явно принял этот fallback.

| Severity | Area | Finding | Required action | Status |
| --- | --- | --- | --- | --- |
| HIGH | validation | One mixed attempt could not represent Ubuntu signature and Windows regression jobs, and receipt validation checked too little | add explicit lanes/platform guards and exact ordered phase plus lane-evidence validation | fixed; final validation re-review PASS |
| HIGH | validation | PowerShell nonterminating errors and baseline ownership were underspecified | set script-level `Stop`, add injected `Write-Error` fixture and guarded `GenerateBaseline` owner | fixed; final validation re-review PASS |
| HIGH | security | `[string[]]` across `pwsh -File` could silently alter three-assets identity | call verifier in-process and reject 0/1/2/4/reordered/duplicate assets | fixed; final security re-review PASS |
| HIGH | security/evidence | Raw assets/logs/reports and invalid primary receipt could enter the uploaded attempt tree | isolate raw roots, sanitize allowlisted extensions/content, publish invalid-primary metadata only and fail on cleanup | fixed; final security re-review PASS |
| HIGH | security/evidence | Sanitizer redacted secret-like environment values only at length >= 8, allowing short PIN/password/key leakage | redact every non-empty matching value; require positive transformation fixtures for lengths 1..7/long and a separate fail-closed missed-redaction fixture | fixed; final security re-review PASS |
| MEDIUM | exact allowlist | `tests.yml` row named only the Ubuntu job and left legacy Windows cache/test orchestration ambiguous | explicitly replace legacy Windows command steps with one Regression command and one whole-attempt upload; duplicate orchestration forbidden | fixed; final architect/governance re-review PASS |
| HIGH | validation/evidence | Lane/platform/attempt/cache/SHA/path preconditions могли throw до attempt envelope и обещанного receipt | принимать RunAttempt-bound values как raw strings, создать safe envelope до semantic checks, записывать allowlisted `attempt:preconditions` failure и fallback-only receipt | fixed in draft; independent re-review pending |
| HIGH | security/evidence | Sanitizer писал прямо в always-upload root; partial unsafe tree мог сохраниться после failure | candidate-only sanitizer, full candidate + publication scratch validation, primary/fallback same-volume publish и safe-marker-bound uploader | fixed in draft; independent re-review pending |
| HIGH | delivery/security | Uploader зависел только от attempt marker и мог стартовать после упавшего assertion | require marker + gate outcome success + gate output true; initialize gate output false and set true last | fixed in draft; independent re-review pending |
| HIGH | validation | `[string]`/case-insensitive tuple checks могли принять null как empty и wrong-case/arbitrary codes | raw JSON type validation, `[AllowNull()][object]`, case-sensitive tuples and phase-derived code map | fixed in draft; independent re-review pending |
| HIGH | evidence | Receipt одновременно описывался как manifest member, создавая self-hash ambiguity | exclude receipt from evidenceManifest; exact tree is receipt union manifest entries | fixed in draft; independent re-review pending |
| HIGH | lifecycle | Root creation, incomplete projection и post-finalizer assertion могли bypass promised fallback | put all recoverable work inside one finalizer boundary; distinguish catastrophic bootstrap/fallback and post-return tamper | fixed in draft; independent re-review pending |
| HIGH | schema | Early Signature failure не мог удовлетворить success-only graph/package evidence schema | add strict phase-dependent `signature-success`/`signature-failure`; sanitize failure is fallback-only | fixed in draft; independent re-review pending |
| MEDIUM | config | Verification env выставлялся после precondition вместо проверки caller-provided value | workflow sets true before attempt; precondition accepts exact true and null/online revocation only | fixed in draft; independent re-review pending |
| MEDIUM | filesystem | Same-volume rename был обещан, но не являлся structural invariant | create sibling roots under canonical parent; native filesystem identity and cross-volume fixtures | fixed in draft; independent re-review pending |
| MEDIUM | outcome | Multiple execution/publication failures не имели deterministic top-level precedence | define catastrophic, publication, unexpected, precondition and primary first-failure precedence | fixed in draft; independent re-review pending |
| MEDIUM | tests | Matrix не покрывала primary native failure, assertion failure и catastrophic/post-return variants | add explicit scenarios, AC8 mappings and adversarial fixtures | fixed in draft; independent re-review pending |
| LOW | approval state | Historical PASS/plan ordering противоречили current NEEDS-FIX | mark prior PASS superseded and put re-review before approval | fixed in draft; independent re-review pending |
| HIGH | validator trust | Earlier checkout/index/filter design still allowed mutable trust/config ambiguity | resolve raw object from exact expected commit, binary `cat-file`, recompute Git object id, execute only isolated temp copy; index is drift check only | superseded/fixed in fourth draft; re-review pending |
| HIGH | validation surface | Baseline/CI scanner contracts and fixtures omitted selected tree/index/mode cases, Desktop CI closure and unrelated gitlink allowance | close baseline schema/surface and add exact stage/mode/link/path/child fixtures | fixed in draft; independent re-review pending |
| HIGH | timeout/evidence | 45-minute job budget was lower than declared phase maxima; sanitizer/finalizer were not bounded | use 120-minute jobs, 95-minute attempt deadline and explicit 5-minute sanitizer/finalizer reserves | fixed in draft; independent re-review pending |
| HIGH | report/schema | Adapter vs sanitizer responsibility and native `0` vs report failure exit were contradictory; scalar types/ranges incomplete | retain `nativeExitCode`, use synthetic phase `2`, rebuild only in sanitizer and close all numeric/array/diagnostic variants | fixed in draft; independent re-review pending |
| HIGH | secrets | Single-layer generic encoded-secret wording did not cover composed encodings | bounded four-layer percent/Base64/JSON/XML decoder with fifth-layer rejection and exact fixtures | fixed in draft; independent re-review pending |
| HIGH | delivery/security | Required contexts alone left admin, force-push and disabled-ruleset bypasses | require `enforce_admins=true`, no force pushes, enabled no-bypass ruleset and preserved reviews from settings snapshot | fixed in draft; explicit user decision still required after technical PASS |
| MEDIUM | outcome | Unexpected failure plus incomplete projection had ambiguous precedence | unexpected flag selects fallback without primary projection; no-flag incompleteness is publication-integrity failure | fixed in draft; independent re-review pending |
| MEDIUM | tests | Earlier registries omitted new trust/cache/Full/TUnit/gate cases | replace with exact internally counted 647-case registry and synchronize all references | fixed in fifth draft; re-review pending |

- Fixed before continuing: prose, scenarios/state/decision/runtime matrices, AC8, canonical PowerShell skeleton, both YAML trusted-gate/upload pairs, test mapping, risks, file table, plan and review audit synchronized.
- Checks rerun: fourth-amendment author validation pending; previous 278/441 evidence is superseded.
- Needs human: требуется explicit user phrase `Спеку подтверждаю` именно для этой dependency/security spec; позднее отдельно потребуется protected-merge decision.
- Residual risks / follow-ups: helper implementation and adversarial fixtures are intentionally not executed or written before approval.

### Post-EXEC Review
- Статус: Не выполнен до EXEC.
- Scope reviewed: approved spec, exact diff, tests, CI, evidence after implementation.
- Decision: Не применимо до EXEC.

## Approval
Получено 2026-07-23: `Спеку подтверждаю` после явного указания файла `specs/2026-07-21-reactiveui-signature-chain-remediation.md`.

Approval распространяется на exact technical allowlist этой dependency/security spec. Protected-merge contract на `main` остаётся отдельным будущим решением после candidate CI с фактически observed contexts/app ids.

## 20. Журнал действий агента
| Фаза (SPEC/EXEC) | Тип намерения/сценария | Уверенность в решении (0.0-1.0) | Каких данных не хватает | Следующее действие | Нужна ли передача управления/решения человеку | Было ли фактическое обращение к человеку / решение человека | Короткое объяснение выбора | Затронутые артефакты/файлы |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- |
| SPEC | CI failure diagnosis | 0.99 | none | define prerequisite | Нет | Нет | revoked chain independently confirmed; no retry/bypass | run 29792038710, upstream releases |
| SPEC | scope design | 0.96 | role reviews | independent reviews | Нет | Нет | two pins plus three known workflow boundaries and permanent guard | this spec |
| SPEC | approval gate | 1.00 | user approval | request only after review PASS | Да | Нет | prior approvals do not cover dependency/runtime config | this spec |
| SPEC | orchestration/evidence hardening | 0.98 | independent re-review | run parser/fence/diff checks, then reviewers | Нет | Нет | separated CI lanes, eliminated native array boundary and kept raw/invalid bytes outside upload tree | this spec |
| SPEC | author validation | 0.99 | independent verdict | hand off to tester/architect/security re-review | Нет | Нет | AST parser, Markdown fences, final-LF/whitespace and no-index diff checks pass; no production file changed | this spec |
| SPEC | historical independent re-review | 1.00 | later audit not yet run | superseded by subsequent adversarial findings; do not request approval | Нет | Нет | historical PASS was valid for the then-current draft but was invalidated by later receipt/publication and upload-gate audits | this spec, historical reviewer verdicts |
| SPEC | final approval-readiness audit | 1.00 | receipt bootstrap and safe publication semantics | stop approval request and amend contract | Нет | Нет | early throws bypassed receipt; direct sanitizer writes could expose partial unsafe upload tree | this spec, audit findings |
| SPEC | receipt/publication amendment | 0.99 | independent re-review | rerun AST/fence/whitespace checks, then validation/architecture/security reviewers | Нет | Нет | raw preconditions now enter receipt envelope; candidate is validated before primary/fallback publication and safe marker | this spec |
| SPEC | second adversarial audit | 1.00 | ten upload/schema/lifecycle gaps | amend spec and keep approval blocked | Нет | Нет | marker-only upload, coercion, self-hash, finalizer boundary and phase-dependent evidence needed explicit contracts | this spec, independent audit |
| SPEC | second amendment author pass | 0.99 | independent re-review | dispatch tester/architect/security reviewers | Нет | Нет | all ten findings mapped into prose, skeleton, YAML, AC8, fixtures and plan; parser/fence/whitespace checks pass | this spec |
| SPEC | third independent review | 1.00 | validator trust, timeout/scalar/secret/branch/registry gaps | amend spec and keep approval blocked | Нет | Нет | three roles independently rejected the previous draft on concrete executable and evidence-contract gaps | this spec, third reviewer findings |
| SPEC | fifth amendment | 0.99 | author validation and independent re-review | rerun checks, freeze exact hash, dispatch reviewers | Нет | Нет | Stage-3 scope removed, wrapper/enforcement model made single-valued, 647 registry and observed-context contract synchronized, Debian trust boundary included | this spec |
| EXEC | Получить отдельное approval NuGet prerequisite | 1.00 | Нет | Commit spec и начать TDD implementation exact allowlist | Нет | Пользователь подтвердил 2026-07-23 | Approval дан после user-authorized adversarial fallback и явного указания файла | Эта spec |
| EXEC | initial signed dependency guard | 0.99 | final candidate evidence | update two direct pins and add isolated Linux Signature attempt | Нет | Нет | `ReactiveUI.Avalonia` 12.0.2 and `ReactiveUI` 23.2.28 restore with verification enabled; three affected projects and six expected packages were locally verified | `src/Directory.Packages.props`, `scripts/Test-NuGetSignatureChain.ps1`, `.github/workflows/tests.yml` |
| EXEC | least-privilege packaging handoff | 0.97 | candidate workflow CI | move publication out of read-only build jobs and make transfer digest-bound | Нет | Нет | Android and Debian build jobs now carry only `contents: read`; release jobs get exactly `actions: read, contents: write` and verify the REST artifact SHA-256 before release upload | `.github/workflows/android-packaging.yml`, `.github/workflows/deb_packaging.yml`, `src/Unlimotion.Test/CiReadmeMediaContract.cs` |
| EXEC | targeted local validation | 0.98 | final exact-SHA CI | commit and observe current candidate checks | Нет | Нет | isolated signature-enabled restore plus `StormCiReadmeMediaExecutableSpecTests` passed 1/1; `MainControlTaskCardLayoutUiTests` passed 20/20. Previous PR run failed four Windows layout tests, not reproduced locally; final candidate CI remains required | local TUnit report, run `30023005049` |
| EXEC | trusted Signature evidence | 0.97 | final exact-SHA CI and Regression evidence parity | commit raw-blob wrapper and observe uploaded receipt | Нет | Нет | producer restores three affected projects in a fresh root, binds six package verifications to author fingerprint `4D2DDD…E755CFB9`, writes receipt, and independent validator accepts only the closed receipt. A first Debug build exposed that restore had omitted conditional Diagnostics assets; `Configuration=Debug` now makes producer and Regression restore configuration-stable | `scripts/Test-NuGetSignatureChain.ps1`, `scripts/Test-NuGetEvidencePublication.ps1`, `.github/workflows/tests.yml`, `src/Unlimotion.Test/CiReadmeMediaContract.cs` |
| EXEC | Regression evidence parity | 0.98 | full exact-SHA regression attempt and candidate CI | commit the Windows lane, then run the isolated producer and validator | Нет | Нет | Windows `Regression` now uses the same expected-commit raw-blob extraction, closed receipt validation, safe-upload gate and separate verdict enforcement as `Signature`; static CI contract passed 1/1. This is an incremental infrastructure block, not a claim that the full security spec is complete. | `scripts/Test-NuGetSignatureChain.ps1`, `scripts/Test-NuGetEvidencePublication.ps1`, `.github/workflows/tests.yml`, `src/Unlimotion.Test/CiReadmeMediaContract.cs`, this spec |
| EXEC | candidate CI failure triage | 1.00 | sequential local contract recheck and fresh CI | correct wrapper interpolation and add regression assertion | Нет | Нет | Candidate run `30028073348` failed before either attempt because `$RepositoryPath:` is not valid PowerShell interpolation in the inline wrapper. Both wrapper copies now use `${RepositoryPath}:`; the contract asserts that form. A simultaneous local test collided with the ongoing full TUnit process, so it is deliberately deferred until that process exits. | `.github/workflows/tests.yml`, `src/Unlimotion.Test/CiReadmeMediaContract.cs`, this spec; run `30028073348` |
| EXEC | exact-SHA Regression fail-path validation | 0.99 | fresh candidate CI | validate receipt, isolate the one Unit failure, then push wrapper repair | Нет | Нет | On committed SHA `976729c`, a fresh isolated run completed restore/build and all 830 Unit tests; 829 passed and `NewTask_TitleNotResetAfterFileSave` failed once. The closed failure receipt was accepted by the independent validator with the exact five recorded phases. The failing test then passed in an isolated rerun, so it is recorded as an unconfirmed state/timing flake outside this CI-security scope. The wrapper interpolation repair and contract scenario passed sequentially. | temp receipt `unlimotion-regression-74e433de0bff4318a160ed2b1f0a190a`, `scripts/Test-NuGetEvidencePublication.ps1`, `.github/workflows/tests.yml`, `src/Unlimotion.Test/CiReadmeMediaContract.cs`, this spec |
| EXEC | current candidate CI | 1.00 | remaining baseline/worker/finalizer implementation | continue the next approved technical block without changing Stage 3 or branch protection | Нет | Нет | Exact SHA `021abb5b377668c3d7c5e6a091212a51f5bd0a09` is green in `Unlimotion Tests` (`Signature`, `Regression`, artifact upload and enforcement), `Unlimotion AndroidPkg`, and `CodeQL Advanced`. This validates the delivered incremental pins, verification, packaging split and receipt gates; the larger approved baseline/worker/finalizer contract remains unfinished. | GitHub Actions runs `30030703656`, `30030706119`, `30030703618`; this spec |
| EXEC | baseline generator foundation | 0.94 | external raw-blob execution and fixture generation | commit tooling-only generator before producing the immutable fixture | Нет | Нет | Added `GenerateBaseline`: it accepts only exact parent HEAD, existing isolated package root and absent output path; records regular Git input blobs, raw nupkg SHA-512 and canonical package graph hashes for Headless/Desktop/Debian. It is intentionally not yet claimed as the complete baseline/verifier contract. | `scripts/Test-NuGetSignatureChain.ps1`, this spec |
| EXEC | raw-blob generator characterization | 0.98 | parent-worktree package source validation and immutable fixture generation | add static guard, then execute generator against the parent only through the trusted launcher | Нет | Нет | The generator blob from `a25a6e3` was binary-extracted and Git-object rehashed before execution; it produced a temporary 87,877-byte full-graph JSON for the clean candidate. The declared parent `e11cae9…` exists locally, but the diagnostic output is deliberately not promoted to the baseline fixture. | temp `unlimotion-baseline-diagnostic-ca7a5d26942b41de949102401f763539`, `src/Unlimotion.Test/CiReadmeMediaContract.cs`, this spec |
| EXEC | parent graph staging correction | 1.00 | commit generator correction, then stage each assets file immediately after its restore | preserve separate Desktop and Debian graphs | Нет | Нет | The parent worktree was created at a short path after path-length failure in a long temp path. Parent restores pass with signature verification enabled, but Desktop and Debian share `obj/project.assets.json`; generator now requires separately staged Headless/Desktop/Debian assets files instead of accepting a silently overwritten path. | `scripts/Test-NuGetSignatureChain.ps1`, parent `e11cae9…`, this spec |
| EXEC | baseline fixture rejection | 1.00 | corrected raw-blob generation | fix serializer, commit it, then regenerate and revalidate the fixture | Нет | Нет | The repository contract was deliberately strengthened from string-presence checks to parsed JSON cardinality and correctly rejected the staged 87,566-byte file: it serialized only Headless (projects=1) instead of the required three project graphs. The file must not be committed in that form. | scripts/Test-NuGetSignatureChain.ps1, src/Unlimotion.Test/CiReadmeMediaContract.cs, distribution/fixtures/reactiveui-signature-chain-baseline.json, this spec |
| EXEC | corrected immutable parent baseline | 0.99 | verifier/worker implementation | use the parsed fixture as the parent anchor in the next technical block | Нет | Нет | Raw blob `60490f2d743768702c083879731e9402c131a26e` from committed `4954a44` was binary-extracted, re-hashed and executed against detached parent `e11cae9…`. The regenerated fixture has five input blobs, three canonical project paths, 111/90/89 packages and three distinct graph hashes; the strengthened TUnit contract passed 1/1 after replacement. | distribution/fixtures/reactiveui-signature-chain-baseline.json, scripts/Test-NuGetSignatureChain.ps1, src/Unlimotion.Test/CiReadmeMediaContract.cs, local TUnit report, this spec |
