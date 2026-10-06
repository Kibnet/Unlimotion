using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unlimotion.Domain;
using Unlimotion.Storage;
using Unlimotion.TaskTree;

namespace Unlimotion.Test;

public class CliStatusObservationReloadTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task VerifiedCapture_RegistersReloadAliasAfterForcedLoadFailure_WithoutSave(bool renamed, bool empty)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Unlimotion.CliStatusAlias", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string id = "logical-task";
            var original = Path.Combine(directory, "old-alias.json");
            var source = renamed ? Path.Combine(directory, "replacement-alias.json") : original;
            var bytes = JsonConvert.SerializeObject(new TaskItem { Id = id, Title = "Verified alias" });
            await File.WriteAllTextAsync(original, bytes);
            var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = directory });
            if (renamed)
            {
                // Learn the old alias without Save; observation must replace its retained reload mapping.
                await storage.ReadDirectoryAsync();
                File.Move(original, source);
            }

            var observation = await storage.ReadObservationAsync();
            await Assert.That(observation.Graph.FilesByTaskId[id]).IsEqualTo(source);
            await Assert.That(File.Exists(Path.Combine(directory, id))).IsFalse();
            var invalid = empty ? "" : "{not-json";
            await File.WriteAllTextAsync(source, invalid);
            await Assert.That(await storage.Load(id, forced: true)).IsNull();

            var failed = await storage.ReloadTaskAsync(id);
            await Assert.That(failed.Outcome).IsEqualTo(TaskReloadOutcome.Failed);
            await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo(invalid);

            await File.WriteAllTextAsync(source, bytes);
            var repaired = await storage.ReloadTaskAsync(id);
            await Assert.That(repaired.Outcome).IsEqualTo(TaskReloadOutcome.Loaded);
            await Assert.That(repaired.Snapshot!.Title).IsEqualTo("Verified alias");
            await Assert.That((await storage.ReadObservationAsync()).SourceManifestHash)
                .IsEqualTo(observation.SourceManifestHash);
            await Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName).ToArray())
                .IsEquivalentTo(new[] { Path.GetFileName(source) });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
