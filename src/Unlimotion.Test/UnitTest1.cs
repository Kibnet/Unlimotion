using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Aggregation;
using DynamicData.PLinq;
using Telerik.JustMock;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Xunit;

namespace Unlimotion.Test
{
    public class Tests
    {
        [Fact]
        public void CreateTaskItem()
        {
            var taskItem = new TaskItem();
            Assert.False(taskItem.IsCompleted);
            Assert.True((taskItem.CreatedDateTime- DateTimeOffset.UtcNow).TotalSeconds < 1);
            Assert.NotNull(taskItem.ContainsTasks);
            Assert.NotNull(taskItem.BlocksTasks);
        }


        /*[Fact]
        public async Task CreateTaskItemViewModel()
        {
            var storage = Mock.Create<ITaskStorage>();
            var list = new List<TaskItem>()
            {
                new TaskItem { Title = "Task 1", Id = "1", ContainsTasks = new List<string> { "1.1", "1.2", "3" } },
                new TaskItem { Title = "Task 1.1", Id = "1.1", BlocksTasks = new List<string> { "1.2" } },
                new TaskItem { Title = "Task 1.2", Id = "1.2", BlocksTasks = new List<string> { "3.1" } },
                new TaskItem { Title = "Task 2", Id = "2", ContainsTasks = new List<string> { "2.1", "3" } },
                new TaskItem { Title = "Task 2.1", Id = "2.1" },
                new TaskItem { Title = "Task 3", Id = "3", ContainsTasks = new List<string> { "3.1" } },
                new TaskItem { Title = "Task 3.1", Id = "3.1" },
            };
            Mock.Arrange(() => storage.GetAll().ToListAsync()).Returns(list);
            var repository = new TaskRepository(storage);
            repository.Init();
            var count = await repository.Tasks.Connect().Filter(m => m.Parents.Count == 0).Count().FirstAsync();
            Assert.Equal(2, count);

        }*/
    }
}