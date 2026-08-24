using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Unlimotion.Notes.Areas;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class DailyMarkdownAreaCatalogTests
{
    [Test]
    public async Task AreaCatalogRoundTripPreservesUnknownAdditiveFields()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string json = """
            {
              "schemaVersion": 1,
              "futureCatalogField": { "enabled": true },
              "areas": [
                {
                  "id": "root",
                  "name": "Работа",
                  "isArchived": false,
                  "sortOrder": 0,
                  "futureAreaField": "keep-me"
                }
              ]
            }
            """;
        await vault.CreateAsync(AreaCatalogStore.RelativePath, json);
        var store = new AreaCatalogStore(vault);
        var loaded = await store.LoadAsync();
        loaded.Catalog.Areas[0].Name = "Работа и проекты";

        await store.SaveAsync(loaded.Catalog, loaded.Revision);
        var written = (await vault.ReadAsync(AreaCatalogStore.RelativePath))!.Text;
        var reloaded = await store.LoadAsync();

        await Assert.That(written.Contains("futureCatalogField", StringComparison.Ordinal)).IsTrue();
        await Assert.That(written.Contains("futureAreaField", StringComparison.Ordinal)).IsTrue();
        await Assert.That(written.Contains("keep-me", StringComparison.Ordinal)).IsTrue();
        await Assert.That(reloaded.Catalog.Areas[0].Name).IsEqualTo("Работа и проекты");
    }

    [Test]
    public async Task ValidationRejectsSelfParentCycleAndMissingParent()
    {
        var self = new AreaCatalog { Areas = [Area("a", "a")] };
        var cycle = new AreaCatalog { Areas = [Area("a", "b"), Area("b", "a")] };
        var missing = new AreaCatalog { Areas = [Area("a", "missing")] };

        var selfFailure = await NotesTestSupport.Capture<InvalidDataException>(self.Validate);
        var cycleFailure = await NotesTestSupport.Capture<InvalidDataException>(cycle.Validate);
        var missingFailure = await NotesTestSupport.Capture<InvalidDataException>(missing.Validate);

        await Assert.That(selfFailure.Message.Contains("own parent", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cycleFailure.Message.Contains("cycle", StringComparison.Ordinal)).IsTrue();
        await Assert.That(missingFailure.Message.Contains("missing parent", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ArchiveKeepsAreaAndChildrenInsteadOfDeletingHierarchy()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var catalog = new AreaCatalog
        {
            Areas =
            [
                new AreaDefinition { Id = "root", Name = "Root", IsArchived = true },
                new AreaDefinition { Id = "child", Name = "Child", ParentId = "root" }
            ]
        };
        var store = new AreaCatalogStore(vault);

        var saved = await store.SaveAsync(catalog, expectedRevision: null);
        var reloaded = await store.LoadAsync();

        await Assert.That(saved.Revision).IsNotEmpty();
        await Assert.That(reloaded.Catalog.Areas.Count).IsEqualTo(2);
        await Assert.That(reloaded.Catalog.Areas[0].IsArchived).IsTrue();
        await Assert.That(reloaded.Catalog.Areas[1].ParentId).IsEqualTo("root");
    }

    private static AreaDefinition Area(string id, string? parentId) =>
        new() { Id = id, Name = id, ParentId = parentId };
}
