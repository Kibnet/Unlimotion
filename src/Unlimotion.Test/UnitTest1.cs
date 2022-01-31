using System;
using NUnit.Framework;
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
    }
}