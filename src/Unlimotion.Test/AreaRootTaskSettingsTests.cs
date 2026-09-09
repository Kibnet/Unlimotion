using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test;

public class AreaRootTaskSettingsTests
{
    [Test]
    public async Task UpdatingAndClearingRootPreservesUnknownFieldsAndSiblingScopes()
    {
        const string json = """
            {"source-a":{"vault-a":{"area-a":{"RootTaskId":"old","futureRoot":true},"area-b":{"RootTaskId":"keep"}},"futureSource":42},"futureFlag":17}
            """;
        var config = Configuration(json);
        var store = new AreaRootTaskSettingsStore(config);
        store.SetRootTaskId("source-a", "vault-a", "area-a", "new");
        await Assert.That(store.GetRootTaskId("source-a", "vault-a", "area-a")).IsEqualTo("new");
        await Assert.That(store.GetRootTaskId("source-a", "vault-a", "area-b")).IsEqualTo("keep");
        store.SetRootTaskId("source-a", "vault-a", "area-a", null);
        var saved = JsonNode.Parse(config[AreaRootTaskSettingsStore.SectionName]!)!;
        await Assert.That(saved["source-a"]!["vault-a"]!["area-a"]!["futureRoot"]!.GetValue<bool>()).IsTrue();
        await Assert.That(saved["source-a"]!["futureSource"]!.GetValue<int>()).IsEqualTo(42);
        await Assert.That(saved["futureFlag"]!.GetValue<int>()).IsEqualTo(17);
        await Assert.That(store.GetRootTaskId("source-a", "vault-a", "area-a")).IsNull();
    }

    [Test]
    public async Task RootsRemainBoundToCapturedSourceAndVaultAcrossSwitchesAndReload()
    {
        var config = Configuration();
        var store = new AreaRootTaskSettingsStore(config);
        store.SetRootTaskId("source-a", "vault-a", "area", "a-root");
        store.SetRootTaskId("source-a", "vault-b", "area", "b-root");
        config["TaskSources:ActiveSourceId"] = "source-b";
        store.SetRootTaskId("source-b", "vault-a", "area", "other-root");
        store.SetRootTaskId("source-a", "vault-a", "area", "captured-root");
        config[TaskSourcesSettings.NoteProfilesSectionName] = "[]";
        var reloaded = new AreaRootTaskSettingsStore(config);
        await Assert.That(reloaded.GetRootTaskId("source-a", "vault-a", "area")).IsEqualTo("captured-root");
        await Assert.That(reloaded.GetRootTaskId("source-a", "vault-b", "area")).IsEqualTo("b-root");
        await Assert.That(reloaded.GetRootTaskId("source-b", "vault-a", "area")).IsEqualTo("other-root");
    }

    [Test]
    [Arguments("[")]
    [Arguments("[]")]
    [Arguments("{\"source-a\":42}")]
    [Arguments("{\"source-a\":{\"vault-a\":null}}")]
    public async Task CorruptSettingsFailWithoutOverwritingThem(string json)
    {
        var config = Configuration(json);
        var store = new AreaRootTaskSettingsStore(config);
        await Assert.That(() => store.SetRootTaskId("source-a", "vault-a", "area", "root"))
            .Throws<InvalidDataException>();
        await Assert.That(config[AreaRootTaskSettingsStore.SectionName]).IsEqualTo(json);
    }

    [Test]
    [Arguments("\"   \"")]
    [Arguments("42")]
    public async Task InvalidStoredRootNeverSilentlyBecomesAnUnparentedDefault(string rootValue)
    {
        var json = new JsonObject
        {
            ["source"] = new JsonObject
            {
                ["vault"] = new JsonObject
                {
                    ["area"] = new JsonObject { ["RootTaskId"] = JsonNode.Parse(rootValue) }
                }
            }
        }.ToJsonString();
        var config = Configuration(json);
        await Assert.That(() => new AreaRootTaskSettingsStore(config).GetRootTaskId("source", "vault", "area"))
            .Throws<InvalidDataException>();
        await Assert.That(config[AreaRootTaskSettingsStore.SectionName]).IsEqualTo(json);
    }

    [Test]
    public async Task RootDefaultsSurviveConfigurationFileReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"area-defaults-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        try
        {
            var config = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
            using var configLifetime = config as IDisposable;
            new AreaRootTaskSettingsStore(config).SetRootTaskId("source", "vault", "area", "root");
            var reloaded = WritableJsonConfigurationFabric.Create(path, reloadOnChange: false);
            using var reloadedLifetime = reloaded as IDisposable;
            await Assert.That(new AreaRootTaskSettingsStore(reloaded).GetRootTaskId("source", "vault", "area"))
                .IsEqualTo("root");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingIdentityFailsWithoutPersistingDefaults()
    {
        var config = Configuration();
        var store = new AreaRootTaskSettingsStore(config);
        await Assert.That(() => store.SetRootTaskId("source-a", "", "area", "root"))
            .Throws<ArgumentException>();
        await Assert.That(config[AreaRootTaskSettingsStore.SectionName]).IsNull();
    }

    private static IConfigurationRoot Configuration(string? json = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AreaRootTaskSettingsStore.SectionName] = json })
        .Build();
}
