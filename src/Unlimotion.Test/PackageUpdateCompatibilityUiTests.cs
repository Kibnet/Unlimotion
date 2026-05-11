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

public class PackageUpdateCompatibilityUiTests
{
    [Test]
    public async Task RoadmapDropAndFolderPickerCompatibility_Work()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.DispatchAsync(async () =>
        {
            var fixture = new MainWindowViewModelFixture();
            var previousPlatformPicker = Dialogs.PlatformOpenFolderDialogAsync;

            try
            {
                var vm = fixture.MainWindowViewModelTest;
                await vm.Connect();

                var sourceTask = await vm.taskRepository!.Add();
                sourceTask.Title = "Package update drag source";
                var targetTask = await vm.taskRepository.Add();
                targetTask.Title = "Package update drag target";

                var view = new MainControl { DataContext = vm };
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
                await TestHelpers.WaitThrottleTime();
                Dispatcher.UIThread.RunJobs();

                await Assert.That(dropArgs.Handled).IsTrue();
                await Assert.That(dropArgs.DragEffects).IsEqualTo(DragDropEffects.Link);
                await Assert.That(sourceTask.Blocks).Contains(targetTask.Id);
                await Assert.That(targetTask.BlockedBy).Contains(sourceTask.Id);

                var selectedPath = Path.Combine(fixture.DefaultTasksFolderPath, "Selected");
                Dialogs.PlatformOpenFolderDialogAsync = (_, _) => Task.FromResult<string?>(selectedPath);

                var result = await new Dialogs().ShowOpenFolderDialogAsync("Data folder");

                await Assert.That(result).IsEqualTo(selectedPath);
            }
            finally
            {
                Dialogs.PlatformOpenFolderDialogAsync = previousPlatformPicker;
                fixture.CleanTasks();
            }
        }, CancellationToken.None);
    }
}
