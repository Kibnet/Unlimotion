using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unlimotion.ViewModel;

namespace Unlimotion.Test;

public sealed class EmojiTextHelperTests
{
    private static readonly Regex CorpusLine = new(
        @"^([0-9A-F ]+)\s*;\s*(fully-qualified|minimally-qualified|unqualified)\s*#");

    [Test]
    public async Task Unicode17_AllQualificationFormsAreRecognizedExactly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Unicode", "emoji-test-17.0.txt");
        var bytes = await File.ReadAllBytesAsync(path);
        await Assert.That(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant())
            .IsEqualTo("1d8a944f88d7952f7ef7c5167fef3c67995bcae24543949710231b03a201acda");
        var publicRegex = new Regex(EmojiTextHelper.EmojiPattern);
        var failures = new List<string>();
        var count = 0;

        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            var entry = CorpusLine.Match(line);
            if (!entry.Success) continue;
            var emoji = string.Concat(entry.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(codePoint => char.ConvertFromUtf32(Convert.ToInt32(codePoint, 16))));
            var title = $"До {emoji} после";
            var segments = EmojiTextHelper.Split(title);
            var emojiSegments = segments.Where(segment => segment.IsEmoji).ToArray();
            count++;

            if (!string.Equals(EmojiTextHelper.ExtractEmoji(title), emoji, StringComparison.Ordinal) ||
                !string.Equals(EmojiTextHelper.RemoveEmoji(title), "До  после", StringComparison.Ordinal) ||
                !string.Equals(string.Concat(segments.Select(segment => segment.Text)), title, StringComparison.Ordinal) ||
                emojiSegments.Length != 1 ||
                !string.Equals(emojiSegments[0].Text, emoji, StringComparison.Ordinal) ||
                !string.Equals(publicRegex.Match(title).Value, emoji, StringComparison.Ordinal))
            {
                failures.Add(entry.Groups[1].Value.Trim());
            }
        }

        await Assert.That(count).IsEqualTo(5216);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unicode17: {failures.Count}/{count} forms are incomplete. First: {string.Join("; ", failures.Take(10))}");
        }
    }

    [Test]
    [Arguments("🫶")]
    [Arguments("🪿")]
    [Arguments("🛝")]
    [Arguments("🫩")]
    [Arguments("🐦‍🔥")]
    [Arguments("🙂‍↔️")]
    [Arguments("🙂‍↕️")]
    [Arguments("🧑🏽‍💻")]
    [Arguments("🏳️‍⚧️")]
    [Arguments("🇷🇺")]
    [Arguments("1️⃣")]
    [Arguments("🏴\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F")]
    [Arguments("🏻")]
    [Arguments("🦰")]
    public async Task CompleteSequence_IsOneEmojiSegmentWithNoTextResidue(string emoji)
    {
        var text = $"Начало {emoji} конец";
        await Assert.That(string.Equals(EmojiTextHelper.ExtractEmoji(text), emoji, StringComparison.Ordinal)).IsTrue();
        await Assert.That(EmojiTextHelper.RemoveEmoji(text)).IsEqualTo("Начало  конец");
        await Assert.That(EmojiTextHelper.Split(text).Where(segment => segment.IsEmoji).Select(segment => segment.Text))
            .IsEquivalentTo([emoji]);
        await Assert.That(string.Concat(EmojiTextHelper.Split(text).Select(segment => segment.Text))).IsEqualTo(text);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("Обычный русский title 0123456789 # * !? . , / + =")]
    [Arguments("\uD83D")]
    [Arguments("\uDE00")]
    [Arguments("\U0001FB00")]
    public async Task PlainOrInvalidText_IsPreservedWithoutEmoji(string? text)
    {
        await Assert.That(EmojiTextHelper.ExtractEmoji(text)).IsEmpty();
        await Assert.That(EmojiTextHelper.RemoveEmoji(text)).IsEqualTo(text ?? string.Empty);
        await Assert.That(EmojiTextHelper.Split(text).Any(segment => segment.IsEmoji)).IsFalse();
        await Assert.That(string.Concat(EmojiTextHelper.Split(text).Select(segment => segment.Text)))
            .IsEqualTo(text ?? string.Empty);
    }

    [Test]
    public async Task SeveralEmojis_PreserveOrderAndExistingCombinedFilterKey()
    {
        const string text = "  🧭 Первая 🫶 вторая 🐦‍🔥 третья";
        await Assert.That(EmojiTextHelper.ExtractEmoji(text)).IsEqualTo("🧭🫶🐦‍🔥");
        await Assert.That(EmojiTextHelper.RemoveEmoji(text, trimStart: true)).IsEqualTo("Первая  вторая  третья");
        await Assert.That(EmojiTextHelper.Split(text).Where(segment => segment.IsEmoji).Select(segment => segment.Text))
            .IsEquivalentTo(["🧭", "🫶", "🐦‍🔥"]);
    }

    [Test]
    public async Task LongTitleAndRepeatedSequencePrefixes_PreserveTextAndEmojiBoundaries()
    {
        var prefix = new string('я', 8192);
        var tail = string.Concat(Enumerable.Repeat("🐦‍🔥 🧑🏽‍💻 🦰 ", 128));
        var input = prefix + tail;
        var segments = EmojiTextHelper.Split(input);
        await Assert.That(string.Equals(string.Concat(segments.Select(segment => segment.Text)), input, StringComparison.Ordinal)).IsTrue();
        await Assert.That(segments.Count(segment => segment.IsEmoji)).IsEqualTo(384);
        await Assert.That(EmojiTextHelper.RemoveEmoji(input)).IsEqualTo(prefix + new string(' ', 384));
    }
}
