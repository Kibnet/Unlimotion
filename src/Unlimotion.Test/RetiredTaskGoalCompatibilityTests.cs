using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.Storage;

namespace Unlimotion.Test;

public sealed class RetiredTaskGoalCompatibilityTests
{
    [Test]
    [Arguments("[\"work\"]")]
    [Arguments("[]")]
    [Arguments("null")]
    public async Task ReadingLegacyFileDoesNotRewriteIt_AndOrdinarySaveRetiresOnlyTheGoalKey(string areaJson)
    {
        using var directory = new TempNotesDirectory();
        var path = Path.Combine(directory.Path, "legacy-task");
        var json = "{\"Id\":\"legacy-task\",\"Title\":\"Legacy\",\"IsGoal\":true,\"AreaIds\":" + areaJson
            + ",\"custom\":{\"IsGoal\":\"keep\"}}";
        await File.WriteAllTextAsync(path, json);
        var bytes = await File.ReadAllBytesAsync(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var storage = new FileTaskStorage(new FileTaskStorageOptions { Path = directory.Path, UseWatcher = false });
        var task = (await storage.Load("legacy-task"))!;
        await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(bytes);
        await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(timestamp);
        await Assert.That(task.AreaIds is null).IsEqualTo(areaJson == "null");
        task.Title = "Edited";
        await storage.Save(task);
        var saved = JObject.Parse(await File.ReadAllTextAsync(path));
        await Assert.That(saved.Property("IsGoal")).IsNull();
        await Assert.That(saved["custom"]!["IsGoal"]!.Value<string>()).IsEqualTo("keep");
        await Assert.That(saved["Title"]!.Value<string>()).IsEqualTo("Edited");
    }

    [Test]
    [Arguments("IsGoal")]
    [Arguments("isGoal")]
    [Arguments("ISGOAL")]
    [Arguments("iSgOaL")]
    public async Task LegacyGoalIsIgnored_WithoutLosingOtherMetadata(string key)
    {
        var json = new JObject
        {
            ["Id"] = "legacy-task", ["Title"] = "Old task", [key] = true,
            ["AreaIds"] = new JArray("work"),
            ["custom"] = new JObject { ["IsGoal"] = "user-owned nested value" }
        };
        var task = JsonConvert.DeserializeObject<TaskItem>(json.ToString())!;
        await Assert.That(task.AreaIds).IsEquivalentTo(["work"]);
        await Assert.That(task.ExtensionData!.ContainsKey(key)).IsFalse();
        await Assert.That(task.ExtensionData["custom"]["IsGoal"]!.Value<string>()).IsEqualTo("user-owned nested value");
        var saved = JObject.Parse(JsonConvert.SerializeObject(task));
        await Assert.That(saved.Property(key)).IsNull();
        await Assert.That(saved["custom"]!["IsGoal"]!.Value<string>()).IsEqualTo("user-owned nested value");
    }

    [Test]
    public async Task ProgrammaticRetiredMetadata_IsNotSerializedOrCloned_AndSourceIsUntouched()
    {
        var original = new TaskItem
        {
            Id = "task", Title = "Task",
            ExtensionData = new Dictionary<string, JToken>
            {
                ["ISGOAL"] = true,
                ["custom"] = new JObject { ["IsGoal"] = true, ["keep"] = 42 }
            }
        };
        var data = original.ExtensionData;
        var before = JsonConvert.SerializeObject(data);
        var clone = TaskItemSnapshot.Clone(original);
        var saved = JObject.Parse(JsonConvert.SerializeObject(original));
        await Assert.That(saved.Property("ISGOAL")).IsNull();
        await Assert.That(clone.ExtensionData!.ContainsKey("ISGOAL")).IsFalse();
        await Assert.That(original.ExtensionData).IsSameReferenceAs(data);
        await Assert.That(JsonConvert.SerializeObject(data)).IsEqualTo(before);
        await Assert.That(clone.ExtensionData["custom"]).IsNotSameReferenceAs(data["custom"]);
        await Assert.That(clone.ExtensionData["custom"]["IsGoal"]!.Value<bool>()).IsTrue();
    }
}
