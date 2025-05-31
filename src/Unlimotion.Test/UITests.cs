using Avalonia.Headless;
using Avalonia.Threading;
using System.Threading.Tasks;
using Unlimotion.Views; // For MainWindow
using Xunit;
using Unlimotion.ViewModel; // For MainWindowViewModel, TaskItemViewModel, TaskItem
using System.Linq;      // For LINQ .FirstOrDefault()
using System;           // For Guid
using System.Reactive.Linq; // For .FirstAsync() on IObservable
using DynamicData.Binding; // Required for AsObservableList()

namespace Unlimotion.Test
{
    public class UITests
    {
        [Fact]
        public async Task MainWindow_Loads_Correctly_And_Has_Correct_Title()
        {
            // Initialize a headless Avalonia application using the modified BuildAvaloniaApp
            var app = AvaloniaApp.Setup(() => App.BuildAvaloniaApp(true));

            MainWindow? mainWindow = null;

            // Create and show the main window on the UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Let MainWindow create its own DataContext as it normally would
                mainWindow = new MainWindow();
                mainWindow.Show(); // Show the window to ensure it goes through its lifecycle
            });

            // Assert that the main window is created
            Assert.NotNull(mainWindow);

            // Assert that the main window title is "Unlimotion"
            // Access the Title property on the UI thread
            string? title = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                title = mainWindow?.Title;
            });
            Assert.Equal("Unlimotion", title);

            // Clean up the window and dispose the application
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                mainWindow?.Close(); // Close the window
            });
            app.Dispose(); // Dispose the headless application
        }

        [Fact]
        public async Task TaskCreation_Adds_New_Task_To_List()
        {
            var app = AvaloniaApp.Setup(() => App.BuildAvaloniaApp(true));
            MainWindow? mainWindow = null;
            MainWindowViewModel? viewModel = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                mainWindow = new MainWindow(); // MainWindow creates its own ViewModel
                mainWindow.Show();
                viewModel = mainWindow.DataContext as MainWindowViewModel;
            });

            Assert.NotNull(mainWindow);
            Assert.NotNull(viewModel);
            Assert.NotNull(viewModel.TaskRepository); // Ensure repository is available from ViewModel

            // Get initial task count from the repository.
            // TaskRepository.Tasks is SourceCache<TaskItem, string>
            // Count() returns IObservable<int>
            int initialCount = await viewModel.TaskRepository.Tasks.Connect().Count().FirstAsync();

            string newTaskTitle = "Test Task " + Guid.NewGuid().ToString();
            TaskItemViewModel? newlyCreatedVm = null; // This will be the ViewModel for the new task

            // Simulate task creation and title update
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel.CreateTaskCommand.Execute(null); // This creates a new task and sets it as SelectedTask
                if (viewModel.SelectedTask != null)
                {
                    // Set title on the ViewModel, which propagates to the TaskItem and triggers save
                    viewModel.SelectedTask.Title = newTaskTitle;
                    newlyCreatedVm = viewModel.SelectedTask;
                }
            });

            // Assert that a new task VM was selected and its title is set
            Assert.NotNull(newlyCreatedVm);
            Assert.Equal(newTaskTitle, newlyCreatedVm.Title);

            // Verify task count has increased in the repository
            int finalCount = await viewModel.TaskRepository.Tasks.Connect().Count().FirstAsync();
            Assert.Equal(initialCount + 1, finalCount);

            // Verify the task exists in the repository (TaskItem) with the correct title and ID
            // TaskRepository.Tasks.Connect() gives IObservable<IChangeSet<TaskItem, string>>
            // .AsObservableList() gives IObservableList<TaskItem>
            var observableList = viewModel.TaskRepository.Tasks.Connect().AsObservableList();

            // Accessing .Items should give the current snapshot of the list.
            // DynamicData typically updates collections synchronously unless a scheduler is specified.
            var createdTaskItem = observableList.Items.FirstOrDefault(t => t.Id == newlyCreatedVm.Id); // newlyCreatedVm.Id comes from underlying Task.Id

            Assert.NotNull(createdTaskItem); // Check if the TaskItem itself was found
            Assert.Equal(newTaskTitle, createdTaskItem.Title); // Check title on the persisted TaskItem

            // Clean up: remove the created task and close the window
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (createdTaskItem != null)
                {
                    // Assuming TaskRepository.Remove can take TaskItem or its ID
                    // If it takes ID: viewModel.TaskRepository.Remove(createdTaskItem.Id);
                    viewModel.TaskRepository.Remove(createdTaskItem);
                }
                mainWindow?.Close();
            });

            observableList.Dispose(); // IObservableList should be disposed
            app.Dispose(); // Dispose the headless application
        }

        [Fact]
        public async Task TaskCompletion_Marks_Task_As_Completed()
        {
            var app = AvaloniaApp.Setup(() => App.BuildAvaloniaApp(true));
            MainWindow? mainWindow = null;
            MainWindowViewModel? viewModel = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                mainWindow = new MainWindow();
                mainWindow.Show();
                viewModel = mainWindow.DataContext as MainWindowViewModel;
            });

            Assert.NotNull(mainWindow);
            Assert.NotNull(viewModel);
            Assert.NotNull(viewModel.TaskRepository);

            string taskTitle = "Test Completion Task " + Guid.NewGuid().ToString();
            TaskItemViewModel? taskToCompleteVm = null;
            TaskItem? persistedTaskItem = null;

            // 1. Create a new task specifically for this test
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                viewModel.CreateTaskCommand.Execute(null);
                if (viewModel.SelectedTask != null)
                {
                    viewModel.SelectedTask.Title = taskTitle;
                    taskToCompleteVm = viewModel.SelectedTask;
                }
            });

            Assert.NotNull(taskToCompleteVm);
            Assert.False(taskToCompleteVm.IsCompleted, "Task should initially be not completed.");

            // 2. Simulate task completion
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (taskToCompleteVm.ToggleCompleteCommand.CanExecute(null))
                {
                    taskToCompleteVm.ToggleCompleteCommand.Execute(null);
                }
            });

            // 3. Verify ViewModel reflects completion
            Assert.True(taskToCompleteVm.IsCompleted, "TaskViewModel should be marked as completed.");

            // 4. Verify the change is persisted in the repository
            var observableList = viewModel.TaskRepository.Tasks.Connect().AsObservableList(); // IObservableList<TaskItem>
            try
            {
                persistedTaskItem = observableList.Items.FirstOrDefault(t => t.Id == taskToCompleteVm.Id);
                Assert.NotNull(persistedTaskItem);
                Assert.True(persistedTaskItem.IsCompleted, "Persisted TaskItem should be marked as completed.");

                // Optional: Toggle again to check if it uncompletes
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (taskToCompleteVm.ToggleCompleteCommand.CanExecute(null))
                    {
                        taskToCompleteVm.ToggleCompleteCommand.Execute(null);
                    }
                });
                Assert.False(taskToCompleteVm.IsCompleted, "TaskViewModel should be marked as not completed after second toggle.");

                // Re-fetch or find the item again from observableList.Items
                persistedTaskItem = observableList.Items.FirstOrDefault(t => t.Id == taskToCompleteVm.Id);
                Assert.NotNull(persistedTaskItem);
                Assert.False(persistedTaskItem.IsCompleted, "Persisted TaskItem should be marked as not completed after second toggle.");
            }
            finally
            {
                observableList.Dispose(); // Ensure IObservableList is disposed
            }

            // Clean up: remove the created task and close the window
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (persistedTaskItem != null) // Use the latest fetched persistedTaskItem
                {
                    viewModel.TaskRepository.Remove(persistedTaskItem);
                }
                mainWindow?.Close();
            });
            app.Dispose();
        }
    }
}
