using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Unlimotion.Testing;

// Opt-in machine discovery and execution receipts. Ordinary dotnet test is unchanged.
public static class TestInventoryHooks
{
    [After(TestDiscovery)]
    public static void ExportInventory(TestDiscoveryContext context)
    {
        var path = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_INVENTORY_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        var tests = context.AllTests.Select(test => new
        {
            uid = test.Metadata.TestDetails.Identity.TestId,
            className = test.Metadata.TestDetails.Class.ClassType.FullName,
            method = test.Metadata.TestDetails.Identity.MethodName,
            displayName = test.Metadata.DisplayName,
            freshProcess = NeedsFreshProcess(test)
        }).OrderBy(test => test.uid, StringComparer.Ordinal).ToArray();
        var temporary = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { schemaVersion = 1, tests }));
        File.Move(temporary, path, overwrite: false);
    }

    private static bool NeedsFreshProcess(TestContext test)
    {
        var type = test.Metadata.TestDetails.Class.ClassType;
        return type.IsDefined(typeof(FreshTestProcessAttribute), inherit: true) ||
               type.GetMethods().Any(method => method.Name == test.Metadata.TestDetails.Identity.MethodName &&
                   method.IsDefined(typeof(FreshTestProcessAttribute), inherit: true));
    }

    [BeforeEvery(HookType.Test)]
    public static void Started(TestContext context) => WriteReceipt(context, "started");

    [AfterEvery(HookType.Test)]
    public static void Finished(TestContext context) => WriteReceipt(context, "finished");

    private static readonly object Sync = new();

    private static void WriteReceipt(TestContext context, string state)
    {
        var directory = Environment.GetEnvironmentVariable("UNLIMOTION_TEST_UID_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var row = JsonSerializer.Serialize(new
        {
            uid = context.Metadata.TestDetails.Identity.TestId,
            className = context.Metadata.TestDetails.Class.ClassType.FullName,
            displayName = context.Metadata.DisplayName,
            state,
            processId = Environment.ProcessId
        });
        lock (Sync)
        {
            File.AppendAllText(Path.Combine(directory, $"inventory-lifecycle-{Environment.ProcessId}.jsonl"), row + Environment.NewLine);
        }
        if (state == "started")
        {
            // TRX escapes invalid XML/control/surrogate characters in display names.
            // A process-qualified receipt binds the actual result to its stable UID.
            var uid = Convert.ToBase64String(Encoding.UTF8.GetBytes(context.Metadata.TestDetails.Identity.TestId));
            context.OutputWriter.WriteLine($"UNLIMOTION_TEST_UID:{Environment.ProcessId}:{uid}");
        }
    }
}

// Real Skia sessions must not share cached glyphs with semantic Headless sessions.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class FreshTestProcessAttribute : Attribute { }
