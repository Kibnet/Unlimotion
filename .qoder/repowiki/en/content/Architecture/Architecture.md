# Architecture

<cite>
**Referenced Files in This Document**   
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [ApplicationViewModel.cs](file://src/Unlimotion/ApplicationViewModel.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs)
- [IStorage.cs](file://src/Unlimotion.TaskTreeManager/IStorage.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs)
- [Task.cs](file://src/Unlimotion.Server.ServiceModel/Task.cs)
- [Program.cs](file://src/Unlimotion.Server/Program.cs)
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
- [Unlimotion.Domain.csproj](file://src/Unlimotion.Domain/Unlimotion.Domain.csproj)
- [Unlimotion.TaskTree.csproj](file://src/Unlimotion.TaskTreeManager/Unlimotion.TaskTree.csproj)
- [Unlimotion.ViewModel.csproj](file://src/Unlimotion.ViewModel/Unlimotion.ViewModel.csproj)
- [Unlimotion.Server.csproj](file://src/Unlimotion.Server/Unlimotion.Server.csproj)
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
Unlimotion is a full-stack personal productivity application designed with a modular architecture to support multiple client interfaces (desktop, web, mobile) connecting to a central server backend. The system implements the MVVM (Model-View-ViewModel) architectural pattern across all UI components, with Views defined in Avalonia XAML files binding to ViewModels in the Unlimotion.ViewModel project. The architecture separates concerns across distinct layers: domain entities in Unlimotion.Domain, business logic in Unlimotion.TaskTreeManager, service interfaces in Unlimotion.Server.ServiceInterface, and data transfer models in Unlimotion.Server.ServiceModel. Dependency injection is implemented using Splat for service registration and resolution across components. The system supports cross-platform access through Avalonia for desktop, custom Android/iOS projects for mobile, and WebAssembly for browser access, with real-time synchronization via SignalR and Git-based backup workflows.

## Project Structure

```mermaid
graph TD
subgraph "Client Applications"
Desktop[Unlimotion.Desktop<br/>Avalonia UI]
Browser[Unlimotion.Browser<br/>WebAssembly]
Android[Unlimotion.Android<br/>Mobile]
iOS[Unlimotion.iOS<br/>Mobile]
end
subgraph "Shared UI Layer"
UI[Unlimotion<br/>Views & Behaviors]
VM[Unlimotion.ViewModel<br/>MVVM Components]
end
subgraph "Business Logic"
TTM[Unlimotion.TaskTreeManager<br/>Business Logic]
Domain[Unlimotion.Domain<br/>Domain Models]
end
subgraph "Server Layer"
Server[Unlimotion.Server<br/>ASP.NET Core Host]
SI[Unlimotion.Server.ServiceInterface<br/>ServiceStack Services]
SM[Unlimotion.Server.ServiceModel<br/>DTOs & Routes]
end
subgraph "Infrastructure"
Interface[Unlimotion.Interface<br/>Contracts]
Splat[Splat<br/>DI Container]
SignalR[SignalR.EasyUse<br/>Real-time]
end
Desktop --> UI
Browser --> UI
Android --> UI
iOS --> UI
UI --> VM
VM --> TTM
TTM --> Domain
TTM --> SI
SI --> Server
SM --> SI
Interface --> VM
Interface --> SI
Splat --> VM
Splat --> TTM
Splat --> Server
SignalR --> Server
SignalR --> UI
```

**Diagram sources**
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
- [Unlimotion.Domain.csproj](file://src/Unlimotion.Domain/Unlimotion.Domain.csproj)
- [Unlimotion.TaskTree.csproj](file://src/Unlimotion.TaskTreeManager/Unlimotion.TaskTree.csproj)
- [Unlimotion.ViewModel.csproj](file://src/Unlimotion.ViewModel/Unlimotion.ViewModel.csproj)
- [Unlimotion.Server.csproj](file://src/Unlimotion.Server/Unlimotion.Server.csproj)

**Section sources**
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
- [Unlimotion.Domain.csproj](file://src/Unlimotion.Domain/Unlimotion.Domain.csproj)
- [Unlimotion.TaskTree.csproj](file://src/Unlimotion.TaskTreeManager/Unlimotion.TaskTree.csproj)

## Core Components

The Unlimotion architecture is built around several core components that implement a clean separation of concerns. The MVVM pattern is consistently applied across all client interfaces, with XAML-based Views in the Unlimotion project binding to ViewModels in the Unlimotion.ViewModel project. Business logic is encapsulated in the Unlimotion.TaskTreeManager, which implements the ITaskTreeManager interface to manage task relationships, availability calculations, and state transitions. Domain entities are defined in Unlimotion.Domain, while service interfaces and data transfer models are separated into Unlimotion.Server.ServiceInterface and Unlimotion.Server.ServiceModel respectively. The Splat library provides dependency injection capabilities, allowing for loose coupling between components and facilitating testability.

**Section sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)

## Architecture Overview

```mermaid
graph TB
subgraph "Clients"
DesktopClient["Desktop Client<br/>(Avalonia)"]
WebClient["Web Client<br/>(WebAssembly)"]
MobileClient["Mobile Clients<br/>(Android/iOS)"]
end
subgraph "Communication"
SignalR["SignalR<br/>Real-time Updates"]
HTTP["HTTP/REST<br/>ServiceStack"]
end
subgraph "Server"
API["ServiceStack API<br/>TaskService, AuthService"]
DI["Splat DI Container"]
Mapper["AutoMapper<br/>DTO Conversion"]
Session["RavenDB Session"]
end
subgraph "Data"
RavenDB["RavenDB<br/>Document Database"]
Git["Git Repository<br/>Backup Storage"]
end
DesktopClient --> SignalR
WebClient --> SignalR
MobileClient --> SignalR
DesktopClient --> HTTP
WebClient --> HTTP
MobileClient --> HTTP
SignalR --> API
HTTP --> API
API --> DI
API --> Mapper
API --> Session
Session --> RavenDB
API --> Git
classDef client fill:#e1f3fb,stroke:#333;
classDef comm fill:#fff2cc,stroke:#333;
classDef server fill:#d5e8d4,stroke:#333;
classDef data fill:#f8cecc,stroke:#333;
class DesktopClient,WebClient,MobileClient client
class SignalR,HTTP comm
class API,DI,Mapper,Session server
class RavenDB,Git data
```

**Diagram sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs)
- [Program.cs](file://src/Unlimotion.Server/Program.cs)

## Detailed Component Analysis

### MVVM Architecture Implementation

The MVVM pattern is implemented across all client interfaces with a clear separation between Views, ViewModels, and business logic. Views are defined in Avalonia XAML files within the Unlimotion project, while corresponding ViewModels are implemented in the Unlimotion.ViewModel project. Data binding connects View elements to ViewModel properties and commands, with the Splat dependency injection framework resolving ViewModel instances.

```mermaid
classDiagram
class MainWindowViewModel {
+IConfiguration Configuration
+SettingsViewModel Settings
+GraphViewModel Graph
+ReactiveCommand Create
+ReactiveCommand CreateSibling
+ReactiveCommand Remove
+TaskItemViewModel CurrentTaskItem
+ObservableCollection~TaskWrapperViewModel~ CurrentItems
+Connect() Task
}
class TaskItemViewModel {
+TaskItem Model
+ITaskStorage Repository
+ReactiveCommand UpdateCommand
+ReactiveCommand DeleteCommand
+ReactiveCommand CompleteCommand
}
class TaskWrapperViewModel {
+TaskItemViewModel TaskItem
+TaskWrapperViewModel Parent
+ObservableCollection~TaskWrapperViewModel~ Children
+bool IsExpanded
}
class ITaskStorage {
<<interface>>
+IObservable~TaskItemViewModel~ Tasks
+Connect() Task
+Init() Task
+Add(TaskItemViewModel) Task
+AddChild(TaskItemViewModel, TaskItemViewModel) Task
+Remove(TaskItemViewModel) Task
}
MainWindowViewModel --> TaskItemViewModel : "contains"
MainWindowViewModel --> TaskWrapperViewModel : "manages"
TaskWrapperViewModel --> TaskItemViewModel : "wraps"
TaskItemViewModel --> ITaskStorage : "uses"
MainWindowViewModel --> ITaskStorage : "uses"
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)

### Business Logic Layer

The Unlimotion.TaskTreeManager implements the core business logic for task management, including task creation, deletion, relationship management, and availability calculations. The ITaskTreeManager interface defines operations for manipulating task hierarchies and state transitions, with implementations that ensure data consistency through transactional operations.

```mermaid
classDiagram
class TaskTreeManager {
-IStorage Storage
+AddTask(TaskItem, TaskItem, bool) TaskItem[]
+AddChildTask(TaskItem, TaskItem) TaskItem[]
+DeleteTask(TaskItem, bool) TaskItem[]
+UpdateTask(TaskItem) TaskItem[]
+CloneTask(TaskItem, TaskItem[]) TaskItem[]
+HandleTaskCompletionChange(TaskItem) TaskItem[]
+CalculateAndUpdateAvailability(TaskItem) TaskItem[]
}
class ITaskTreeManager {
<<interface>>
+AddTask(TaskItem, TaskItem, bool) TaskItem[]
+AddChildTask(TaskItem, TaskItem) TaskItem[]
+DeleteTask(TaskItem, bool) TaskItem[]
+UpdateTask(TaskItem) TaskItem[]
+CloneTask(TaskItem, TaskItem[]) TaskItem[]
+HandleTaskCompletionChange(TaskItem) TaskItem[]
+CalculateAndUpdateAvailability(TaskItem) TaskItem[]
}
class IStorage {
<<interface>>
+Save(TaskItem) Task~bool~
+Remove(string) Task~bool~
+Load(string) Task~TaskItem?~
}
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
+string[] ContainsTasks
+string[]? ParentTasks
+string[] BlocksTasks
+string[]? BlockedByTasks
+RepeaterPattern Repeater
+int Importance
+bool Wanted
}
TaskTreeManager --> IStorage : "depends on"
TaskTreeManager --> TaskItem : "manipulates"
TaskTreeManager --> ITaskTreeManager : "implements"
ITaskTreeManager --> TaskItem : "parameters"
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs)
- [IStorage.cs](file://src/Unlimotion.TaskTreeManager/IStorage.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)

### Server-Client Communication

The server-client communication architecture uses ServiceStack for RESTful API endpoints and SignalR for real-time updates. The Unlimotion.Server project hosts the ASP.NET Core application with ServiceStack services that handle CRUD operations on tasks, while SignalR enables push notifications for data synchronization across connected clients.

```mermaid
sequenceDiagram
participant Client as "Client App"
participant ViewModel as "MainWindowViewModel"
participant Storage as "ITaskStorage"
participant Server as "Unlimotion.Server"
participant Service as "TaskService"
participant DB as "RavenDB Session"
Client->>ViewModel : User creates task
ViewModel->>Storage : Add(task)
Storage->>Server : HTTP POST /tasks/bulk
Server->>Service : Process BulkInsertTasks
Service->>DB : Store tasks in RavenDB
DB-->>Service : Confirmation
Service-->>Server : Success
Server-->>Storage : 200 OK
Storage->>ViewModel : Update observable collection
ViewModel->>Client : UI updates via data binding
Server->>Client : SignalR push notification
Client->>ViewModel : Process real-time update
ViewModel->>Client : UI synchronizes
```

**Diagram sources**
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs)
- [Task.cs](file://src/Unlimotion.Server.ServiceModel/Task.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)

### Data Flow and State Management

The data flow in Unlimotion follows a unidirectional pattern from user interaction through ViewModel commands to business logic processing and persistent storage. State management is implemented through observable collections and reactive programming patterns, ensuring UI consistency across multiple views.

```mermaid
flowchart TD
UserInteraction["User Interaction\n(Create, Update, Delete)"] --> ViewModelCommand["ViewModel Command\n(ReactiveCommand)"]
ViewModelCommand --> BusinessLogic["Business Logic\n(TaskTreeManager)"]
BusinessLogic --> StorageOperation["Storage Operation\n(IStorage Implementation)"]
StorageOperation --> Persistence["Persistent Storage\n(RavenDB or File)"]
Persistence --> Notification["Change Notification\n(Observable Collection)"]
Notification --> UIUpdate["UI Update\n(Data Binding)"]
Persistence --> SignalR["SignalR Broadcast\n(Real-time Sync)"]
SignalR --> OtherClients["Other Connected Clients"]
OtherClients --> OtherViewModels["Other Client ViewModels"]
OtherViewModels --> OtherUI["Other Client UI Updates"]
style UserInteraction fill:#e1f3fb,stroke:#333
style ViewModelCommand fill:#fff2cc,stroke:#333
style BusinessLogic fill:#d5e8d4,stroke:#333
style StorageOperation fill:#d5e8d4,stroke:#333
style Persistence fill:#f8cecc,stroke:#333
style Notification fill:#fff2cc,stroke:#333
style UIUpdate fill:#e1f3fb,stroke:#333
style SignalR fill:#fff2cc,stroke:#333
style OtherClients fill:#e1f3fb,stroke:#333
style OtherViewModels fill:#fff2cc,stroke:#333
style OtherUI fill:#e1f3fb,stroke:#333
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [IStorage.cs](file://src/Unlimotion.TaskTreeManager/IStorage.cs)

## Dependency Analysis

```mermaid
graph LR
A[Unlimotion.Desktop] --> B[Unlimotion]
C[Unlimotion.Browser] --> B
D[Unlimotion.Android] --> B
E[Unlimotion.iOS] --> B
B --> F[Unlimotion.ViewModel]
F --> G[Unlimotion.TaskTreeManager]
G --> H[Unlimotion.Domain]
G --> I[Unlimotion.Server.ServiceInterface]
I --> J[Unlimotion.Server.ServiceModel]
I --> K[Unlimotion.Interface]
L[Unlimotion.Server] --> I
L --> M[Unlimotion.Domain]
F --> K
G --> K
L --> K
style A fill:#e1f3fb,stroke:#333
style C fill:#e1f3fb,stroke:#333
style D fill:#e1f3fb,stroke:#333
style E fill:#e1f3fb,stroke:#333
style B fill:#fff2cc,stroke:#333
style F fill:#fff2cc,stroke:#333
style G fill:#d5e8d4,stroke:#333
style H fill:#d5e8d4,stroke:#333
style I fill:#d5e8d4,stroke:#333
style J fill:#d5e8d4,stroke:#333
style K fill:#f8cecc,stroke:#333
style L fill:#f8cecc,stroke:#333
style M fill:#f8cecc,stroke:#333
```

**Diagram sources**
- [Unlimotion.csproj](file://src/Unlimotion/Unlimotion.csproj)
- [Unlimotion.Domain.csproj](file://src/Unlimotion.Domain/Unlimotion.Domain.csproj)
- [Unlimotion.TaskTree.csproj](file://src/Unlimotion.TaskTreeManager/Unlimotion.TaskTree.csproj)
- [Unlimotion.ViewModel.csproj](file://src/Unlimotion.ViewModel/Unlimotion.ViewModel.csproj)
- [Unlimotion.Server.csproj](file://src/Unlimotion.Server/Unlimotion.Server.csproj)

## Performance Considerations

The Unlimotion architecture incorporates several performance optimizations to ensure responsive user experiences. The use of observable collections with reactive programming patterns minimizes unnecessary UI updates by only propagating changes when data actually changes. The TaskTreeManager implements retry policies with Polly for resilient database operations, preventing transient failures from affecting user experience. RavenDB is used as the primary database backend, providing efficient document storage and querying capabilities with built-in indexing. For offline scenarios, file-based storage provides fast local access with periodic synchronization to the central server. The MVVM pattern with compiled bindings in Avalonia ensures efficient data binding performance, while the separation of concerns allows for targeted optimizations in specific components without affecting the overall system architecture.

## Troubleshooting Guide

Common issues in the Unlimotion system typically relate to connectivity, data synchronization, or configuration. Connection errors to the server backend can occur when the RavenDB instance is unavailable or network connectivity is interrupted; these are handled by the ITaskStorage implementation with retry logic and user notifications. Data synchronization issues between clients may arise from SignalR connection problems, which are mitigated by periodic polling fallback mechanisms. Configuration issues often relate to Git backup settings or server connection parameters, which are managed through the WritableJsonConfiguration system that persists settings across application restarts. When debugging issues, developers should first check the Serilog output for server-side errors, then verify client-server connectivity, and finally examine the local storage state to identify data consistency issues.

**Section sources**
- [App.axaml.cs](file://src/Unlimotion/App.axaml.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [Program.cs](file://src/Unlimotion.Server/Program.cs)

## Conclusion

Unlimotion's architecture demonstrates a well-structured, full-stack application design that effectively separates concerns across multiple layers while maintaining flexibility for cross-platform deployment. The consistent application of the MVVM pattern across all client interfaces ensures a uniform development experience and facilitates code reuse. The separation of domain models, business logic, service interfaces, and data transfer objects creates a maintainable architecture that can evolve over time. The use of modern technologies like Avalonia, RavenDB, ServiceStack, and SignalR provides a robust foundation for a responsive, real-time productivity application. The dependency injection system using Splat enables loose coupling between components, improving testability and maintainability. Overall, the architecture successfully balances complexity with functionality, providing a scalable solution for personal task management across multiple devices and platforms.