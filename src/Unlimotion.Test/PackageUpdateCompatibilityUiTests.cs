using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Unlimotion.ViewModel;
using Unlimotion.Views;
using Unlimotion.Views.Graph;

namespace Unlimotion.Test;

[NotInParallel("AvaloniaHeadless")]
[ParallelLimiter<SharedUiStateParallelLimit>]
public class PackageUpdateCompatibilityUiTests
{
    [Test]
    public async Task RoadmapDropAndFolderPickerCompatibility_Work()
    {
        await using var session = SafeHeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousPlatformPicker = Dialogs.PlatformOpenFolderDialogAsync;
            MainControl? view = null;
            Window? window = null;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var sourceTask = await vm.taskRepository!.Add();
                sourceTask.Title = "Package update drag source";
                var targetTask = await vm.taskRepository.Add();
                targetTask.Title = "Package update drag target";

                view = new MainControl { DataContext = vm };
                var targetControl = new ContentControl { DataContext = targetTask };
                using var dragData = DragDataFormats.CreateTransfer(GraphControl.CustomDataFormat, sourceTask);
                var dropArgs = new DragEventArgs(
                    DragDrop.DropEvent,
                    dragData,
                    targetControl,
                    new Point(1, 1),
                    KeyModifiers.Control);
                dropArgs.Source = targetControl;

                await MainControl.Drop(view, dropArgs);
                await Assert.That(await TestHelpers.WaitUntilAsync(
                        () =>
                        {
                            var currentSourceTask = TestHelpers.GetTask(vm, sourceTask.Id);
                            var currentTargetTask = TestHelpers.GetTask(vm, targetTask.Id);
                            return currentSourceTask.Blocks.Contains(targetTask.Id) &&
                                   currentTargetTask.BlockedBy.Contains(sourceTask.Id);
                        },
                        TimeSpan.FromSeconds(10)))
                    .IsTrue();
                Dispatcher.UIThread.RunJobs();
                var updatedSourceTask = TestHelpers.GetTask(vm, sourceTask.Id);
                var updatedTargetTask = TestHelpers.GetTask(vm, targetTask.Id);

                await Assert.That(dropArgs.Handled).IsTrue();
                await Assert.That(dropArgs.DragEffects).IsEqualTo(DragDropEffects.Link);
                await Assert.That(updatedSourceTask.Blocks).Contains(targetTask.Id);
                await Assert.That(updatedTargetTask.BlockedBy).Contains(sourceTask.Id);

                var selectedPath = Path.Combine(fixture.DefaultTasksFolderPath, "Selected");
                Dialogs.PlatformOpenFolderDialogAsync = (_, _) => Task.FromResult<string?>(selectedPath);

                var result = await new Dialogs().ShowOpenFolderDialogAsync("Data folder");

                await Assert.That(result).IsEqualTo(selectedPath);

                var startPath = Path.Combine(fixture.DefaultTasksFolderPath, "Start");
                Directory.CreateDirectory(startPath);
                window = new Window();
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var options = await Dialogs.CreateFolderPickerOpenOptionsAsync(
                    window.StorageProvider,
                    "Data folder",
                    startPath);

                await Assert.That(options.SuggestedStartLocation).IsNotNull();
                var suggestedPath = DialogExtensions.TryGetLocalPath(options.SuggestedStartLocation!);
                await Assert.That(suggestedPath).IsNotNull();
                await Assert.That(NormalizePath(suggestedPath!)).IsEqualTo(NormalizePath(startPath));
            }
            finally
            {
                Dialogs.PlatformOpenFolderDialogAsync = previousPlatformPicker;
                if (view != null)
                {
                    view.DataContext = null;
                }

                window?.Close();
                await DrainUiThreadAsync();
                await fixture.CleanTasksAsync();
                await DrainUiThreadAsync();
            }
        }, CancellationToken.None);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static async Task DrainUiThreadAsync(int quietMilliseconds = 200)
    {
        var drainUntil = DateTime.UtcNow.AddMilliseconds(quietMilliseconds);
        do
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        while (DateTime.UtcNow < drainUntil);

        Dispatcher.UIThread.RunJobs();
    }
}
