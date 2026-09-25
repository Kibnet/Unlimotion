using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using CliProgram = global::Unlimotion.Cli.Program;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using FileTaskStorage = global::Unlimotion.Storage.FileTaskStorage;
using FileTaskStorageOptions = global::Unlimotion.Storage.FileTaskStorageOptions;
using IRecoverableTaskGraphWriteScope = global::Unlimotion.TaskTree.IRecoverableTaskGraphWriteScope;

namespace Unlimotion.Test;

public sealed class UnlimotionCliIntegrationTests
{
    [Test]
    public async Task Status_UsesTasksEnvironmentWithoutExplicitPath()
    {
        using var temp = TempTaskDirectory.Create();

        var result = await RunCliWithEnvironment(
            temp.DirectoryPath,
            null,
            "status", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(result.StdOut).GetProperty("taskCount").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task Create_UsesTasksEnvironmentAndExplicitPathTakesPrecedence()
    {
        using var environmentTasks = TempTaskDirectory.Create();
        using var explicitTasks = TempTaskDirectory.Create();

        var environmentCreate = await RunCliWithEnvironment(
            environmentTasks.DirectoryPath, null, "create", "--title", "Environment task", "--format", "json");
        var explicitCreate = await RunCliWithEnvironment(
            environmentTasks.DirectoryPath, null, "create", "--tasks", explicitTasks.DirectoryPath,
            "--title", "Explicit task", "--format", "json");
        var environmentStatus = await RunCliWithEnvironment(
            environmentTasks.DirectoryPath, null, "status", "--format", "json");
        var explicitStatus = await RunCliWithEnvironment(
            environmentTasks.DirectoryPath, null, "status", "--tasks", explicitTasks.DirectoryPath,
            "--format", "json");

        await Assert.That(environmentCreate.ExitCode).IsEqualTo(0);
        await Assert.That(explicitCreate.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(environmentStatus.StdOut).GetProperty("taskCount").GetInt32()).IsEqualTo(1);
        await Assert.That(ParseJson(explicitStatus.StdOut).GetProperty("taskCount").GetInt32()).IsEqualTo(1);
        var environmentTaskId = ParseJson(environmentCreate.StdOut).GetProperty("task").GetProperty("id").GetString();
        var explicitTaskId = ParseJson(explicitCreate.StdOut).GetProperty("task").GetProperty("id").GetString();
        await Assert.That(environmentTaskId).IsNotEqualTo(explicitTaskId);
    }

    [Test]
    public async Task MissingTasksEnvironmentFailsWithoutDesktopFallback()
    {
        using var temp = TempTaskDirectory.Create();
        var missing = Path.Combine(temp.DirectoryPath, "MissingTasks");

        var result = await RunCliWithEnvironment(missing, null, "status", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await AssertJsonError(result.StdOut, "invalidArguments");
    }

    [Test]
    public async Task FileTasksEnvironmentFailsWithoutDesktopFallback()
    {
        using var temp = TempTaskDirectory.Create();
        var filePath = Path.Combine(temp.DirectoryPath, "not-a-directory.txt");
        await File.WriteAllTextAsync(filePath, "test");

        var result = await RunCliWithEnvironment(filePath, null, "status", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await AssertJsonError(result.StdOut, "invalidArguments");
    }

    [Test]
    public async Task RelativeTasksEnvironmentUsesProcessWorkingDirectory()
    {
        using var temp = TempTaskDirectory.Create();
        var relativeTasksPath = Path.Combine(temp.DirectoryPath, "Задачи с пробелами");
        Directory.CreateDirectory(relativeTasksPath);

        var result = await RunCliWithEnvironment("Задачи с пробелами", temp.DirectoryPath,
            "status", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(result.StdOut).GetProperty("taskCount").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitEmptyPathDoesNotUseTasksEnvironment()
    {
        using var temp = TempTaskDirectory.Create();

        var result = await RunCliWithEnvironment(temp.DirectoryPath, null,
            "status", "--tasks", "", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(2);
    }

    [Test]
    public async Task Complete_BlockerUnlocksBlockedTaskAndSetsUnlockedDate()
    {
        using var temp = TempTaskDirectory.Create();
        var blocker = CreateTask("blocker", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        blocker.BlocksTasks.Add("waiter");
        var waiter = CreateTask("waiter", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        waiter.BlockedByTasks.Add("blocker");

        await SaveTasks(temp.DirectoryPath, blocker, waiter);

        var result = await RunCli("complete", "--tasks", temp.DirectoryPath, "--id", blocker.Id, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var blockerAfter = await LoadTask(temp.DirectoryPath, blocker.Id);
        var waiterAfter = await LoadTask(temp.DirectoryPath, waiter.Id);
        await Assert.That(blockerAfter.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(waiterAfter.IsCanBeCompleted).IsTrue();
        await Assert.That(waiterAfter.UnlockedDateTime).IsNotNull();
    }

    [Test]
    public async Task SetStatus_BlockerBackToPreparedRollsInProgressBlockedTaskBackToPrepared()
    {
        using var temp = TempTaskDirectory.Create();
        var blocker = CreateTask("blocker", DomainTaskStatus.Completed, isCanBeCompleted: true);
        blocker.BlocksTasks.Add("worker");
        var worker = CreateTask("worker", DomainTaskStatus.InProgress, isCanBeCompleted: true);
        worker.BlockedByTasks.Add("blocker");
        worker.UnlockedDateTime = DateTimeOffset.UtcNow.AddMinutes(-5);

        await SaveTasks(temp.DirectoryPath, blocker, worker);

        var result = await RunCli("set-status", "--tasks", temp.DirectoryPath, "--id", blocker.Id, "--status", "Prepared", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var blockerAfter = await LoadTask(temp.DirectoryPath, blocker.Id);
        var workerAfter = await LoadTask(temp.DirectoryPath, worker.Id);
        await Assert.That(blockerAfter.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(workerAfter.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(workerAfter.IsCanBeCompleted).IsFalse();
        await Assert.That(workerAfter.UnlockedDateTime).IsNull();
    }

    [Test]
    public async Task Complete_RepeatingTaskCreatesIndependentOccurrenceSubtree()
    {
        using var temp = TempTaskDirectory.Create();
        var plannedBegin = DateTimeOffset.UtcNow.AddDays(-1);
        var plannedEnd = DateTimeOffset.UtcNow;

        var child = CreateTask("child", DomainTaskStatus.Completed, isCanBeCompleted: true);
        child.ParentTasks.Add("source");
        var blocker = CreateTask("blocker", DomainTaskStatus.Completed, isCanBeCompleted: true);
        blocker.BlocksTasks.Add("source");
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        blocked.BlockedByTasks.Add("source");
        var source = CreateTask("source", DomainTaskStatus.Prepared, isCanBeCompleted: true, title: "Repeating source");
        source.ContainsTasks.Add(child.Id);
        source.BlockedByTasks.Add(blocker.Id);
        source.BlocksTasks.Add(blocked.Id);
        source.Repeater = new RepeaterPattern
        {
            Type = RepeaterType.Daily,
            Period = 1
        };
        source.PlannedBeginDateTime = plannedBegin;
        source.PlannedEndDateTime = plannedEnd;

        await SaveTasks(temp.DirectoryPath, child, blocker, blocked, source);

        var result = await RunCli("complete", "--tasks", temp.DirectoryPath, "--id", source.Id, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var allTasks = await LoadAllTasks(temp.DirectoryPath);
        var clone = allTasks.Single(task => task.Id != source.Id && task.Title == source.Title);
        var childClone = allTasks.Single(task => task.Id != child.Id && task.Title == child.Title);
        var childAfter = await LoadTask(temp.DirectoryPath, child.Id);
        var blockerAfter = await LoadTask(temp.DirectoryPath, blocker.Id);
        var blockedAfter = await LoadTask(temp.DirectoryPath, blocked.Id);

        await Assert.That(clone.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(clone.PlannedBeginDateTime!.Value.ToUnixTimeSeconds()).IsEqualTo(plannedBegin.AddDays(1).ToUnixTimeSeconds());
        await Assert.That(clone.ContainsTasks).IsEquivalentTo([childClone.Id]);
        await Assert.That(clone.BlockedByTasks).IsEmpty();
        await Assert.That(clone.BlocksTasks).IsEmpty();
        await Assert.That(childClone.Status).IsEqualTo(DomainTaskStatus.NotReady);
        await Assert.That(childClone.ParentTasks).IsEquivalentTo([clone.Id]);
        await Assert.That(childAfter.ParentTasks).IsEquivalentTo([source.Id]);
        await Assert.That(blockerAfter.BlocksTasks).IsEquivalentTo([source.Id]);
        await Assert.That(blockedAfter.BlockedByTasks).IsEquivalentTo([source.Id]);
    }

    [Test]
    public async Task Complete_DuplicateIdsBlocksWriteAndReportsBothFilePaths()
    {
        using var temp = TempTaskDirectory.Create();
        await WriteRawTask(temp.DirectoryPath, "duplicate-a", """
        {
          "Id": "duplicate",
          "Title": "Duplicate A",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00"
        }
        """);
        await WriteRawTask(temp.DirectoryPath, "duplicate-b", """
        {
          "Id": "duplicate",
          "Title": "Duplicate B",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00"
        }
        """);

        var result = await RunCli("complete", "--tasks", temp.DirectoryPath, "--id", "duplicate", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        var root = ParseJson(result.StdOut);
        await Assert.That(root.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("validationFailed");
        var message = root.GetProperty("error").GetProperty("message").GetString() ?? string.Empty;
        await Assert.That(message.Contains("duplicate-a", StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains("duplicate-b", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SetCriterion_CompletedTaskReturnsDeniedAndDoesNotChangeFile()
    {
        using var temp = TempTaskDirectory.Create();
        var completed = CreateTask("completed", DomainTaskStatus.Completed, isCanBeCompleted: true);
        completed.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Done",
            IsSatisfied = false
        });
        await SaveTasks(temp.DirectoryPath, completed);
        var filePath = System.IO.Path.Combine(temp.DirectoryPath, completed.Id);
        var before = await File.ReadAllTextAsync(filePath);

        var result = await RunCli(
            "set-criterion",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            completed.Id,
            "--criterion",
            "criterion",
            "--satisfied",
            "true",
            "--format",
            "json");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await AssertJsonError(result.StdOut, "businessRuleDenied");
        var after = await File.ReadAllTextAsync(filePath);
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task SatisfyCriterion_PreservesUnknownJsonFields()
    {
        using var temp = TempTaskDirectory.Create();
        await WriteRawTask(temp.DirectoryPath, "unknown", """
        {
          "Id": "unknown",
          "Title": "Unknown fields",
          "Description": "",
          "Status": "Prepared",
          "IsCanBeCompleted": true,
          "CreatedDateTime": "2026-01-01T00:00:00.000+00:00",
          "ExtraTop": "keep-top",
          "CompletionCriteria": [
            {
              "Id": "criterion",
              "Text": "Done",
              "IsSatisfied": false,
              "ExtraCriterion": "keep-criterion"
            }
          ],
          "StatusHistory": [
            {
              "Status": "Prepared",
              "ChangedAt": "2026-01-01T00:00:00.000+00:00",
              "Author": "seed",
              "ExtraHistory": "keep-history"
            }
          ]
        }
        """);

        var result = await RunCli(
            "satisfy-criterion",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            "unknown",
            "--criterion",
            "criterion",
            "--format",
            "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var json = JObject.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(temp.DirectoryPath, "unknown")));
        var criteria = (JArray)json["CompletionCriteria"]!;
        var history = (JArray)json["StatusHistory"]!;
        await Assert.That((string?)json["ExtraTop"]).IsEqualTo("keep-top");
        await Assert.That((string?)criteria[0]!["ExtraCriterion"]).IsEqualTo("keep-criterion");
        await Assert.That((bool?)criteria[0]!["IsSatisfied"]).IsTrue();
        await Assert.That((string?)history[0]!["ExtraHistory"]).IsEqualTo("keep-history");
    }

    [Test]
    public async Task JsonMode_ReturnsErrorEnvelopeForMissingArgsUnknownTaskAndDeniedCommand()
    {
        using var temp = TempTaskDirectory.Create();

        var missingArgs = await RunCli("complete", "--tasks", temp.DirectoryPath, "--format", "json");
        await Assert.That(missingArgs.ExitCode).IsEqualTo(2);
        await AssertJsonError(missingArgs.StdOut, "invalidArguments");

        var unknownTask = await RunCli("task", "--tasks", temp.DirectoryPath, "--id", "missing", "--format", "json");
        await Assert.That(unknownTask.ExitCode).IsEqualTo(1);
        await AssertJsonError(unknownTask.StdOut, "notFound");

        var completed = CreateTask("completed", DomainTaskStatus.Completed, isCanBeCompleted: true);
        completed.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Done",
            IsSatisfied = false
        });
        await SaveTasks(temp.DirectoryPath, completed);

        var denied = await RunCli(
            "set-criterion",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            completed.Id,
            "--criterion",
            "criterion",
            "--satisfied",
            "true",
            "--format",
            "json");

        await Assert.That(denied.ExitCode).IsEqualTo(1);
        await AssertJsonError(denied.StdOut, "businessRuleDenied");
    }

    [Test]
    public async Task Help_DoesNotAdvertiseExplainFlag()
    {
        var result = await RunCli("--help");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StdOut.Contains("unlimotion-cli task --tasks", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("--status <status>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("--startable true|false", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("execution question", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("--question-id <question-id>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("--link <absolute-uri>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("UNLIMOTION_TASKS", StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.StdOut.Contains("--explain", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task JsonMode_SuccessOmitsDeniedFields()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("success", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        await SaveTasks(temp.DirectoryPath, task);

        var result = await RunCli(
            "set-status",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            task.Id,
            "--status",
            "InProgress",
            "--format",
            "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var root = ParseJson(result.StdOut);
        await Assert.That(root.GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That(root.TryGetProperty("deniedKind", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("error", out _)).IsFalse();
    }

    [Test]
    public async Task SetStatus_RejectsNumericEnumValuesWithoutWriting()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("numeric-status", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        await SaveTasks(temp.DirectoryPath, task);
        var filePath = Path.Combine(temp.DirectoryPath, task.Id);
        var before = await File.ReadAllTextAsync(filePath);

        foreach (var numericStatus in new[] { "3", "99" })
        {
            var result = await RunCli(
                "set-status",
                "--tasks",
                temp.DirectoryPath,
                "--id",
                task.Id,
                "--status",
                numericStatus,
                "--format",
                "json");

            await Assert.That(result.ExitCode).IsEqualTo(2);
            await AssertJsonError(result.StdOut, "invalidArguments");
        }

        await Assert.That(await File.ReadAllTextAsync(filePath)).IsEqualTo(before);
    }

    [Test]
    public async Task Parser_RejectsOptionsThatDoNotBelongToCommand()
    {
        using var temp = TempTaskDirectory.Create();

        var irrelevant = await RunCli(
            "status",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            "ignored",
            "--format",
            "json");
        var hiddenExplain = await RunCli(
            "task",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            "missing",
            "--explain",
            "--format",
            "json");

        await Assert.That(irrelevant.ExitCode).IsEqualTo(2);
        await AssertJsonError(irrelevant.StdOut, "invalidArguments");
        await Assert.That(hiddenExplain.ExitCode).IsEqualTo(2);
        await AssertJsonError(hiddenExplain.StdOut, "invalidArguments");
    }

    [Test]
    public async Task Candidates_ReturnsPreparedStartableTasksInDeterministicOrder()
    {
        using var temp = TempTaskDirectory.Create();
        var lowPriority = CreateTask("low", DomainTaskStatus.Prepared, isCanBeCompleted: true, title: "Low");
        lowPriority.Importance = 1;
        var highPriority = CreateTask("high", DomainTaskStatus.Prepared, isCanBeCompleted: true, title: "High");
        highPriority.Importance = 10;
        var inProgress = CreateTask("in-progress", DomainTaskStatus.InProgress, isCanBeCompleted: true);
        var blocked = CreateTask("blocked", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        await SaveTasks(temp.DirectoryPath, lowPriority, highPriority, inProgress, blocked);

        var result = await RunCli(
            "candidates",
            "--tasks",
            temp.DirectoryPath,
            "--limit",
            "2",
            "--format",
            "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var root = ParseJson(result.StdOut);
        await Assert.That(root.ValueKind).IsEqualTo(JsonValueKind.Array);
        var candidates = root.EnumerateArray().ToArray();
        await Assert.That(candidates).Count().IsEqualTo(2);
        await Assert.That(candidates[0].GetProperty("id").GetString()).IsEqualTo(highPriority.Id);
        await Assert.That(candidates[1].GetProperty("id").GetString()).IsEqualTo(lowPriority.Id);
        await Assert.That(candidates.All(item => item.GetProperty("status").GetString() == "Prepared"))
            .IsTrue();
    }

    [Test]
    public async Task Claim_ConcurrentAgentsProduceOneLeaseAndOneStatusTransition()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("claimable", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        await SaveTasks(temp.DirectoryPath, task);

        var firstClaim = RunCli(
            "claim",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            task.Id,
            "--agent",
            "agent-a",
            "--expected-status",
            "Prepared",
            "--format",
            "json");
        var secondClaim = RunCli(
            "claim",
            "--tasks",
            temp.DirectoryPath,
            "--id",
            task.Id,
            "--agent",
            "agent-b",
            "--expected-status",
            "Prepared",
            "--format",
            "json");

        var results = await Task.WhenAll(firstClaim, secondClaim);
        await Assert.That(results.Count(result => result.ExitCode == 0)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.ExitCode == 1)).IsEqualTo(1);
        var claimedJson = ParseJson(results.Single(result => result.ExitCode == 0).StdOut);
        await Assert.That(claimedJson.GetProperty("success").GetBoolean()).IsTrue();
        var execution = claimedJson.GetProperty("execution");
        var winningAgent = execution.GetProperty("agentId").GetString();
        var leaseId = execution.GetProperty("leaseId").GetString();
        await Assert.That(Guid.TryParse(leaseId, out _)).IsTrue();
        await AssertJsonError(results.Single(result => result.ExitCode == 1).StdOut, "claimConflict");

        var persisted = await LoadTask(temp.DirectoryPath, task.Id);
        await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.InProgress);
        var json = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(temp.DirectoryPath, task.Id)));
        await Assert.That((string?)json["AgentExecution"]?["AgentId"]).IsEqualTo(winningAgent);
        await Assert.That((string?)json["AgentExecution"]?["LeaseId"]).IsEqualTo(leaseId);
        await Assert.That(((JArray)json["StatusHistory"]!).Count).IsEqualTo(2);
        await Assert.That(((string?)json["Description"])?.Split("<!-- unlimotion-agent-execution:v1:start -->").Length - 1)
            .IsEqualTo(1);
    }

    [Test]
    public async Task ExecutionLifecycle_QuestionAnswerResultAndCompleteAreLeaseBound()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("lifecycle", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        task.Description = "Пользовательское начало\nПользовательский конец";
        await SaveTasks(temp.DirectoryPath, task);

        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--expected-status", "Prepared", "--format", "json");
        var lease = ParseJson(claim.StdOut).GetProperty("execution").GetProperty("leaseId").GetString()!;

        var wrongLease = await RunCli(
            "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", Guid.NewGuid().ToString("D"),
            "--text", "Нужна деталь?", "--format", "json");
        await Assert.That(wrongLease.ExitCode).IsEqualTo(1);
        await AssertJsonError(wrongLease.StdOut, "leaseMismatch");
        await Assert.That(wrongLease.StdOut.Contains(lease, StringComparison.Ordinal)).IsFalse();

        var question = await RunCli(
            "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", lease,
            "--text", "Нужна деталь?", "--format", "json");
        await Assert.That(question.ExitCode).IsEqualTo(0);
        var questionJson = ParseJson(question.StdOut);
        await Assert.That(questionJson.GetProperty("execution").GetProperty("state").GetString())
            .IsEqualTo("AwaitingInput");
        var questionId = questionJson.GetProperty("execution").GetProperty("questions")[0]
            .GetProperty("id").GetString()!;

        var secondQuestion = await RunCli(
            "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", lease,
            "--text", "Ещё вопрос?", "--format", "json");
        await Assert.That(secondQuestion.ExitCode).IsEqualTo(1);
        await AssertJsonError(secondQuestion.StdOut, "executionStateDenied");

        var answer = await RunCli(
            "execution", "answer", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", lease, "--question-id", questionId,
            "--text", "Да, вот деталь", "--format", "json");
        await Assert.That(answer.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(answer.StdOut).GetProperty("execution").GetProperty("state").GetString())
            .IsEqualTo("Active");

        var result = await RunCli(
            "execution", "result", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", lease, "--summary", "Черновой итог",
            "--link", "https://example.com/result", "--format", "json");
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var oldComplete = await RunCli(
            "complete", "--tasks", temp.DirectoryPath, "--id", task.Id, "--format", "json");
        await Assert.That(oldComplete.ExitCode).IsEqualTo(1);
        await AssertJsonError(oldComplete.StdOut, "executionStateDenied");

        var complete = await RunCli(
            "execution", "complete", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", lease, "--summary", "Готовый итог",
            "--link", "file:///C:/result.txt", "--format", "json");
        await Assert.That(complete.ExitCode).IsEqualTo(0);
        var persisted = await LoadTask(temp.DirectoryPath, task.Id);
        await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.Completed);
        await Assert.That(persisted.AgentExecution?.State).IsEqualTo(AgentExecutionState.Completed);
        await Assert.That(persisted.AgentExecution?.Result?.Summary).IsEqualTo("Готовый итог");
        await Assert.That(persisted.Description.StartsWith("Пользовательское начало", StringComparison.Ordinal)).IsTrue();
        await Assert.That(persisted.Description.Contains("Пользовательский конец", StringComparison.Ordinal)).IsTrue();
        await Assert.That(persisted.Description.Split("<!-- unlimotion-agent-execution:v1:start -->").Length - 1)
            .IsEqualTo(1);
    }

    [Test]
    public async Task ReleaseThenClaim_PreservesAttemptAndTaskIncludeReturnsRequestedSections()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("reclaim", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        task.CompletionCriteria.Add(new TaskCompletionCriterion { Id = "criterion", Text = "Проверить", IsSatisfied = false });
        await SaveTasks(temp.DirectoryPath, task);

        var firstClaim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--expected-status", "Prepared", "--format", "json");
        var firstLease = ParseJson(firstClaim.StdOut).GetProperty("execution").GetProperty("leaseId").GetString()!;
        var release = await RunCli(
            "release", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", firstLease,
            "--reason", "Нужен другой исполнитель", "--format", "json");
        await Assert.That(release.ExitCode).IsEqualTo(0);

        var secondClaim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-b", "--expected-status", "Prepared", "--format", "json");
        await Assert.That(secondClaim.ExitCode).IsEqualTo(0);
        var secondExecution = ParseJson(secondClaim.StdOut).GetProperty("execution");
        await Assert.That(secondExecution.GetProperty("leaseId").GetString()).IsNotEqualTo(firstLease);

        var snapshot = await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--include", "details,criteria", "--include", "history,execution",
            "--format", "json");
        await Assert.That(snapshot.ExitCode).IsEqualTo(0);
        var root = ParseJson(snapshot.StdOut);
        await Assert.That(root.TryGetProperty("details", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("criteria", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("history", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("execution", out var execution)).IsTrue();
        await Assert.That(root.TryGetProperty("relations", out _)).IsFalse();
        await Assert.That(execution.GetProperty("previousAttempts").GetArrayLength()).IsEqualTo(1);
        await Assert.That(execution.GetProperty("previousAttempts")[0].GetProperty("leaseId").GetString())
            .IsEqualTo(firstLease);

        var beforeOldLease = await File.ReadAllBytesAsync(Path.Combine(temp.DirectoryPath, task.Id));
        var oldLeaseResult = await RunCli(
            "execution", "result", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", firstLease,
            "--summary", "Не должен сохраниться", "--format", "json");
        await Assert.That(oldLeaseResult.ExitCode).IsEqualTo(1);
        await AssertJsonError(oldLeaseResult.StdOut, "leaseMismatch");
        await Assert.That(oldLeaseResult.StdOut.Contains(secondExecution.GetProperty("leaseId").GetString()!, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(temp.DirectoryPath, task.Id)))
            .IsEquivalentTo(beforeOldLease);
    }

    [Test]
    public async Task TaskInclude_RelationsAreOneStepSortedAndLegacyResponseIsUnchanged()
    {
        using var temp = TempTaskDirectory.Create();
        var root = CreateTask("root", DomainTaskStatus.Prepared, true, "Корень");
        var child = CreateTask("z-child", DomainTaskStatus.Completed, true, "Дочерняя");
        var parent = CreateTask("a-parent", DomainTaskStatus.Prepared, true, "Родитель");
        var blocker = CreateTask("b-blocker", DomainTaskStatus.Completed, true, "Блокер");
        var blocked = CreateTask("c-blocked", DomainTaskStatus.Prepared, false, "Заблокированная");
        root.ContainsTasks.Add(child.Id);
        child.ParentTasks.Add(root.Id);
        root.ParentTasks.Add(parent.Id);
        parent.ContainsTasks.Add(root.Id);
        root.BlockedByTasks.Add(blocker.Id);
        blocker.BlocksTasks.Add(root.Id);
        root.BlocksTasks.Add(blocked.Id);
        blocked.BlockedByTasks.Add(root.Id);
        await SaveTasks(temp.DirectoryPath, root, child, parent, blocker, blocked);

        var legacy = await RunCli("task", "--tasks", temp.DirectoryPath, "--id", root.Id, "--format", "json");
        var legacyJson = ParseJson(legacy.StdOut);
        await Assert.That(legacyJson.GetProperty("taskId").GetString()).IsEqualTo(root.Id);
        await Assert.That(legacyJson.TryGetProperty("task", out _)).IsFalse();
        await Assert.That(legacyJson.TryGetProperty("relations", out _)).IsFalse();

        var snapshot = await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", root.Id,
            "--include", "relations", "--format", "json");
        var repeat = await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", root.Id,
            "--include", "relations", "--format", "json");
        await Assert.That(snapshot.StdOut).IsEqualTo(repeat.StdOut);
        var relations = ParseJson(snapshot.StdOut).GetProperty("relations").EnumerateArray().ToArray();
        await Assert.That(string.Join("|", relations.Select(item =>
                $"{item.GetProperty("type").GetString()}:{item.GetProperty("id").GetString()}")))
            .IsEqualTo("BlockedByTasks:b-blocker|BlocksTasks:c-blocked|ContainsTasks:z-child|ParentTasks:a-parent");
        await Assert.That(relations.All(item => !item.TryGetProperty("relations", out _))).IsTrue();
    }

    [Test]
    public async Task ExecutionComplete_UnsatisfiedCriterionFailsWithoutWriting()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("criteria-denied", DomainTaskStatus.Prepared, true);
        task.CompletionCriteria.Add(new TaskCompletionCriterion
        {
            Id = "criterion",
            Text = "Проверить результат",
            IsSatisfied = false
        });
        await SaveTasks(temp.DirectoryPath, task);
        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--expected-status", "Prepared", "--format", "json");
        var lease = ParseJson(claim.StdOut).GetProperty("execution").GetProperty("leaseId").GetString()!;
        var taskPath = Path.Combine(temp.DirectoryPath, task.Id);
        var before = await File.ReadAllBytesAsync(taskPath);

        var complete = await RunCli(
            "execution", "complete", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--lease", lease,
            "--summary", "Преждевременный итог", "--format", "json");

        await Assert.That(complete.ExitCode).IsEqualTo(1);
        await AssertJsonError(complete.StdOut, "businessRuleDenied");
        await Assert.That(await File.ReadAllBytesAsync(taskPath)).IsEquivalentTo(before);
        var persisted = await LoadTask(temp.DirectoryPath, task.Id);
        await Assert.That(persisted.Status).IsEqualTo(DomainTaskStatus.InProgress);
        await Assert.That(persisted.AgentExecution?.State).IsEqualTo(AgentExecutionState.Active);
        await Assert.That(persisted.AgentExecution?.Result).IsNull();
    }

    [Test]
    public async Task ExecutionBoundaryAndMarkerInputsFailWithoutWriting()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("input-boundaries", DomainTaskStatus.Prepared, true);
        await SaveTasks(temp.DirectoryPath, task);
        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--expected-status", "Prepared", "--format", "json");
        var lease = ParseJson(claim.StdOut).GetProperty("execution").GetProperty("leaseId").GetString()!;
        var taskPath = Path.Combine(temp.DirectoryPath, task.Id);
        var before = await File.ReadAllBytesAsync(taskPath);

        var cases = new List<string[]>
        {
            new[] { "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
                "--agent", new string('a', 201), "--lease", lease, "--text", "Вопрос", "--format", "json" },
            new[] { "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
                "--agent", "agent", "--lease", lease, "--text", new string('q', 4001), "--format", "json" },
            new[] { "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
                "--agent", "agent", "--lease", lease,
                "--text", "<!-- unlimotion-agent-execution:v1:start -->", "--format", "json" },
            new[] { "execution", "result", "--tasks", temp.DirectoryPath, "--id", task.Id,
                "--agent", "agent", "--lease", lease, "--summary", new string('s', 16001), "--format", "json" },
            new[] { "release", "--tasks", temp.DirectoryPath, "--id", task.Id,
                "--agent", "agent", "--lease", lease, "--reason", new string('r', 4001), "--format", "json" }
        };
        var tooManyLinks = new List<string>
        {
            "execution", "result", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--lease", lease, "--summary", "Итог"
        };
        for (var index = 0; index < 21; index++)
        {
            tooManyLinks.Add("--link");
            tooManyLinks.Add($"https://example.com/{index}");
        }
        tooManyLinks.Add("--format");
        tooManyLinks.Add("json");
        cases.Add(tooManyLinks.ToArray());

        foreach (var arguments in cases)
        {
            var result = await RunCli(arguments);
            await Assert.That(result.ExitCode).IsEqualTo(1);
            await AssertJsonError(result.StdOut, "invalidArguments");
            await Assert.That(await File.ReadAllBytesAsync(taskPath)).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task Create_WithParentsPersistsSymmetricValidGraph()
    {
        using var temp = TempTaskDirectory.Create();
        var firstParent = CreateTask(Guid.NewGuid().ToString("D"), DomainTaskStatus.Prepared, true, "Первый");
        var secondParent = CreateTask(Guid.NewGuid().ToString("D"), DomainTaskStatus.Prepared, true, "Второй");
        await SaveTasks(temp.DirectoryPath, firstParent, secondParent);

        var result = await RunCli(
            "create", "--tasks", temp.DirectoryPath, "--title", "Дочерняя задача",
            "--description", "Контекст", "--parent", firstParent.Id, "--parent", secondParent.Id,
            "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var childId = ParseJson(result.StdOut).GetProperty("task").GetProperty("id").GetString()!;
        var child = await LoadTask(temp.DirectoryPath, childId);
        var firstAfter = await LoadTask(temp.DirectoryPath, firstParent.Id);
        var secondAfter = await LoadTask(temp.DirectoryPath, secondParent.Id);
        await Assert.That(child.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(child.ParentTasks).IsEquivalentTo([firstParent.Id, secondParent.Id]);
        await Assert.That(firstAfter.ContainsTasks).Contains(childId);
        await Assert.That(secondAfter.ContainsTasks).Contains(childId);

        var validation = await RunCli("validate", "--tasks", temp.DirectoryPath, "--format", "json");
        await Assert.That(validation.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(validation.StdOut).GetProperty("isValid").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task ExecutionComplete_RepeatingTaskCreatesOccurrenceWithoutLeaseOrMarker()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("execution-repeater", DomainTaskStatus.Prepared, isCanBeCompleted: true, title: "Повтор");
        task.Description = "Постоянный контекст";
        task.PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1);
        task.Repeater = new RepeaterPattern { Type = RepeaterType.Daily, Period = 1 };
        await SaveTasks(temp.DirectoryPath, task);

        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--expected-status", "Prepared", "--format", "json");
        var lease = ParseJson(claim.StdOut).GetProperty("execution").GetProperty("leaseId").GetString()!;
        var complete = await RunCli(
            "execution", "complete", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent-a", "--lease", lease,
            "--summary", "Готово", "--format", "json");

        await Assert.That(complete.ExitCode).IsEqualTo(0);
        var tasks = await LoadAllTasks(temp.DirectoryPath);
        var occurrence = tasks.Single(item => item.Id != task.Id && item.Title == task.Title);
        await Assert.That(occurrence.AgentExecution).IsNull();
        await Assert.That(occurrence.Description).IsEqualTo("Постоянный контекст");
        await Assert.That(occurrence.Description.Contains("unlimotion-agent-execution", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ExecutionDeniedInputs_DoNotChangePersistedTask()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("denied-execution", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        await SaveTasks(temp.DirectoryPath, task);
        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--expected-status", "Prepared", "--format", "json");
        var lease = ParseJson(claim.StdOut).GetProperty("execution").GetProperty("leaseId").GetString()!;
        var question = await RunCli(
            "execution", "question", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--lease", lease, "--text", "Вопрос", "--format", "json");
        await Assert.That(question.ExitCode).IsEqualTo(0);
        var taskPath = Path.Combine(temp.DirectoryPath, task.Id);
        var before = await File.ReadAllTextAsync(taskPath);

        var unknownQuestion = await RunCli(
            "execution", "answer", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--lease", lease, "--question-id", Guid.NewGuid().ToString("D"),
            "--text", "Ответ", "--format", "json");
        await Assert.That(unknownQuestion.ExitCode).IsEqualTo(1);
        await AssertJsonError(unknownQuestion.StdOut, "questionNotFound");

        var pendingComplete = await RunCli(
            "execution", "complete", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--lease", lease, "--summary", "Итог", "--format", "json");
        await Assert.That(pendingComplete.ExitCode).IsEqualTo(1);
        await AssertJsonError(pendingComplete.StdOut, "executionStateDenied");

        var invalidLink = await RunCli(
            "execution", "result", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "agent", "--lease", lease, "--summary", "Итог",
            "--link", "relative/path", "--format", "json");
        await Assert.That(invalidLink.ExitCode).IsEqualTo(1);
        await AssertJsonError(invalidLink.StdOut, "invalidArguments");
        await Assert.That(await File.ReadAllTextAsync(taskPath)).IsEqualTo(before);
    }

    [Test]
    public async Task Create_MissingParentDoesNotCreateTaskFile()
    {
        using var temp = TempTaskDirectory.Create();
        var result = await RunCli(
            "create", "--tasks", temp.DirectoryPath, "--title", "Дочерняя",
            "--parent", "missing", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await AssertJsonError(result.StdOut, "notFound");
        await Assert.That(Directory.EnumerateFiles(temp.DirectoryPath)
            .Where(static path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal)))
            .IsEmpty();
    }

    [Test]
    public async Task Create_DescriptionPreservesMultilineMarkdownAndControlCharacters()
    {
        using var temp = TempTaskDirectory.Create();
        var description = "# Контекст\r\n\r\n- первый пункт\n\t- вложенный пункт\r" +
                          "```csharp\nConsole.WriteLine(\"Привет\");\n```\n" +
                          "C:\\Данные\\задача.md\nhttps://example.com/path?q=1\u0001";
        var result = await RunCli(
            "create", "--tasks", temp.DirectoryPath, "--title", "Дочерняя",
            "--description", description, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StdOut);
        var childId = ParseJson(result.StdOut).GetProperty("task").GetProperty("id").GetString()!;
        var child = await LoadTask(temp.DirectoryPath, childId);
        await Assert.That(child.Description).IsEqualTo(description);

        var snapshot = await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", childId,
            "--include", "details", "--format", "json");
        await Assert.That(snapshot.ExitCode).IsEqualTo(0).Because(snapshot.StdOut);
        await Assert.That(ParseJson(snapshot.StdOut).GetProperty("details")
            .GetProperty("descriptionUserText").GetString()).IsEqualTo(description);
    }

    [Test]
    public async Task Claim_WhenAuditIsTruncatedExposesSignalInJson()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("audit-truncated", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        task.AgentExecution = new AgentExecutionRecord
        {
            AgentId = "released-agent",
            LeaseId = Guid.NewGuid().ToString("D"),
            State = AgentExecutionState.Released,
            ClaimedAt = now,
            UpdatedAt = now,
            ReleasedAt = now,
            ReleaseReason = "Освобождена",
            PreviousAttempts = Enumerable.Range(0, 20)
                .Select(index => new AgentExecutionAttempt
                {
                    AgentId = $"agent-{index}",
                    LeaseId = Guid.NewGuid().ToString("D"),
                    State = AgentExecutionState.Released,
                    ClaimedAt = now.AddMinutes(-index - 1),
                    UpdatedAt = now.AddMinutes(-index - 1),
                    ReleasedAt = now.AddMinutes(-index - 1),
                    ReleaseReason = "Освобождена"
                })
                .ToList()
        };
        task.Description = "<!-- unlimotion-agent-execution:v1:start -->\nОсвобождена\n<!-- unlimotion-agent-execution:v1:end -->";
        await SaveTasks(temp.DirectoryPath, task);

        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "new-agent", "--expected-status", "Prepared", "--format", "json");

        await Assert.That(claim.ExitCode).IsEqualTo(0);
        var root = ParseJson(claim.StdOut);
        await Assert.That(root.GetProperty("auditTruncated").GetBoolean()).IsTrue();
        var snapshot = await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--include", "execution", "--format", "json");
        var execution = ParseJson(snapshot.StdOut).GetProperty("execution");
        await Assert.That(execution.GetProperty("auditTruncated").GetBoolean()).IsTrue();
        await Assert.That(execution.GetProperty("previousAttempts").GetArrayLength()).IsEqualTo(20);
    }

    [Test]
    public async Task Validate_RecoversAbandonedJournalBeforeReadingDirectory()
    {
        using var temp = TempTaskDirectory.Create();
        var storage = CreateStorage(temp.DirectoryPath);
        var task = CreateTask("partial-create", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        var scope = (IRecoverableTaskGraphWriteScope)storage.BeginWriteScope();
        await storage.WithWriteLockAsync(async () =>
        {
            await storage.Save(task);
            return true;
        });
        scope.Dispose();

        var result = await RunCli("validate", "--tasks", temp.DirectoryPath, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var root = ParseJson(result.StdOut);
        await Assert.That(root.GetProperty("taskCount").GetInt32()).IsEqualTo(0);
        await Assert.That(root.GetProperty("isValid").GetBoolean()).IsTrue();
        await Assert.That(Directory.GetFiles(Path.Combine(temp.DirectoryPath, ".unlimotion.transactions"), "*.json"))
            .IsEmpty();
    }

    [Test]
    public async Task Complete_RepeatingSubtreeWithMalformedMarkerFailsWithoutWriting()
    {
        using var temp = TempTaskDirectory.Create();
        var root = CreateTask("repeater-root", DomainTaskStatus.Prepared, true, "Повтор");
        root.Repeater = new RepeaterPattern { Type = RepeaterType.Daily, Period = 1 };
        root.PlannedBeginDateTime = DateTimeOffset.UtcNow.AddDays(-1);
        root.ContainsTasks.Add("repeater-child");
        var child = CreateTask("repeater-child", DomainTaskStatus.Completed, true, "Дочерняя");
        child.ParentTasks.Add(root.Id);
        child.Description = "<!-- unlimotion-agent-execution:v1:end -->";
        await SaveTasks(temp.DirectoryPath, root, child);
        var rootPath = Path.Combine(temp.DirectoryPath, root.Id);
        var childPath = Path.Combine(temp.DirectoryPath, child.Id);
        var rootBefore = await File.ReadAllBytesAsync(rootPath);
        var childBefore = await File.ReadAllBytesAsync(childPath);

        var result = await RunCli(
            "complete", "--tasks", temp.DirectoryPath, "--id", root.Id, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await AssertJsonError(result.StdOut, "descriptionMarkerConflict");
        await Assert.That(await File.ReadAllBytesAsync(rootPath)).IsEquivalentTo(rootBefore);
        await Assert.That(await File.ReadAllBytesAsync(childPath)).IsEquivalentTo(childBefore);
        await Assert.That((await LoadAllTasks(temp.DirectoryPath)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Candidates_AcceptsExplicitStatusStartableAndDefaultSort()
    {
        using var temp = TempTaskDirectory.Create();
        var active = CreateTask("active", DomainTaskStatus.InProgress, isCanBeCompleted: true);
        var prepared = CreateTask("prepared", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        await SaveTasks(temp.DirectoryPath, active, prepared);

        var result = await RunCli(
            "candidates", "--tasks", temp.DirectoryPath, "--limit", "10",
            "--status", "InProgress", "--startable", "true", "--sort", "default",
            "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0);
        var candidates = ParseJson(result.StdOut);
        await Assert.That(candidates.GetArrayLength()).IsEqualTo(1);
        await Assert.That(candidates[0].GetProperty("id").GetString()).IsEqualTo(active.Id);
    }

    [Test]
    public async Task UnknownCommand_IsReportedBeforeMissingTasksPath()
    {
        var result = await RunCli("unknown-command", "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(2);
        var root = ParseJson(result.StdOut);
        await Assert.That(root.GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("invalidArguments");
        await Assert.That(root.GetProperty("error").GetProperty("message").GetString())
            .Contains("Unknown command");
    }

    [Test]
    public async Task Apply_DryRunThenApplyUsesEtagAndReceiptWithoutMutatingPreview()
    {
        using var temp = TempTaskDirectory.Create();
        var root = CreateTask("root", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        var child = CreateTask("child", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        root.ContainsTasks.Add(child.Id);
        child.ParentTasks.Add(root.Id);
        await SaveTasks(temp.DirectoryPath, root, child);

        var scoped = await RunCli("unlocked", "--tasks", temp.DirectoryPath, "--root", root.Id, "--format", "json");
        await Assert.That(scoped.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(scoped.StdOut).GetArrayLength()).IsEqualTo(1)
            .Because("the root is unavailable while its unfinished child remains startable in the selected scope.");

        var snapshot = await RunCli("task", "--tasks", temp.DirectoryPath, "--id", child.Id, "--include", "details", "--format", "json");
        var snapshotJson = ParseJson(snapshot.StdOut);
        var etag = snapshotJson.GetProperty("etag").GetString() ?? throw new InvalidOperationException("Expanded task snapshot did not return etag.");
        await Assert.That(snapshotJson.GetProperty("details").GetProperty("descriptionUserText").GetString()).IsEqualTo(string.Empty);

        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1,
          "applicationId": "A-duration-1",
          "proposalRefs": [{ "id": "P-42", "revision": 1 }],
          "author": "test-agent",
          "reason": "Approved estimate",
          "preconditions": [{ "taskId": "child", "etag": "{{etag}}", "status": "Prepared" }],
          "operations": [{ "operationId": "P-42-duration", "kind": "setField", "taskId": "child", "field": "plannedDuration", "value": "PT45M" }]
        }
        """);

        var preview = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--dry-run", "--format", "json");
        await Assert.That(preview.ExitCode).IsEqualTo(0);
        var previewJson = ParseJson(preview.StdOut);
        await Assert.That(previewJson.GetProperty("mode").GetString()).IsEqualTo("preview");
        await Assert.That(previewJson.GetProperty("receiptWritten").GetBoolean()).IsFalse();
        await Assert.That((await LoadTask(temp.DirectoryPath, child.Id)).PlannedDuration).IsNull();

        var applied = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");
        await Assert.That(applied.ExitCode).IsEqualTo(0);
        var appliedJson = ParseJson(applied.StdOut);
        await Assert.That(appliedJson.GetProperty("mode").GetString()).IsEqualTo("applied");
        await Assert.That(appliedJson.GetProperty("receiptWritten").GetBoolean()).IsTrue();
        await Assert.That(appliedJson.GetProperty("validation").GetProperty("isValid").GetBoolean()).IsTrue();
        await Assert.That(appliedJson.GetProperty("authoritativeTasks").GetArrayLength()).IsGreaterThanOrEqualTo(1);
        await Assert.That((await LoadTask(temp.DirectoryPath, child.Id)).PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(45));

        var receipt = Directory.GetFiles(Path.Combine(temp.DirectoryPath, ".unlimotion.applies", "v1"), "*.json").Single();
        File.Delete(receipt);
        var repeated = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");
        await Assert.That(repeated.ExitCode).IsEqualTo(0);
        await Assert.That(ParseJson(repeated.StdOut).GetProperty("mode").GetString()).IsEqualTo("alreadyApplied");
    }

    [Test]
    public async Task Apply_RejectsDependencyCycleBeforeWriting()
    {
        using var temp = TempTaskDirectory.Create();
        var first = CreateTask("first", DomainTaskStatus.Prepared, isCanBeCompleted: true);
        var second = CreateTask("second", DomainTaskStatus.Prepared, isCanBeCompleted: false);
        first.BlocksTasks.Add(second.Id);
        second.BlockedByTasks.Add(first.Id);
        await SaveTasks(temp.DirectoryPath, first, second);

        var firstSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", first.Id, "--include", "details", "--format", "json")).StdOut);
        var secondSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", second.Id, "--include", "details", "--format", "json")).StdOut);
        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1, "applicationId": "A-cycle-1", "proposalRefs": [{ "id": "P-43", "revision": 1 }], "author": "test-agent", "reason": "cycle test",
          "preconditions": [
            { "taskId": "first", "etag": "{{firstSnapshot.GetProperty("etag").GetString()}}" },
            { "taskId": "second", "etag": "{{secondSnapshot.GetProperty("etag").GetString()}}" }
          ],
          "operations": [{ "operationId": "P-43-edge", "kind": "addRelation", "relation": "blocks", "fromTaskId": "second", "toTaskId": "first" }]
        }
        """);

        var result = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(ParseJson(result.StdOut).GetProperty("error").GetProperty("kind").GetString()).IsEqualTo("validationFailed");
        var persistedFirst = await LoadTask(temp.DirectoryPath, first.Id);
        var persistedSecond = await LoadTask(temp.DirectoryPath, second.Id);
        await Assert.That(persistedFirst.BlockedByTasks).IsEmpty();
        await Assert.That(persistedSecond.BlocksTasks).IsEmpty();
    }

    [Test]
    public async Task Apply_ChangesExistingTaskFieldsCriteriaAndRelation()
    {
        using var temp = TempTaskDirectory.Create();
        var current = CreateTask("current", DomainTaskStatus.Prepared, true, "Текущий шаг");
        current.Description = "Старый контекст";
        var next = CreateTask("next", DomainTaskStatus.Prepared, true, "Следующий шаг");
        await SaveTasks(temp.DirectoryPath, current, next);

        var currentSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", current.Id, "--include", "details", "--format", "json")).StdOut);
        var nextSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", next.Id, "--include", "details", "--format", "json")).StdOut);
        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1,
          "applicationId": "A-existing-task-edit",
          "proposalRefs": [{ "id": "P-edit", "revision": 1 }],
          "author": "test-agent",
          "reason": "Обновить утверждённый план текущего шага",
          "preconditions": [
            { "taskId": "current", "etag": "{{currentSnapshot.GetProperty("etag").GetString()}}" },
            { "taskId": "next", "etag": "{{nextSnapshot.GetProperty("etag").GetString()}}" }
          ],
          "operations": [
            {
              "operationId": "rename",
              "kind": "setField",
              "taskId": "current",
              "field": "title",
              "value": "Проверить договор"
            },
            {
              "operationId": "context",
              "kind": "setField",
              "taskId": "current",
              "field": "descriptionUserText",
              "value": "Сверить правки с приложениями."
            },
            {
              "operationId": "duration",
              "kind": "setField",
              "taskId": "current",
              "field": "plannedDuration",
              "value": "PT90M"
            },
            {
              "operationId": "criterion",
              "kind": "addCriterion",
              "taskId": "current",
              "criterionId": "legal-review",
              "text": "Все существенные правки проверены",
              "isSatisfied": false
            },
            {
              "operationId": "block-next",
              "kind": "addRelation",
              "relation": "blocks",
              "fromTaskId": "current",
              "toTaskId": "next"
            }
          ]
        }
        """);

        var result = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StdOut);
        var currentAfter = await LoadTask(temp.DirectoryPath, "current");
        var nextAfter = await LoadTask(temp.DirectoryPath, "next");
        await Assert.That(currentAfter.Title).IsEqualTo("Проверить договор");
        await Assert.That(currentAfter.Description).IsEqualTo("Сверить правки с приложениями.");
        await Assert.That(currentAfter.PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(90));
        await Assert.That(currentAfter.CompletionCriteria.Single().Text).IsEqualTo("Все существенные правки проверены");
        await Assert.That(currentAfter.BlocksTasks).Contains("next");
        await Assert.That(nextAfter.BlockedByTasks).Contains("current");
    }

    [Test]
    public async Task Apply_CreatesTaskWithParentDescriptionEstimateAndCriterion()
    {
        using var temp = TempTaskDirectory.Create();
        var goal = CreateTask("goal", DomainTaskStatus.Prepared, true, "Цель");
        await SaveTasks(temp.DirectoryPath, goal);

        var goalSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", goal.Id, "--include", "details", "--format", "json")).StdOut);
        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1,
          "applicationId": "A-create-task",
          "proposalRefs": [{ "id": "P-create", "revision": 1 }],
          "author": "test-agent",
          "reason": "Создать выбранный следующий шаг",
          "preconditions": [{ "taskId": "goal", "etag": "{{goalSnapshot.GetProperty("etag").GetString()}}" }],
          "operations": [{
            "operationId": "create-next",
            "kind": "createTask",
            "newTaskId": "next",
            "title": "Подготовить ответ",
            "descriptionUserText": "Собрать вопросы и сформировать ответ.",
            "plannedDuration": "PT45M",
            "parentIds": ["goal"],
            "criteria": [{ "criterionId": "answer-ready", "text": "Ответ готов к отправке", "isSatisfied": false }]
          }]
        }
        """);

        var result = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StdOut);
        var created = await LoadTask(temp.DirectoryPath, "next");
        var goalAfter = await LoadTask(temp.DirectoryPath, "goal");
        await Assert.That(created.Description).IsEqualTo("Собрать вопросы и сформировать ответ.");
        await Assert.That(created.PlannedDuration).IsEqualTo(TimeSpan.FromMinutes(45));
        await Assert.That(created.ParentTasks).Contains("goal");
        await Assert.That(created.CompletionCriteria.Single().Id).IsEqualTo("answer-ready");
        await Assert.That(goalAfter.ContainsTasks).Contains("next");
    }

    [Test]
    public async Task Apply_CreateTaskPreservesMultilineMarkdownDescription()
    {
        using var temp = TempTaskDirectory.Create();
        var goal = CreateTask("goal", DomainTaskStatus.Prepared, true, "Цель");
        await SaveTasks(temp.DirectoryPath, goal);
        var goalSnapshot = ParseJson((await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", goal.Id,
            "--include", "details", "--format", "json")).StdOut);
        var description = "# План\r\n\r\n- шаг 1\n\t- деталь\r\n\u0000\u0001Финиш";
        var encodedDescription = JsonSerializer.Serialize(description);
        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1,
          "applicationId": "A-create-multiline-description",
          "proposalRefs": [{ "id": "P-create-multiline", "revision": 1 }],
          "author": "test-agent",
          "reason": "Сохранить Markdown без потерь",
          "preconditions": [{ "taskId": "goal", "etag": "{{goalSnapshot.GetProperty("etag").GetString()}}" }],
          "operations": [{
            "operationId": "create-next",
            "kind": "createTask",
            "newTaskId": "next",
            "title": "Подготовить ответ",
            "descriptionUserText": {{encodedDescription}},
            "parentIds": ["goal"]
          }]
        }
        """);

        var result = await RunCli(
            "apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StdOut);
        await Assert.That((await LoadTask(temp.DirectoryPath, "next")).Description).IsEqualTo(description);
        var snapshot = ParseJson((await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", "next",
            "--include", "details", "--format", "json")).StdOut);
        await Assert.That(snapshot.GetProperty("details").GetProperty("descriptionUserText").GetString())
            .IsEqualTo(description);
    }

    [Test]
    public async Task Apply_SetDescriptionPreservesMultilineMarkdownAndExecutionBlock()
    {
        using var temp = TempTaskDirectory.Create();
        var task = CreateTask("current", DomainTaskStatus.Prepared, true, "Текущий шаг");
        await SaveTasks(temp.DirectoryPath, task);
        var claim = await RunCli(
            "claim", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--agent", "test-agent", "--expected-status", "Prepared", "--format", "json");
        await Assert.That(claim.ExitCode).IsEqualTo(0).Because(claim.StdOut);
        var before = ParseJson((await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--include", "details,execution", "--format", "json")).StdOut);
        var description = "# Обновлённый контекст\n\n> цитата\r\n\tкод\u0000\u0001";
        var encodedDescription = JsonSerializer.Serialize(description);
        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1,
          "applicationId": "A-set-multiline-description",
          "proposalRefs": [{ "id": "P-set-multiline", "revision": 1 }],
          "author": "test-agent",
          "reason": "Обогатить контекст Markdown",
          "preconditions": [{ "taskId": "current", "etag": "{{before.GetProperty("etag").GetString()}}" }],
          "operations": [{
            "operationId": "set-description",
            "kind": "setField",
            "taskId": "current",
            "field": "descriptionUserText",
            "value": {{encodedDescription}}
          }]
        }
        """);

        var result = await RunCli(
            "apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StdOut);
        var persisted = await LoadTask(temp.DirectoryPath, task.Id);
        await Assert.That(persisted.AgentExecution).IsNotNull();
        await Assert.That(AgentExecutionDescriptionRenderer.Inspect(persisted.Description, out _))
            .IsEqualTo(AgentExecutionMarkerState.Single);
        await Assert.That(AgentExecutionDescriptionRenderer.TryRemove(persisted.Description, out var userText, out _)).IsTrue();
        await Assert.That(userText).IsEqualTo(description);

        var after = ParseJson((await RunCli(
            "task", "--tasks", temp.DirectoryPath, "--id", task.Id,
            "--include", "details,execution", "--format", "json")).StdOut);
        await Assert.That(after.GetProperty("details").GetProperty("descriptionUserText").GetString())
            .IsEqualTo(description);
    }

    [Test]
    public async Task Apply_CreatesNextStepAndBlocksItInOneApprovedRequest()
    {
        using var temp = TempTaskDirectory.Create();
        var goal = CreateTask("goal", DomainTaskStatus.Prepared, true, "Цель");
        var current = CreateTask("current", DomainTaskStatus.Prepared, true, "Текущий шаг");
        await SaveTasks(temp.DirectoryPath, goal, current);

        var goalSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", goal.Id, "--include", "details", "--format", "json")).StdOut);
        var currentSnapshot = ParseJson((await RunCli("task", "--tasks", temp.DirectoryPath, "--id", current.Id, "--include", "details", "--format", "json")).StdOut);
        using var requestFile = TempRequestFile.Create();
        await File.WriteAllTextAsync(requestFile.Path, $$"""
        {
          "schemaVersion": 1,
          "applicationId": "A-create-and-link-next",
          "proposalRefs": [{ "id": "P-next", "revision": 1 }],
          "author": "test-agent",
          "reason": "Создать и связать утверждённый следующий шаг",
          "preconditions": [
            { "taskId": "goal", "etag": "{{goalSnapshot.GetProperty("etag").GetString()}}" },
            { "taskId": "current", "etag": "{{currentSnapshot.GetProperty("etag").GetString()}}" }
          ],
          "operations": [
            {
              "operationId": "create-next",
              "kind": "createTask",
              "newTaskId": "next",
              "title": "Подготовить ответ",
              "parentIds": ["goal"]
            },
            {
              "operationId": "block-next",
              "kind": "addRelation",
              "relation": "blocks",
              "fromTaskId": "current",
              "toTaskId": "next"
            }
          ]
        }
        """);

        var result = await RunCli("apply", "--tasks", temp.DirectoryPath, "--request", requestFile.Path, "--format", "json");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StdOut);
        var currentAfter = await LoadTask(temp.DirectoryPath, "current");
        var nextAfter = await LoadTask(temp.DirectoryPath, "next");
        await Assert.That(currentAfter.BlocksTasks).Contains("next");
        await Assert.That(nextAfter.BlockedByTasks).Contains("current");
        await Assert.That(nextAfter.ParentTasks).Contains("goal");
    }

    private static TaskItem CreateTask(
        string id,
        DomainTaskStatus status,
        bool isCanBeCompleted,
        string? title = null)
    {
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        return new TaskItem
        {
            Id = id,
            UserId = "test-user",
            Title = title ?? id,
            Description = string.Empty,
            Status = status,
            IsCanBeCompleted = isCanBeCompleted,
            CreatedDateTime = createdAt,
            UnlockedDateTime = isCanBeCompleted ? createdAt : null,
            StatusHistory =
            [
                new TaskStatusHistoryEntry
                {
                    Status = status,
                    ChangedAt = createdAt,
                    Author = "seed"
                }
            ]
        };
    }

    private static async Task SaveTasks(string directory, params TaskItem[] tasks)
    {
        var storage = CreateStorage(directory);
        foreach (var task in tasks)
        {
            await storage.Save(task);
        }
    }

    private static async Task<TaskItem> LoadTask(string directory, string id)
    {
        var storage = CreateStorage(directory);
        return await storage.Load(id, forced: true) ?? throw new InvalidOperationException($"Task '{id}' was not found.");
    }

    private static async Task<IReadOnlyList<TaskItem>> LoadAllTasks(string directory)
    {
        var storage = CreateStorage(directory);
        var tasks = new List<TaskItem>();
        await foreach (var task in storage.GetAll())
        {
            tasks.Add(task);
        }

        return tasks;
    }

    private static FileTaskStorage CreateStorage(string directory) => new(new FileTaskStorageOptions
    {
        Path = directory,
        PreserveUnknownJson = true,
        UseDirectoryLock = true
    });

    private static async Task WriteRawTask(string directory, string fileName, string json)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, fileName), json);
    }

    private static Task<CliRunResult> RunCli(params string[] args) => RunCliWithEnvironment(null, null, args);

    private static async Task<CliRunResult> RunCliWithEnvironment(
        string? tasksEnvironmentPath,
        string? workingDirectory,
        params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory
        };
        process.StartInfo.Environment.Remove("UNLIMOTION_TASKS");
        if (tasksEnvironmentPath != null)
        {
            process.StartInfo.Environment["UNLIMOTION_TASKS"] = tasksEnvironmentPath;
        }
        process.StartInfo.ArgumentList.Add(typeof(CliProgram).Assembly.Location);
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start CLI process.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var waitForExit = process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitForExit, Task.Delay(TimeSpan.FromSeconds(30)));
        if (completed != waitForExit)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }

            throw new TimeoutException($"CLI process did not finish in time: {string.Join(" ", args)}");
        }

        return new CliRunResult(process.ExitCode, await stdout, await stderr);
    }

    private static JsonElement ParseJson(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    private static async Task AssertJsonError(string output, string expectedKind)
    {
        var root = ParseJson(output);
        await Assert.That(root.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("error").GetProperty("kind").GetString()).IsEqualTo(expectedKind);
        await Assert.That(root.GetProperty("error").GetProperty("message").GetString()).IsNotNull();
    }

    private sealed record CliRunResult(int ExitCode, string StdOut, string StdErr);

    private sealed class TempRequestFile : IDisposable
    {
        private TempRequestFile(string path) => Path = path;

        public string Path { get; }

        public static TempRequestFile Create() => new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "unlimotion-cli-tests", $"request-{Guid.NewGuid():N}.json"));

        public void Dispose()
        {
            try { File.Delete(Path); }
            catch { /* Best-effort test cleanup. */ }
        }
    }

    private sealed class TempTaskDirectory : IDisposable
    {
        private TempTaskDirectory(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TempTaskDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unlimotion-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempTaskDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
