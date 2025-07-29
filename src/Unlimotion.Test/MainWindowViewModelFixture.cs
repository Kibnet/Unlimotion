using Avalonia.Notification;
using Splat;
using System;
using System.IO;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public class MainWindowViewModelFixture
    {
        private const string _defaultConfigName = "TestSettings.json";
        private const string _defaultTasksFolderName = "Tasks";
        public MainWindowViewModel MainWindowViewModelTest { get; private set; }
        public string DefaultTasksFolderPath => Path.Combine(Environment.CurrentDirectory, _defaultTasksFolderName);
        public string DefaultSnapshotsFolderPath => Path.Combine(Environment.CurrentDirectory, "Snapshots");
        public string DefaultRootTasPath => Path.Combine(DefaultSnapshotsFolderPath, RootTaskId);
        public const string RootTaskId = "baaf00ad-e250-4828-8bec-a6b42525fda0";
        public const string RootTask2Id = "10c107c1-a6f0-41fe-9b44-1f2fc5ff0fcf";
        public const string SubTask22Id = "53c5b18d-3818-4467-b8bb-0346b21ebbc7";  
        public const string RootTask3Id = "d63fbe66-4a91-44e4-b704-d85091831c56";
        public const string BlockedTask2Id = "a1d12137-8bca-46d2-bb1c-a413149123d8";
        public const string RootTask4Id = "c119a20a-6b75-40df-97c2-d2ca3822085f";
        public const string SubTask41Id = "b5a0c236-e738-4619-8f2a-d9454414fe6f";
        public const string ArchiveTask1Id = "0f154faf-7e8e-4cb2-9824-c9f1bfcf1984";
        public const string ArchiveTask11Id = "c136273b-99c9-4157-a8f2-5a128cb8b6de";
        public const string ArchivedTask1Id = "f6c3c536-217a-4190-b548-4d41a5c88bc2";
        public const string ArchivedTask11Id = "35250eba-d745-4928-ae1c-740601a71b58";
        public const string CompletedTaskId = "a0cc3a70-1fb1-41f7-895c-c3425d893d39";

        public TaskItem RootTask = new TaskItem
        {
            Id = RootTaskId,
            Title = "Root Task 1",
            Description = "Root Task 1 Description"
        };

        public MainWindowViewModelFixture()
        {
            Directory.CreateDirectory(DefaultTasksFolderPath);
            CopyTaskFromSnapshotsFolder();
            App.Init(_defaultConfigName);

            var notificationMessageManagerMock = new NotificationManagerWrapperMock();
            Locator.CurrentMutable.RegisterConstant<INotificationManagerWrapper>(notificationMessageManagerMock);
            MainWindowViewModelTest = new MainWindowViewModel();

        }

        private void CopyTaskFromSnapshotsFolder()
        {
            string[] files = Directory.GetFiles(DefaultSnapshotsFolderPath);

            foreach (string file in files)
            {
                var fileInfo = new FileInfo(file);
                var newFilePath = Path.Combine(DefaultTasksFolderPath, fileInfo.Name);
                fileInfo.MoveTo(newFilePath, true);
            }
        }
    }
}
