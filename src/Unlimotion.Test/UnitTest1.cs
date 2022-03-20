using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData.Aggregation;
using NUnit.Framework;
using Telerik.JustMock;
using Unlimotion.ViewModel;

namespace Unlimotion.Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void CreateTaskItem()
        {
            var taskItem = new TaskItem();
            Assert.IsNotNull(taskItem.Id);
            Assert.IsNotEmpty(taskItem.Id);
            Assert.IsFalse(taskItem.IsCompleted);
            Assert.IsTrue((taskItem.CreatedDateTime- DateTimeOffset.UtcNow).TotalSeconds < 1);
            Assert.IsNotNull(taskItem.ContainsTasks);
            Assert.IsNotNull(taskItem.BlocksTasks);
        }


        [Test]
        public async Task CreateTaskItemViewModel()
        {
            var storage = Mock.Create<ITaskStorage>();
            var list = new[]
            {
                new TaskItem { Title = "Task 1", Id = "1", ContainsTasks = new List<string> { "1.1", "1.2", "3" } },
                new TaskItem { Title = "Task 1.1", Id = "1.1", BlocksTasks = new List<string> { "1.2" } },
                new TaskItem { Title = "Task 1.2", Id = "1.2", BlocksTasks = new List<string> { "3.1" } },
                new TaskItem { Title = "Task 2", Id = "2", ContainsTasks = new List<string> { "2.1", "3" } },
                new TaskItem { Title = "Task 2.1", Id = "2.1" },
                new TaskItem { Title = "Task 3", Id = "3", ContainsTasks = new List<string> { "3.1" } },
                new TaskItem { Title = "Task 3.1", Id = "3.1" },
            };
            Mock.Arrange(() => storage.GetAll()).Returns(list);
            var repository = new TaskRepository(storage);
            var count = await repository.GetRoots().Count().FirstAsync();
            Assert.AreEqual(2, count);

        }
    }
}