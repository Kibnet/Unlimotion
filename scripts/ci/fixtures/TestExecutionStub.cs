using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

if (args.Contains("--sentinel")) { Thread.Sleep(30000); return 0; }
var mode = Environment.GetEnvironmentVariable("TEST_EXECUTION_STUB_FAULT");
var project = args[0].EndsWith("Unlimotion.Test.dll") ? "main" : "headless";
var tests = project == "main"
    ? new[] { Case("alpha", "Alpha"), Case("beta", "Beta"), Case("gamma", "Beta", true), Case("benchmark", "NightAgentWorkflowReplayTests") }
    : new[] { Case("headless", "Headless") };
if (args.Contains("--list-tests"))
{
    if (mode == "discovery" && project == "main") return 17;
    File.WriteAllText(Environment.GetEnvironmentVariable("UNLIMOTION_TEST_INVENTORY_PATH")!, JsonSerializer.Serialize(new { schemaVersion = 1, tests }));
    return 0;
}
var filter = Array.IndexOf(args, "--filter-uid");
var selected = tests.Where(test => filter < 0 || args.Skip(filter + 1).Contains(test.uid)).ToArray();
var directory = args.Single(arg => arg.StartsWith("--results-directory="))["--results-directory=".Length..];
var logRoot = Environment.GetEnvironmentVariable("TEST_EXECUTION_STUB_LOG")!;
Directory.CreateDirectory(logRoot);
var started = DateTimeOffset.UtcNow;
var isAlpha = selected.Any(test => test.uid == "alpha");
File.WriteAllText(Path.Combine(logRoot, $"started-{Environment.ProcessId}.txt"), project);
if (mode is "timeout" or "cancellation" && isAlpha) Thread.Sleep(30000);
Thread.Sleep(300);
var exit = mode == "failure" && isAlpha ? 2 : 0;
var failed = (mode is "failure" or "failure-zero-exit") && isAlpha;
var receipt = Path.Combine(directory, $"inventory-lifecycle-{Environment.ProcessId}.jsonl");
foreach (var test in selected)
{
    foreach (var state in new[] { "started", "finished" })
    {
        if (mode == "missing" && isAlpha && state == "finished") continue;
        File.AppendAllText(receipt, JsonSerializer.Serialize(new { test.uid, test.className, test.displayName, state, processId = Environment.ProcessId }) + Environment.NewLine);
        if (mode == "duplicate" && isAlpha && state == "started")
            File.AppendAllText(receipt, JsonSerializer.Serialize(new { test.uid, test.className, test.displayName, state, processId = Environment.ProcessId }) + Environment.NewLine);
    }
}
XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
new XDocument(new XElement(ns + "TestRun",
    new XElement(ns + "TestDefinitions", selected.Select(test => new XElement(ns + "UnitTest", new XAttribute("id", test.uid),
        new XElement(ns + "TestMethod", new XAttribute("className", test.className), new XAttribute("name", test.displayName))))),
    new XElement(ns + "Results", selected.Select(test => new XElement(ns + "UnitTestResult", new XAttribute("testId", test.uid),
        new XAttribute("executionId", Guid.NewGuid()), new XAttribute("testName", test.displayName),
        new XAttribute("duration", "00:00:00.300"), new XAttribute("startTime", started.ToString("o")), new XAttribute("endTime", DateTimeOffset.UtcNow.ToString("o")),
        new XAttribute("outcome", mode == "skip" && isAlpha ? "NotExecuted" : failed ? "Failed" : "Passed"),
        new XElement(ns + "Output", new XElement(ns + "StdOut", Marker(test, mode, isAlpha)))))),
    new XElement(ns + "ResultSummary", new XAttribute("outcome", failed ? "Failed" : "Completed"),
        new XElement(ns + "Counters", new XAttribute("total", selected.Length), new XAttribute("executed", selected.Length),
            new XAttribute("passed", failed ? 0 : selected.Length), new XAttribute("failed", failed ? selected.Length : 0)))))
    .Save(Path.Combine(directory, "results.trx"));
File.WriteAllText(Path.Combine(directory, "stub-report.html"), "<html>stub</html>");
File.WriteAllText(Path.Combine(logRoot, Environment.ProcessId + ".json"), JsonSerializer.Serialize(new
{
    project, uids = selected.Select(test => test.uid), startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
    workingDirectory = Environment.CurrentDirectory,
    renderedChild = Environment.GetEnvironmentVariable("UNLIMOTION_RENDERED_TEST_CHILD") == "1"
}));
return exit;

static TestCase Case(string uid, string type, bool fresh = false) => new(uid, "Unlimotion.Test." + type, uid, uid, fresh);
static string Marker(TestCase test, string? mode, bool isAlpha)
{
    if (isAlpha && mode == "trx-missing") return "No UID receipt";
    var uid = isAlpha && mode == "trx-wrong" ? "unexpected-uid" : test.uid;
    var marker = $"UNLIMOTION_TEST_UID:{Environment.ProcessId}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(uid))}";
    return isAlpha && mode == "trx-duplicate" ? marker + "\n" + marker : marker;
}
record TestCase(string uid, string className, string method, string displayName, bool freshProcess);
