# Проверка ускоренного чтения современного JSON задач

## 0. Контекст / owner

Task1 e74c5d1a, существующие ветка/worktree; агент, Codex Windows, назначенная модель/effort без изменения. Central QUEST/performance/review/tool execution, dotnet desktop/storage; Expanded по canonical `_template.md`: существенная serializer-совместимость. Пользователь разрешил автономную работу и autoapproval после review/fixes. Только частный обратимый prototype в DROOT, product/runtime/storage config не меняется. Нет новой tracker задачи/ветки, Task2/3/merge/release. Источники: execution12Oct, remaining-experiments15Sep, single-read15Oct, surface diagnostic19Oct; current HEAD2afd4ab2, product exact43.

## 1. Цель / outcome / stop

Проверить, даёт ли отдельный строгий reader для canonical modern JSON достаточный выигрыш относительно текущего Json.NET при полном сохранении поведения. Выход: inspectable prototype, source/input hashes, parity/fallback tests, paired CPU/wall/allocations report и retain-for-next-design либо reject. Это Windows in-memory feasibility, не Android gain, не пользовательская готовность5/1/5. Ни один исход не включает prototype в продукт. Для product integration нужна отдельная дополненная SPEC/review.

Остановить на любой semantic mismatch, input drift, build/test failure, лимите120s benchmark или недостаточном выигрыше; не подбирать thresholds по результату и не перебирать повторно отклонённые варианты. Один frozen implementation/7pairs после correctness; найденную ошибку сначала исправить и заново пройти correctness, не исключать плохие пары молча.

## 2. AS-IS / проблема

Два отдельных successful baseline43 profile запуска19Oct: initialgraph12.800/11.961s, models5.721/6.112s, publication6.927/7.351s, wholeInit26.821/26.998s. Это не paired baseline/performance acceptance; process CPU/allocations пересекаются с фоновыми потоками. `FileTaskStorage.DeserializeTaskSnapshot` читает bytes, создаёт local Json.NET serializer/converters, использует cached immutable contract resolver, repair reader и SHA. `MigrateTaskStatusModel` имеет отдельный raw scan. Initialgraph включает оба; serializer не доказан единственной причиной12s. Изолированный A0 проверяет одну гипотезу до новой дорогой native серии.

N2/worker/buffers/single-read/bulk publication/Rx/INPC/runtime certificate уже отклонялись; не повторяем. Новый кандидат меняет только изолированную JSON→TaskItem операцию, без worker/cache/реактивных подписок. Caller и продукт пока прежние.

## 3. Дизайн / Non-goals

Private console `DROOT/modern-json-reader-probe1` net10.0, SDK из текущегоglobal.json (10.0.400/latestPatch, actual10.0.401 проверить), ProjectReference на unchanged FileStorage/Domain. Не добавлять NuGet/проект/код в solution. Существующие Json.NET serialization/repair и модели не меняются. Referenced build outputs направить вне source через `--artifacts-path` или project properties; подтвердить git diff после build.

Кандидат — DTO + System.Text.Json **metadata source generation**, не обещание serialization fast-path для deserialization. Generated metadata поддерживает deserialize; semantic differences documented: [source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation), [Newtonsoft migration](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/migrate-from-newtonsoft). Реальный выигрыш — гипотеза.

Строго ограниченный fast subset: canonical exact-case modern keys, supported primitive types/status/date/time/list values, complete required identities/timestamps/nested fields. DTO независим от Domain Json.NET attributes, mapped явно; обязательны Id/CreatedDateTime, все поля, чьё отсутствие меняет Domain constructor defaults (например criterion Id). Unknown properties на любом DTO уровне, unsupported legacy/casing/coercion/date/AgentExecution object, repair/BOM/UTF16 и duplicate property cases идут в **неизменённый baseline fallback** с исходными bytes/path. Никакое поле не молча теряется. AgentExecution null поддерживается, object — fallback; это начальное bounded решение, не скрытое сокращение итогового contract. StatusHistory/Criteria/Repeater map всех поддерживаемых полей; ExtensionData всегда остаётся baseline responsibility, неизвестное → fallback. Null collections/entries, missing required/default-sensitive values, enum coercions, duplicate/case variants и trailing content отдельно проверяются. Разрешено сузить subset ради доказанной совместимости, но coverage gate неизменен.

Baseline exact settings: shared DefaultContractResolver, **local** serializers/converters, IsoDateTimeConverter format/culture/styles + StringEnumConverter, actual public JsonRepairingReader.DeserializeWithRepair(bytes,path,serializer,false). Source-bound receipt включает FileTaskStorage/JsonRepairingReader/Domain hashes; benchmark parser settings сверяются с фактическим source. Ни одна ветка не вызывает Save/repair sidecar/ReadGraph и не создаёт lock/report в fixture.

## 4. Data / runtime / ответственность

| Область | Source of truth / действие | Проверка |
|---|---|---|
| Fixture | private `fold6-dataset`, manifest `directory-worker-fixture-task-sha256.json` | exact3094 names/SHA before+after; immutable bytes loaded once |
| Baseline | product2afd exact43 FileTaskStorage settings + public repair reader | source SHA/settings/parity assertions |
| Candidate | private DTO/context/mapper/strict subset + original-byte fallback | per-file candidate/fallback counts, no data writes |
| Outputs | fresh DROOT namespace, no overwrites | code/binary/build log/input/run SHA and elapsed/allocations |
| Product/CLI | unchanged | git diff/source hash; no tracker Completed/merge |

Public API/config/model/migration: no changes. Prototype удалить/не интегрировать при reject; исходные evidence сохранить. Сборка не означает установку/публикацию. Private fixture/task texts не коммитить.

## 5. Сценарии / state / invariants

| Trigger | Expected output | Evidence / AC |
|---|---|---|
| Canonical modern task bytes | Same complete TaskItem as baseline, fast subset or honest fallback | full projected state equality, A1/A2 |
| Unknown/legacy/odd nested/duplicate/encoding/error | Baseline semantics/exceptions, no field loss or repair writes | synthetic adversarial matrix, A1/A3 |
| Fixed3094 fixture | exact models, stable inventory, coverage and paired metrics | A2/A4 |
| Threshold miss/mismatch | reject, product unchanged | A5 |

UI storyboard остаётся Loading→same root→same card/action; этот probe не открывает UI, поэтому native visual/video/новые UI tests **не применимы к этой фазе**. Для product integration будут обязательны существующие UI tests и обычные Android APK paired root/card/action. Не переносить probe PASS на UI. State: input loaded→correctness→warmup→frozen seven pairs→disposition; error→stop/report, никакой live file write.

## 6. Acceptance / tests / commands

| AC | Verifier | Stop / artifact |
|---|---|---|
| A1 identical full model or baseline failure | Typed recursive oracle **before** serialization: exact runtime types, DateTimeOffset.Ticks+Offset.Ticks, DateTime.Ticks+Kind, TimeSpan.Ticks, enum values, null versus empty, ordered lists/duplicates, every public persisted field/nested value/ExtensionData and JToken type/value; Json.NET canonical tree only supplemental | any mismatch rejects; detailed case receipt |
| A2 exact3094 fixtures/models, coverage≥90% | original names/SHA, baseline vs candidate per-file; record fallback reason/count | drift/mismatch/lowcoverage rejects |
| A3 no mutation / strict fallback | unknown at each nested level, all declared scalar/list fields, omitted defaults, nulls, duplicates/casing, integer/string enums, DateTimeOffset offsets, TimeSpan, unicode/emoji, BOM/UTF16, repairs/trailing garbage; bytes unchanged | exact pass count / hashes |
| A4 useful isolated gain | 2excluded warmup passes each;7paired alternating B/C then C/B, same process/preloaded immutable bytes; wall+processCPU+totalallocated each full3094pass | median paired candidate/baseline wall≤.60, allocations≤.80, faster≥6/7; otherwise reject |
| A5 correct boundary | source/input hashes+git status, no production code/artifact install; no Android claim | report only, next product design gated |

Elapsed stopwatch around complete full-inventory deserialize includes fallback; no disk I/O inside timed block. Same SHA+clone add-on, if measured, reported separately and not substituted for parse gate. GC before both sides equally, outside timer; no forcedGC insidepass. `GC.GetTotalAllocatedBytes` and processCPU are process-wide; standalone no other benchmark worker, overhead/order/Windows vs Android limitations preserved. Store all7pairs and medians; no throughput extrapolation as user launch time. Max120s measured/warmup phase; compile≤180s subprocess bound. No AVD/media/tests concurrently.

Planned commands absolute pwsh7/dotnet10.0.401 paths: `dotnet build <privatecsproj> -c Release --artifacts-path <privatebuild>`; run resulting exact console DLL with `dotnet <dll>`, bounded180s including setup/benchmark120s. Targeted prototype checks plus unchanged existing relevant storage/JSON tests **before** possible integration; product full suites/UI CI не повторять только ради private probe. Postintegrating newproduct needs targeted→full→native/exactCI per separateSPEC. Generic dotnet paths must be resolved/pinned, no ambiguity.

## 7. Решения / риски / альтернативы

Agent chooses isolated new serializer hypothesis because initialgraph repeats~12s and prior I/O/Rx/cache alternatives have failed. Confidence .7 about opportunity, not predicted performance. User-owned equivalent choices absent, autoapproval standing. No scope/threshold relaxation or storage backend switch.

Objections: «Опять ускоритель без результата» → hard A0 rejection before native; «Потеряются неизвестные поля/старые файлы» → strict fallback original bytes, complete parity, no product integration; «Windows не Fold6» → feasibility only, later ordinaryAndroid required; «12s не только JSON» → explicit phase limitation, no claimed full gain. Costs: extra DTO maintenance/fallback can erase gain, startupsourcegenmetadata overhead included after measuredwarmup only; future coldmetadata cost remains unresolved until productdesign/ordinaryAndroid. Compare manual Utf8JsonReader: more mapping code/coercion risk; choose generatedmetadata prototype first, not both variants. Changing cache/canonicalstorage excluded by provenance/correctness history.

## 8. Files / before→after

One tracked SPEC; all prototype C#/csproj/helper/raw outputs in fresh DROOT privatefolder. Product parser/default behavior before=after, no API/persistedstate changes. If A0passes, next SPEC defines precise supported subset/product fast-path integration/characterization/UI/native absolute5/1/5 requirements; this document does not authorize integration automatically.

## 9. Quality gate / review

Author Scope/Contract/Adversarial/Role planning: no native/product write; whole-model/unknown/nested/default/duplicate/encoding/fallback path covered by plannedverifiers. Domain: compatibility; Tester: no omitted fields/fasterpath coverage; UX: original root/card scenario deferred explicitly, no fake UX PASS; Operations: privateoutputs/exactsource/boundedruns. Review advisory+author fallback, writable runtime limitation explicit. No EXEC yet. Linter20/20 planned coverage (outcome/ASIS/cause/design/boundaries; integrations/subset/errors/performance; data/rollback/compatibility; AC/commands/evidence; stops/decisions/objections; profile). Rubric5/5/5/5/5/5=30/30 **author planning only**, separate review may find issues; autoapprove only after PASS/fixes. Actual counts/timings unknown.

## 10. Журнал

SPEC19Oct: surface diagnostic insufficient/cleanup closed; two existing Coreprofiles re-read. Prepared bounded serializer feasibility, review pending; no prototype EXEC/product changes. Task1 active, PR325draft, remaining scope unchanged.

Preflight correction: absolute `C:/Program Files/dotnet/dotnet.exe --version` now returns **10.0.402**, not historical10.0.401. Use a private probe `global.json` pinned10.0.402/rollForward:disable, record actual SDK/runtime; baseline/candidate same runtime. Repo global.json and existing APK/source proof unchanged. This replaces planned10.0.401 command text above for this Windows-only probe; no Android/runtime equivalence claim. Duplicate detection option must be verified against installed net10 reference API, not newest web page (.NET11 preview can be shown despite view parameter).

### Parity review fixes, before EXEC

Два MEDIUM исправлены в плане: canonical JSON tree не основной oracle, т.к. .fff serialization и DateTimeOffset equality скрывают precision/offset. Typed recursive oracle перечислен A1; проверить его отрицательными controls: sub-ms date ticks, same-instant/different offset, TimeSpan ticks, null→empty, reordered/duplicate list, nested unknown JToken type/value. Для fixtures со всеми present dynamic defaults — exact typed equality без masking.

Отсутствующие CreatedDateTime/criterion.Id вызывают nondeterministic Domain defaults. Для **только этих fallback cases** не сравнивать две независимо созданные baseline модели и не маскировать arbitrary values. Candidate принимает test-only fallback delegate: capture exact original byte[] reference/path, вызвать **actual baseline method один раз**, сохранить возвращённый object/exception и доказать, что candidate вернул тот же object reference/то же exception instance. Дополнительно проверить generated timestamp в границах baseline call и generated valid GUID; present values всегда exact oracle. No alternate baseline implementation or mocked return as semantic proof; injected wrapper delegates to actual method. Такой dispatch test доказывает отсутствие промежуточной модификации всех полей, без wildcard masking. Timed benchmark всегда actual baseline delegate, и его полный overhead входит в candidate measurement.

Установленный `net10.0/10.0.13/System.Text.Json.xml` содержит JsonSerializerOptions/JsonSourceGenerationOptionsAttribute.AllowDuplicateProperties; prototype задаёт false, duplicate canonical/case variants → original baseline fallback. Нужен actual control, наличие API не доказывает поведение. Separate rereview pending.

Targeted fallback-proof correction: same reference само по себе не доказывает отсутствие мутации возвращённого объекта. Wrapper после **единственного actual baseline call, до возврата candidate** сохраняет независимый immutable typed deep snapshot всех полей, включая фактически сгенерированные defaults. После candidate return нужны exact typed state equality с этим snapshot +same reference и originalbyte/path checks. Snapshot — самостоятельное immutable представление (например exact typed fingerprint string), не shallow copy/ссылка на модель. Oracle negative control включает deliberate mutation returned object после wrapper: обязан стать красным. Second parse/masking/clock injection не нужны. Это заменяет чрезмерное утверждение «одна same-reference проверка доказывает отсутствие модификации» выше.

### Planning closure / autoapproval

Separate advisory targeted re-review PASS: оба oracle findings и последующее immutable-snapshot уточнение закрыты;20/20linter и30/30rubric planningonly. Author adversarial check: sub-ms/offset mutation и returned-object mutation теперь имеют отрицательные controls; dynamic defaults доказываются одним actual baseline dispatch без wildcard. Нет оставшихся user-owned решений. Автоматически подтверждено по прямому поручению пользователя. EXEC допускает только private feasibility, productintegration/Android/parent acceptance остаются закрытыми.

### Prototype implementation / correctness stop

Private build1 PASS. Actual code advisory3MEDIUM (implicit enum subset, source→binary binding, partial pair retention) исправлены до timing: strict canonical enum converters/explicit fallback tests; build3 44source+7DLL proof and loaded runtime assembly hashes; mutable warmup/pair/first/second/partial metrics attached to result before run and checkpointed outside measured blocks. Build2 failure от старого nested obj glob сохранён; compile input explicitly3sourcefiles, build3 PASS0warnings. Product source unchanged.

Run1 остановился на первой canonical typed parity, **ни одной timing пары/fixture performance gate ещё нет**. Separate run2 без изменения Reader добавил field-level diagnostic и повторил тот же failure: DateTimeOffset baseline/current converter переводит explicit nonlocal offsets в local+03, прямой STJ сохранял raw offset. Это не повод маскировать oracle или менять смысл данных. All44sources build3/4 archived before changes; original failed results preserved.

Узкое исправление mapping в private build5: custom DateTimeOffset converter использует `Utf8JsonReader.TryGetDateTime` и `new DateTimeOffset(date)` для UTC/local/unspecified interpretation исходного baseline DateTime token. Это гипотеза совместимого преобразования, не product timezone change;7additional lexical date controls (Z, +00, +09:30, -07, unspecified, extrema) проходят через тот же full typed oracle/fallback. Build5 PASS44sources7DLL0warnings, focused advisory review до следующего correctness→timing run pending. Все thresholds, original-byte fallback и forbid-integration прежние.

### Final EXEC disposition — REJECT на coverage gate

Date-subset review нашёл ещё один MEDIUM: STJ допускает date-only/reduced-time и8–16fractiondigits, тогда как baseline имеет другой parser/ParseExact. В build6 добавлен ASCII canonical full-time guard с секундами/≤7fractiondigits и12explicit fallback controls;7date-shape controls сохранены. Actual44source/7DLL/hash proof и focused advisory PASS перед run3. Старые build3/4/5 source snapshots и failed runs сохранены.

Run3: **72correctness/oracle controls PASS, exact typed parity3094/3094**. Fast2345, fallback749, coverage0.7579185520<0.90 → **REJECT**, без warmup/timing/7pairs. Порог не снижается, subset в этом эксперименте не расширяется, product integration/native/Android gain запрещены. Исходный result complete=false/errorCoverageGate сохранён; отдельный `modern-json-reader-probe1/run3-audit1.json` фиксирует итоговый reject и postfailure provenance. Все44buildsources, outputpayload, loaded runtime assemblies/input hashes и3094fixtureSHA после остановки совпали. Source product unchanged; SDK10.0.402/runtime10.0.13 только privateWindows.

Static schema analysis, **не actual per-file exception attribution**:671tasks имеют неизвестные root fields (SortOrder607,AreaIds65,IsGoal38; counts пересекаются),73 имеют AgentExecution object. Это объясняет ограниченность strict subset качественно; не объявлять точное распределение причин749fallback без отдельной трассы. Generic bridge с полным сохранением unknown data — возможный **новый** дизайн, не продолжение/перезапуск этого отвергнутого варианта и не разрешён данным документом. Для него нужна отдельная prospective SPEC с прежними gate thresholds/typed unknown fidelity. PostEXEC advisory запрошен; Task1InProgress/Active,5/1/5открыты,PR325draft,Task2/3/merge/release не выполнялись.

PostEXEC closure: separate advisory PASS для **REJECT disposition**, открытых findings нет:72/72,3094typedparity, coverageFAIL/0timing,44sources/7DLL/14executionpayload/6loadedassemblies/3094SHA verified. Author Scope/Contract/Adversarial/Role: исходный slowload не закрыт, narrowprototype rejectedbeforetiming, semantic datefixes не скрыты, unknown fidelity boundary не ослаблен, no integration/Android/user-readiness claim. Current oldoracle suffices for this strictsubset disposition; следующий design обязан исправить BigInteger magnitude oracle, т.к. unknown values начнут идти fast bridge. Отдельный advisory+authorfallback в writable runtime, technicalisolation не заявляется. Этот эксперимент завершён как отвергнутый; родительская Task1 продолжается.
