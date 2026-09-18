using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Unlimotion.IconGenerator;

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".");
var check = args.Contains("--check");
if (!File.Exists(Path.Combine(root, "src/Unlimotion/Unlimotion.csproj")))
    throw new ArgumentException("Pass the Unlimotion repository root.");
AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
byte[] Render(int size, bool opaque = false, bool adaptive = false)
{
    var scale = adaptive ? 0.69 : size <= 48 ? 1.1 : 1;
    var mark = new FrozenIconD { Width = 512 * scale, Height = 512 * scale, SmallIcon = size <= 48 };
    var control = new Border { Width = 512, Height = 512, Background = opaque ? Brushes.White : Brushes.Transparent, Child = mark };
    control.Measure(new Size(512, 512));
    control.Arrange(new Rect(0, 0, 512, 512));
    using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96d * size / 512, 96d * size / 512));
    bitmap.Render(control);
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    if (!opaque) return stream.ToArray();
    using var source = SKBitmap.Decode(stream.ToArray());
    using var rgb = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgb888x, SKAlphaType.Opaque));
    using (var canvas = new SKCanvas(rgb)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(source, 0, 0); }
    using var image = SKImage.FromBitmap(rgb);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
void Add(string path, byte[] bytes) => files.Add(path, bytes);
void Text(string path, string value) => Add(path, Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n")));
var sizes = new[] { 16, 24, 32, 48, 64, 96, 128, 192, 256, 512, 1024, 2048 };
var png = sizes.ToDictionary(n => n, n => Render(n));
foreach (var size in sizes) Add($"assets/branding/png/unlimotion-{size}.png", png[size]);
Add("assets/branding/master-D-2048.png", png[2048]);
Add("assets/branding/master-D-opaque-2048.png", Render(2048, true));
byte[] Ico()
{
    var selected = new[] { 16, 24, 32, 48, 64, 128, 256 };
    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)selected.Length);
    var offset = 6 + selected.Length * 16;
    foreach (var n in selected)
    {
        writer.Write((byte)(n == 256 ? 0 : n)); writer.Write((byte)(n == 256 ? 0 : n));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(png[n].Length); writer.Write(offset); offset += png[n].Length;
    }
    foreach (var n in selected) writer.Write(png[n]);
    return stream.ToArray();
}
byte[] Icns()
{
    using var stream = new MemoryStream();
    void BE(int value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, value); stream.Write(b); }
    stream.Write("icns"u8); BE(0);
    foreach (var (type, n) in new[] { ("icp4",16), ("icp5",32), ("icp6",64), ("ic07",128), ("ic08",256), ("ic09",512), ("ic10",1024), ("ic11",32), ("ic12",64), ("ic13",256), ("ic14",512) })
    { stream.Write(Encoding.ASCII.GetBytes(type)); BE(png[n].Length + 8); stream.Write(png[n]); }
    var length = (int)stream.Length; stream.Position = 4; BE(length); return stream.ToArray();
}
var ico = Ico();
Add("src/Unlimotion/Assets/Unlimotion.ico", ico);
Add("src/Unlimotion.Desktop/Assets/Unlimotion.ico", ico);
Add("src/Unlimotion.Desktop/Assets/Unlimotion.icns", Icns());
foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
    Add($"src/Unlimotion.Desktop/Assets/hicolor/{size}x{size}/apps/unlimotion.png", png[size]);
Add("src/Unlimotion.Android/Icon.png", Render(512, true));
foreach (var (density, size, fg) in new[] { ("mdpi",48,108), ("hdpi",72,162), ("xhdpi",96,216), ("xxhdpi",144,324), ("xxxhdpi",192,432) })
{
    Add($"src/Unlimotion.Android/Resources/mipmap-{density}/ic_launcher.png", Render(size, true));
    Add($"src/Unlimotion.Android/Resources/drawable-{density}/ic_launcher_foreground.png", Render(fg, adaptive: true));
}
Text("src/Unlimotion.Android/Resources/mipmap-anydpi-v26/ic_launcher.xml", "<adaptive-icon xmlns:android=\"http://schemas.android.com/apk/res/android\">\n  <background android:drawable=\"@android:color/white\" />\n  <foreground android:drawable=\"@drawable/ic_launcher_foreground\" />\n</adaptive-icon>\n");
foreach (var locale in new[] { "en-US", "ru-RU" }) Add($"fastlane/metadata/android/{locale}/images/icon.png", Render(512, true));
foreach (var directory in new[] { "wwwroot", "AppBundle" })
{
    Add($"src/Unlimotion.Browser/{directory}/favicon.ico", ico);
    foreach (var n in new[] { 32, 192, 512 }) Add($"src/Unlimotion.Browser/{directory}/icon-{n}.png", png[n]);
    Add($"src/Unlimotion.Browser/{directory}/apple-touch-icon.png", Render(180, true));
}
var ios = new List<object>();
foreach (var (idiom, points, scales) in new[] {
    ("iphone",20d,new[]{2,3}), ("iphone",29d,new[]{2,3}), ("iphone",40d,new[]{2,3}), ("iphone",60d,new[]{2,3}),
    ("ipad",20d,new[]{1,2}), ("ipad",29d,new[]{1,2}), ("ipad",40d,new[]{1,2}), ("ipad",76d,new[]{1,2}), ("ipad",83.5,new[]{2}), ("ios-marketing",1024d,new[]{1}) })
foreach (var scale in scales)
{
    var n = (int)(points * scale);
    var name = $"icon-{n}.png";
    var path = $"src/Unlimotion.iOS/Assets.xcassets/AppIcon.appiconset/{name}";
    if (!files.ContainsKey(path)) Add(path, Render(n, true));
    var p = points.ToString(System.Globalization.CultureInfo.InvariantCulture);
    ios.Add(new { idiom, size = $"{p}x{p}", scale = $"{scale}x", filename = name });
}
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
Text("src/Unlimotion.iOS/Assets.xcassets/AppIcon.appiconset/Contents.json", JsonSerializer.Serialize(new { images = ios, info = new { version = 1, author = "Unlimotion" } }, jsonOptions));
Text("src/Unlimotion.iOS/Assets.xcassets/Contents.json", "{\"info\":{\"version\":1,\"author\":\"Unlimotion\"}}\n");

// Validate every encoded bitmap and each embedded container image before touching destinations.
void Png(byte[] bytes, int? size = null)
{
    using var data = SKData.CreateCopy(bytes);
    using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Corrupt PNG");
    using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Corrupt PNG");
    if (decoded.Width != decoded.Height || (size.HasValue && decoded.Width != size)) throw new InvalidDataException("Wrong icon dimensions");
}
foreach (var (path, bytes) in files)
{
    if (path.EndsWith(".png")) Png(bytes);
    if (path.EndsWith(".ico"))
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        if (count != 7) throw new InvalidDataException("Incomplete ICO");
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 8));
            var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 12));
            Png(bytes.AsSpan(offset, length).ToArray(), bytes[entry] == 0 ? 256 : bytes[entry]);
        }
    }
    if (path.EndsWith(".icns"))
    {
        if (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4)) != bytes.Length) throw new InvalidDataException("Invalid ICNS length");
        for (var offset = 8; offset < bytes.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4));
            Png(bytes.AsSpan(offset + 8, length - 8).ToArray()); offset += length;
        }
    }
}
var manifest = files.Select(f => new { path = f.Key, bytes = f.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(f.Value)) }).ToArray();
Text("assets/branding/manifest.json", JsonSerializer.Serialize(manifest, jsonOptions) + "\n");
bool Matches(string path, byte[] expected) => File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(expected);
if (args.Contains("--self-test"))
{
    var fixture = Path.Combine(Path.GetTempPath(), $"unlimotion-icon-{Guid.NewGuid():N}.png");
    try
    {
        if (Matches(fixture, png[16])) throw new Exception("Missing-file check failed");
        File.WriteAllBytes(fixture, new byte[] { 1, 2, 3 });
        if (Matches(fixture, png[16])) throw new Exception("Corrupt-file check failed");
        var rejected = false;
        try { Png(new byte[] { 1, 2, 3 }); } catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Corrupt PNG was accepted");
        Console.WriteLine("Negative fixtures: missing and corrupt files rejected.");
    }
    finally { if (File.Exists(fixture)) File.Delete(fixture); }
}
var mismatches = files.Where(f => !Matches(Path.Combine(root, f.Key), f.Value)).Select(f => f.Key).ToArray();
if (check)
{
    foreach (var mismatch in mismatches) Console.Error.WriteLine($"Missing or stale: {mismatch}");
    if (mismatches.Length != 0) Environment.Exit(1);
}
else
{
    foreach (var (path, bytes) in files)
    {
        var target = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
    }
}
Console.WriteLine($"{(check ? "Verified" : "Generated")} {files.Count} icon resources; all PNG/ICO/ICNS payloads decoded.");
if (args.Contains("--preview"))
{
    using var sheet = new SKBitmap(1000, 850);
    using var canvas = new SKCanvas(sheet);
    canvas.Clear(SKColors.White);
    using var dark = new SKPaint { Color = new SKColor(32, 32, 32) };
    canvas.DrawRect(500, 0, 500, 850, dark);
    using var master = SKBitmap.Decode(png[512]);
    canvas.DrawBitmap(master, new SKRect(0, 0, 500, 500));
    canvas.DrawBitmap(master, new SKRect(500, 0, 1000, 500));
    foreach (var side in new[] { 0, 500 })
    {
        var x = side + 12;
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128 })
        {
            using var icon = SKBitmap.Decode(png[size]); canvas.DrawBitmap(icon, x, 520); x += size + 12;
        }
        using var mobile = SKBitmap.Decode(files["src/Unlimotion.Android/Resources/drawable-xxhdpi/ic_launcher_foreground.png"]);
        canvas.Save(); canvas.ClipPath(Circle(side + 125, 735, 90));
        canvas.DrawRect(side + 35, 645, 180, 180, new SKPaint { Color = SKColors.White });
        canvas.DrawBitmap(mobile, new SKRect(side - 10, 600, side + 260, 870)); canvas.Restore();
        using var store = SKBitmap.Decode(files["src/Unlimotion.Android/Icon.png"]);
        canvas.DrawBitmap(store, new SKRect(side + 280, 655, side + 460, 835));
    }
    var target = Path.Combine(root, "artifacts/validation/icon-d/contact-sheet.png");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using var image = SKImage.FromBitmap(sheet); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(target, data.ToArray());
    Console.WriteLine(target);
}
SKPath Circle(float x, float y, float radius) { var path = new SKPath(); path.AddCircle(x, y, radius); return path; }
