using System;
using Unlimotion.Domain;
using System.Threading.Tasks;

namespace Unlimotion.Test
{
    public class Tests
    {
        [Test]
        public async Task CreateTaskItem()
        {
            var taskItem = new TaskItem();
            await Assert.That(taskItem.IsCompleted).IsFalse();
            await Assert.That((taskItem.CreatedDateTime- DateTimeOffset.UtcNow).TotalSeconds < 1).IsTrue();
            await Assert.That(taskItem.ContainsTasks).IsNotNull();
            await Assert.That(taskItem.BlocksTasks).IsNotNull();
        }
    }
}
