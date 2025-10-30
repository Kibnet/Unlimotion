# Data Flow and Persistence

<cite>
**Referenced Files in This Document**   
- [IStorage.cs](file://src/Unlimotion.TaskTreeManager/IStorage.cs)
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [GitPullJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPullJob.cs)
- [GitPushJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPushJob.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)
- [ITaskStorage.cs](file://src/Unlimotion.ViewModel/ITaskStorage.cs)
- [TaskStorageSettings.cs](file://src/Unlimotion.ViewModel/TaskStorageSettings.cs)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs)
- [DbUpdatedEventArgs.cs](file://src/Unlimotion.ViewModel/DbUpdatedEventArgs.cs)
- [IDatabaseWatcher.cs](file://src/Unlimotion.ViewModel/IDatabaseWatcher.cs)
- [FileDbWatcher.cs](file://src/Unlimotion.ViewModel/FileDbWatcher.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Data Flow Architecture](#data-flow-architecture)
3. [Core Components](#core-components)
4. [Persistence Layer](#persistence-layer)
5. [Storage Configuration and Selection](#storage-configuration-and-selection)
6. [TaskTreeManager and Business Logic](#tasktreemanager-and-business-logic)
7. [Data Synchronization and Event-Driven Updates](#data-synchronization-and-event-driven-updates)
8. [Git-Based Backup Mechanism](#git-based-backup-mechanism)
9. [Data Consistency and Conflict Resolution](#data-consistency-and-conflict-resolution)
10. [Conclusion](#conclusion)

## Introduction
Unlimotion implements a sophisticated data flow and persistence architecture that manages task data from user interaction through to storage and synchronization. The system is designed with a clear separation of concerns, where the ViewModel layer handles user interactions, the TaskTreeManager manages business logic and task relationships, and the IStorage implementations handle data persistence. This documentation details the complete data pathway, focusing on how tasks are created, modified, and persisted through the system, with special attention to the dual storage options (file-based and server-based), data synchronization mechanisms, and the Git-based backup system.

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L0-L1076)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)

## Data Flow Architecture
The data flow in Unlimotion follows a well-defined pathway from user interaction to persistent storage. When a user performs an action in the UI, such as creating or modifying a task, the MainWindowViewModel receives the command and orchestrates the data operations through the ITaskStorage interface. This interface serves as the primary gateway to the persistence layer, abstracting the underlying storage implementation.

The data flow begins with ViewModel commands that trigger operations on the ITaskStorage instance. These operations are then processed by the TaskTreeManager, which maintains an in-memory representation of task relationships and enforces business rules. The TaskTreeManager coordinates with the appropriate IStorage implementation to persist changes, whether to JSON files on disk or to a RavenDB server. After persistence, changes are synchronized back to the UI through DynamicData collections, ensuring that all views reflect the current state of the data.

```mermaid
flowchart TD
A["User Interaction\n(UI Events)"] --> B["ViewModel Commands\n(MainWindowViewModel)"]
B --> C["ITaskStorage Interface\n(Operation Orchestration)"]
C --> D["TaskTreeManager\n(Business Logic &\nRelationship Management)"]
D --> E["IStorage Implementation\n(Persistence Layer)"]
E --> F["FileTaskStorage\n(JSON Files)"]
E --> G["ServerTaskStorage\n(RavenDB Server)"]
F --> H["DynamicData Synchronization\n(UI Updates)"]
G --> H
H --> I["Multiple Views\n(Consistent State)"]
style A fill:#f9f,stroke:#333
style B fill:#bbf,stroke:#333
style C fill:#f96,stroke:#333
style D fill:#6f9,stroke:#333
style E fill:#69f,stroke:#333
style F fill:#9f6,stroke:#333
style G fill:#96f,stroke:#333
style H fill:#ff9,stroke:#333
style I fill:#9ff,stroke:#333
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L0-L1076)
- [ITaskStorage.cs](file://src/Unlimotion.ViewModel/ITaskStorage.cs#L0-L32)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L0-L721)

## Core Components
The Unlimotion data flow architecture is built around several core components that work together to manage task data. The MainWindowViewModel serves as the central orchestrator, consuming the ITaskStorage interface to perform data operations. The ITaskStorage interface defines the contract for all storage operations, providing a consistent API regardless of the underlying implementation.

The TaskTreeManager is responsible for implementing business logic and maintaining the integrity of task relationships. It handles operations such as adding tasks, creating parent-child relationships, and managing blocking dependencies. The IStorage interface provides the persistence mechanism, with two concrete implementations: FileTaskStorage for local JSON file storage and ServerTaskStorage for remote RavenDB storage.

These components are connected through dependency injection, with the Splat framework managing service registration and resolution. The architecture follows the dependency inversion principle, with higher-level components depending on abstractions rather than concrete implementations, allowing for flexible configuration and easy testing.

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L0-L1076)
- [ITaskStorage.cs](file://src/Unlimotion.ViewModel/ITaskStorage.cs#L0-L32)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L0-L721)

## Persistence Layer
The persistence layer in Unlimotion is implemented through the IStorage interface and its concrete implementations. The IStorage interface defines three core methods for data persistence: Save, Remove, and Load, which correspond to the basic CRUD operations. This interface is implemented by both FileTaskStorage and ServerTaskStorage, providing a consistent API for data operations regardless of the storage backend.

The FileTaskStorage implementation persists tasks as individual JSON files in a designated directory, with each task stored in a file named by its unique ID. This approach allows for simple file system operations and easy backup through standard file synchronization tools. The ServerTaskStorage implementation connects to a RavenDB server, using SignalR for real-time synchronization and ServiceStack for API communication.

Both storage implementations handle data serialization and deserialization, with FileTaskStorage using Newtonsoft.Json and ServerTaskStorage using AutoMapper to convert between domain models and service models. The storage layer also manages connection state, with ServerTaskStorage implementing automatic reconnection logic and authentication token management.

```mermaid
classDiagram
class IStorage {
<<interface>>
+Task<bool> Save(TaskItem item)
+Task<bool> Remove(string itemId)
+Task<TaskItem?> Load(string itemId)
}
class ITaskStorage {
<<interface>>
+SourceCache<TaskItemViewModel, string> Tasks
+ITaskTreeManager TaskTreeManager
+Task Init()
+IAsyncEnumerable<TaskItem> GetAll()
+Task<bool> Connect()
+Task Disconnect()
+event EventHandler<TaskStorageUpdateEventArgs> Updating
+event Action<Exception?>? OnConnectionError
}
class FileTaskStorage {
+SourceCache<TaskItemViewModel, string> Tasks
+ITaskTreeManager TaskTreeManager
+string Path
+event EventHandler<TaskStorageUpdateEventArgs> Updating
+event Action<Exception?>? OnConnectionError
+event EventHandler<EventArgs> Initiated
-IMapper mapper
-ITaskTreeManager taskTreeManager
+FileTaskStorage(string path)
+Task Init()
+IAsyncEnumerable<TaskItem> GetAll()
+Task<bool> Save(TaskItem taskItem)
+Task<bool> Remove(string itemId)
+Task<TaskItem?> Load(string itemId)
+Task<bool> Connect()
+Task Disconnect()
+void SetPause(bool pause)
}
class ServerTaskStorage {
+SourceCache<TaskItemViewModel, string> Tasks
+ITaskTreeManager TaskTreeManager
+event EventHandler<TaskStorageUpdateEventArgs>? Updating
+event Action<Exception?>? OnConnectionError
+event Action? OnConnected
+event Action? OnDisconnected
+event EventHandler OnSignOut
+event EventHandler OnSignIn
+bool IsActive
+string Url
+bool IsConnected
+bool IsSignedIn
-HubConnection _connection
-IJsonServiceClient serviceClient
-IChatHub _hub
-ITaskTreeManager taskTreeManager
+ServerTaskStorage(string url)
+Task Init()
+Task<TaskItem> Load(string itemId)
+Task<bool> Connect()
+Task Disconnect()
+Task SignOut()
+Task BulkInsert(IEnumerable<TaskItem> taskItems)
}
ITaskStorage <|-- FileTaskStorage
ITaskStorage <|-- ServerTaskStorage
IStorage <|-- FileTaskStorage
IStorage <|-- ServerTaskStorage
class TaskItem {
+string Id
+string UserId
+string Title
+string Description
+bool? IsCompleted
+bool IsCanBeCompleted
+DateTimeOffset CreatedDateTime
+DateTimeOffset? UnlockedDateTime
+DateTimeOffset? CompletedDateTime
+DateTimeOffset? ArchiveDateTime
+DateTimeOffset? PlannedBeginDateTime
+DateTimeOffset? PlannedEndDateTime
+TimeSpan? PlannedDuration
+List<string> ContainsTasks
+List<string>? ParentTasks
+List<string> BlocksTasks
+List<string>? BlockedByTasks
+RepeaterPattern Repeater
+int Importance
+bool Wanted
+int Version
+DateTime? SortOrder
}
FileTaskStorage --> TaskItem : "persists as JSON files"
ServerTaskStorage --> TaskItem : "persists to RavenDB"
class SourceCache~TaskItemViewModel, string~ {
+IObservable<IChangeSet<TObject, TKey>> Connect()
+void AddOrUpdate(TObject item)
+void Remove(TKey key)
}
FileTaskStorage --> SourceCache~TaskItemViewModel, string~ : "manages UI-bound collection"
ServerTaskStorage --> SourceCache~TaskItemViewModel, string~ : "manages UI-bound collection"
```

**Diagram sources**
- [IStorage.cs](file://src/Unlimotion.TaskTreeManager/IStorage.cs#L0-L10)
- [ITaskStorage.cs](file://src/Unlimotion.ViewModel/ITaskStorage.cs#L0-L32)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L0-L721)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs#L0-L32)

## Storage Configuration and Selection
The selection and configuration of the appropriate storage backend in Unlimotion is managed by the TaskStorages class, which provides a static registration mechanism for configuring the storage implementation based on the IsServerMode setting. This configuration is initialized in the App.axaml.cs file during application startup, where the system reads the TaskStorageSettings from the configuration and registers the appropriate storage implementation with the Splat dependency injection container.

The TaskStorages.RegisterStorage method is responsible for determining which storage implementation to use based on the IsServerMode flag. When IsServerMode is true, the RegisterServerTaskStorage method creates and registers a ServerTaskStorage instance with the specified URL. When IsServerMode is false, the RegisterFileTaskStorage method creates and registers a FileTaskStorage instance with the specified path.

This registration process follows a clean replacement pattern, where any previously registered storage implementation is disconnected and unregistered before the new implementation is registered. This allows users to switch between storage modes at runtime through the application settings. The SettingsViewModel provides a ConnectCommand that triggers the storage registration and connection process, enabling users to connect to their chosen storage backend with a single action.

```mermaid
sequenceDiagram
participant App as App.axaml.cs
participant TaskStorages as TaskStorages.cs
participant Settings as SettingsViewModel
participant MainWindowVM as MainWindowViewModel
App->>TaskStorages : Init(configPath)
App->>TaskStorages : RegisterStorage(isServerMode, configuration)
TaskStorages->>TaskStorages : Get TaskStorageSettings
alt IsServerMode == true
TaskStorages->>TaskStorages : RegisterServerTaskStorage(URL)
TaskStorages->>Splat : RegisterConstant(ServerTaskStorage)
else IsServerMode == false
TaskStorages->>TaskStorages : RegisterFileTaskStorage(Path)
TaskStorages->>Splat : RegisterConstant(FileTaskStorage)
TaskStorages->>Splat : RegisterConstant(FileDbWatcher)
end
App->>TaskStorages : SetSettingsCommands()
Settings->>TaskStorages : ConnectCommand.Execute()
TaskStorages->>MainWindowVM : RegisterStorage(isServerMode, configuration)
MainWindowVM->>ITaskStorage : Connect()
MainWindowVM->>ITaskStorage : Init()
MainWindowVM->>ITaskStorage : Tasks.Connect()
Note over TaskStorages : Storage registration based on IsServerMode
Note over MainWindowVM : Data operations orchestrated through ITaskStorage
```

**Diagram sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs#L0-L232)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs#L0-L223)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L0-L1076)
- [TaskStorageSettings.cs](file://src/Unlimotion.ViewModel/TaskStorageSettings.cs#L0-L34)

## TaskTreeManager and Business Logic
The TaskTreeManager is the central component responsible for implementing business logic and maintaining the integrity of task relationships in Unlimotion. It serves as an intermediary between the storage layer and the application logic, ensuring that all operations on tasks follow the defined business rules and maintain data consistency.

The TaskTreeManager implements the ITaskTreeManager interface, which defines a comprehensive set of methods for manipulating tasks and their relationships. These methods include AddTask, AddChildTask, DeleteTask, UpdateTask, CloneTask, and various methods for managing parent-child and blocking relationships. Each method returns a list of affected tasks, allowing the calling code to update the UI accordingly.

A key aspect of the TaskTreeManager's functionality is its handling of task availability and completion. The CalculateAndUpdateAvailability method determines whether a task can be completed based on its dependencies, setting the IsCanBeCompleted property accordingly. When a task's completion status changes, the HandleTaskCompletionChange method is called, which updates the task's timestamps and handles repeater logic for recurring tasks.

The TaskTreeManager also implements a retry policy using the Polly library to handle transient failures during data operations. The IsCompletedAsync method wraps operations in a retry policy that attempts the operation for up to two minutes, providing resilience against temporary storage issues.

```mermaid
classDiagram
class ITaskTreeManager {
<<interface>>
+Task<List<TaskItem>> AddTask(TaskItem change, TaskItem? currentTask, bool isBlocked)
+Task<List<TaskItem>> AddChildTask(TaskItem change, TaskItem currentTask)
+Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage)
+Task<List<TaskItem>> UpdateTask(TaskItem change)
+Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents)
+Task<List<TaskItem>> AddNewParentToTask(TaskItem change, TaskItem additionalParent)
+Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent)
+Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask)
+Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
+Task<TaskItem> LoadTask(string taskId)
+Task<List<TaskItem>> DeleteParentChildRelation(TaskItem parent, TaskItem child)
+Task<List<TaskItem>> CalculateAndUpdateAvailability(TaskItem task)
+Task<List<TaskItem>> HandleTaskCompletionChange(TaskItem task)
}
class TaskTreeManager {
-IStorage Storage
+TaskTreeManager(IStorage storage)
+Task<List<TaskItem>> AddTask(TaskItem change, TaskItem? currentTask, bool isBlocked)
+Task<List<TaskItem>> AddChildTask(TaskItem change, TaskItem currentTask)
+Task<List<TaskItem>> DeleteTask(TaskItem change, bool deleteInStorage)
+Task<List<TaskItem>> UpdateTask(TaskItem change)
+Task<List<TaskItem>> CloneTask(TaskItem change, List<TaskItem> stepParents)
+Task<List<TaskItem>> AddNewParentToTask(TaskItem change, TaskItem additionalParent)
+Task<List<TaskItem>> MoveTaskToNewParent(TaskItem change, TaskItem newParent, TaskItem? prevParent)
+Task<List<TaskItem>> UnblockTask(TaskItem taskToUnblock, TaskItem blockingTask)
+Task<List<TaskItem>> BlockTask(TaskItem taskToBlock, TaskItem blockingTask)
+Task<TaskItem> LoadTask(string taskId)
+Task<List<TaskItem>> DeleteParentChildRelation(TaskItem parent, TaskItem child)
+Task<List<TaskItem>> CalculateAndUpdateAvailability(TaskItem task)
+Task<List<TaskItem>> HandleTaskCompletionChange(TaskItem task)
-Task<AutoUpdatingDictionary<string, TaskItem>> BreakParentChildRelation(TaskItem parent, TaskItem child)
-Task<AutoUpdatingDictionary<string, TaskItem>> CreateParentChildRelation(TaskItem parent, TaskItem child)
-Task<AutoUpdatingDictionary<string, TaskItem>> CreateBlockingBlockedByRelation(TaskItem taskToBlock, TaskItem blockingTask)
-Task<AutoUpdatingDictionary<string, TaskItem>> BreakBlockingBlockedByRelation(TaskItem taskToUnblock, TaskItem blockingTask)
-Task<bool> IsCompletedAsync(Func<Task<bool>> task, TimeSpan? timeout)
-Task<Dictionary<string, TaskItem>> CalculateAvailabilityForTask(TaskItem task)
-Task<List<TaskItem>> GetAffectedTasks(TaskItem task)
}
ITaskTreeManager <|-- TaskTreeManager
class TaskItem {
+string Id
+string UserId
+string Title
+string Description
+bool? IsCompleted
+bool IsCanBeCompleted
+DateTimeOffset CreatedDateTime
+DateTimeOffset? UnlockedDateTime
+DateTimeOffset? CompletedDateTime
+DateTimeOffset? ArchiveDateTime
+DateTimeOffset? PlannedBeginDateTime
+DateTimeOffset? PlannedEndDateTime
+TimeSpan? PlannedDuration
+List<string> ContainsTasks
+List<string>? ParentTasks
+List<string> BlocksTasks
+List<string>? BlockedByTasks
+RepeaterPattern Repeater
+int Importance
+bool Wanted
+int Version
+DateTime? SortOrder
}
TaskTreeManager --> TaskItem : "manipulates task relationships"
TaskTreeManager --> IStorage : "delegates persistence"
class AutoUpdatingDictionary~string, TaskItem~ {
+Dictionary<string, TaskItem> Dict
+void AddOrUpdate(string key, TaskItem value)
+void AddOrUpdateRange(Dictionary<string, TaskItem> items)
}
TaskTreeManager --> AutoUpdatingDictionary~string, TaskItem~ : "tracks affected tasks"
```

**Diagram sources**
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs#L0-L42)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs#L0-L32)

## Data Synchronization and Event-Driven Updates
Unlimotion employs a sophisticated data synchronization mechanism to ensure that changes to task data are consistently reflected across all views in the application. This is achieved through the combination of DynamicData collections, event-driven updates, and a file system watcher for local storage.

The core of the synchronization system is the SourceCache<TaskItemViewModel, string> collection, which is maintained by both FileTaskStorage and ServerTaskStorage implementations. This collection serves as the single source of truth for task data in the UI, with all views binding to filtered and transformed versions of this collection. The DynamicData library provides powerful reactive operators for filtering, sorting, and transforming the data stream, ensuring that views are automatically updated when the underlying data changes.

For local file storage, the FileDbWatcher class monitors the task storage directory for file system changes, raising OnUpdated events when files are created, modified, or deleted. These events are translated into DbUpdatedEventArgs and propagated to the FileTaskStorage, which updates the in-memory cache accordingly. This allows the application to respond to changes made by external processes or other instances of the application.

The ServerTaskStorage implementation uses SignalR to receive real-time updates from the server, with the IChatHub interface defining methods for receiving task updates and deletions. When a task is updated or deleted on the server, the corresponding SignalR event is received and processed by the ServerTaskStorage, which updates the local cache.

```mermaid
sequenceDiagram
participant UI as UI Views
participant ViewModel as TaskItemViewModel
participant Storage as ITaskStorage
participant TreeManager as TaskTreeManager
participant Persistence as IStorage
participant Watcher as FileDbWatcher
participant FileSystem as File System
UI->>Storage : User action (e.g., create task)
Storage->>TreeManager : AddTask(change, currentTask)
TreeManager->>Persistence : Save(taskItem)
Persistence->>FileSystem : Write JSON file
Persistence->>Storage : Return success
TreeManager->>Storage : Return affected tasks
Storage->>Storage : UpdateCache(affected tasks)
Storage->>ViewModel : Update(TaskItem)
ViewModel->>UI : Notify property changes
UI->>UI : Update view
FileSystem->>Watcher : File created/modified
Watcher->>Storage : OnUpdated(DbUpdatedEventArgs)
Storage->>Storage : DbWatcherOnUpdated(event)
Storage->>Persistence : Load(taskId)
Persistence->>Storage : Return TaskItem
Storage->>ViewModel : Update(TaskItem) or Create new
ViewModel->>UI : Notify property changes
UI->>UI : Update view
participant Server as RavenDB Server
participant SignalR as SignalR Hub
Server->>SignalR : Task updated
SignalR->>Storage : ReceiveTaskItem(data)
Storage->>Storage : Subscribe<ReceiveTaskItem>
Storage->>Persistence : Update(viewModel.Model)
Persistence->>Storage : Return success
Storage->>ViewModel : Update(TaskItem)
ViewModel->>UI : Notify property changes
UI->>UI : Update view
Note over Storage : Central orchestration of data operations
Note over Watcher : Monitors file system for external changes
Note over SignalR : Real-time updates from server
```

**Diagram sources**
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L0-L721)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)
- [FileDbWatcher.cs](file://src/Unlimotion.ViewModel/FileDbWatcher.cs#L0-L152)
- [DbUpdatedEventArgs.cs](file://src/Unlimotion.ViewModel/DbUpdatedEventArgs.cs#L0-L10)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs#L0-L232)

## Git-Based Backup Mechanism
Unlimotion includes a comprehensive Git-based backup mechanism that provides automated synchronization of local task data with a remote Git repository. This system is implemented using Quartz.NET for scheduling and LibGit2Sharp for Git operations, providing reliable and configurable backup functionality.

The backup system is configured through the GitSettings class, which defines properties for the remote repository URL, branch, credentials, and backup intervals. The system can be enabled or disabled through the BackupEnabled flag, and users can configure the pull and push intervals independently. The backup mechanism is only active when using the FileTaskStorage implementation, as server-based storage already provides its own synchronization and backup capabilities.

The scheduling of backup operations is managed by the Quartz.NET scheduler, which is initialized in the App.axaml.cs file. When Git backup is enabled, two jobs are scheduled: GitPullJob for pulling changes from the remote repository, and GitPushJob for pushing local changes to the remote repository. These jobs are triggered at the configured intervals, ensuring regular synchronization.

The actual Git operations are implemented in the BackupViaGitService class, which provides methods for cloning, pulling, and pushing. The Pull method implements a robust merge strategy that stashes local changes before pulling, merges the remote changes, and then reapplies the stashed changes. This approach minimizes the risk of conflicts and ensures that local changes are preserved. The Push method stages all changes, creates a commit, and pushes to the remote repository, with appropriate error handling and notifications.

```mermaid
sequenceDiagram
participant App as App.axaml.cs
participant Scheduler as Quartz Scheduler
participant GitPullJob as GitPullJob
participant GitPushJob as GitPushJob
participant GitService as BackupViaGitService
participant Repository as Git Repository
participant FileStorage as FileTaskStorage
participant Watcher as FileDbWatcher
App->>Scheduler : Initialize scheduler
App->>Scheduler : Schedule GitPullJob
App->>Scheduler : Schedule GitPushJob
App->>Scheduler : Start scheduler (if backup enabled)
Scheduler->>GitPullJob : Execute(context)
GitPullJob->>GitService : Pull()
GitService->>Watcher : SetEnable(false)
GitService->>FileStorage : SetPause(true)
GitService->>Repository : Fetch from remote
GitService->>Repository : Check for changes
alt Changes exist
GitService->>Repository : Stash local changes
GitService->>Repository : Checkout branch
GitService->>Repository : Merge remote changes
GitService->>Watcher : ForceUpdateFile(changed files)
GitService->>Repository : Apply stashed changes
GitService->>Repository : Handle conflicts if any
end
GitService->>Watcher : SetEnable(true)
GitService->>FileStorage : SetPause(false)
Scheduler->>GitPushJob : Execute(context)
GitPushJob->>GitService : Push("Backup created")
GitService->>Watcher : SetEnable(false)
GitService->>FileStorage : SetPause(true)
GitService->>Repository : Stage all changes
GitService->>Repository : Commit with message
GitService->>Repository : Push to remote
GitService->>Watcher : SetEnable(true)
GitService->>FileStorage : SetPause(false)
Note over GitService : Robust conflict handling and error recovery
Note over Scheduler : Configurable intervals for pull and push operations
Note over Watcher : Temporarily disabled during Git operations to prevent conflicts
```

**Diagram sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs#L0-L232)
- [GitPullJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPullJob.cs#L0-L19)
- [GitPushJob.cs](file://src/Unlimotion/Scheduling/Jobs/GitPushJob.cs#L0-L20)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L0-L356)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [FileDbWatcher.cs](file://src/Unlimotion.ViewModel/FileDbWatcher.cs#L0-L152)

## Data Consistency and Conflict Resolution
Unlimotion employs several strategies to ensure data consistency and handle conflicts that may arise during concurrent modifications or synchronization operations. The system is designed to maintain data integrity across multiple views and storage backends, with careful attention to transactional boundaries and conflict resolution.

For local file storage, the FileTaskStorage implementation uses a combination of file system monitoring and in-memory caching to maintain consistency. The FileDbWatcher monitors the storage directory for external changes, ensuring that modifications made by other processes are reflected in the application state. During Git operations, both the FileDbWatcher and file operations are temporarily paused to prevent conflicts between file system changes and Git operations.

When using server-based storage, the ServerTaskStorage implementation leverages SignalR for real-time synchronization, ensuring that all connected clients receive updates immediately. The system handles connection interruptions with automatic reconnection logic and maintains a queue of pending operations to be retried when the connection is restored.

The Git-based backup system implements a sophisticated conflict resolution strategy that prioritizes the preservation of local changes. When pulling from the remote repository, the system stashes local changes before merging, applies the remote changes, and then reapplies the stashed changes. This approach minimizes the risk of losing local modifications while still incorporating remote updates. If conflicts occur during the merge process, the system notifies the user and requires manual resolution.

The TaskTreeManager also contributes to data consistency by enforcing business rules and maintaining the integrity of task relationships. Operations that modify task relationships are wrapped in retry policies to handle transient failures, and the system ensures that related tasks are updated consistently when a task's state changes.

**Section sources**
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L0-L721)
- [BackupViaGitService.cs](file://src/Unlimotion.Services/BackupViaGitService.cs#L0-L356)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)
- [FileDbWatcher.cs](file://src/Unlimotion.ViewModel/FileDbWatcher.cs#L0-L152)

## Conclusion
Unlimotion's data flow and persistence architecture demonstrates a well-designed separation of concerns, with clear boundaries between the UI layer, business logic, and storage implementations. The system provides flexible storage options through the ITaskStorage interface, allowing users to choose between local file storage and remote server storage based on their needs.

The TaskTreeManager serves as the central orchestrator of business logic, ensuring that task relationships and availability rules are consistently enforced. The use of DynamicData for data synchronization enables reactive updates across multiple views, providing a responsive user experience.

The Git-based backup mechanism adds an additional layer of data protection for local storage, with configurable scheduling and robust conflict resolution. This system ensures that task data is regularly synchronized with a remote repository, providing protection against data loss.

Overall, the architecture balances flexibility, reliability, and performance, providing a solid foundation for task management while accommodating different user requirements and deployment scenarios. The use of dependency injection, interface abstraction, and reactive programming patterns contributes to a maintainable and extensible codebase.

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L0-L1076)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L0-L837)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs#L0-L457)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L0-L721)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs#L0-L232)