# Architecture Overview

<cite>
**Referenced Files in This Document**   
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [AppModelMapping.cs](file://src/Unlimotion/AppModelMapping.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)
- [ViewLocator.cs](file://src/Unlimotion/ViewLocator.cs)
- [AppExtensions.cs](file://src/Unlimotion/AppExtensions.cs)
- [ClientSettings.cs](file://src/Unlimotion/ClientSettings.cs)
- [TaskStorageSettings.cs](file://src/Unlimotion/TaskStorageSettings.cs)
- [AppSettings.cs](file://src/Unlimotion.Server/AppSettings.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Architecture Overview](#architecture-overview)
5. [Detailed Component Analysis](#detailed-component-analysis)
6. [Dependency Analysis](#dependency-analysis)
7. [Performance Considerations](#performance-considerations)
8. [Troubleshooting Guide](#troubleshooting-guide)
9. [Conclusion](#conclusion)

## Introduction
The Unlimotion application is a cross-platform task management system built using modern .NET technologies with a clear separation of concerns through the MVVM (Model-View-ViewModel) architectural pattern. The application leverages AvaloniaUI for cross-platform UI development, ReactiveUI for reactive programming, and Splat for dependency injection. It features both local file-based and server-based data storage options, with synchronization capabilities via Git and a SignalR-based real-time communication system. The architecture emphasizes separation between UI, business logic, and data access layers, with well-defined interfaces and dependency inversion principles applied throughout the codebase.

## Project Structure
The Unlimotion application follows a modular, layered architecture with distinct components separated into different projects within the solution. The structure demonstrates a clear separation between platform-specific implementations, shared business logic, domain models, and server-side components.

```mermaid
graph TD
subgraph "Client Applications"
Desktop[Unlimotion.Desktop]
Android[Unlimotion.Android]
Browser[Unlimotion.Browser]
iOS[Unlimotion.iOS]
end
subgraph "Shared Core"
UI[Unlimotion]
ViewModel[Unlimotion.ViewModel]
Domain[Unlimotion.Domain]
Interface[Unlimotion.Interface]
TaskTree[Unlimotion.TaskTreeManager]
end
subgraph "Server Components"
Server[Unlimotion.Server]
ServiceInterface[Unlimotion.Server.ServiceInterface]
ServiceModel[Unlimotion.Server.ServiceModel]
end
subgraph "Utilities"
AppNameGenerator[Unlimotion.AppNameGenerator]
TelegramBot[Unlimotion.TelegramBot]
Test[Unlimotion.Test]
end
Desktop --> UI
Android --> UI
Browser --> UI
iOS --> UI
UI --> ViewModel
UI --> Domain
UI --> Interface
UI --> TaskTree
ViewModel --> Domain
ViewModel --> Interface
ViewModel --> TaskTree
Server --> ServiceInterface
Server --> ServiceModel
Server --> Domain
Server --> Interface
ServiceInterface --> ServiceModel
ServiceInterface --> Domain
Test --> UI
Test --> ViewModel
Test --> Server
Test --> FileTaskStorage
Test --> ServerTaskStorage
```

**Diagram sources**
- [Unlimotion.sln](file://src/Unlimotion.sln)
- [Directory.Build.props](file://src/Directory.Build.props)

**Section sources**
- [Unlimotion.sln](file://src/Unlimotion.sln)
- [Directory.Build.props](file://src/Directory.Build.props)

## Core Components
The Unlimotion application is built around several core components that implement the MVVM pattern and facilitate communication between the UI, business logic, and data layers. The application uses Splat for dependency injection, ReactiveUI for reactive programming, and follows a clean separation between concerns. The main components include the App class for application initialization, MainWindowViewModel for managing the main window's state and behavior, TaskItemViewModel for individual task management, and various storage implementations for data persistence.

**Section sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)

## Architecture Overview
The Unlimotion application follows a layered architecture based on the MVVM (Model-View-ViewModel) pattern, with clear separation between UI, business logic, and data access layers. The architecture leverages AvaloniaUI for cross-platform UI development, ReactiveUI for reactive programming, and Splat for dependency injection. The system supports both local file-based storage and server-based storage with real-time synchronization via SignalR.

```mermaid
graph TD
subgraph "Presentation Layer"
View[Views]
ViewModel[ViewModels]
Converter[Converters]
end
subgraph "Business Logic Layer"
Services[Services]
Managers[Managers]
Extensions[Extensions]
end
subgraph "Data Access Layer"
Storage[Storage Implementations]
TaskTreeManager[TaskTreeManager]
Mapper[AutoMapper]
end
subgraph "External Services"
Server[Unlimotion Server]
Git[Git Repository]
SignalR[SignalR Hub]
end
View --> ViewModel
ViewModel --> Services
Services --> Storage
Storage --> TaskTreeManager
Storage --> Server
Storage --> Git
Server --> SignalR
TaskTreeManager --> Storage
style View fill:#f9f,stroke:#333
style ViewModel fill:#bbf,stroke:#333
style Services fill:#f96,stroke:#333
style Storage fill:#6f9,stroke:#333
style Server fill:#69f,stroke:#333
```

**Diagram sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)

**Section sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)

## Detailed Component Analysis

### MVVM Architecture Implementation
The Unlimotion application implements the MVVM (Model-View-ViewModel) pattern with a clear separation between the UI (View), business logic (ViewModel), and data (Model). The ViewLocator class automatically resolves views from viewmodels by convention, following the naming pattern where "ViewModel" is replaced with "View" in the class name.

```mermaid
classDiagram
class ViewLocator {
+Build(object param) Control
+Match(object data) bool
}
class App {
+Initialize() void
+OnFrameworkInitializationCompleted() void
+Init(string configPath) void
}
class MainWindowViewModel {
+Connect() Task
+RegisterCommands() void
+Title string
+Settings SettingsViewModel
+Graph GraphViewModel
+CurrentTaskItem TaskItemViewModel
}
class TaskItemViewModel {
+Model TaskItem
+Title string
+Description string
+IsCompleted bool?
+CreatedDateTime DateTimeOffset
+UnlockedDateTime DateTimeOffset?
+CompletedDateTime DateTimeOffset?
+ArchiveDateTime DateTimeOffset?
+PlannedBeginDateTime DateTime?
+PlannedEndDateTime DateTime?
+PlannedDuration TimeSpan?
+ContainsTasks ReadOnlyObservableCollection~TaskItemViewModel~
+ParentsTasks ReadOnlyObservableCollection~TaskItemViewModel~
+BlocksTasks ReadOnlyObservableCollection~TaskItemViewModel~
+BlockedByTasks ReadOnlyObservableCollection~TaskItemViewModel~
+Repeater RepeaterPatternViewModel
+Importance int
+Wanted bool
+Update(TaskItem taskItem) void
+SaveItemCommand ReactiveCommand~Unit, Unit~
}
class TaskItem {
+Id string
+UserId string
+Title string
+Description string
+IsCompleted bool?
+CreatedDateTime DateTimeOffset
+UnlockedDateTime DateTimeOffset?
+CompletedDateTime DateTimeOffset?
+ArchiveDateTime DateTimeOffset?
+PlannedBeginDateTime DateTime?
+PlannedEndDateTime DateTime?
+PlannedDuration TimeSpan?
+ContainsTasks string[]
+ParentTasks List~string?
+BlocksTasks string[]
+BlockedByTasks List~string?
+Repeater RepeaterPattern
+Importance int
+Wanted bool
+Version int
+SortOrder DateTime?
}
ViewLocator --> App : "used by"
App --> MainWindowViewModel : "creates"
MainWindowViewModel --> TaskItemViewModel : "contains"
TaskItemViewModel --> TaskItem : "wraps"
```

**Diagram sources**
- [ViewLocator.cs](file://src/Unlimotion/ViewLocator.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)

**Section sources**
- [ViewLocator.cs](file://src/Unlimotion/ViewLocator.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)

### Data Storage and Synchronization
The Unlimotion application supports multiple data storage backends through a common interface, allowing users to choose between local file-based storage and server-based storage. The ITaskStorage interface defines the contract for task operations, with FileTaskStorage and ServerTaskStorage providing concrete implementations. The system uses a task tree manager to handle complex relationships between tasks, such as parent-child and blocking relationships.

```mermaid
classDiagram
class ITaskStorage {
<<interface>>
+Tasks SourceCache~TaskItemViewModel, string~
+Connect() Task~bool~
+Disconnect() Task
+Init() Task
+Add(TaskItemViewModel, TaskItemViewModel, bool) Task~bool~
+AddChild(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Delete(TaskItemViewModel, bool) Task~bool~
+Delete(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Update(TaskItemViewModel) Task~bool~
+Update(TaskItem) Task~bool~
+Clone(TaskItemViewModel, TaskItemViewModel[]) Task~TaskItemViewModel~
+CopyInto(TaskItemViewModel, TaskItemViewModel[]) Task~bool~
+MoveInto(TaskItemViewModel, TaskItemViewModel[], TaskItemViewModel) Task~bool~
+Block(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Unblock(TaskItemViewModel, TaskItemViewModel) Task~bool~
+RemoveParentChildConnection(TaskItemViewModel, TaskItemViewModel) Task
+GetAll() IAsyncEnumerable~TaskItem~
+Load(string) Task~TaskItem~
+Save(TaskItem) Task~bool~
+Remove(string) Task~bool~
+BulkInsert(IEnumerable~TaskItem~) Task
}
class FileTaskStorage {
+Path string
+Tasks SourceCache~TaskItemViewModel, string~
+TaskTreeManager ITaskTreeManager
+Connect() Task~bool~
+Disconnect() Task
+Init() Task
+Add(TaskItemViewModel, TaskItemViewModel, bool) Task~bool~
+AddChild(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Delete(TaskItemViewModel, bool) Task~bool~
+Delete(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Update(TaskItemViewModel) Task~bool~
+Update(TaskItem) Task~bool~
+Clone(TaskItemViewModel, TaskItemViewModel[]) Task~TaskItemViewModel~
+CopyInto(TaskItemViewModel, TaskItemViewModel[]) Task~bool~
+MoveInto(TaskItemViewModel, TaskItemViewModel[], TaskItemViewModel) Task~bool~
+Block(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Unblock(TaskItemViewModel, TaskItemViewModel) Task~bool~
+RemoveParentChildConnection(TaskItemViewModel, TaskItemViewModel) Task
+GetAll() IAsyncEnumerable~TaskItem~
+Load(string) Task~TaskItem~
+Save(TaskItem) Task~bool~
+Remove(string) Task~bool~
+BulkInsert(IEnumerable~TaskItem~) Task
}
class ServerTaskStorage {
+Url string
+Tasks SourceCache~TaskItemViewModel, string~
+TaskTreeManager ITaskTreeManager
+IsConnected bool
+IsSignedIn bool
+Connect() Task~bool~
+Disconnect() Task
+Init() Task
+Add(TaskItemViewModel, TaskItemViewModel, bool) Task~bool~
+AddChild(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Delete(TaskItemViewModel, bool) Task~bool~
+Delete(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Update(TaskItemViewModel) Task~bool~
+Update(TaskItem) Task~bool~
+Clone(TaskItemViewModel, TaskItemViewModel[]) Task~TaskItemViewModel~
+CopyInto(TaskItemViewModel, TaskItemViewModel[]) Task~bool~
+MoveInto(TaskItemViewModel, TaskItemViewModel[], TaskItemViewModel) Task~bool~
+Block(TaskItemViewModel, TaskItemViewModel) Task~bool~
+Unblock(TaskItemViewModel, TaskItemViewModel) Task~bool~
+RemoveParentChildConnection(TaskItemViewModel, TaskItemViewModel) Task
+GetAll() IAsyncEnumerable~TaskItem~
+Load(string) Task~TaskItem~
+Save(TaskItem) Task~bool~
+Remove(string) Task~bool~
+BulkInsert(IEnumerable~TaskItem~) Task
}
class ITaskTreeManager {
<<interface>>
+AddTask(TaskItem, TaskItem, bool) Task~IEnumerable~TaskItem~~
+AddChildTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+DeleteTask(TaskItem, bool) Task~IEnumerable~TaskItem~~
+DeleteParentChildRelation(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+UpdateTask(TaskItem) Task~IEnumerable~TaskItem~~
+CloneTask(TaskItem, TaskItem[]) Task~IEnumerable~TaskItem~~
+AddNewParentToTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+MoveTaskToNewParent(TaskItem, TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+BlockTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+UnblockTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
}
class TaskTreeManager {
+AddTask(TaskItem, TaskItem, bool) Task~IEnumerable~TaskItem~~
+AddChildTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+DeleteTask(TaskItem, bool) Task~IEnumerable~TaskItem~~
+DeleteParentChildRelation(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+UpdateTask(TaskItem) Task~IEnumerable~TaskItem~~
+CloneTask(TaskItem, TaskItem[]) Task~IEnumerable~TaskItem~~
+AddNewParentToTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+MoveTaskToNewParent(TaskItem, TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+BlockTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
+UnblockTask(TaskItem, TaskItem) Task~IEnumerable~TaskItem~~
}
ITaskStorage <|-- FileTaskStorage
ITaskStorage <|-- ServerTaskStorage
ITaskTreeManager <|-- TaskTreeManager
FileTaskStorage --> TaskTreeManager
ServerTaskStorage --> TaskTreeManager
```

**Diagram sources**
- [ITaskStorage.cs](file://src/Unlimotion.ViewModel/ITaskStorage.cs)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)

**Section sources**
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)

### Dependency Injection and Reactive Programming
The Unlimotion application uses Splat as its dependency injection framework, registering services and components in the Locator.CurrentMutable container. The application leverages ReactiveUI for reactive programming, enabling automatic UI updates in response to data changes. The architecture follows the reactive paradigm, with observables and reactive commands used extensively throughout the codebase to manage state changes and user interactions.

```mermaid
classDiagram
class App {
+Init(string configPath) void
+GetMainWindowViewModel() MainWindowViewModel
}
class MainWindowViewModel {
+MainWindowViewModel() void
+Connect() Task
+RegisterCommands() void
}
class TaskItemViewModel {
+TaskItemViewModel(TaskItem, ITaskStorage) void
+Init(ITaskStorage) void
}
class ITaskStorage {
<<interface>>
}
class IAppNameDefinitionService {
<<interface>>
+GetAppName() string
}
class INotificationManagerWrapper {
<<interface>>
+SuccessToast(string) void
+ErrorToast(string) void
+Ask(string, string, Action) void
}
class IDialogs {
<<interface>>
+ShowOpenFolderDialogAsync(string) Task~string~
}
class IRemoteBackupService {
<<interface>>
+CloneOrUpdateRepo() void
+Pull() void
+Push(string) void
}
class IConfiguration {
<<interface>>
+Get~T~(string) T
+Set(string, object) void
}
class IMapper {
<<interface>>
+Map~TDestination~(object source) TDestination
}
App --> MainWindowViewModel : "creates"
App --> ITaskStorage : "registers"
App --> IAppNameDefinitionService : "registers"
App --> INotificationManagerWrapper : "registers"
App --> IDialogs : "registers"
App --> IRemoteBackupService : "registers"
App --> IConfiguration : "registers"
App --> IMapper : "registers"
MainWindowViewModel --> ITaskStorage : "uses"
MainWindowViewModel --> IAppNameDefinitionService : "uses"
MainWindowViewModel --> INotificationManagerWrapper : "uses"
MainWindowViewModel --> IConfiguration : "uses"
MainWindowViewModel --> IMapper : "uses"
TaskItemViewModel --> ITaskStorage : "uses"
```

**Diagram sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)

**Section sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)

### Server Integration and API Design
The Unlimotion server is built using ServiceStack, providing a RESTful API for task management operations. The server uses JWT authentication for secure access, with token-based authentication and refresh mechanisms. The API design follows a service-oriented architecture with clear separation between service interfaces, service models, and data models. SignalR is used for real-time communication between clients and the server, enabling instant synchronization of task changes across all connected clients.

```mermaid
sequenceDiagram
participant Client as "Unlimotion Client"
participant ViewModel as "MainWindowViewModel"
participant Storage as "ServerTaskStorage"
participant ServiceClient as "JsonServiceClient"
participant Server as "Unlimotion Server"
participant Hub as "ChatHub"
participant Database as "RavenDB"
Client->>ViewModel : User action (e.g., create task)
ViewModel->>Storage : Call Add() method
Storage->>ServiceClient : Call serviceClient.PostAsync()
ServiceClient->>Server : HTTP POST /api/CreateTask
Server->>Hub : Broadcast task update
Hub->>Client : Send ReceiveTaskItem message
Server->>Database : Save task data
Database-->>Server : Confirmation
Server-->>ServiceClient : Return result
ServiceClient-->>Storage : Return task ID
Storage-->>ViewModel : Return success
ViewModel-->>Client : Update UI
```

**Diagram sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)
- [ChatHub.cs](file://src/Unlimotion.Server/hubs/ChatHub.cs)

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [AppHost.cs](file://src/Unlimotion.Server/AppHost.cs)

## Dependency Analysis
The Unlimotion application has a well-structured dependency graph that follows the dependency inversion principle. Core interfaces are defined in shared libraries, with concrete implementations provided in specific projects. This allows for loose coupling between components and facilitates testing and extensibility.

```mermaid
graph TD
Unlimotion --> Unlimotion.ViewModel
Unlimotion --> Unlimotion.Domain
Unlimotion --> Unlimotion.Interface
Unlimotion --> Unlimotion.TaskTreeManager
Unlimotion --> Unlimotion.Server.ServiceModel
Unlimotion.ViewModel --> Unlimotion.Domain
Unlimotion.ViewModel --> Unlimotion.Interface
Unlimotion.ViewModel --> Unlimotion.TaskTreeManager
Unlimotion.Server --> Unlimotion.Server.ServiceInterface
Unlimotion.Server --> Unlimotion.Server.ServiceModel
Unlimotion.Server --> Unlimotion.Domain
Unlimotion.Server --> Unlimotion.Interface
Unlimotion.Server.ServiceInterface --> Unlimotion.Server.ServiceModel
Unlimotion.Server.ServiceInterface --> Unlimotion.Domain
Unlimotion.Desktop --> Unlimotion
Unlimotion.Android --> Unlimotion
Unlimotion.Browser --> Unlimotion
Unlimotion.iOS --> Unlimotion
Unlimotion.Test --> Unlimotion
Unlimotion.Test --> Unlimotion.ViewModel
Unlimotion.Test --> Unlimotion.Server
style Unlimotion fill:#f9f,stroke:#333
style Unlimotion.ViewModel fill:#bbf,stroke:#333
style Unlimotion.Domain fill:#6f9,stroke:#333
style Unlimotion.Interface fill:#6f9,stroke:#333
style Unlimotion.TaskTreeManager fill:#6f9,stroke:#333
style Unlimotion.Server fill:#69f,stroke:#333
style Unlimotion.Server.ServiceInterface fill:#69f,stroke:#333
style Unlimotion.Server.ServiceModel fill:#69f,stroke:#333
style Unlimotion.Desktop fill:#f96,stroke:#333
style Unlimotion.Android fill:#f96,stroke:#333
style Unlimotion.Browser fill:#f96,stroke:#333
style Unlimotion.iOS fill:#f96,stroke:#333
style Unlimotion.Test fill:#ff6,stroke:#333
```

**Diagram sources**
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
- [Unlimotion.ViewModel.csproj](file://src/Unlimotion.ViewModel/Unlimotion.ViewModel.csproj)
- [Unlimotion.Domain.csproj](file://src/Unlimotion.Domain/Unlimotion.Domain.csproj)
- [Unlimotion.Interface.csproj](file://src/Unlimotion.Interface/Unlimotion.Interface.csproj)
- [Unlimotion.TaskTree.csproj](file://src/Unlimotion.TaskTreeManager/Unlimotion.TaskTree.csproj)
- [Unlimotion.Server.csproj](file://src/Unlimotion.Server/Unlimotion.Server.csproj)
- [Unlimotion.Server.ServiceInterface.csproj](file://src/Unlimotion.Server.ServiceInterface/Unlimotion.Server.ServiceInterface.csproj)
- [Unlimotion.Server.ServiceModel.csproj](file://src/Unlimotion.Server.ServiceModel/Unlimotion.Server.ServiceModel.csproj)
- [Unlimotion.Desktop.csproj](file://src/Unlimotion.Desktop/Unlimotion.Desktop.csproj)
- [Unlimotion.Android.csproj](file://src/Unlimotion.Android/Unlimotion.Android.csproj)
- [Unlimotion.Browser.csproj](file://src/Unlimotion.Browser/Unlimotion.Browser.csproj)
- [Unlimotion.iOS.csproj](file://src/Unlimotion.iOS/Unlimotion.iOS.csproj)
- [Unlimotion.Test.csproj](file://src/Unlimotion.Test/Unlimotion.Test.csproj)

**Section sources**
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
- [Unlimotion.ViewModel.csproj](file://src/Unlimotion.ViewModel/Unlimotion.ViewModel.csproj)
- [Unlimotion.Domain.csproj](file://src/Unlimotion.Domain/Unlimotion.Domain.csproj)

## Performance Considerations
The Unlimotion application implements several performance optimizations to ensure responsive user experience, particularly when dealing with large numbers of tasks. The application uses reactive programming patterns with throttling to prevent excessive updates, employs caching mechanisms to minimize redundant operations, and leverages efficient data structures for task management.

The TaskItemViewModel class implements property change throttling with a default interval of 10 seconds, preventing excessive save operations when multiple properties are changed in quick succession. The application also uses SourceCache from the DynamicData library to efficiently manage collections of tasks, providing optimized filtering, sorting, and transformation operations.

For data persistence, the FileTaskStorage implementation uses a directory-based storage system where each task is stored in a separate JSON file, allowing for efficient individual task operations without loading the entire dataset. The ServerTaskStorage implementation uses a combination of local caching and server synchronization to minimize network requests and provide responsive UI updates.

**Section sources**
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)

## Troubleshooting Guide
When encountering issues with the Unlimotion application, consider the following common problems and solutions:

1. **Connection issues with server storage**: Verify that the server URL is correctly configured in the TaskStorage settings. Check that the server is running and accessible from the client machine. Ensure that SSL/TLS certificates are properly configured, as the application bypasses certificate validation in development mode.

2. **Authentication failures**: Ensure that valid login credentials are provided in the TaskStorage settings. The application will attempt to register a new user if the specified credentials are not found on the server. Check that the JWT token has not expired and that the refresh token mechanism is functioning correctly.

3. **Data synchronization problems**: When using Git-based backup, verify that the remote repository URL, branch, and authentication credentials are correctly configured. Check that the Git service has the necessary permissions to push and pull from the repository.

4. **Performance issues with large datasets**: The application may experience performance degradation when managing a large number of tasks. Consider using server-based storage for better performance with large datasets, as it provides more efficient querying and indexing capabilities compared to the file-based storage.

5. **UI update problems**: If the UI is not updating correctly in response to data changes, verify that the reactive programming patterns are properly implemented. Check that property change notifications are being raised correctly and that the appropriate observables are subscribed to.

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [FileTaskStorage.cs](file://src/Unlimotion/FileTaskStorage.cs)
- [TaskStorages.cs](file://src/Unlimotion/TaskStorages.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)

## Conclusion
The Unlimotion application demonstrates a well-architected MVVM implementation with clear separation of concerns, leveraging modern .NET technologies for cross-platform development. The architecture effectively combines AvaloniaUI for UI development, ReactiveUI for reactive programming, and Splat for dependency injection, creating a maintainable and extensible codebase.

The application's support for multiple storage backends (local file and server-based) provides flexibility for different deployment scenarios, while the use of ServiceStack for the server API and SignalR for real-time communication enables robust client-server interactions. The integration of Git for backup and synchronization adds an additional layer of data protection and collaboration capabilities.

Key architectural strengths include the consistent application of the MVVM pattern, the use of reactive programming for responsive UI updates, and the modular design that facilitates testing and maintenance. The application could benefit from additional documentation and more comprehensive error handling, particularly in edge cases related to network connectivity and data synchronization.

Overall, the Unlimotion application represents a sophisticated task management solution with a solid architectural foundation that supports its cross-platform capabilities and rich feature set.