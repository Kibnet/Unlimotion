using Microsoft.Extensions.Configuration;
using Splat;
using System;
using System.IO;
using Unlimotion.Test.Mocks;
using Unlimotion.ViewModel;
using WritableJsonConfiguration;

namespace Unlimotion.Test
{
    public class MainWindowViewModelFixture
    {
        private const string _defaultConfigName = "Settings.json";
        public MainWindowViewModel MainWindowViewModelTest { get; private set; }
        public const string RootTaskId = "05c23d1e-b720-43a4-9166-8dd8f38c345c";
        public static TaskItem RootTask = new TaskItem
        {
            Id = RootTaskId,
            Title = "Root Task 1",
            Description = "Root Task 1 Description"
        };

        public MainWindowViewModelFixture()
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _defaultConfigName);
            IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create(settingsPath);
            Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));
            var testStorageMock = new TestStorageMock();
            Locator.CurrentMutable.RegisterConstant<ITaskStorage>(testStorageMock);
            var taskRepositoryMock = new TaskRepositoryMock(testStorageMock);
            //taskRepositoryMock.Init();
            Locator.CurrentMutable.RegisterConstant<ITaskRepository>(taskRepositoryMock);

            MainWindowViewModelTest = new MainWindowViewModel();
        }
    }
}
