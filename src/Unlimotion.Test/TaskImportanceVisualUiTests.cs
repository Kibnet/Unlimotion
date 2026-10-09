using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;
using Unlimotion.Views;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class TaskImportanceVisualUiTests
{
    [Test]
    public async Task Importance_CliApplyShowsSavedValueAfterFreshLoad()
    {
        if (await ImportanceRenderedProcess.RunIfNeededAsync(nameof(Importance_CliApplyShowsSavedValueAfterFreshLoad))) return;
        var persistedTask = await UnlimotionCliIntegrationTests.CreateCliImportanceTaskForUi();
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ImportanceRenderedAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new ImportanceCardFixture(persistedTask);
            await fixture.OpenAsync(1400, 12, false);
            await fixture.SettleSnapshotAsync();
            await Assert.That(fixture.Task.Importance).IsEqualTo(42);
            await Assert.That(fixture.Input.Value).IsEqualTo(42m);
            await Assert.That(fixture.TextBox.Text).IsEqualTo("42");
            var directory = ImportanceImageComparison.CreateArtifactDirectory("cli-importance-fresh-load");
            using var frame = fixture.CaptureComponent(directory);
            Console.WriteLine($"CLI importance fresh-load screenshot: {Path.Combine(directory, "card.png")}");
        }, CancellationToken.None);
    }

    [Test]
    public async Task Importance_All24States_MatchReviewedComponentBaselines()
    {
        if (await ImportanceRenderedProcess.RunIfNeededAsync(nameof(Importance_All24States_MatchReviewedComponentBaselines))) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ImportanceRenderedAppBuilder));
        await session.DispatchAsync(async () =>
        {
            var runDirectory = ImportanceImageComparison.CreateArtifactDirectory("matrix");
            var errors = new List<Exception>();
            foreach (var dark in new[] { false, true })
            foreach (var width in new[] { 360, 1400 })
            foreach (var fontSize in new[] { 12, 24 })
            foreach (var value in new[] { 0, 9, 100 })
            {
                var caseName = $"{(dark ? "dark" : "light")}-font{fontSize}-width{width}-value{value}";
                try
                {
                    await using var fixture = new ImportanceCardFixture();
                    await fixture.OpenAsync(width, fontSize, dark);
                    await fixture.EnterAsync(value);
                    var directory = Path.Combine(runDirectory, caseName);
                    using var actual = fixture.CaptureComponent(directory);
                    fixture.AssertGeometry();
                    await Assert.That(fixture.Input.FontSize).IsEqualTo((double)fontSize);
                    ImportanceImageComparison.MatchBaseline(actual, caseName + ".png", directory);
                }
                catch (Exception error) { errors.Add(new InvalidOperationException($"{caseName}: {error.Message}", error)); }
            }
            if (errors.Count > 0) throw new AggregateException("Importance visual matrix failed", errors);
        }, CancellationToken.None);
    }

    [Test]
    public async Task Importance_TypingMouseKeyboardAndReload_PreserveTheSavedValue()
    {
        if (await ImportanceRenderedProcess.RunIfNeededAsync(nameof(Importance_TypingMouseKeyboardAndReload_PreserveTheSavedValue))) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ImportanceRenderedAppBuilder));
        await session.DispatchAsync(async () =>
        {
            string persisted;
            MainWindowViewModel previousVm;
            await using (var fixture = new ImportanceCardFixture())
            {
                await fixture.OpenAsync(1400, 12, false);
                previousVm = fixture.Vm;
                await fixture.EnterAsync(9);
                await fixture.WaitForSavedAsync(9);
                await Assert.That(fixture.Task.Importance).IsEqualTo(9);
                var originalTitle = fixture.Task.Title;
                var originalStatus = fixture.Task.Status;
                var originalWanted = fixture.Task.Wanted;
                var originalRelations = Relations(fixture.Task);

                fixture.Click(fixture.Increase);
                await fixture.WaitForSavedAsync(10);
                fixture.Click(fixture.Decrease);
                await fixture.WaitForSavedAsync(9);
                fixture.TextBox.Focus();
                fixture.Press(Key.Home, PhysicalKey.Home);
                fixture.Press(Key.Up, PhysicalKey.ArrowUp);
                await fixture.SettleSnapshotAsync();
                await fixture.WaitForSavedAsync(10);
                fixture.TextBox.Focus();
                fixture.Press(Key.End, PhysicalKey.End);
                fixture.Press(Key.Down, PhysicalKey.ArrowDown);
                await fixture.SettleSnapshotAsync();
                await fixture.WaitForSavedAsync(9);
                await fixture.EnterAsync(0);
                fixture.Click(fixture.Decrease);
                await fixture.WaitForSavedAsync(0);
                fixture.TextBox.Focus();
                fixture.Press(Key.End, PhysicalKey.End);
                fixture.Press(Key.Down, PhysicalKey.ArrowDown);
                await fixture.SettleSnapshotAsync();
                await fixture.WaitForSavedAsync(0);
                await fixture.EnterAsync(100);
                fixture.Click(fixture.Increase);
                await fixture.WaitForSavedAsync(100);
                fixture.TextBox.Focus();
                fixture.Press(Key.Home, PhysicalKey.Home);
                fixture.Press(Key.Up, PhysicalKey.ArrowUp);
                await fixture.SettleSnapshotAsync();
                await fixture.WaitForSavedAsync(100);
                await Assert.That(fixture.Input.Increment).IsEqualTo(1m);
                await Assert.That(AutomationProperties.GetName(fixture.Input)).IsEqualTo(LocalizationService.Current.Get("Importance"));
                await Assert.That(fixture.Task.Title).IsEqualTo(originalTitle);
                await Assert.That(fixture.Task.Status).IsEqualTo(originalStatus);
                await Assert.That(fixture.Task.Wanted).IsEqualTo(originalWanted);
                await Assert.That(Relations(fixture.Task)).IsEqualTo(originalRelations);
                await fixture.EnterAsync(9);
                await fixture.WaitForSavedAsync(9);
                persisted = File.ReadAllText(fixture.TaskPath);
            }

            await using var reloaded = new ImportanceCardFixture(persisted);
            await reloaded.OpenAsync(1400, 12, false);
            await Assert.That(ReferenceEquals(previousVm, reloaded.Vm)).IsFalse();
            await Assert.That(reloaded.Task.Importance).IsEqualTo(9);
            await Assert.That(reloaded.Input.Text).IsEqualTo("9");
            await reloaded.SettleSnapshotAsync();
            var directory = ImportanceImageComparison.CreateArtifactDirectory("reload-9");
            using var actual = reloaded.CaptureComponent(directory);
            reloaded.AssertGeometry();
            ImportanceImageComparison.MatchBaseline(actual, "light-font12-width1400-value9.png", directory);
        }, CancellationToken.None);
    }

    [Test]
    public async Task Importance_ImageComparison_RejectsHiddenDigitAndHorizontalButtons()
    {
        if (await ImportanceRenderedProcess.RunIfNeededAsync(nameof(Importance_ImageComparison_RejectsHiddenDigitAndHorizontalButtons))) return;
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ImportanceRenderedAppBuilder));
        await session.DispatchAsync(async () =>
        {
            await using var fixture = new ImportanceCardFixture();
            await fixture.OpenAsync(1400, 12, false);
            await fixture.EnterAsync(9);
            var directory = ImportanceImageComparison.CreateArtifactDirectory("negative-controls");
            using var valid = fixture.CaptureComponent(directory);
            ImportanceImageComparison.MatchBaseline(valid, "light-font12-width1400-value9.png", directory);

            // Change only presentation: the stored value must remain 9 while its pixels disappear.
            fixture.TextBox.Foreground = Brushes.Transparent;
            using var hidden = fixture.CaptureComponent(Path.Combine(directory, "hidden-digit"));
            await Assert.That(() => ImportanceImageComparison.MatchBaseline(hidden,
                "light-font12-width1400-value9.png", Path.Combine(directory, "hidden-digit")))
                .Throws<InvalidOperationException>();
            await Assert.That(fixture.Input.Value).IsEqualTo(9m);
            fixture.TextBox.ClearValue(TextBox.ForegroundProperty);

            fixture.Input.GetVisualDescendants().OfType<StackPanel>()
                .Single(panel => panel.Name == "PART_SpinnerPanel").Orientation = Avalonia.Layout.Orientation.Horizontal;
            using var horizontal = fixture.CaptureComponent(Path.Combine(directory, "horizontal-buttons"));
            await Assert.That(() => ImportanceImageComparison.MatchBaseline(horizontal,
                "light-font12-width1400-value9.png", Path.Combine(directory, "horizontal-buttons")))
                .Throws<InvalidOperationException>();

            // Reproduce the old scoped style in the same actual MainWindow/font environment.
            fixture.ConfigureAppearance(1400, 24, false);
            fixture.Input.Classes.Remove("TaskHeaderImportanceInput");
            fixture.Input.Width = 86;
            fixture.Input.MinHeight = 32;
            fixture.Input.Margin = new Thickness(0, 0, 6, 4);
            using var legacy = fixture.CaptureComponent(Path.Combine(directory, "legacy-font24"));
            await Assert.That(() => fixture.AssertGeometry()).Throws<InvalidOperationException>();
        }, CancellationToken.None);
    }

    private static string Relations(TaskItemViewModel task) => string.Join("|",
        new[] { task.Parents, task.Contains, task.Blocks, task.BlockedBy }
            .Select(ids => string.Join(",", ids.OrderBy(id => id))));
}

public sealed class ImportanceRenderedAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .WithCustomFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class ImportanceCardFixture : IAsyncDisposable
{
    private readonly MainWindowViewModelFixture _fixture = new();
    private readonly ILocalizationService _previousLocalization = LocalizationService.Current;
    private readonly ThemeVariant? _previousTheme = Application.Current!.RequestedThemeVariant;
    private readonly double _previousFontSize = (double)Application.Current!.Resources["AppFontSize"]!;
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
    private readonly CultureInfo? _previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly TimeSpan _previousThrottle = TaskItemViewModel.DefaultThrottleTime;
    public MainWindowViewModel Vm => _fixture.MainWindowViewModelTest;
    public TaskItemViewModel Task => Vm.CurrentTaskItem!;
    public string TaskPath => Path.Combine(_fixture.DefaultTasksFolderPath, MainWindowViewModelFixture.RootTask2Id);
    public Window Window { get; private set; } = null!;
    private MainControl View { get; set; } = null!;
    public NumericUpDown Input => Find<NumericUpDown>("CurrentTaskImportanceInput");
    public TextBox TextBox => Input.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_TextBox");
    public RepeatButton Increase => Input.GetVisualDescendants().OfType<RepeatButton>().Single(button => button.Name == "PART_IncreaseButton");
    public RepeatButton Decrease => Input.GetVisualDescendants().OfType<RepeatButton>().Single(button => button.Name == "PART_DecreaseButton");

    public ImportanceCardFixture(string? persistedTask = null)
    {
        // Use a human-scale debounce in this isolated rendered process, instead of the suite's
        // 10 ms acceleration. Exercise normal autosave without changing production timing.
        TaskItemViewModel.DefaultThrottleTime = TimeSpan.FromMilliseconds(250);
        if (persistedTask != null) File.WriteAllText(TaskPath, persistedTask);
    }

    public Task OpenAsync(int width, int fontSize, bool dark)
    {
        Vm.Settings.LanguageMode = LocalizationService.RussianLanguage;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(LocalizationService.RussianLanguage);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        Vm.Settings.FontSize = fontSize;
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ApplyFontSize(fontSize);
        foreach (var key in LocalizationService.Current.GetResourceKeys(CultureInfo.InvariantCulture))
            Application.Current.Resources[key] = LocalizationService.Current.Get(key);
        return OpenCoreAsync();

        async Task OpenCoreAsync()
        {
            await Vm.Connect();
            Vm.AllTasksMode = true;
            Vm.DetailsAreOpen = true;
            TestHelpers.SetCurrentTask(Vm, MainWindowViewModelFixture.RootTask2Id);
            View = new MainControl { DataContext = Vm };
            Window = new MainWindow { Width = width, Height = width == 360 ? 844 : 900, Content = View, DataContext = Vm };
            Window.Show();
            Window.Activate();
            Drain();
            var splitView = View.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
            if (splitView != null) { splitView.IsPaneOpen = true; splitView.OpenPaneLength = Math.Min(width, 600); }
            Drain();
            if (Input.Bounds.Width <= 0) throw new InvalidOperationException("Importance card was not arranged.");
        }
    }

    public void ConfigureAppearance(int width, int fontSize, bool dark)
    {
        Vm.Settings.FontSize = fontSize;
        ApplyFontSize(fontSize);
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Window.Width = width;
        Window.Height = width == 360 ? 844 : 900;
        Drain();
        var splitView = View.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
        if (splitView != null) { splitView.IsPaneOpen = true; splitView.OpenPaneLength = Math.Min(width, 600); }
        Drain();
    }

    // The fixture VM is independent of App's runtime VM; apply the same production resource method.
    private static void ApplyFontSize(double size) => typeof(App)
        .GetMethod("ApplyFontSize", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(Application.Current, [size]);

    public async Task EnterAsync(int value)
    {
        TextBox.Focus();
        Press(Key.A, PhysicalKey.A, RawInputModifiers.Control);
        Window.KeyTextInput(value.ToString(CultureInfo.InvariantCulture));
        Press(Key.Tab, PhysicalKey.Tab);
        Find<CheckBox>("CurrentTaskWantedCheckBox").Focus();
        Drain();
        if (!await TestHelpers.WaitUntilAsync(() => { Drain(); return Task.Importance == value && Input.Value == value; }, TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException($"Typing {value} did not reach the model: text={TextBox.Text}, value={Input.Value}, model={Task.Importance}.");
        await SettleSnapshotAsync();
    }

    public async Task SettleSnapshotAsync()
    {
        Find<CheckBox>("CurrentTaskWantedCheckBox").Focus();
        Window.MouseMove(new Point(1, 1));
        await System.Threading.Tasks.Task.Delay(250);
        Drain();
    }

    public async Task WaitForSavedAsync(int value)
    {
        // A readable JSON file precedes completion of the save and its storage feedback.
        // Keep sequential edits separate from that feedback, without forcing a save.
        if (!await TestHelpers.WaitUntilAsync(() =>
        {
            Drain();
            try { return Task.Importance == value && JObject.Parse(File.ReadAllText(TaskPath))["Importance"]?.Value<int>() == value; }
            catch (IOException) { return false; }
        }, TimeSpan.FromSeconds(5))) throw new InvalidOperationException($"Autosave of Importance={value} did not complete: " +
            $"model={Task.Importance}, input={Input.Value}, text={TextBox.Text}; task file: {TaskPath}.");
        await Task.WaitForPendingSavesAsync();
        await System.Threading.Tasks.Task.Delay(500);
        Drain();
        if (Task.Importance != value || Input.Value != value ||
            !decimal.TryParse(TextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var displayed) || displayed != value ||
            JObject.Parse(File.ReadAllText(TaskPath))["Importance"]?.Value<int>() != value)
            throw new InvalidOperationException($"Saved Importance={value} did not stay synchronized: " +
                $"model={Task.Importance}, input={Input.Value}, text={TextBox.Text}.");
    }


    public void Click(Control control)
    {
        Drain();
        // Rendered hit testing needs an up-to-date composition scene before pointer input.
        using var frame = Window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless did not render a frame for pointer input.");
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("Button has no window coordinates.");
        Window.MouseMove(point);
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        Drain();
    }

    public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Console.WriteLine($"Before {key}: focus={TextBox.IsFocused}, model={Task.Importance}, value={Input.Value}, text={TextBox.Text}");
        Window.KeyPress(key, modifiers, physical, null);
        Window.KeyRelease(key, modifiers, physical, null);
        Drain();
        Console.WriteLine($"After {key}: focus={TextBox.IsFocused}, model={Task.Importance}, value={Input.Value}, text={TextBox.Text}");
    }

    public SKBitmap CaptureComponent(string directory)
    {
        Directory.CreateDirectory(directory);
        // Use the same full repaint for reused cards and freshly reloaded windows.
        foreach (var control in Input.GetVisualDescendants().Prepend(Input).OfType<Control>())
            control.InvalidateVisual();
        Drain();
        using var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Headless did not render pixels.");
        frame.Save(Path.Combine(directory, "card.png"));
        using var full = SKBitmap.Decode(Path.Combine(directory, "card.png"));
        var position = Input.TranslatePoint(default, Window) ?? throw new InvalidOperationException("Input has no window coordinates.");
        var scale = Window.RenderScaling;
        var crop = SKRectI.Create((int)Math.Floor(position.X * scale), (int)Math.Floor(position.Y * scale),
            (int)Math.Ceiling(Input.Bounds.Width * scale), (int)Math.Ceiling(Input.Bounds.Height * scale));
        var bitmap = new SKBitmap(crop.Width, crop.Height);
        if (!full.ExtractSubset(bitmap, crop)) { bitmap.Dispose(); throw new InvalidOperationException($"Invalid crop: {crop}."); }
        ImportanceImageComparison.Save(bitmap, Path.Combine(directory, "actual.png"));
        SaveLayout(directory);
        return bitmap;
    }

    public void SaveLayout(string directory)
    {
        var numberArea = TextBox.GetVisualDescendants().OfType<TextPresenter>().First();
        File.WriteAllText(Path.Combine(directory, "layout.txt"),
            $"Input={Input.Bounds}; TextBox={TextBox.Bounds}; TextPresenter={numberArea.Bounds}; " +
            $"Increase={Increase.Bounds}; Decrease={Decrease.Bounds}; " +
            $"Font={Input.FontFamily}/{Input.FontSize}; Scale={Window.RenderScaling}; Text={TextBox.Text}");
        File.WriteAllText(Path.Combine(directory, "metadata.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Avalonia = typeof(Window).Assembly.GetName().Version?.ToString(),
            Skia = typeof(SKBitmap).Assembly.GetName().Version?.ToString(),
            Font = Input.FontFamily.ToString(), Input.FontSize, Scale = Window.RenderScaling,
            WindowWidth = Window.Width, WindowHeight = Window.Height,
            Theme = Input.ActualThemeVariant.ToString(), Culture = CultureInfo.CurrentCulture.Name
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Importance artifacts: {directory}");
    }

    public void AssertGeometry()
    {
        var up = Increase.TranslatePoint(default, Input)!.Value;
        var down = Decrease.TranslatePoint(default, Input)!.Value;
        var text = TextBox.GetVisualDescendants().OfType<TextPresenter>().First();
        var formatted = new FormattedText(TextBox.Text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(TextBox.FontFamily), TextBox.FontSize, Brushes.Black);
        if (Math.Abs(up.X - down.X) > 0.5 || down.Y < up.Y + Increase.Bounds.Height - 0.5)
            throw new InvalidOperationException($"Spin buttons must be vertical: up={up}, down={down}.");
        if (text.Bounds.Width < formatted.Width || text.Bounds.Height < formatted.Height)
            throw new InvalidOperationException($"Number is clipped: presenter={text.Bounds}, required={formatted.Width}x{formatted.Height}.");
        if (Increase.Bounds.Width > 26 || Decrease.Bounds.Width > 26 || Increase.Bounds.Height < 16 || Decrease.Bounds.Height < 16)
            throw new InvalidOperationException("Spin buttons must be compact and clickable.");
        var scroll = Find<ScrollViewer>("CurrentTaskDetailsScrollViewer");
        var point = Input.TranslatePoint(default, scroll)!.Value;
        if (point.X < 0 || point.X + Input.Bounds.Width > scroll.Bounds.Width + 0.5)
            throw new InvalidOperationException("Importance overflows the task card.");
    }

    private T Find<T>(string id) where T : Control => View.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static void Drain() { Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs(); }

    public async ValueTask DisposeAsync()
    {
        if (Window != null) { Window.Content = null; Window.Close(); Drain(); }
        await _fixture.DisposeAsync();
        LocalizationService.Current = _previousLocalization;
        Application.Current!.RequestedThemeVariant = _previousTheme;
        ApplyFontSize(_previousFontSize);
        foreach (var key in _previousLocalization.GetResourceKeys(CultureInfo.InvariantCulture))
            Application.Current.Resources[key] = _previousLocalization.Get(key);
        CultureInfo.DefaultThreadCurrentCulture = _previousDefaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultUiCulture;
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
        TaskItemViewModel.DefaultThrottleTime = _previousThrottle;
        Drain();
    }
}
