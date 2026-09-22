using System;
using System.IO;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;
using Unlimotion.Notes.Vault;

namespace Unlimotion.Test;

public class DailyNoteSettingsStoreTests
{
    [Test]
    public async Task MissingSidecarUsesLegacyDefaultWithoutCreatingAFile()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var store = new DailyNoteSettingsStore(vault);

        var loaded = await store.LoadAsync();

        await Assert.That(loaded.Revision).IsNull();
        await Assert.That(loaded.Settings.SchemaVersion).IsEqualTo(DailyNoteSettings.CurrentSchemaVersion);
        await Assert.That(loaded.Naming.FileNameFormat).IsEqualTo("yyyy-MM-dd");
        await Assert.That(File.Exists(vault.ResolveSafePath(DailyNoteSettingsStore.RelativePath))).IsFalse();
    }

    [Test]
    public async Task SaveAndReloadPreserveUnknownAdditiveFields()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        const string original = """
            {
              "schemaVersion": 1,
              "dailyFileNameFormat": "yyyy-MM-dd",
              "futureSetting": { "keep": true }
            }
            """;
        await vault.CreateAsync(DailyNoteSettingsStore.RelativePath, original);
        var store = new DailyNoteSettingsStore(vault);
        var loaded = await store.LoadAsync();

        var saved = await store.SaveAsync(
            loaded.Settings with { DailyFileNameFormat = "yyyy.MM.dd" },
            loaded.Revision);
        var written = (await vault.ReadAsync(DailyNoteSettingsStore.RelativePath))!.Text;
        var reloaded = await store.LoadAsync();

        await Assert.That(written.Contains("futureSetting", StringComparison.Ordinal)).IsTrue();
        await Assert.That(written.Contains("\"keep\": true", StringComparison.Ordinal)).IsTrue();
        await Assert.That(saved.Naming.FileNameFormat).IsEqualTo("yyyy.MM.dd");
        await Assert.That(reloaded.Naming.GetRelativePath(new DateOnly(2026, 8, 25)))
            .IsEqualTo("Ежедневные/2026.08.25.md");
    }

    [Test]
    public async Task SaveUsesOptimisticRevisionAndNeverOverwritesAnExternalSidecarChange()
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        var store = new DailyNoteSettingsStore(vault);
        var first = await store.SaveAsync(new DailyNoteSettings(), expectedRevision: null);
        await vault.WriteAsync(
            DailyNoteSettingsStore.RelativePath,
            "{\"schemaVersion\":1,\"dailyFileNameFormat\":\"dd.MM.yyyy\"}\n",
            first.Revision);

        var conflict = await NotesTestSupport.CaptureAsync<VaultRevisionConflictException>(() =>
            store.SaveAsync(first.Settings with { DailyFileNameFormat = "yyyy.MM.dd" }, first.Revision));
        var persisted = await store.LoadAsync();

        await Assert.That(conflict.RelativePath.Replace('\\', '/')).IsEqualTo(DailyNoteSettingsStore.RelativePath);
        await Assert.That(persisted.Naming.FileNameFormat).IsEqualTo("dd.MM.yyyy");
    }

    [Test]
    [Arguments("{\"schemaVersion\":1,\"dailyFileNameFormat\":\"yyyy/MM/dd\"}")]
    [Arguments("{\"schemaVersion\":2,\"dailyFileNameFormat\":\"yyyy-MM-dd\"}")]
    [Arguments("{\"schemaVersion\":1}")]
    [Arguments("{\"schemaVersion\":")]
    public async Task CorruptOrInvalidSidecarIsRejected(string json)
    {
        using var directory = new TempNotesDirectory();
        var vault = new FileNoteVault(directory.Path);
        await vault.CreateAsync(DailyNoteSettingsStore.RelativePath, json);
        var store = new DailyNoteSettingsStore(vault);

        var failure = await NotesTestSupport.CaptureAsync<InvalidDataException>(() => store.LoadAsync());

        await Assert.That(failure.Message).IsNotEmpty();
    }
}
