using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using NUnit.Framework;
using FluentAssertions;
using ServiceStack;
using Raven.Client.Documents.Session;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.Server.Domain;
using Raven.Client.Documents;



namespace Unlimotion.Test;

[TestFixture]
public class HttpTaskApiTests
{
    private JsonServiceClient serviceClient;
    private readonly IDocumentStore store;

    public HttpTaskApiTests()
    {
        serviceClient = ConfigureServiceClient();  //http - клиент
        store = GetDocumentStore();                //RavenDb - клиент       
    }
    private static IDocumentStore GetDocumentStore()
    {
        return DocumentStoreHolder.Store;
    }
    private JsonServiceClient ConfigureServiceClient()
    {
        serviceClient = new JsonServiceClient("http://localhost:5004");
        var tokens = serviceClient.Post(new AuthViaPassword
        {
            Login = "ksu", Password = "123"  //to-do: доставать из настроек, а не константой
        });
        serviceClient.BearerToken = tokens.AccessToken;
        return serviceClient;
    }

    [TestCaseSource(nameof(Cases))]
    public async Task GetTaskListCases(InputTestData testCase)
    {
        //arrange
        using (IAsyncDocumentSession session = store.OpenAsyncSession())
        {
            foreach (var taskItem in testCase.ExpectedListItems)
            {
                await session.StoreAsync(taskItem);
            }
            await session.SaveChangesAsync();
        }

        //act
        TaskItemPage actualTaskList;
        switch (testCase.ListType)
        {
            case TaskListTypes.Unlocked:
                actualTaskList = await serviceClient.GetAsync(new GetUnlockedTasks());
                break;
            case TaskListTypes.Completed:
                actualTaskList = await serviceClient.GetAsync(new GetCompletedTasks());
                break;
            case TaskListTypes.Archived:
                actualTaskList = await serviceClient.GetAsync(new GetArchivedTasks());
                break;
            default: throw new ArgumentException("Передано не верное значение типа списка тасков");
        }

        //assert
        actualTaskList.Tasks.Should().NotBeEmpty();
        foreach (var taskItem in actualTaskList.Tasks)
        {
            switch (testCase.ListType)
            {
                case TaskListTypes.Unlocked:
                    taskItem.UnlockedDateTime.Should().NotBeNull();
                    break;
                case TaskListTypes.Completed:
                    taskItem.CompletedDateTime.Should().NotBeNull();
                    break;
                case TaskListTypes.Archived:
                    taskItem.ArchiveDateTime.Should().NotBeNull();
                    break;
            }
        }
    }   

    private static IEnumerable<TestCaseData> Cases
    { 
       get
       {
          yield return new TestCaseData(new InputTestData(TaskListTypes.Unlocked, GetExpectedTaskItems(TaskListTypes.Unlocked)))
                .SetName("Список разлоченных задач");
          yield return new TestCaseData(new InputTestData(TaskListTypes.Completed, GetExpectedTaskItems(TaskListTypes.Completed)))
                .SetName("Список завершенных задач");
          yield return new TestCaseData(new InputTestData(TaskListTypes.Archived, GetExpectedTaskItems(TaskListTypes.Archived)))
                .SetName("Список архивированных задач");
       }
    }
    private static List<TaskItem> GetExpectedTaskItems(TaskListTypes listType)
    {
        return new List<TaskItem>()
         {
           new TaskItem
           {
              Id = "200-A", 
              UserId = "User/ac25cb04-991d-4983-aedf-8f4b8d4b43c2",
              Title = "Тестовый таск 1",
              Description = "Тестовый таск 1",
              IsCompleted = false,
              CreatedDateTime =  new DateTimeOffset(new DateTime(2024, 1, 10)),
              UnlockedDateTime = (listType  == TaskListTypes.Unlocked) ? new DateTimeOffset(new DateTime(2024, 2, 20)) : null,
              CompletedDateTime = (listType  == TaskListTypes.Completed) ? new DateTimeOffset(new DateTime(2024, 2, 20)) : null,
              ArchiveDateTime = (listType  == TaskListTypes.Archived) ? new DateTimeOffset(new DateTime(2024, 2, 20)) : null,
              PlannedBeginDateTime = null,
              PlannedEndDateTime = null,
              PlannedDuration = null,
              ContainsTasks = new List<string>(),
              BlocksTasks = new List<string>(),
              Repeater = new RepeaterPattern
              {
                 Type = RepeaterType.Weekly,
                 Period = 1,
                 AfterComplete = false,
                 Pattern = [0, 2, 3, 4]
              },
              Importance = 1,
              Wanted = false
           },
           new TaskItem
           {
              Id = "201-A",
              UserId = "User/ac25cb04-991d-4983-aedf-8f4b8d4b43c2",
              Title = "Тестовый таск 2",
              Description = "Тестовый таск 2",
              IsCompleted = false,
              CreatedDateTime =  new DateTimeOffset(new DateTime(2024, 1, 25)),
              UnlockedDateTime = (listType  == TaskListTypes.Unlocked) ? new DateTimeOffset(new DateTime(2024, 2, 20)) : null,
              CompletedDateTime = (listType  == TaskListTypes.Completed) ? new DateTimeOffset(new DateTime(2024, 2, 20)) : null,
              ArchiveDateTime = (listType  == TaskListTypes.Archived) ? new DateTimeOffset(new DateTime(2024, 2, 20)) : null,
              PlannedBeginDateTime = null,
              PlannedEndDateTime = null,
              PlannedDuration = null,
              ContainsTasks = new List<string>(),
              BlocksTasks = new List<string>(),
              Repeater = new RepeaterPattern
              {
                 Type = RepeaterType.Weekly,
                 Period = 1,
                 AfterComplete = false,
                 Pattern = [0, 2, 3, 4]
              },
              Importance = 1,
              Wanted = false
           },
           new TaskItem
           {
              Id = "202-A", 
              UserId = "User/ac25cb04-991d-4983-aedf-8f4b8d4b43c2",
              Title = "Тестовый таск 3",
              Description = "Тестовый таск 3",
              IsCompleted = false,
              CreatedDateTime =  new DateTimeOffset(new DateTime(2024, 1, 25)),
              UnlockedDateTime = null,
              CompletedDateTime = null,
              ArchiveDateTime = null,
              PlannedBeginDateTime = null,
              PlannedEndDateTime = null,
              PlannedDuration = null,
              ContainsTasks = new List<string>(),
              BlocksTasks = new List<string>(),
              Repeater = new RepeaterPattern
              {
                 Type = RepeaterType.Weekly,
                 Period = 1,
                 AfterComplete = false,
                 Pattern = [0, 2, 3, 4]
              },
              Importance = 1,
              Wanted = false
           }
        };
    }   
}

  

    