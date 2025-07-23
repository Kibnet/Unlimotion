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
        public TaskItem RootTask = new TaskItem
        {
            Id = RootTaskId,
            Title = "Root Task 1",
            Description = "Root Task 1 Description"
        };

        public MainWindowViewModelFixture()
        {
            Directory.CreateDirectory(DefaultTasksFolderPath);
            App.Init(_defaultConfigName);
            MainWindowViewModelTest = new MainWindowViewModel();

        }
    }
}
