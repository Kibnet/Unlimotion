using System;
using System.Threading.Tasks;
using Unlimotion.Notes.Daily;

namespace Unlimotion.Test;

public class DailyNoteNamingTests
{
    [Test]
    [Arguments("yyyy-MM-dd", "2026-08-25")]
    [Arguments("yyyy.MM.dd", "2026.08.25")]
    [Arguments("dd_MM_yyyy", "25_08_2026")]
    [Arguments("yyyyMMdd", "20260825")]
    public async Task AcceptedNumericFormatsRoundTripAsSafeDailyPaths(string format, string expectedStem)
    {
        var naming = DailyNoteNaming.Create(format);
        var date = new DateOnly(2026, 8, 25);

        var parsed = naming.TryParseRelativePath($"Ежедневные/{expectedStem}.md", out var parsedDate);

        await Assert.That(naming.FormatStem(date)).IsEqualTo(expectedStem);
        await Assert.That(naming.GetRelativePath(date)).IsEqualTo($"Ежедневные/{expectedStem}.md");
        await Assert.That(parsed).IsTrue();
        await Assert.That(parsedDate).IsEqualTo(date);
    }

    [Test]
    [Arguments("")]
    [Arguments("yyyy.")]
    [Arguments("yyyy/MM/dd")]
    [Arguments("yyyy-MM-dd-HH")]
    [Arguments("yyyy-MM-dd-yyyy")]
    [Arguments("yyyy-M-dd")]
    [Arguments("yyyy MM dd")]
    [Arguments("yyyy#MM#dd")]
    [Arguments("yyyy.[MM].dd")]
    public async Task UnsupportedOrUnsafeFormatsAreRejected(string format)
    {
        var accepted = DailyNoteNaming.TryCreate(format, out var naming, out var validationError);
        var failure = await NotesTestSupport.Capture<ArgumentException>(() => DailyNoteNaming.Create(format));

        await Assert.That(accepted).IsFalse();
        await Assert.That(naming).IsNull();
        await Assert.That(validationError).IsNotEmpty();
        await Assert.That(failure.ParamName).IsEqualTo("fileNameFormat");
    }

    [Test]
    public async Task ParserAcceptsOnlyOneExactActiveDailyChild()
    {
        var naming = DailyNoteNaming.Create("yyyy.MM.dd");

        var dot = naming.TryParseRelativePath("Ежедневные\\2026.08.25.md", out var dotDate);
        var nested = naming.TryParseRelativePath("Ежедневные/archive/2026.08.25.md", out _);
        var hyphen = naming.TryParseRelativePath("Ежедневные/2026-08-25.md", out _);
        var upperExtension = naming.TryParseRelativePath("Ежедневные/2026.08.25.MD", out _);
        var thematic = naming.TryParseRelativePath("Темы/2026.08.25.md", out _);

        await Assert.That(dot).IsTrue();
        await Assert.That(dotDate).IsEqualTo(new DateOnly(2026, 8, 25));
        await Assert.That(nested).IsFalse();
        await Assert.That(hyphen).IsFalse();
        await Assert.That(upperExtension).IsFalse();
        await Assert.That(thematic).IsFalse();
    }

    [Test]
    public async Task DefaultNamingRetainsTheLegacyHyphenContract()
    {
        var date = new DateOnly(2026, 8, 25);

        await Assert.That(DailyNoteNaming.Default.FileNameFormat).IsEqualTo("yyyy-MM-dd");
        await Assert.That(DailyNoteNaming.Default.GetRelativePath(date)).IsEqualTo("Ежедневные/2026-08-25.md");
        await Assert.That(DailyNoteService.GetRelativePath(date)).IsEqualTo("Ежедневные/2026-08-25.md");
    }
}
