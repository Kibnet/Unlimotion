using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unlimotion.Domain;
using CliProgram = global::Unlimotion.Cli.Program;
using DomainTaskStatus = Unlimotion.Domain.TaskStatus;
using FileTaskStorage = global::Unlimotion.Storage.FileTaskStorage;
using FileTaskStorageOptions = global::Unlimotion.Storage.FileTaskStorageOptions;

namespace Unlimotion.Test;

public sealed class UnlimotionCliIntegrationTests
{
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
    public async Task Complete_RepeatingTaskCreatesNextOccurrenceAndRestoresReverseLinks()
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
        var childAfter = await LoadTask(temp.DirectoryPath, child.Id);
        var blockerAfter = await LoadTask(temp.DirectoryPath, blocker.Id);
        var blockedAfter = await LoadTask(temp.DirectoryPath, blocked.Id);

        await Assert.That(clone.Status).IsEqualTo(DomainTaskStatus.Prepared);
        await Assert.That(clone.PlannedBeginDateTime!.Value.ToUnixTimeSeconds()).IsEqualTo(plannedBegin.AddDays(1).ToUnixTimeSeconds());
        await Assert.That(clone.ContainsTasks).Contains(child.Id);
        await Assert.That(clone.BlockedByTasks).Contains(blocker.Id);
        await Assert.That(clone.BlocksTasks).Contains(blocked.Id);
        await Assert.That(childAfter.ParentTasks).Contains(clone.Id);
        await Assert.That(blockerAfter.BlocksTasks).Contains(clone.Id);
        await Assert.That(blockedAfter.BlockedByTasks).Contains(clone.Id);
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
        await Assert.That(result.StdOut.Contains("--explain", StringComparison.Ordinal)).IsFalse();
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

    private static async Task<CliRunResult> RunCli(params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
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
