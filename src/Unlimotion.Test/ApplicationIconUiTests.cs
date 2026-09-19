using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class ApplicationIconUiTests
{
    [Test]
    public async Task MainWindow_LoadsPackagedDIcon()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            using var stream = AssetLoader.Open(new Uri("avares://Unlimotion/Assets/Unlimotion.ico"));
            using var data = new MemoryStream();
            stream.CopyTo(data);
            var bytes = data.ToArray();
            await Assert.That(BitConverter.ToUInt16(bytes, 4)).IsEqualTo((ushort)7);
            var expected = new[] { 16, 24, 32, 48, 64, 128, 256 };
            for (var i = 0; i < expected.Length; i++)
            {
                await Assert.That(bytes[6 + i * 16] == 0 ? 256 : bytes[6 + i * 16]).IsEqualTo(expected[i]);
                var entry = 6 + i * 16;
                var length = BitConverter.ToInt32(bytes, entry + 8);
                var offset = BitConverter.ToInt32(bytes, entry + 12);
                using var bitmap = SkiaSharp.SKBitmap.Decode(bytes.AsSpan(offset, length).ToArray());
                // The approved graphite body is blue-biased, not a white outline.
                // Inspect every embedded size, including the optical small variants.
                var graphitePixels = 0;
                foreach (var pixel in bitmap.Pixels)
                {
                    if (pixel.Alpha > 200 && pixel.Red >= 28 && pixel.Red < 150 &&
                        pixel.Blue > pixel.Red + 10)
                        graphitePixels++;
                }
                await Assert.That(graphitePixels >= bitmap.Width / 2).IsTrue();
                await Assert.That(bitmap.GetPixel(0, 0).Alpha).IsEqualTo((byte)0);
                // Optical scaling shifts the bowl centre between pixels. Require
                // a clear interior in its central region, not one rounded coordinate.
                var minimumAlpha = 255;
                for (var y = (int)(bitmap.Height * 0.45); y <= (int)(bitmap.Height * 0.52); y++)
                    for (var x = (int)(bitmap.Width * 0.64); x <= (int)(bitmap.Width * 0.73); x++)
                        minimumAlpha = Math.Min(minimumAlpha, bitmap.GetPixel(x, y).Alpha);
                await Assert.That(minimumAlpha).IsLessThanOrEqualTo(2);
            }
            var window = new MainWindow();
            try
            {
                window.Show();
                await Assert.That(window.Icon is not null).IsTrue();
                // Headless WindowIcon.Save is a stub; assert the packaged bytes,
                // and inspect the native title-bar icon in the recorded UI run.
                var root = new DirectoryInfo(AppContext.BaseDirectory);
                while (root != null && !Directory.Exists(Path.Combine(root.FullName, "assets", "branding"))) root = root.Parent;
                await Assert.That(root is not null).IsTrue();
                var expectedBytes = File.ReadAllBytes(Path.Combine(root!.FullName, "src", "Unlimotion", "Assets", "Unlimotion.ico"));
                await Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)))
                    .IsEqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expectedBytes)));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
