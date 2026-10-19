# Проверка JSON-reader с сохранением дополнительных полей

## 0. Контекст / authority / форма

Та же Task1 e74c5d1a/branch/worktree, private Windows Codex, модель/effort без изменений. Expanded по central template: unknown-data preservation, два JSON object representations. Owners QUEST/performance/review/dotnet desktop/storage/tool execution. Пользователь разрешил autoapproval после review/fixes. Parent SPEC `2026-10-19-modern-json-reader-feasibility.md` остаётся **REJECT**; это новый prospective design, не снижение его90%/.60/.80 gates и не повтор той же реализации. Только private prototype2; product integration запрещена до отдельной SPEC/review. Task2/3/merge/release не начинаются.

## 1. Outcome / AS-IS / проблема

Private strict prototype1:72controls и3094typed parity прошли, но2345fast/749fallback=75.791855%<90% → stop до любых warmup/timing. Static schema на exact3094fixture:671tasks с unknownrootfields SortOrder607/AreaIds65/IsGoal38 (пересечения),73AgentExecutionobjects. Это не полный actualexceptiontrace749, но strictDTO unknown-key rejection доказан code/controls. Молчаливое удаление неизвестных полей недопустимо: Domain хранит их как JToken и сохраняет обратно.

Проверяем **новую** гипотезу: DTO reader + полноценный ExtensionData bridge может сохранить exact модель и увеличить coverage при полезном aggregate gain. Выход — complete compatibility/coverage/7pairs audit или reject, без app change/Android gain/parent5/1/5 claim. Native diagnostic30 закрыт insufficient и не повторяется ради warning. Existing coldprofiles initialgraph~12s/wholeInit~27s, JSON не единственная причина.

## 2. Решение / обязанности

New private `DROOT/modern-json-reader-probe2`, copied unchanged proven baseline/canonical enum/date guards из immutable prototype1 build6. Oracle получает указанную ниже **новую numeric revision**, old72 golden scenarios сохраняются. Private SDK10.0.402/runtime recorded, build output outside source; explicit Compile inputs предотвращают старый objglob. Separate fresh source/binary/output receipts. Product/source/CLI/storage APIs/defaults/schema прежние.

На TaskDto/HistoryDto/CriterionDto/RepeaterDto добавить `[JsonExtensionData] Dictionary<string,JsonElement>?` и убрать Disallow (extension data имеет собственный preservation contract). В mapper **каждое** значение переводится в Domain.ExtensionData через `JToken.Parse(element.GetRawText())` using pinned actual Newtonsoft13.0.4. Нет primitive coercion/manual selective field extraction, no reserialization of task files. Сохраняются JToken type/value, dates/kinds/precision, numeric types, list order/duplicates, nested objects и null. Unknown subtree использует тот же Json.NET token parser family; семантика подтверждается typed oracle, не предположением о равенстве JSON. Known typed fields/date converters/required defaults не меняются.

Guard до принятия fast model: неизвестный key, который совпадает **case-insensitively** с любым known/ignored Domain member, вызывает original-byte fallback (например title рядом с Title, criterion text, repeater period). Любой `$` metadata key на DTO extension level тоже whole-file fallback; legacy ignored TaskItem getters/setters IsCompleted/CompletedDateTime/ArchiveDateTime/StartedDateTime/PreparedDateTime fallback. Дубликаты остаются запрещены sourcegen options и идут baseline. Unknown dictionaries ordinal, порядок key/value сохраняется. Literal JSON property с именем ExtensionData/имя DTO extension property отдельно проверяется; при неоднозначности допускается fallback, не потеря данных. AgentExecution object по-прежнему fallback, null поддерживается; не расширять этот submodel.

Unknown JsonElement subtree тоже проверяет duplicates через `JToken.Parse(raw, JsonLoadSettings{DuplicatePropertyNameHandling=Error})`; не предполагать, что serializer option проверяет непрозрачный JsonElement recursively. Json.NET parse exception bridge → whole-file original baseline fallback. Для допустимого subtree иная token semantics не меняется; scalar/date/numeric fidelity обязана пройти oracle.

Весь bridge выполняется внутри narrow TryRead; JsonException/NotSupported (STJ либо bridge Json.NET parse exception) → **тот же original-byte/path baseline**, без partial mapped model. Другие ошибки не маскировать как supported read. Baseline converter/settings/repair flagfalse прежние. No Save/sidecar/lock/report/миграций/credentials. Никакого persistedcache или watcher/invalidation change.

## 3. Сценарии / runtime/data / state

| Trigger | Expected state/output | Proof |
|---|---|---|
| Modern task с scalar/array/nested extra fields | Same complete typed TaskItem/ExtensionData, fast либо honest fallback | typed recursive oracle +A1/A2 |
| Case variants/metadata/legacy/duplicates/noncanonicaldate | Actual unchanged baseline dispatch, no field loss | originalbytes/path/reference+immutable snapshot, A1 |
| Full3094samefixture | ≥90%fast, compatible all tasks, fixed metrics | A2/A3 |
| mismatch/lowcoverage/no gain | reject, product unchanged | A4 |

UI/user storyboard остаётся Loading→same root/card/action. Эта private phase не открывает UI, layout/state приложения не меняет; новые UItests/video неприменимы к этой фазе. Productintegration потребует existing Headless/UI и paired ordinaryAndroid root/card/action/absolute5/1/5. No proxy UX PASS.

| Contract | Source / boundary | Verification |
|---|---|---|
| Task fixture | private fold6-dataset +directory-worker-fixture-task-sha256.json | exact3094inventory/SHA before+after |
| Known model | original Domain+baseline settings | typed allfield state, defaults/precision/order |
| Unknown model | Domain JToken extensiondata | actual token types, DateTime kinds, numeric precision |
| Runtime | SDK10.0.402 pin, actualloadedassemblies/proof | source→build→DLL/deps/runtime hashes before/after |
| Outputs | fresh prototype2 namespaces | preserve all failed/partial data; no task texts publication |

State machine: sourcefreeze→correctness(all controls+fixtures)→coverage gate→2warmups each→7alternatingpairs→threshold/disposition. Stop at failure before next phase, no candidate retry after coverage/performance failure. Correctness bug may be fixed only with new source/binary freeze/full correctness rerun, bad artifacts retained.

## 4. AC / проверка / immutable oracle

Inherited source-bound baseline/private module/oracle definitions from previous SPEC, including **exact** DateTimeOffset ticks+offset, DateTime kind, TimeSpan ticks, runtime types, null/empty/order/duplicates, unknownJToken values. Nondeterministic missing defaults: actualbaseline once→immutable fulltyped snapshot before return→after compare/ref/originalbytes; generated timestamp/GUID invariants. No wildcard masking or rounded serialization oracle. 72existing controls preserved; new cases below supplement them.

**Oracle numeric revision (planning MEDIUM fix):** old reflection fallback не кодирует BigInteger magnitude. Private2 Oracle явно обрабатывает `System.Numerics.BigInteger`: runtime type + exact signed invariant decimal magnitude (либо эквивалентные signed bytes), новый sourceSHA frozen before run. Negative control обязан различить JValue integers `18446744073709551617` и `18446744073709551619` (одинаковые sign/parity/прочие flags), а также знак; неизменные old72 scenarios заново проходят revised oracle. Не маскировать unknown numbers и не сравнивать только rendered JSON/flags. Это scoped private oracle change, product unchanged.

| AC | Verifier / evidence | Stop |
|---|---|---|
| A1 full compatibility | all old72 controls +new unknown bridge tests at4DTOlevels: nested/null/arrays/duplicate order; integer/BigInteger/decimal-looking/double exponent/negativezero; DateTime-looking/offset/unicode strings; known-case collisions/metadata/ignoredmembers/unknown duplicate/literalExtensionData | any mismatch → no timing |
| A2 full fixture/current coverage | exact3094names+SHA/typedstate; perfilefast/fallback and actual fallback-reason histogram (capture outsidetiming); coverage≥.90 | below90% rejects |
| A3 meaningful isolated gain |2excluded warmups pervariant;7alternatingB/C,C/B pairs; entirepreloaded3094corpus+fallback included; wall/processCPU/allocated, allfull/partialsamples checkpointed | medianpairedwall≤.60, allocations≤.80, faster≥6/7; otherwise reject |
| A4 honest boundary/provenance | source/payload/actualruntime/fixtureSHA before+after, git productdiffempty, reviewedraw/disposition | no UI/Android/integration/parent PASS |

For unknown root/history/criterion/repeater positive preservation cases expectedFast=true when supported; ambiguity/metadata/case collisions expectedFast=false. Full numeric/date/JToken fingerprint proof обязательна: JSON text equality недостаточна. Add oracle negative controls changing nestedunknown value/type/datekind/precision and returnedobject mutation; inherited controls остаются.

GC symmetric outside timer, no file I/O/oracle/JSON reports inside timed interval. Same runtime/corpus/order, fullfallback overhead included, no data-dependent input filtering. Process-wide counters include runtime threads; Windows vs Android limitation. Save first/second sample as it completes; resultlist attached before loop; errors preserve raw stdout/stderr/partial metrics. Build≤180s, correctness/setup+run host180s, benchmarkwarmup+timing≤120s, no AVD/media/build/test concurrent with timing. No collection extension after bad pair/cutoff.

Planned commands pinned absolute dotnet.exe +private global.json, `dotnet build <prototype2.csproj> -c Release --artifacts-path <freshbuild>` then exact DLL via verified source/binary runner. Targeted controls execute beforebenchmark in samebounded console. Existing product full/UI/CI не повторять без productdelta; beforeintegration add actual targeted→full→Android suite in separateSPEC. One private hypothesis, no newNuGet or schema/backend change.

## 5. Решения / альтернативы / риски

Agent chooses preserving unknown data вместо их потери; generic JToken bridge соответствует Domain and avoids per-field legacy whitelists. Confidence .8 about semantic design, performance unknown. Existing75.8% subset staysrejected; no thresholdrelaxation. Whitelisting only SortOrder/AreaIds/IsGoal rejected as brittle/data-dependent. Raising supported AgentExecution to95fields excluded: ~2.4% fixture and no need for90%coverage. Fullserializer replacement and persistentcache excluded.

Objections: «Скрытый повтор rejected» → prospective newpreservationdesign, fresh sources/output, same hard gates; «Unknown types/dates изменятся» → JToken bridge exact typed/oracle tests, baselinefallback; «Bridge дороже самого reader» → completeinventorytiming includes bridge and73fallbacks, .60/.80 threshold. «Windows≠Fold6» → explicit privatefeasibility, no nativeclaim. Partial failures do not count toward7pairs. If unknown parser fidelity cannot be proved, reject; no masking unknown state.

Data/config/public API/model migration: no changes. Rollback = do not integrate/delete no artifacts needed; keep rejectedprototype and evidence. Do not commit data/C#/binaries from privatefolders; only SPEC eventually belongs to Task1 branch. Parent tracker result updated viaCLI/readback, no Completed until actual5/1/5 acceptance.

## 6. Files / quality gate / journal

Tracked: this SPEC. Private: copied Reader/Oracle/Program/csproj/globalpin plus small bridge, helpers/proofs/raw results in prototype2. Product before=after2afd/43, no formatter/storage policy or installs changed.

Author Scope/Contract/Adversarial/Role planning: DomainunknownJTokenroundtrip preservation, Developernarrowfallback+date/enumguard unchanged, Testerallnested/case/default/duplicate controls, Operationsfrozenowninput/boundedrun/partialretention. UXнеприменим кprivatephase, futureintegrationgated. Linter20 plannedcoverage coversoutcome/ASIS/cause/design/bounds; responsibilities/integrations/errors/performance; data/rollback/compatibility;AC/commands/evidence;stops/decisions/objections/profile. Rubric5/5/5/5/5/5 authorplanningonly; advisorymustreviewbeforeautoapproval. Runtimewritable, separateadvisory+authorfallback, no technicalisolationclaim. EXECnotstarted.

SPEC19Oct: based on completedreject/actualschema observations, prepared newunknown-preservingfeasibility. Planningreviewpending; no prototype2 code yet. CurrentTask1InProgress/Active/PR325draft.

Planning closure: separate advisory re-review PASS, BigInteger oracle MEDIUM закрыт exact magnitude/type revision+specificnegativecontrols; recursiveunknown duplicates explicitfallback.20/20linter/30/30rubric для плана. Author adversarial pass включает known-name/case collision, "$" metadata и два одинаково flagged больших integer; no masking/no loss. Автоматически подтверждено по поручению пользователя. Actual source review до timing обязателен; product/native integration запрещены.

EXEC19Oct: actual source advisory PASS до запуска. Private build1:45sources/7DLL bound,0warnings, SDK10.0.402/runtime10.0.13; product HEAD2afd4ab2 unchanged. run1:109correctness/oracle controls PASS, exacttyped parity3094/3094. Fast2965/fallback129, coverage95.830639948% passes90%; actual fallback73 root/AgentExecution object +56 Repeater.Type. Four excluded warmups and seven alternating pairs completed, faster7/7. Paired wall ratios .441436/.362471/.351646/.814331/.777145/.863280/.736840; median .7368396372 exceeds .60 hard gate. Allocation median .5011864557 passes .80. Disposition REJECT: no extension/retry/threshold change/product integration/Android gain claim. Quantized process CPU includes zero samples; no CPU gain or causal JIT/host-load claim. All partial/full samples retained. Private run1-audit1.json independently recomputes ratios/counts/warmups and verifies source/payload/loaded assemblies/runtime/3094task SHA before/after. Source result SHA256 7e8b5d2804c8668563e3190670ed42c28b52d2767f836ca5582c6fe58cc933cd. Author scope/contract/adversarial/role fallback: no data loss, no acceptance substitution, honest failed threshold; Task1 remains Active,5/1/5 open. Separate postEXEC advisory closure pending.

PostEXEC closure: separate advisory PASS for REJECT disposition, recomputed all7pairs/4warmups/109controls/3094branches and exact provenance45sources/7DLL/14payload/6loadedassemblies/3094fixtureSHA. Нет находок: Scope, Contract, Adversarial and relevant Developer/Tester/Operations passes closed; author fallback agrees. Writable runtime, read-only conduct; no technical independent isolation claim. Wall gate remains failed, experiment closed REJECT; no Android/product/parent acceptance.
