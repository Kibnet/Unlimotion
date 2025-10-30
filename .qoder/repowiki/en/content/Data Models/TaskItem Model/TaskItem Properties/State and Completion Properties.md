# State and Completion Properties

<cite>
**Referenced Files in This Document**
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs)
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskCompletionChangeTests.cs](file://src/Unlimotion.Test/TaskCompletionChangeTests.cs)
- [TaskAvailabilityCalculationTests.cs](file://src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Core State Properties](#core-state-properties)
3. [Three-State Boolean System](#three-state-boolean-system)
4. [Business Logic Architecture](#business-logic-architecture)
5. [State Transition Management](#state-transition-management)
6. [View Filtering and UI Integration](#view-filtering-and-ui-integration)
7. [Timestamp Management](#timestamp-management)
8. [Dependency Calculation Engine](#dependency-calculation-engine)
9. [Edge Cases and Error Handling](#edge-cases-and-error-handling)
10. [Testing Framework](#testing-framework)
11. [Performance Considerations](#performance-considerations)
12. [Troubleshooting Guide](#troubleshooting-guide)

## Introduction

The Unlimotion task management system implements a sophisticated three-state boolean system for task completion tracking, complemented by a dependency-aware availability calculation engine. This system manages task states through two primary properties: `IsCompleted` and `IsCanBeCompleted`, which drive business logic, UI behavior, and data persistence throughout the application.

The state management architecture separates concerns between presentation layer UI bindings and business logic domain calculations, ensuring consistent behavior across all application interfaces while maintaining optimal performance and reliability.

## Core State Properties

### TaskItem State Properties

The TaskItem domain model defines four critical state properties that govern task lifecycle and availability:

```mermaid
classDiagram
class TaskItem {
+bool? IsCompleted
+bool IsCanBeCompleted
+DateTimeOffset CreatedDateTime
+DateTimeOffset? UnlockedDateTime
+DateTimeOffset? CompletedDateTime
+DateTimeOffset? ArchiveDateTime
+string[] ContainsTasks
+string[] ParentTasks
+string[] BlocksTasks
+string[] BlockedByTasks
}
class TaskTreeManager {
+CalculateAndUpdateAvailability(TaskItem) TaskItem[]
+HandleTaskCompletionChange(TaskItem) TaskItem[]
+CalculateAvailabilityForTask(TaskItem) Dictionary~string,TaskItem~
+GetAffectedTasks(TaskItem) TaskItem[]
}
class TaskItemViewModel {
+bool IsCanBeCompleted
+bool IsCompleted
+DateTimeOffset? CompletedDateTime
+DateTimeOffset? ArchiveDateTime
+DateTimeOffset? UnlockedDateTime
}
TaskItem --> TaskTreeManager : "processed by"
TaskItemViewModel --> TaskItem : "wraps"
TaskTreeManager --> TaskItem : "manages"
```

**Diagram sources**
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs#L6-L32)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L10-L15)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs#L18-L25)

**Section sources**
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs#L6-L32)

## Three-State Boolean System

### IsCompleted Property

The `IsCompleted` property implements a nullable boolean system with three distinct states:

| State | Value | Meaning |
|-------|-------|---------|
| Active | `false` | Task is currently in progress and can be completed |
| Completed | `true` | Task has been marked as finished |
| Archived | `null` | Task is archived and cannot be completed |

This three-state system enables flexible task lifecycle management without requiring separate boolean flags for each state.

### State Transitions and Validation

The system enforces strict state transition rules through the TaskTreeManager business logic:

```mermaid
stateDiagram-v2
[*] --> Active : IsCompleted = false
Active --> Completed : IsCompleted = true
Active --> Archived : IsCompleted = null
Completed --> Active : IsCompleted = false
Completed --> Archived : IsCompleted = null
Archived --> Active : IsCompleted = false
Archived --> Completed : IsCompleted = true
note right of Active : Can be completed<br/>IsCanBeCompleted determines availability
note right of Completed : Cannot be uncompleted<br/>Triggers timestamp updates
note right of Archived : Cannot be completed<br/>Triggers archive timestamp
```

**Section sources**
- [TaskCompletionChangeTests.cs](file://src/Unlimotion.Test/TaskCompletionChangeTests.cs#L12-L127)

## Business Logic Architecture

### TaskTreeManager Responsibility

The TaskTreeManager serves as the central orchestrator for all task state management operations, implementing the separation of concerns principle by moving business logic from the presentation layer to the domain layer.

```mermaid
sequenceDiagram
participant UI as "UI Layer"
participant VM as "TaskItemViewModel"
participant TM as "TaskTreeManager"
participant Storage as "Storage Layer"
participant DB as "Database"
UI->>VM : Change IsCompleted
VM->>TM : HandleTaskCompletionChange(task)
TM->>TM : Update timestamps
TM->>TM : Calculate availability
TM->>Storage : Save updated task
Storage->>DB : Persist changes
TM-->>VM : Return affected tasks
VM-->>UI : Update UI bindings
Note over TM,DB : Business logic executed here
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L758-L836)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs#L130-L140)

### Interface Definition

The ITaskTreeManager interface defines the contract for all state management operations:

**Section sources**
- [ITaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/ITaskTreeManager.cs#L7-L42)

## State Transition Management

### HandleTaskCompletionChange Method

The HandleTaskCompletionChange method orchestrates all state transitions and associated side effects:

```mermaid
flowchart TD
Start([Task Completion Change]) --> CheckState{"Check IsCompleted State"}
CheckState --> |true & null CompletedDateTime| SetCompleted["Set CompletedDateTime<br/>Clear ArchiveDateTime"]
CheckState --> |false| ClearDates["Clear Both Timestamps"]
CheckState --> |null & null ArchiveDateTime| SetArchived["Set ArchiveDateTime"]
CheckState --> |true & Has Repeater| CloneTask["Create Cloned Task"]
SetCompleted --> SaveTask["Save Task to Storage"]
ClearDates --> SaveTask
SetArchived --> SaveTask
CloneTask --> SaveCloned["Save Cloned Task"]
SaveCloned --> SaveTask
SaveTask --> CalcAvailability["Calculate Availability"]
CalcAvailability --> UpdateAffected["Update Affected Tasks"]
UpdateAffected --> End([Complete])
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L758-L836)

### Timestamp Management

Each state transition triggers specific timestamp updates:

| State Change | CompletedDateTime | ArchiveDateTime | Repeater Logic |
|--------------|-------------------|-----------------|----------------|
| Active → Completed | Set to UTC Now | Cleared | Create next occurrence |
| Active → Archived | Cleared | Set to UTC Now | No action |
| Completed → Active | Cleared | Cleared | No action |
| Completed → Archived | Cleared | Set to UTC Now | Create next occurrence |
| Archived → Active | Cleared | Cleared | No action |
| Archived → Completed | Set to UTC Now | Cleared | Create next occurrence |

**Section sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L765-L810)

## View Filtering and UI Integration

### MainWindowViewModel Filtering Logic

The MainWindowViewModel implements sophisticated filtering logic based on IsCompleted states:

```mermaid
flowchart TD
AllTasks[All Tasks View] --> FilterCheck{"ShowCompleted & ShowArchived"}
FilterCheck --> |Both True| ShowAll["Show All Tasks<br/>Active + Completed + Archived"]
FilterCheck --> |Only Completed| ShowCompleted["Show Completed Only<br/>IsCompleted = true"]
FilterCheck --> |Only Archived| ShowArchived["Show Archived Only<br/>IsCompleted = null"]
FilterCheck --> |Neither| ShowActive["Show Active Only<br/>IsCompleted = false"]
ShowAll --> ApplyFilter["Apply Additional Filters<br/>(Emoji, Date, etc.)"]
ShowCompleted --> ApplyFilter
ShowArchived --> ApplyFilter
ShowActive --> ApplyFilter
ApplyFilter --> BindUI["Bind to UI Collections"]
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L150-L170)

### Collection Binding Strategy

The MainWindowViewModel maintains separate collections for each view mode:

| View Mode | Filter Condition | Collection |
|-----------|------------------|------------|
| All Tasks | `IsCompleted == false \| IsCompleted == true \| IsCompleted == null` | `CurrentItems` |
| Unlocked | Available tasks with `IsCanBeCompleted = true` | `UnlockedItems` |
| Completed | `IsCompleted == true` | `CompletedItems` |
| Archived | `IsCompleted == null` | `ArchivedItems` |

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L591-L628)

## Timestamp Management

### Automatic Timestamp Updates

The system automatically manages timestamp properties based on state transitions:

```mermaid
sequenceDiagram
participant Task as "TaskItem"
participant Manager as "TaskTreeManager"
participant Storage as "Storage"
Note over Task : Initial State : IsCompleted=false<br/>Timestamps : null, null, null
Task->>Manager : Set IsCompleted=true
Manager->>Manager : Check CompletedDateTime
Manager->>Manager : Set CompletedDateTime = UTC Now
Manager->>Manager : Clear ArchiveDateTime
Manager->>Storage : Save Task
Note over Task : State : IsCompleted=true<br/>Timestamps : UTC Now, null, null
Task->>Manager : Set IsCompleted=null
Manager->>Manager : Check ArchiveDateTime
Manager->>Manager : Set ArchiveDateTime = UTC Now
Manager->>Storage : Save Task
Note over Task : State : IsCompleted=null<br/>Timestamps : UTC Now, UTC Now, null
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L765-L810)

### Repeater Pattern Integration

When tasks have repeater patterns, the system creates cloned instances for future occurrences:

**Section sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L775-L810)

## Dependency Calculation Engine

### IsCanBeCompleted Calculation

The IsCanBeCompleted property determines whether a task can be marked as completed based on dependency satisfaction:

```mermaid
flowchart TD
Start([Calculate Availability]) --> CheckContains{"Contains Tasks?"}
CheckContains --> |Yes| CheckAllCompleted["Check All Contained Tasks<br/>IsCompleted != false"]
CheckContains --> |No| CheckBlockers{"Blocked By Tasks?"}
CheckAllCompleted --> AllCompleted{"All Completed?"}
AllCompleted --> |Yes| CheckBlockers
AllCompleted --> |No| SetUnavailable["Set IsCanBeCompleted = false<br/>Clear UnlockedDateTime"]
CheckBlockers --> |Yes| CheckBlockersCompleted["Check All Blocking Tasks<br/>IsCompleted != false"]
CheckBlockers --> |No| SetAvailable["Set IsCanBeCompleted = true<br/>Set UnlockedDateTime = UTC Now"]
CheckBlockersCompleted --> AllBlockersCompleted{"All Blockers Completed?"}
AllBlockersCompleted --> |Yes| SetAvailable
AllBlockersCompleted --> |No| SetUnavailable
SetAvailable --> SaveTask["Save Task to Storage"]
SetUnavailable --> SaveTask
SaveTask --> End([Complete])
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L665-L710)

### Dependency Propagation

The system implements bidirectional dependency propagation:

```mermaid
graph TD
subgraph "Parent-Child Dependencies"
P1[Parent Task] --> C1[Child Task 1]
P1 --> C2[Child Task 2]
C1 --> P1
C2 --> P1
end
subgraph "Blocking Dependencies"
T1[Task A] -.->|blocks| T2[Task B]
T2 -.->|blocked by| T1
end
subgraph "Propagation Effects"
C1 --> P1_Propagate["Recalculate Parent Availability"]
T2 --> T1_Propagate["Recalculate Blocking Task Availability"]
end
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L690-L730)

**Section sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L665-L730)

## Edge Cases and Error Handling

### Circular Dependency Detection

The system handles potential circular dependencies through careful propagation logic:

```mermaid
flowchart TD
Start([Task Modification]) --> CheckCircular{"Circular Dependency<br/>Detected?"}
CheckCircular --> |Yes| LogWarning["Log Warning<br/>Continue Operation"]
CheckCircular --> |No| ProcessNormal["Process Normal<br/>Dependency Update"]
LogWarning --> UpdateAffected["Update Affected Tasks"]
ProcessNormal --> UpdateAffected
UpdateAffected --> CheckLoop{"Potential Infinite<br/>Loop Detected?"}
CheckLoop --> |Yes| LimitRecursion["Limit Recursion Depth<br/>Timeout After 2 Minutes"]
CheckLoop --> |No| ContinueProcess["Continue Processing"]
LimitRecursion --> Finalize["Finalize Changes"]
ContinueProcess --> Finalize
Finalize --> End([Complete])
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L597-L620)

### Forced Completion Scenarios

The system supports forced completion through various mechanisms:

| Scenario | Trigger | Behavior |
|----------|---------|----------|
| Manual Override | User action | Bypass availability checks |
| Bulk Operations | Administrative action | Process multiple tasks |
| Migration | Data import | Restore historical states |
| Recovery | System failure | Restore from backup |

**Section sources**
- [TaskAvailabilityCalculationTests.cs](file://src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs#L593-L651)

## Testing Framework

### Unit Test Coverage

The testing framework provides comprehensive coverage of state management scenarios:

```mermaid
classDiagram
class TaskCompletionChangeTests {
+HandleTaskCompletionChange_CompletedTask_SetsCompletedDateTime()
+HandleTaskCompletionChange_UncompletedTask_ClearsDates()
+HandleTaskCompletionChange_ArchivedTask_SetsArchiveDateTime()
+HandleTaskCompletionChange_CompletedTaskWithRepeater_CreatesClone()
}
class TaskAvailabilityCalculationTests {
+TaskWithNoDependencies_ShouldBeAvailable()
+TaskWithCompletedChild_ShouldBeAvailable()
+TaskWithIncompleteChild_ShouldNotBeAvailable()
+TaskWithCompletedBlocker_ShouldBeAvailable()
+UpdateTask_WithIsCompletedChange_ShouldRecalculateAffectedTasks()
}
class InMemoryStorage {
+Save(TaskItem)
+Load(string) TaskItem
+GetAll() IEnumerable~TaskItem~
}
TaskCompletionChangeTests --> InMemoryStorage
TaskAvailabilityCalculationTests --> InMemoryStorage
```

**Diagram sources**
- [TaskCompletionChangeTests.cs](file://src/Unlimotion.Test/TaskCompletionChangeTests.cs#L10-L127)
- [TaskAvailabilityCalculationTests.cs](file://src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs#L10-L47)

### Test Scenarios

The test suite covers critical edge cases:

| Test Category | Scenarios Tested |
|---------------|------------------|
| State Transitions | Basic transitions, repeater patterns, timestamp management |
| Dependency Logic | Child completion, blocker resolution, availability calculation |
| Error Conditions | Circular dependencies, missing tasks, storage failures |
| Performance | Large task trees, recursive calculations, timeout handling |

**Section sources**
- [TaskCompletionChangeTests.cs](file://src/Unlimotion.Test/TaskCompletionChangeTests.cs#L10-L127)
- [TaskAvailabilityCalculationTests.cs](file://src/Unlimotion.Test/TaskAvailabilityCalculationTests.cs#L10-L47)

## Performance Considerations

### Optimization Strategies

The system implements several performance optimizations:

1. **Lazy Loading**: Dependencies are loaded only when needed
2. **Batch Operations**: Multiple state changes are batched for efficiency
3. **Caching**: Frequently accessed task data is cached
4. **Async Processing**: All I/O operations are asynchronous
5. **Timeout Protection**: Long-running operations are protected with timeouts

### Memory Management

The TaskTreeManager uses AutoUpdatingDictionary for efficient memory management during bulk operations, preventing memory leaks during complex dependency recalculations.

## Troubleshooting Guide

### Common Issues and Solutions

| Issue | Symptoms | Solution |
|-------|----------|----------|
| Stuck Tasks | Tasks remain unavailable despite dependencies completed | Run manual availability recalculation |
| Timestamp Errors | Incorrect timestamps on state changes | Verify timezone settings and UTC conversion |
| Circular Dependencies | Infinite loops during availability calculation | Review task relationships and break cycles |
| Performance Degradation | Slow response during bulk operations | Check for large task trees and optimize queries |

### Debugging State Issues

For debugging state-related problems:

1. **Verify Task Relationships**: Check ContainsTasks, ParentTasks, BlocksTasks, BlockedByTasks
2. **Inspect Availability Calculations**: Use CalculateAvailabilityForTask directly
3. **Monitor Timestamp Changes**: Track CompletedDateTime and ArchiveDateTime updates
4. **Validate State Transitions**: Ensure proper IsCompleted state changes

**Section sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager/TaskTreeManager.cs#L597-L620)