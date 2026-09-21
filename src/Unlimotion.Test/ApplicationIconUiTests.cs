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
    public async Task MainWindow_LoadsPackagedEIcon()
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
                // E combines a near-black body with a solid neutral white underlay.
                // Both must survive in every packaged size, not just the master.
                var darkPixels = 0;
                var whitePixels = 0;
                foreach (var pixel in bitmap.Pixels)
                {
                    if (pixel.Alpha > 200 && pixel.Red < 65 && pixel.Green < 65 && pixel.Blue < 90)
                        darkPixels++;
                    if (pixel.Alpha > 200 && pixel.Red > 225 && pixel.Green > 225 && pixel.Blue > 225)
                        whitePixels++;
                }
                await Assert.That(darkPixels >= bitmap.Width / 2).IsTrue();
                await Assert.That(whitePixels >= bitmap.Width / 2).IsTrue();
                await Assert.That(bitmap.GetPixel(0, 0).Alpha).IsEqualTo((byte)0);
                // Rasterization shifts the bowl centre between pixels. Require
                // a clear interior in its central region, not one rounded coordinate.
                var minimumAlpha = 255;
                for (var y = (int)(bitmap.Height * 0.45); y <= (int)(bitmap.Height * 0.52); y++)
                    for (var x = (int)(bitmap.Width * 0.64); x <= (int)(bitmap.Width * 0.73); x++)
                        minimumAlpha = Math.Min(minimumAlpha, bitmap.GetPixel(x, y).Alpha);
                await Assert.That(minimumAlpha).IsLessThanOrEqualTo(2);
                // The wide underlay must not touch the canvas, even at 16 px.
                for (var edge = 0; edge < bitmap.Width; edge++)
                {
                    await Assert.That(bitmap.GetPixel(edge, 0).Alpha).IsEqualTo((byte)0);
                    await Assert.That(bitmap.GetPixel(edge, bitmap.Height - 1).Alpha).IsEqualTo((byte)0);
                    // At 16 px the 9-unit margin is subpixel; antialiasing may
                    // reach the side pixel, but no opaque silhouette is clipped.
                    await Assert.That(bitmap.GetPixel(0, edge).Alpha).IsLessThan((byte)200);
                    await Assert.That(bitmap.GetPixel(bitmap.Width - 1, edge).Alpha).IsLessThan((byte)200);
                }
                if (bitmap.Width == 256)
                {
                    var minX = bitmap.Width;
                    var maxX = 0;
                    for (var y = 0; y < bitmap.Height; y++)
                        for (var x = 0; x < bitmap.Width; x++)
                            if (bitmap.GetPixel(x, y).Alpha > 200)
                            {
                                minX = Math.Min(minX, x);
                                maxX = Math.Max(maxX, x);
                            }
                    await Assert.That(maxX - minX + 1).IsGreaterThan(240);
                }
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
