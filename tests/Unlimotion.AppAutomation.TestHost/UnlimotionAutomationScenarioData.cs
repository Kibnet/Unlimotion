using System.Text.Json;
using Unlimotion.Domain;
using Unlimotion.ViewModel;
using Unlimotion.ViewModel.Localization;

namespace Unlimotion.AppAutomation.TestHost;

public static class UnlimotionAutomationScenarioData
{
    public const string SmokeCurrentTaskId = "f41774af-38f6-486c-9c5d-e4ba3300438c";
    public const string SmokeCurrentTaskTitle = "Blocked task 7";
    public const string ReadmeDemoCurrentTaskId = "workshop-narrative";
    public const string ReadmeDemoCurrentTaskTitle = "Design launch week workshop narrative";
    public const string ReadmeDemoCurrentTaskTitleRu = "Собрать сценарий воркшопа к неделе запуска";
    public const string ReadmeDemoWindowTitle = "Unlimotion README Demo";
    public const string ReadmeDemoWindowTitleRu = "Unlimotion README демо";
    public static readonly IReadOnlyList<string> ReadmeDemoLastOpenedTaskIds =
    [
        "launch-pilot",
        "release-checklist",
        "activity-flow",
        "mentor-outreach",
        "translate-readme-copy",
        ReadmeDemoCurrentTaskId
    ];

    public static string GetCurrentTaskId(UnlimotionAutomationScenario scenario)
    {
        return scenario switch
        {
            UnlimotionAutomationScenario.ReadmeDemo => ReadmeDemoCurrentTaskId,
            _ => SmokeCurrentTaskId
        };
    }

    public static string NormalizeReadmeLanguage(string? language)
    {
        return language?.Trim().ToLowerInvariant() switch
        {
            LocalizationService.RussianLanguage => LocalizationService.RussianLanguage,
            LocalizationService.EnglishLanguage => LocalizationService.EnglishLanguage,
            _ => LocalizationService.EnglishLanguage
        };
    }

    public static string GetCurrentTaskTitle(UnlimotionAutomationScenario scenario)
    {
        return GetCurrentTaskTitle(scenario, LocalizationService.EnglishLanguage);
    }

    public static string GetCurrentTaskTitle(UnlimotionAutomationScenario scenario, string? language)
    {
        return scenario switch
        {
            UnlimotionAutomationScenario.ReadmeDemo => IsRussian(language)
                ? ReadmeDemoCurrentTaskTitleRu
                : ReadmeDemoCurrentTaskTitle,
            _ => SmokeCurrentTaskTitle
        };
    }

    public static string? GetWindowTitle(UnlimotionAutomationScenario scenario, string? language)
    {
        return scenario switch
        {
            UnlimotionAutomationScenario.ReadmeDemo => IsRussian(language)
                ? ReadmeDemoWindowTitleRu
                : ReadmeDemoWindowTitle,
            _ => null
        };
    }

    public static void SeedTasks(
        UnlimotionAutomationScenario scenario,
        string repositoryRoot,
        string tasksPath,
        string? language = null)
    {
        switch (scenario)
        {
            case UnlimotionAutomationScenario.ReadmeDemo:
                SeedReadmeDemoTasks(tasksPath, language);
                break;
            default:
                CopySmokeSnapshots(repositoryRoot, tasksPath);
                break;
        }
    }

    public static void WriteConfig(
        UnlimotionAutomationScenario scenario,
        string configPath,
        string tasksPath,
        string? language = null)
    {
        switch (scenario)
        {
            case UnlimotionAutomationScenario.ReadmeDemo:
                WriteReadmeDemoConfig(configPath, tasksPath, language);
                break;
            default:
                WriteSmokeConfig(configPath, tasksPath);
                break;
        }
    }

    private static bool IsRussian(string? language)
    {
        return string.Equals(NormalizeReadmeLanguage(language), LocalizationService.RussianLanguage, StringComparison.Ordinal);
    }

    private static void CopySmokeSnapshots(string repositoryRoot, string tasksPath)
    {
        var snapshotsPath = Path.Combine(repositoryRoot, "src", "Unlimotion.Test", "Snapshots");
        foreach (var sourcePath in Directory.EnumerateFiles(snapshotsPath))
        {
            var destinationPath = Path.Combine(tasksPath, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static void WriteSmokeConfig(string configPath, string tasksPath)
    {
        var config = new
        {
            TaskStorage = new
            {
                Path = tasksPath,
                URL = string.Empty,
                Login = string.Empty,
                Password = string.Empty,
                IsServerMode = "False"
            },
            Git = new
            {
                BackupEnabled = "False",
                ShowStatusToasts = "False",
                RemoteUrl = string.Empty,
                Branch = "master",
                UserName = "YourEmail",
                Password = "YourToken",
                PullIntervalSeconds = "30",
                PushIntervalSeconds = "60",
                RemoteName = "origin",
                PushRefSpec = "refs/heads/master",
                CommitterName = "Backuper",
                CommitterEmail = "Backuper@unlimotion.ru"
            },
            AllTasks = new
            {
                ShowCompleted = "False",
                ShowArchived = "False",
                ShowWanted = "False",
                CurrentSortDefinition = "Comfort",
                CurrentSortDefinitionForUnlocked = "Comfort"
            },
            Appearance = new
            {
                Theme = AppearanceSettings.LightTheme,
                FontSize = AppearanceSettings.DefaultFontSize,
                Language = LocalizationService.EnglishLanguage
            }
        };

        WriteJson(configPath, config);
    }

    private static void WriteReadmeDemoConfig(string configPath, string tasksPath, string? language)
    {
        var languageMode = NormalizeReadmeLanguage(language);
        var config = new
        {
            TaskStorage = new
            {
                Path = tasksPath,
                URL = string.Empty,
                Login = string.Empty,
                Password = string.Empty,
                IsServerMode = "False"
            },
            Git = new
            {
                BackupEnabled = "False",
                ShowStatusToasts = "False",
                RemoteUrl = string.Empty,
                Branch = "main",
                UserName = string.Empty,
                Password = string.Empty,
                PullIntervalSeconds = "30",
                PushIntervalSeconds = "60",
                RemoteName = "origin",
                PushRefSpec = "refs/heads/main",
                CommitterName = "Unlimotion Demo",
                CommitterEmail = "demo@unlimotion.app"
            },
            AllTasks = new
            {
                ShowCompleted = "True",
                ShowArchived = "True",
                ShowWanted = "False",
                CurrentSortDefinition = "Comfort",
                CurrentSortDefinitionForUnlocked = "Comfort"
            },
            Appearance = new
            {
                Theme = AppearanceSettings.LightTheme,
                FontSize = AppearanceSettings.DefaultFontSize,
                Language = languageMode
            }
        };

        WriteJson(configPath, config);
    }

    private static void SeedReadmeDemoTasks(string tasksPath, string? language)
    {
        var russian = IsRussian(language);
        string Text(string english, string russianText) => russian ? russianText : english;

        var now = DateTimeOffset.Now;
        var createdBase = new DateTimeOffset(now.Year, now.Month, now.Day, 12, 0, 0, now.Offset);
        var completedAt = createdBase.AddHours(-1);
        var archivedAt = createdBase.AddHours(-2);

        var tasks = new[]
        {
            CreateTask(
                id: "launch-pilot",
                title: Text("🚀 Launch the learning space pilot", "🚀 Запустить пилот учебного пространства"),
                description: Text(
                    "Coordinate the launch week deliverables that connect the product story, facilitation flow, and public rollout.",
                    "Скоординировать материалы недели запуска, которые связывают продуктовую историю, фасилитацию и публичный релиз."),
                created: createdBase.AddHours(-10),
                updated: createdBase.AddMinutes(-50),
                contains: ["workshop-narrative", "publish-landing"],
                importance: 95,
                wanted: true),
            CreateTask(
                id: "facilitator-toolkit",
                title: Text("🧠 Build the facilitator toolkit", "🧠 Собрать набор фасилитатора"),
                description: Text(
                    "Prepare the materials that facilitators need for the pilot week sessions.",
                    "Подготовить материалы, которые понадобятся фасилитаторам на сессиях пилотной недели."),
                created: createdBase.AddHours(-9),
                updated: createdBase.AddMinutes(-35),
                contains: ["workshop-narrative", "feedback-summary"],
                importance: 80,
                wanted: true),
            CreateTask(
                id: ReadmeDemoCurrentTaskId,
                title: Text(ReadmeDemoCurrentTaskTitle, ReadmeDemoCurrentTaskTitleRu),
                description: Text(
                    """
                    Shape the core pilot story so the desktop UI demo, facilitator notes, and launch copy all describe the same experience.

                    The task intentionally carries dates, relations, a repeater, and a longer description to make the README screenshots representative.
                    """,
                    """
                    Сформировать основную историю пилота так, чтобы demo desktop UI, заметки фасилитатора и тексты запуска описывали один и тот же опыт.

                    В задаче специально есть даты, связи, повторитель и длинное описание, чтобы скриншоты README выглядели репрезентативно.
                    """),
                created: createdBase.AddHours(-4),
                updated: createdBase.AddMinutes(-20),
                contains: ["activity-flow", "speaker-notes"],
                parents: ["launch-pilot", "facilitator-toolkit"],
                blocks: ["publish-landing"],
                blockedBy: ["sync-visual-assets"],
                plannedBegin: createdBase.AddDays(1),
                plannedEnd: createdBase.AddDays(5),
                plannedDuration: TimeSpan.FromDays(6),
                repeater: new RepeaterPattern
                {
                    Type = RepeaterType.Weekly,
                    Period = 1,
                    AfterComplete = false,
                    Pattern = [1, 3, 5]
                },
                importance: 88,
                wanted: true,
                isCanBeCompleted: false),
            CreateTask(
                id: "activity-flow",
                title: Text("Map activity flow and timing", "Разложить активности и тайминг"),
                description: Text(
                    "Break the workshop into clear phases and estimate how much time every step needs.",
                    "Разбить воркшоп на понятные этапы и оценить длительность каждого шага."),
                created: createdBase.AddHours(-3),
                updated: createdBase.AddMinutes(-90),
                parents: [ReadmeDemoCurrentTaskId],
                plannedBegin: createdBase.AddDays(1),
                plannedEnd: createdBase.AddDays(3),
                plannedDuration: TimeSpan.FromDays(2),
                importance: 62,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-80)),
            CreateTask(
                id: "speaker-notes",
                title: Text("Draft speaker notes and examples", "Подготовить заметки спикера и примеры"),
                description: Text(
                    "Collect the examples, transitions, and fallback notes that make the live session smoother.",
                    "Собрать примеры, переходы и запасные заметки, которые делают живую сессию плавнее."),
                created: createdBase.AddHours(-2),
                updated: createdBase.AddMinutes(-70),
                parents: [ReadmeDemoCurrentTaskId],
                plannedBegin: createdBase.AddDays(2),
                plannedEnd: createdBase.AddDays(5),
                plannedDuration: TimeSpan.FromDays(2),
                importance: 58,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-60)),
            CreateTask(
                id: "sync-visual-assets",
                title: Text("Sync the final visual assets with design", "Синхронизировать финальные визуальные материалы"),
                description: Text(
                    "Pull the latest screenshots, illustrations, and accent colors into the demo set.",
                    "Перенести последние скриншоты, иллюстрации и акцентные цвета в демонстрационный набор."),
                created: createdBase.AddHours(-8),
                updated: createdBase.AddMinutes(-45),
                parents: ["release-checklist"],
                blocks: [ReadmeDemoCurrentTaskId],
                plannedBegin: createdBase.AddHours(1),
                plannedEnd: createdBase.AddDays(1),
                plannedDuration: TimeSpan.FromDays(1),
                importance: 74,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-55)),
            CreateTask(
                id: "publish-landing",
                title: Text("Publish the launch landing page", "Опубликовать лендинг запуска"),
                description: Text(
                    "Publish the public-facing page once the workshop narrative is ready and assets are synced.",
                    "Опубликовать публичную страницу после готовности сценария воркшопа и синхронизации материалов."),
                created: createdBase.AddHours(-7),
                updated: createdBase.AddMinutes(-30),
                parents: ["launch-pilot"],
                blockedBy: [ReadmeDemoCurrentTaskId],
                plannedBegin: createdBase.AddDays(3),
                plannedEnd: createdBase.AddDays(4),
                plannedDuration: TimeSpan.FromDays(2),
                importance: 70,
                isCanBeCompleted: false),
            CreateTask(
                id: "feedback-summary",
                title: Text("Summarize pilot feedback interviews", "Собрать выводы из интервью по пилоту"),
                description: Text(
                    "Package the strongest observations into one shareable summary.",
                    "Собрать самые сильные наблюдения в одну сводку, которой удобно делиться."),
                created: createdBase.AddHours(-6),
                updated: completedAt.AddMinutes(-10),
                parents: ["facilitator-toolkit"],
                completed: completedAt,
                importance: 52,
                isCanBeCompleted: true),
            CreateTask(
                id: "confirm-catering",
                title: Text("Confirm catering headcount and menu", "Подтвердить количество гостей и меню"),
                description: Text(
                    "Lock the catering numbers and final menu before the launch week begins.",
                    "Зафиксировать количество участников и финальное меню до начала недели запуска."),
                created: createdBase.AddDays(-2),
                updated: completedAt.AddMinutes(-18),
                completed: completedAt.AddMinutes(-15),
                importance: 46,
                isCanBeCompleted: true),
            CreateTask(
                id: "send-invite-reminder",
                title: Text("Send the launch week reminder email", "Отправить напоминание о неделе запуска"),
                description: Text(
                    "Share the final schedule, venue details, and checklist with all registered participants.",
                    "Разослать финальное расписание, детали площадки и чеклист всем зарегистрированным участникам."),
                created: createdBase.AddDays(-1),
                updated: completedAt.AddMinutes(-6),
                completed: completedAt.AddMinutes(-4),
                importance: 49,
                isCanBeCompleted: true),
            CreateTask(
                id: "winter-retrospective",
                title: Text("Archive the winter pilot retrospective", "Архивировать ретроспективу зимнего пилота"),
                description: Text(
                    "Keep the old retrospective for reference without leaving it in the active flow.",
                    "Сохранить старую ретроспективу для справки, не оставляя ее в активном потоке."),
                created: createdBase.AddDays(-4),
                updated: archivedAt.AddMinutes(-10),
                archived: archivedAt,
                importance: 18,
                isCanBeCompleted: true),
            CreateTask(
                id: "archive-beta-notes",
                title: Text("Archive beta onboarding notes", "Архивировать заметки бета-онбординга"),
                description: Text(
                    "Retain the early onboarding notes for reference while removing them from the active workspace.",
                    "Оставить ранние заметки онбординга для справки и убрать их из активного рабочего пространства."),
                created: createdBase.AddDays(-14),
                updated: archivedAt.AddMinutes(-8),
                archived: archivedAt.AddMinutes(-6),
                importance: 16,
                isCanBeCompleted: true),
            CreateTask(
                id: "archive-summer-checklist",
                title: Text("Archive the summer showcase checklist", "Архивировать чеклист летнего шоукейса"),
                description: Text(
                    "Move the previous showcase preparation list into the archive after the event wrap-up.",
                    "Перенести прошлый список подготовки шоукейса в архив после завершения события."),
                created: createdBase.AddDays(-21),
                updated: archivedAt.AddMinutes(-4),
                archived: archivedAt.AddMinutes(-2),
                importance: 12,
                isCanBeCompleted: true),
            CreateTask(
                id: "release-checklist",
                title: Text("🛠 Prepare the desktop release checklist", "🛠 Подготовить чеклист desktop-релиза"),
                description: Text(
                    "Track the tasks needed to ship refreshed visuals, packaging, and docs together.",
                    "Отследить задачи, нужные для одновременного выпуска обновленных визуалов, пакетов и документации."),
                created: createdBase.AddHours(-5),
                updated: createdBase.AddMinutes(-25),
                contains: ["sync-visual-assets", "capture-readme-tour", "translate-readme-copy"],
                importance: 84,
                wanted: true),
            CreateTask(
                id: "capture-readme-tour",
                title: Text("Capture the README tab tour GIF", "Записать GIF-тур по вкладкам README"),
                description: Text(
                    "Produce the animated pass that demonstrates the current desktop tabs.",
                    "Собрать анимированный проход, который показывает актуальные вкладки desktop-приложения."),
                created: createdBase.AddHours(-1),
                updated: createdBase.AddMinutes(-15),
                parents: ["release-checklist"],
                plannedBegin: createdBase.AddDays(1),
                plannedEnd: createdBase.AddDays(2),
                plannedDuration: TimeSpan.FromHours(6),
                repeater: new RepeaterPattern
                {
                    Type = RepeaterType.Daily,
                    Period = 1,
                    AfterComplete = false,
                    Pattern = []
                },
                importance: 66,
                wanted: true,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-40)),
            CreateTask(
                id: "mentor-outreach",
                title: Text("Coordinate mentor outreach follow-ups", "Скоординировать последующие контакты с менторами"),
                description: Text(
                    "Confirm who will be available during pilot week office hours and capture the final handoff notes.",
                    "Подтвердить, кто будет доступен во время консультаций пилотной недели, и зафиксировать финальные заметки передачи."),
                created: createdBase.AddHours(-4),
                updated: createdBase.AddMinutes(-16),
                plannedBegin: createdBase.AddDays(1),
                plannedEnd: createdBase.AddDays(2),
                plannedDuration: TimeSpan.FromHours(8),
                importance: 61,
                wanted: true,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-26)),
            CreateTask(
                id: "venue-signage",
                title: Text("Prepare venue signage and wayfinding", "Подготовить навигацию и таблички на площадке"),
                description: Text(
                    "Draft and print the signs that keep the welcome area and workshop rooms easy to navigate.",
                    "Сверстать и напечатать указатели, чтобы зона встречи и комнаты воркшопа были понятны участникам."),
                created: createdBase.AddHours(-3),
                updated: createdBase.AddMinutes(-14),
                plannedBegin: createdBase.AddDays(1),
                plannedEnd: createdBase.AddDays(3),
                plannedDuration: TimeSpan.FromDays(1),
                importance: 57,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-24)),
            CreateTask(
                id: "session-playlist",
                title: Text("Curate the session playlist and ambience cues", "Собрать плейлист и звуковые подсказки сессии"),
                description: Text(
                    "Prepare the opening and transition audio cues that support the workshop pacing without distraction.",
                    "Подготовить вступительные и переходные аудио-сигналы, которые поддерживают темп воркшопа и не отвлекают."),
                created: createdBase.AddHours(-2),
                updated: createdBase.AddMinutes(-12),
                plannedBegin: createdBase.AddDays(2),
                plannedEnd: createdBase.AddDays(4),
                plannedDuration: TimeSpan.FromHours(4),
                importance: 44,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-22)),
            CreateTask(
                id: "translate-readme-copy",
                title: Text("Update the English and Russian README copy", "Обновить английский и русский текст README"),
                description: Text(
                    "Bring the screenshots and the written walkthrough back in sync.",
                    "Синхронизировать скриншоты и текстовое описание приложения."),
                created: createdBase.AddMinutes(-30),
                updated: createdBase.AddMinutes(-10),
                parents: ["release-checklist"],
                plannedBegin: createdBase.AddDays(1),
                plannedEnd: createdBase.AddDays(3),
                plannedDuration: TimeSpan.FromDays(2),
                importance: 64,
                isCanBeCompleted: true,
                unlocked: createdBase.AddMinutes(-35))
        };

        foreach (var task in tasks)
        {
            var destinationPath = Path.Combine(tasksPath, task.Id);
            WriteJson(destinationPath, task);
        }

        WriteJson(
            Path.Combine(tasksPath, "migration.report"),
            new
            {
                Version = 1,
                Timestamp = DateTimeOffset.UtcNow,
                DryRun = false,
                ForceRecheck = true,
                FilesTotal = tasks.Length,
                Updated = tasks.Length,
                Summary = new
                {
                    ParentsAdded = 0,
                    ChildNormalized = 0
                },
                Issues = Array.Empty<string>()
            });

        WriteJson(
            Path.Combine(tasksPath, "availability.migration.report"),
            new
            {
                Version = 1,
                Timestamp = DateTimeOffset.UtcNow,
                ForceRecheck = true,
                TasksProcessed = tasks.Length,
                ChangedTasks = 0,
                Message = Text(
                    "Synthetic demo availability already seeded.",
                    "Доступность синтетического demo-набора уже подготовлена.")
            });
    }

    private static TaskItem CreateTask(
        string id,
        string title,
        string description,
        DateTimeOffset created,
        DateTimeOffset? updated = null,
        DateTimeOffset? unlocked = null,
        DateTimeOffset? completed = null,
        DateTimeOffset? archived = null,
        DateTimeOffset? plannedBegin = null,
        DateTimeOffset? plannedEnd = null,
        TimeSpan? plannedDuration = null,
        IReadOnlyList<string>? contains = null,
        IReadOnlyList<string>? parents = null,
        IReadOnlyList<string>? blocks = null,
        IReadOnlyList<string>? blockedBy = null,
        RepeaterPattern? repeater = null,
        int importance = 0,
        bool wanted = false,
        bool isCanBeCompleted = true)
    {
        return new TaskItem
        {
            Id = id,
            Title = title,
            Description = description,
            CreatedDateTime = created,
            UpdatedDateTime = updated,
            UnlockedDateTime = unlocked,
            CompletedDateTime = completed,
            ArchiveDateTime = archived,
            PlannedBeginDateTime = plannedBegin,
            PlannedEndDateTime = plannedEnd,
            PlannedDuration = plannedDuration,
            ContainsTasks = contains?.ToList() ?? [],
            ParentTasks = parents?.ToList() ?? [],
            BlocksTasks = blocks?.ToList() ?? [],
            BlockedByTasks = blockedBy?.ToList() ?? [],
            Repeater = repeater ?? new RepeaterPattern(),
            Importance = importance,
            Wanted = wanted,
            IsCompleted = archived != null ? null : completed != null,
            IsCanBeCompleted = isCanBeCompleted,
            Version = 1
        };
    }

    private static void WriteJson<T>(string path, T payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }
}
