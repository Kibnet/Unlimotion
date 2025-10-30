# Unlocked View

<cite>
**Referenced Files in This Document**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [UnlockedTimeFilter.cs](file://src/Unlimotion.ViewModel/UnlockedTimeFilter.cs)
- [DurationFilter.cs](file://src/Unlimotion.ViewModel/DurationFilter.cs)
- [SortDefinition.cs](file://src/Unlimotion.ViewModel/SortDefinition.cs)
- [TaskItem.cs](file://src/Unlimotion.Domain/TaskItem.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [GraphViewModel.cs](file://src/Unlimotion.ViewModel/GraphViewModel.cs)
- [GraphControl.axaml.cs](file://src/Unlimotion\Views\GraphControl.axaml.cs)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager\TaskTreeManager.cs)
- [TaskAvailabilityCalculationTests.cs](file://src/Unlimotion.Test\TaskAvailabilityCalculationTests.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [System Architecture](#system-architecture)
3. [Core Components](#core-components)
4. [Reactive Pipeline](#reactive-pipeline)
5. [Filtering Logic](#filtering-logic)
6. [Task Availability Calculation](#task-availability-calculation)
7. [Graph View Integration](#graph-view-integration)
8. [Performance Considerations](#performance-considerations)
9. [Code Examples](#code-examples)
10. [Troubleshooting Guide](#troubleshooting-guide)
11. [Conclusion](#conclusion)

## Introduction

The Unlocked View is a sophisticated task management feature in the Unlimotion application that displays tasks currently available for work based on complex time-based unlocking logic. This system implements a reactive pipeline that monitors task properties and dynamically updates the task list in real-time, providing users with immediate visibility into available work items.

The Unlocked View serves as the primary interface for users to identify and prioritize tasks that can be worked on immediately, combining multiple filtering criteria including time-based availability, duration preferences, emoji categories, and user-defined wanted status filters.

## System Architecture

The Unlocked View architecture follows a reactive programming pattern built on the DynamicData library, enabling real-time updates as task properties change. The system consists of several interconnected layers that work together to provide seamless task availability management.

```mermaid
graph TB
subgraph "Presentation Layer"
MWV[MainWindowViewModel]
GV[GraphViewModel]
GC[GraphControl]
end
subgraph "Business Logic Layer"
TTM[TaskTreeManager]
UTF[UnlockedTimeFilter]
DF[DurationFilter]
SD[SortDefinition]
end
subgraph "Data Layer"
TI[TaskItem]
TVM[TaskItemViewModel]
TS[ITaskStorage]
end
subgraph "Reactive Pipeline"
DS[DynamicData Streams]
AR[AutoRefresh Observables]
FL[Filter Logic]
end
MWV --> TTM
MWV --> UTF
MWV --> DF
MWV --> SD
GV --> MWV
GC --> GV
TTM --> TI
UTF --> TVM
DF --> TVM
SD --> TVM
MWV --> DS
DS --> AR
AR --> FL
FL --> MWV
TI --> TVM
TVM --> TS
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L15-L50)
- [GraphViewModel.cs](file://src/Unlimotion.ViewModel/GraphViewModel.cs#L8-L30)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager\TaskTreeManager.cs#L597-L737)

## Core Components

### MainWindowViewModel UnlockedItems Property

The `UnlockedItems` property serves as the central collection for displaying tasks available for work. This reactive collection is automatically updated whenever task properties change, ensuring real-time synchronization with the underlying data.

```mermaid
classDiagram
class MainWindowViewModel {
+ReadOnlyObservableCollection~TaskWrapperViewModel~ UnlockedItems
+ReadOnlyObservableCollection~UnlockedTimeFilter~ UnlockedTimeFilters
+ReadOnlyObservableCollection~DurationFilter~ DurationFilters
+SortDefinition CurrentSortDefinitionForUnlocked
+bool ShowWanted
+Connect() Task
+SelectCurrentTask() void
}
class TaskWrapperViewModel {
+TaskItemViewModel TaskItem
+TaskWrapperActions Actions
+bool IsExpanded
+string BreadScrumbs
}
class TaskItemViewModel {
+bool IsCanBeCompleted
+bool? IsCompleted
+DateTimeOffset? UnlockedDateTime
+DateTimeOffset? PlannedBeginDateTime
+DateTimeOffset? PlannedEndDateTime
+TimeSpan? PlannedDuration
+bool Wanted
}
MainWindowViewModel --> TaskWrapperViewModel : "manages"
TaskWrapperViewModel --> TaskItemViewModel : "wraps"
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L971-L994)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs#L20-L50)

### UnlockedTimeFilter Implementation

The `UnlockedTimeFilter` class defines six distinct time-based categories that determine task availability:

| Filter Category | Predicate Logic | Use Case |
|----------------|-----------------|----------|
| Unplanned | `PlannedBeginDateTime == null && PlannedEndDateTime == null` | Tasks without scheduling |
| Overdue | `PlannedEndDateTime != null && DateTime.Now.Date > PlannedEndDateTime?.Date` | Missed deadlines |
| Urgent | `PlannedEndDateTime != null && DateTime.Now.Date == PlannedEndDateTime?.Date` | Same-day deadlines |
| Today | `PlannedBeginDateTime != null && DateTime.Now.Date == PlannedBeginDateTime?.Date` | Scheduled for today |
| Maybe | Complex logic for future or partially scheduled tasks | Flexible scheduling |
| Future | `PlannedBeginDateTime != null && PlannedBeginDateTime?.Date > DateTime.Now.Date` | Upcoming tasks |

**Section sources**
- [UnlockedTimeFilter.cs](file://src/Unlimotion.ViewModel/UnlockedTimeFilter.cs#L15-L56)

### DurationFilter Categories

The `DurationFilter` provides granular control over task duration preferences, allowing users to focus on tasks of appropriate time commitment:

| Duration Range | Predicate | Typical Use Cases |
|---------------|-----------|-------------------|
| No duration | `PlannedDuration == null` | Tasks without time estimates |
| ≤5m | `PlannedDuration <= TimeSpan.FromMinutes(5)` | Quick tasks |
| 5m< & ≤30m | `5m < PlannedDuration <= 30m` | Short tasks |
| 30m< & ≤2h | `30m < PlannedDuration <= 2h` | Medium tasks |
| 2h< & ≤1d | `2h < PlannedDuration <= 1d` | Long tasks |
| 1d< | `PlannedDuration > 1d` | Extended projects |

**Section sources**
- [DurationFilter.cs](file://src/Unlimotion.ViewModel/DurationFilter.cs#L13-L47)

## Reactive Pipeline

The reactive pipeline forms the backbone of the Unlocked View's real-time functionality, continuously monitoring task properties and updating the display accordingly.

```mermaid
sequenceDiagram
participant Task as TaskItem
participant VM as MainWindowViewModel
participant TTM as TaskTreeManager
participant DS as DynamicData Stream
participant UI as User Interface
Task->>VM : Property Changed (PlannedDuration)
VM->>TTM : CalculateAndUpdateAvailability()
TTM->>TTM : Evaluate IsCanBeCompleted
TTM->>Task : Update UnlockedDateTime
TTM->>DS : Trigger AutoRefresh
DS->>VM : Filter & Transform
VM->>UI : Update UnlockedItems Collection
Note over Task,UI : Real-time cascading updates
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L499-L535)
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager\TaskTreeManager.cs#L662-L737)

### AutoRefresh Mechanism

The system employs sophisticated auto-refresh capabilities that monitor specific task properties for changes:

```mermaid
flowchart TD
Start([Task Property Change]) --> Check{Property Monitored?}
Check --> |Yes| TriggerRefresh[Trigger AutoRefresh]
Check --> |No| End([No Action])
TriggerRefresh --> Evaluate[Recalculate Availability]
Evaluate --> UpdateUnlocked[Update UnlockedDateTime]
UpdateUnlocked --> FilterTasks[Apply Filters]
FilterTasks --> UpdateUI[Update UI Collection]
UpdateUI --> End
TriggerRefresh --> MonitorOther[Monitor Related Properties]
MonitorOther --> CascadeUpdates[Cascade to Dependent Tasks]
CascadeUpdates --> Evaluate
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L499-L535)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel\TaskItemViewModel.cs#L203-L232)

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs#L499-L535)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel\TaskItemViewModel.cs#L203-L232)

## Filtering Logic

The Unlocked View implements a multi-layered filtering system that combines time-based, duration-based, and user-defined filters to present the most relevant tasks.

### Combined Filter Pipeline

```mermaid
flowchart LR
Tasks[Tasks Collection] --> TimeFilter[UnlockedTimeFilter]
TimeFilter --> DurationFilter[DurationFilter]
DurationFilter --> EmojiFilter[Emoji Filter]
EmojiFilter --> EmojiExcludeFilter[Emoji Exclude Filter]
EmojiExcludeFilter --> WantedFilter[Wanted Filter]
WantedFilter --> Sort[Sort Definition]
Sort --> Final[UnlockedItems Collection]
subgraph "Filter Types"
TimeFilter
DurationFilter
EmojiFilter
EmojiExcludeFilter
WantedFilter
end
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel\MainWindowViewModel.cs#L507-L525)

### Emoji Filter System

The emoji filter system allows users to categorize and filter tasks by emoji tags, with special handling for exclusion filters:

| Filter Type | Purpose | Implementation |
|------------|---------|----------------|
| Standard Emoji Filter | Show tasks with specific emojis | `task.GetAllEmoji.Contains(item.Emoji)` |
| Exclude Emoji Filter | Hide tasks with specific emojis | `!task.GetAllEmoji.Contains(item.Emoji)` |
| All Emoji Filter | Show all tasks regardless of emoji | `filter.All(e => e.ShowTasks == false)` |

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel\MainWindowViewModel.cs#L280-L350)

## Task Availability Calculation

The core business logic for determining task availability resides in the `TaskTreeManager` class, which calculates whether a task can be completed based on its dependencies and blocking relationships.

### Availability Calculation Rules

```mermaid
flowchart TD
Start([Evaluate Task]) --> CheckCompleted{IsCompleted?}
CheckCompleted --> |true| Available[Available]
CheckCompleted --> |false| CheckContains[Check Contains Tasks]
CheckContains --> ContainsCompleted{All Contains Completed?}
ContainsCompleted --> |false| Blocked[Blocked]
ContainsCompleted --> |true| CheckBlocks[Check Blocking Tasks]
CheckBlocks --> BlocksCompleted{All Blocks Completed?}
BlocksCompleted --> |false| Blocked
BlocksCompleted --> |true| Available
Available --> SetUnlocked[Set UnlockedDateTime]
Blocked --> ClearUnlocked[Clear UnlockedDateTime]
SetUnlocked --> UpdateStorage[Update Storage]
ClearUnlocked --> UpdateStorage
UpdateStorage --> End([Complete])
```

**Diagram sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager\TaskTreeManager.cs#L662-L737)

### UnlockedDateTime Management

The `UnlockedDateTime` property serves as a timestamp marker indicating when a task became available for work:

| Scenario | Action | Timestamp |
|----------|--------|-----------|
| Task becomes available | Set to current UTC time | `DateTimeOffset.UtcNow` |
| Task becomes blocked | Clear to null | `null` |
| No availability change | No action | No change |

**Section sources**
- [TaskTreeManager.cs](file://src/Unlimotion.TaskTreeManager\TaskTreeManager.cs#L697-L737)
- [TaskAvailabilityCalculationTests.cs](file://src/Unlimotion.Test\TaskAvailabilityCalculationTests.cs#L231-L272)

## Graph View Integration

The Graph View consumes a filtered subset of tasks from the Unlocked View, specifically designed for visual representation of task relationships and availability.

### Graph Data Flow

```mermaid
sequenceDiagram
participant MWV as MainWindowViewModel
participant GV as GraphViewModel
participant GC as GraphControl
participant TTM as TaskTreeManager
MWV->>GV : Set UnlockedTasks
GV->>GC : Subscribe to UnlockedTasks
GC->>GC : Throttle updates (100ms)
GC->>GC : BuildFromTasks(UnlockedTasks)
GC->>GC : Process task relationships
GC->>GC : Render graph visualization
Note over MWV,GC : OnlyUnlocked mode active
```

**Diagram sources**
- [GraphViewModel.cs](file://src\Unlimotion.ViewModel\GraphViewModel.cs#L15-L30)
- [GraphControl.axaml.cs](file://src\Unlimotion\Views\GraphControl.axaml.cs#L41-L79)

### Graph Rendering Logic

The Graph Control implements specialized rendering logic for the Unlocked View subset:

| Feature | Implementation | Purpose |
|---------|---------------|---------|
| Throttled Updates | `Throttle(TimeSpan.FromMilliseconds(100))` | Prevent excessive redraws |
| OnlyUnlocked Mode | Conditional task source selection | Focus on available tasks |
| Relationship Processing | BlockEdge and ContainEdge creation | Visualize task dependencies |
| Self-Links | Automatic self-edge for orphaned tasks | Complete graph representation |

**Section sources**
- [GraphControl.axaml.cs](file://src\Unlimotion\Views\GraphControl.axaml.cs#L41-L79)
- [GraphViewModel.cs](file://src\Unlimotion.ViewModel\GraphViewModel.cs#L15-L30)

## Performance Considerations

The Unlocked View is designed to handle large datasets efficiently through several optimization strategies:

### Reactive Performance Optimizations

| Technique | Implementation | Benefit |
|-----------|---------------|---------|
| Property Throttling | `PropertyChangedThrottleTimeSpanDefault` | Reduces excessive updates |
| AutoRefresh Optimization | Targeted property monitoring | Minimizes unnecessary recalculations |
| Collection Throttling | 100ms throttling for Graph updates | Prevents UI freezing |
| Selective Filtering | Multi-stage filter pipeline | Early termination of irrelevant tasks |

### Memory Management Strategies

```mermaid
flowchart TD
TaskChange[Task Property Change] --> Throttle{Throttle Applied?}
Throttle --> |Yes| BatchProcess[Batch Processing]
Throttle --> |No| ImmediateProcess[Immediate Processing]
BatchProcess --> FilterCheck[Filter Evaluation]
ImmediateProcess --> FilterCheck
FilterCheck --> CacheCheck{Cached Result?}
CacheCheck --> |Yes| SkipUpdate[Skip Update]
CacheCheck --> |No| UpdateRequired[Update Required]
UpdateRequired --> MemoryCleanup[Memory Cleanup]
SkipUpdate --> End([Complete])
MemoryCleanup --> End
```

### Scalability Guidelines

For optimal performance with large datasets:

1. **Filter Early**: Apply filters as early as possible in the reactive pipeline
2. **Throttle Updates**: Use appropriate throttling intervals for different operations
3. **Selective Monitoring**: Monitor only essential task properties
4. **Memory Cleanup**: Dispose of reactive subscriptions properly
5. **Batch Operations**: Group related updates together

## Code Examples

### Task Duration Modification Impact

When modifying a task's planned duration, the following sequence demonstrates the reactive pipeline in action:

```mermaid
sequenceDiagram
participant User as User
participant Task as TaskItem
participant VM as MainWindowViewModel
participant Filter as DurationFilter
participant UI as Unlocked View
User->>Task : Modify PlannedDuration
Task->>VM : PropertyChanged Event
VM->>VM : AutoRefresh OnObservable
VM->>Filter : Evaluate Duration Predicate
Filter->>Filter : Update Task Visibility
Filter->>UI : Collection Change Notification
UI->>UI : Update Task Display
```

**Diagram sources**
- [TaskItemViewModel.cs](file://src\Unlimotion.ViewModel\TaskItemViewModel.cs#L203-L232)
- [DurationFilter.cs](file://src\Unlimotion.ViewModel\DurationFilter.cs#L13-L47)

### Task Becomes Available Example

When a task transitions from blocked to available:

```mermaid
sequenceDiagram
participant Task as TaskItem
participant TTM as TaskTreeManager
participant VM as MainWindowViewModel
participant UI as Unlocked View
Task->>TTM : IsCompleted = false
TTM->>TTM : CalculateAvailabilityForTask()
TTM->>TTM : Set IsCanBeCompleted = true
TTM->>Task : Set UnlockedDateTime = Now
TTM->>VM : AutoRefresh Triggered
VM->>VM : Recalculate Filters
VM->>UI : Add to UnlockedItems
UI->>UI : Update Display
```

**Diagram sources**
- [TaskTreeManager.cs](file://src\Unlimotion.TaskTreeManager\TaskTreeManager.cs#L697-L737)
- [MainWindowViewModel.cs](file://src\Unlimotion.ViewModel\MainWindowViewModel.cs#L499-L535)

**Section sources**
- [TaskTreeManager.cs](file://src\Unlimotion.TaskTreeManager\TaskTreeManager.cs#L697-L737)
- [TaskItemViewModel.cs](file://src\Unlimotion.ViewModel\TaskItemViewModel.cs#L203-L232)

## Troubleshooting Guide

### Common Issues and Solutions

| Issue | Symptoms | Solution |
|-------|----------|----------|
| Tasks not appearing in Unlocked View | Expected tasks missing | Check `IsCanBeCompleted` and `IsCompleted` properties |
| Performance degradation | Slow UI updates | Verify throttling settings and filter efficiency |
| Incorrect task availability | Wrong tasks shown | Review `UnlockedTimeFilter` predicates |
| Graph view not updating | Static graph display | Check `OnlyUnlocked` mode and subscription timing |

### Debugging Techniques

1. **Property Monitoring**: Enable detailed logging for task property changes
2. **Filter Validation**: Verify filter predicates return expected results
3. **Reactive Chain Inspection**: Trace the reactive pipeline for bottlenecks
4. **Memory Profiling**: Monitor memory usage during large dataset operations

### Performance Tuning

For optimal performance:

- Adjust throttling intervals based on dataset size
- Optimize filter predicates for early termination
- Monitor reactive subscription lifecycle
- Implement selective property monitoring

## Conclusion

The Unlocked View represents a sophisticated implementation of reactive task management, combining real-time filtering, complex availability calculations, and efficient performance optimization. Through its multi-layered architecture and reactive pipeline, it provides users with immediate visibility into available work items while maintaining excellent performance characteristics even with large datasets.

The system's design emphasizes separation of concerns, with business logic residing in the domain layer while presentation logic remains focused on UI coordination. This architectural approach ensures maintainability, testability, and scalability for future enhancements.

Key strengths of the implementation include:

- **Real-time responsiveness**: Immediate updates as task properties change
- **Flexible filtering**: Multi-dimensional task categorization and sorting
- **Performance optimization**: Intelligent throttling and selective monitoring
- **Visual integration**: Seamless Graph View consumption of filtered data
- **Extensible design**: Modular filter system supporting future enhancements

The Unlocked View serves as an excellent example of modern reactive programming patterns applied to task management, demonstrating how complex business logic can be effectively separated from presentation concerns while maintaining excellent user experience.