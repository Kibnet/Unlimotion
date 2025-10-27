# Server Synchronization

<cite>
**Referenced Files in This Document**  
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs)
- [TaskItemHubMold.cs](file://src/Unlimotion.Interface/TaskItemHubMold.cs)
- [ClientSettings.cs](file://src/Unlimotion/ClientSettings.cs)
- [IChatHub.cs](file://src/Unlimotion.Interface/IChatHub.cs)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Communication Protocol](#communication-protocol)
4. [Authentication and Security](#authentication-and-security)
5. [Data Synchronization](#data-synchronization)
6. [Conflict Resolution and State Reconciliation](#conflict-resolution-and-state-reconciliation)
7. [Error Handling and Retry Logic](#error-handling-and-retry-logic)
8. [Offline Operation Support](#offline-operation-support)
9. [Performance Considerations](#performance-considerations)
10. [Configuration and Endpoints](#configuration-and-endpoints)
11. [Troubleshooting Guide](#troubleshooting-guide)

## Introduction

The server synchronization system in Unlimotion enables bidirectional data exchange between client applications and the backend server using ServiceStack and SignalR technologies. This document details the implementation of the `ServerTaskStorage` component, which manages task data synchronization, including authentication, conflict resolution, delta updates, and offline operation support. The system is designed to provide reliable, secure, and efficient synchronization of task data across multiple devices while maintaining data integrity and consistency.

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L1-L50)

## Architecture Overview

The synchronization system follows a client-server architecture with the following key components:

```mermaid
graph TB
subgraph "Client Application"
A[ServerTaskStorage]
B[TaskItemViewModel]
C[ClientSettings]
end
subgraph "Communication Layer"
D[SignalR Hub]
E[ServiceStack API]
end
subgraph "Server"
F[TaskService]
G[AuthService]
H[RavenDB]
end
A --> D
A --> E
D --> F
E --> F
E --> G
F --> H
G --> H
```

**Diagram sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L1-L100)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs#L1-L20)
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs#L1-L20)

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L1-L100)

## Communication Protocol

The synchronization system uses a dual-channel communication approach combining HTTP REST APIs and WebSocket-based SignalR for real-time updates.

### REST API Endpoints
```mermaid
flowchart TD
Client --> |GET /GetAllTasks| TaskService
Client --> |GET /GetTask| TaskService
Client --> |POST /BulkInsertTasks| TaskService
Client --> |POST /AuthViaPassword| AuthService
Client --> |POST /RegisterNewUser| AuthService
Client --> |POST /PostRefreshToken| AuthService
```

### SignalR Hub Methods
```mermaid
sequenceDiagram
participant Client
participant Hub
participant Server
Client->>Hub : Login(token, os, ip, clientVersion)
Hub->>Server : Authenticate session
Server-->>Hub : Auth result
Hub-->>Client : LogOn event
Client->>Hub : SaveTask(TaskItemHubMold)
Hub->>Server : Process task save
Server-->>Hub : Task ID
Hub-->>Client : Task saved
Client->>Hub : DeleteTasks([ids])
Hub->>Server : Process deletions
Server-->>Hub : Deletion confirmed
Hub-->>Client : Tasks deleted
```

**Diagram sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L200-L300)
- [IChatHub.cs](file://src/Unlimotion.Interface/IChatHub.cs#L1-L15)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs#L30-L50)

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L200-L300)
- [IChatHub.cs](file://src/Unlimotion.Interface/IChatHub.cs#L1-L15)

## Authentication and Security

The system implements a JWT-based authentication mechanism with access and refresh tokens for secure communication.

```mermaid
sequenceDiagram
participant Client
participant AuthService
participant RavenDB
Client->>AuthService : AuthViaPassword(login, password)
AuthService->>RavenDB : Validate credentials
RavenDB-->>AuthService : User data
AuthService->>AuthService : Generate JWT tokens
AuthService-->>Client : TokenResult {accessToken, refreshToken, expireTime}
Client->>AuthService : PostRefreshToken(refreshToken)
AuthService->>AuthService : Validate refresh token
AuthService->>AuthService : Generate new tokens
AuthService-->>Client : New TokenResult
```

The client stores authentication tokens in `ClientSettings` and automatically handles token refresh when expired. The system uses bearer token authentication with ServiceStack's JWT provider, ensuring secure transmission of credentials and session data.

**Diagram sources**
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs#L50-L150)
- [ClientSettings.cs](file://src/Unlimotion/ClientSettings.cs#L1-L15)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L150-L200)

**Section sources**
- [AuthService.cs](file://src/Unlimotion.Server.ServiceInterface/AuthService.cs#L50-L150)
- [ClientSettings.cs](file://src/Unlimotion/ClientSettings.cs#L1-L15)

## Data Synchronization

The synchronization system uses a hybrid approach combining initial full sync with delta updates for ongoing changes.

### Data Flow
```mermaid
flowchart TD
A[Client Connect] --> B{Token Valid?}
B --> |No| C[Authenticate]
B --> |Yes| D[Start SignalR Connection]
D --> E[Subscribe to Hub Events]
E --> F[Receive Real-time Updates]
F --> G[Update Local Cache]
H[Local Change] --> I[Push via SaveTask]
I --> J[Server Processes]
J --> K[Broadcast to Other Clients]
K --> L[ReceiveTaskItem Event]
L --> G
```

### Serialization Format
The `TaskItemHubMold` class defines the serialization format used for task data transmission:

```mermaid
classDiagram
class TaskItemHubMold {
+string Id
+string Title
+string Description
+bool? IsCompleted
+DateTimeOffset? UnlockedDateTime
+DateTimeOffset? CompletedDateTime
+DateTimeOffset? ArchiveDateTime
+DateTimeOffset? PlannedBeginDateTime
+DateTimeOffset? PlannedEndDateTime
+TimeSpan? PlannedDuration
+List<string> ContainsTasks
+List<string>? ParentTasks
+List<string> BlocksTasks
+List<string> BlockedByTasks
+RepeaterPatternHubMold Repeater
+int Importance
+bool Wanted
+int Version
+DateTime SortOrder
}
```

This model serves as the DTO (Data Transfer Object) for task data, enabling efficient mapping between client and server representations through AutoMapper.

**Diagram sources**
- [TaskItemHubMold.cs](file://src/Unlimotion.Interface/TaskItemHubMold.cs#L1-L30)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L300-L400)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs#L30-L50)

**Section sources**
- [TaskItemHubMold.cs](file://src/Unlimotion.Interface/TaskItemHubMold.cs#L1-L30)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L300-L400)

## Conflict Resolution and State Reconciliation

The synchronization system implements optimistic concurrency control with version-based conflict detection.

### Version Management
Each `TaskItemHubMold` contains a `Version` property that increments with each modification. When a client attempts to save a task, the server compares the incoming version with the current version in the database:

```mermaid
sequenceDiagram
participant Client
participant Server
participant Database
Client->>Server : SaveTask(hubTask, version=5)
Server->>Database : Get current task
Database-->>Server : task.version=6
alt Version Conflict
Server-->>Client : Reject update (409 Conflict)
Client->>Client : Prompt user to resolve
else No Conflict
Server->>Database : Save updated task (version=7)
Database-->>Server : Success
Server-->>Client : Update confirmed
end
```

Upon reconnection after offline operation, the client performs a full sync to reconcile state differences. The `GetAllTasks` endpoint retrieves all tasks from the server, allowing the client to identify and resolve any discrepancies between local and remote data.

**Section sources**
- [TaskItemHubMold.cs](file://src/Unlimotion.Interface/TaskItemHubMold.cs#L1-L30)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L400-L500)

## Error Handling and Retry Logic

The system implements robust error handling and automatic retry mechanisms to ensure reliable operation under network instability.

### Connection Management
```mermaid
stateDiagram-v2
[*] --> Disconnected
Disconnected --> Connecting : Connect()
Connecting --> Connected : Success
Connecting --> Reconnecting : Failure
Reconnecting --> Connecting : Random delay (2-6s)
Connected --> Disconnected : Connection closed
Connected --> Reconnecting : Error
Reconnecting --> Disconnected : Max retries exceeded
```

The `ServerTaskStorage` class uses a `SemaphoreSlim` to prevent concurrent connection attempts and implements exponential backoff with jitter for reconnection attempts. When a connection fails, the system waits for a random interval between 2-6 seconds before retrying, preventing thundering herd problems.

### Error Propagation
The system exposes several events for error handling:
- `OnConnectionError`: Fired when connection or authentication fails
- `OnConnected`: Fired when successfully connected
- `OnDisconnected`: Fired when disconnected

These events allow the UI layer to respond appropriately to connectivity changes and display relevant messages to users.

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L500-L600)

## Offline Operation Support

The system provides comprehensive offline operation capabilities through local caching and queued operations.

### Offline Workflow
```mermaid
flowchart TD
A[Network Available] --> |Online Mode| B[Real-time Sync]
A --> |Network Lost| C[Offline Mode]
C --> D[Local Cache Updates]
D --> E[Queue Outbound Operations]
E --> |Network Restored| F[Reconnection]
F --> G[Process Queued Operations]
G --> H[State Reconciliation]
H --> B
```

When operating offline, all task modifications are stored in the local `SourceCache<TaskItemViewModel, string>` and outbound operations are queued. Upon reconnection, the system automatically processes queued operations and performs state reconciliation to ensure data consistency.

The `IsActive` flag in `ServerTaskStorage` controls whether the synchronization loop continues, allowing graceful shutdown and preventing unnecessary connection attempts when the application is closing.

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L600-L700)

## Performance Considerations

The synchronization system includes several optimizations for handling large datasets and minimizing bandwidth usage.

### Bulk Operations
For efficiency with multiple tasks, the system supports bulk operations:
- `BulkInsertTasks`: Allows inserting multiple tasks in a single request
- `DeleteTasks`: Enables deletion of multiple tasks via SignalR

### Delta Updates
The system uses delta updates rather than full dataset transfers for individual task modifications, reducing bandwidth consumption. Only changed tasks are transmitted, and the `TaskItemHubMold` format is optimized for minimal payload size.

### Connection Optimization
The dual-channel approach separates initial data loading (HTTP) from real-time updates (SignalR), optimizing connection usage. HTTP connections are stateless and can be load-balanced, while SignalR maintains a persistent connection for immediate push notifications.

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L300-L400)
- [TaskService.cs](file://src/Unlimotion.Server.ServiceInterface/TaskService.cs#L50-L70)

## Configuration and Endpoints

The synchronization system is configured through client settings and environment variables.

### Configuration Parameters
```mermaid
erDiagram
CLIENTSETTINGS {
string AccessToken
string RefreshToken
DateTimeOffset ExpireTime
string UserId
string Login
}
TASKSTORAGESETTINGS {
string Login
string Password
string ServerUrl
}
```

The `ClientSettings` class stores authentication tokens and user information, while `TaskStorageSettings` contains login credentials and server URL configuration. These settings are managed through the `IConfiguration` service and persisted across application restarts.

### Endpoint Configuration
The system uses the following endpoints:
- `/ChatHub`: SignalR hub for real-time communication
- `/json/reply/`: ServiceStack endpoint for REST API calls
- Authentication endpoints: `/AuthViaPassword`, `/RegisterNewUser`, `/PostRefreshToken`
- Task endpoints: `/GetAllTasks`, `/GetTask`, `/BulkInsertTasks`

**Section sources**
- [ClientSettings.cs](file://src/Unlimotion/ClientSettings.cs#L1-L15)
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L100-L150)

## Troubleshooting Guide

This section provides guidance for diagnosing and resolving common synchronization issues.

### Common Issues and Solutions
| Issue | Possible Cause | Solution |
|------|---------------|----------|
| Authentication failures | Invalid credentials or expired tokens | Verify login credentials; check token expiration |
| Connection timeouts | Network issues or server unavailability | Check network connectivity; verify server status |
| Sync conflicts | Concurrent modifications | Implement conflict resolution UI; use version control |
| Data loss | Improper shutdown or cache corruption | Ensure proper disposal; implement data validation |
| High bandwidth usage | Frequent updates or large datasets | Optimize update frequency; use bulk operations |

### Monitoring Synchronization Status
The system provides several indicators for monitoring synchronization:
- `IsConnected`: Boolean property indicating current connection status
- `IsSignedIn`: Boolean property indicating authentication status
- Events: `OnConnected`, `OnDisconnected`, `OnConnectionError` for real-time status updates

Developers can subscribe to these events to monitor synchronization status and provide feedback to users.

**Section sources**
- [ServerTaskStorage.cs](file://src/Unlimotion/ServerTaskStorage.cs#L600-L715)
- [ClientSettings.cs](file://src/Unlimotion/ClientSettings.cs#L1-L15)