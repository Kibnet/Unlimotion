# Search Functionality

<cite>
**Referenced Files in This Document**   
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)
- [SearchBar.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchBar.axaml.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [GraphControl.axaml.cs](file://src/Unlimotion/Views/GraphControl.axaml.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)
- [GraphViewModel.cs](file://src/Unlimotion.ViewModel/GraphViewModel.cs)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Search Architecture Overview](#search-architecture-overview)
3. [Core Search Components](#core-search-components)
4. [Search Implementation Details](#search-implementation-details)
5. [Search UI Components](#search-ui-components)
6. [Search Algorithm and Text Processing](#search-algorithm-and-text-processing)
7. [Search Integration with Task Filtering](#search-integration-with-task-filtering)
8. [Performance Considerations](#performance-considerations)
9. [Conclusion](#conclusion)

## Introduction
The Unlimotion application implements a comprehensive search functionality that allows users to find tasks based on various criteria including text content, emojis, and task properties. The search system is integrated throughout the application and provides real-time filtering with throttling to optimize performance. This document details the architecture, implementation, and integration of the search functionality within the Unlimotion application.

## Search Architecture Overview

```mermaid
graph TD
subgraph "UI Layer"
SearchControl["SearchControl (UI Component)"]
SearchBar["SearchBar (UI Component)"]
end
subgraph "ViewModel Layer"
MainWindowVM["MainWindowViewModel"]
GraphVM["GraphViewModel"]
SearchDef["SearchDefinition"]
end
subgraph "Data Layer"
TaskItemVM["TaskItemViewModel"]
end
SearchControl --> |Binds to| SearchDef
SearchBar --> |Part of| SearchControl
SearchDef --> |Used by| MainWindowVM
SearchDef --> |Used by| GraphVM
MainWindowVM --> |Applies search filter to| TaskItemVM
GraphVM --> |Applies search filter to| TaskItemVM
TaskItemVM --> |Provides searchable data from| TaskItem
style SearchControl fill:#f9f,stroke:#333
style SearchBar fill:#f9f,stroke:#333
style SearchDef fill:#bbf,stroke:#333
style MainWindowVM fill:#bbf,stroke:#333
style GraphVM fill:#bbf,stroke:#333
style TaskItemVM fill:#9f9,stroke:#333
```

**Diagram sources**
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [GraphViewModel.cs](file://src/Unlimotion.ViewModel/GraphViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)

**Section sources**
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)

## Core Search Components

The search functionality in Unlimotion is built around several key components that work together to provide a seamless search experience. The core components include the SearchDefinition class, which holds the search state, and the UI controls that allow users to input search queries.

The SearchDefinition class serves as the central data structure for search operations, containing the search text and utility methods for text normalization. This class is used by multiple view models throughout the application to maintain a consistent search state.

```mermaid
classDiagram
class SearchDefinition {
+const int DefaultThrottleMs = 600
+static string NormalizeText(string s)
+string? SearchText
}
class MainWindowViewModel {
+SearchDefinition Search
-IObservable~Func~ searchTopFilter
}
class GraphViewModel {
+SearchDefinition Search
}
SearchDefinition <|-- MainWindowViewModel : "contains"
SearchDefinition <|-- GraphViewModel : "contains"
style SearchDefinition fill : #bbf,stroke : #333
style MainWindowViewModel fill : #bbf,stroke : #333
style GraphViewModel fill : #bbf,stroke : #333
```

**Diagram sources**
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [GraphViewModel.cs](file://src/Unlimotion.ViewModel/GraphViewModel.cs)

**Section sources**
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [GraphViewModel.cs](file://src/Unlimotion.ViewModel/GraphViewModel.cs)

## Search UI Components

The user interface for search consists of two main components: SearchControl and SearchBar. These components provide the visual interface for users to input search queries and clear them when needed.

```mermaid
classDiagram
class UserControl
class SearchControl {
+static readonly DirectProperty SearchTextProperty
+static readonly StyledProperty WatermarkProperty
+string SearchText
+string Watermark
+SearchControl()
+OnClearClick(object? sender, RoutedEventArgs e)
}
class SearchBar {
+SearchBar()
}
UserControl <|-- SearchControl
UserControl <|-- SearchBar
SearchBar --> SearchControl : "contained within"
style SearchControl fill : #f9f,stroke : #333
style SearchBar fill : #f9f,stroke : #333
```

**Diagram sources**
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)
- [SearchBar.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchBar.axaml.cs)

**Section sources**
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)
- [SearchBar.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchBar.axaml.cs)

## Search Implementation Details

The search functionality is implemented using reactive programming patterns with the ReactiveUI framework. When a user types in the search box, the search text is processed through a series of operators that throttle the input, filter out unchanged values, and apply the search filter to the task collection.

```mermaid
sequenceDiagram
participant User as "User"
participant SearchControl as "SearchControl"
participant ViewModel as "MainWindowViewModel"
participant Filter as "Search Filter"
participant Tasks as "Task Collection"
User->>SearchControl : Types search query
SearchControl->>ViewModel : Updates Search.SearchText
ViewModel->>ViewModel : Throttle(600ms)
ViewModel->>ViewModel : DistinctUntilChanged()
ViewModel->>Filter : Create filter function
Filter->>Tasks : Apply filter to tasks
Tasks-->>ViewModel : Filtered task collection
ViewModel-->>User : Display filtered results
Note over ViewModel,Filter : Search processing with throttling
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [SearchControl.axaml.cs](file://src/Unlimotion/Views/SearchControl/SearchControl.axaml.cs)

## Search Algorithm and Text Processing

The search algorithm in Unlimotion processes text queries by normalizing the search input and comparing it against multiple fields of task items. The text normalization process ensures that searches are case-insensitive and handle Unicode characters properly.

```mermaid
flowchart TD
Start([Start Search]) --> Normalize["Normalize Search Text"]
Normalize --> Split["Split into Words"]
Split --> CheckEmpty{"Words Empty?"}
CheckEmpty --> |Yes| ReturnAll["Return All Tasks"]
CheckEmpty --> |No| ProcessTasks["Process Each Task"]
ProcessTasks --> Extract["Extract Searchable Data"]
Extract --> Concat["Concatenate: Title + Description + Emoji + ID"]
Concat --> NormalizeSource["Normalize Source Text"]
NormalizeSource --> CheckWords["Check All Words Present"]
CheckWords --> |All Present| Include["Include Task"]
CheckWords --> |Not All Present| Exclude["Exclude Task"]
Include --> NextTask["Next Task"]
Exclude --> NextTask
NextTask --> MoreTasks{"More Tasks?"}
MoreTasks --> |Yes| ProcessTasks
MoreTasks --> |No| ReturnResults["Return Filtered Results"]
ReturnAll --> ReturnResults
ReturnResults --> End([End])
style Start fill:#f9f,stroke:#333
style End fill:#f9f,stroke:#333
style Normalize fill:#bbf,stroke:#333
style Split fill:#bbf,stroke:#333
style CheckEmpty fill:#ffcc00,stroke:#333
style ReturnAll fill:#bbf,stroke:#333
style ProcessTasks fill:#bbf,stroke:#333
style Extract fill:#bbf,stroke:#333
style Concat fill:#bbf,stroke:#333
style NormalizeSource fill:#bbf,stroke:#333
style CheckWords fill:#ffcc00,stroke:#333
style Include fill:#bbf,stroke:#333
style Exclude fill:#bbf,stroke:#333
style NextTask fill:#bbf,stroke:#333
style MoreTasks fill:#ffcc00,stroke:#333
style ReturnResults fill:#bbf,stroke:#333
```

**Diagram sources**
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)

**Section sources**
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)

## Search Integration with Task Filtering

The search functionality is integrated with other filtering mechanisms in the application, such as emoji filtering and date filtering. These filters work together to provide a comprehensive task filtering experience.

```mermaid
flowchart TD
A["Task Filtering System"] --> B["Search Filter"]
A --> C["Emoji Filter"]
A --> D["Date Filter"]
A --> E["Duration Filter"]
A --> F["Completion Status Filter"]
B --> G["Text-based search on title, description, emoji, and ID"]
C --> H["Filter by emoji presence/absence"]
D --> I["Filter by creation, completion, or archive dates"]
E --> J["Filter by planned duration"]
F --> K["Filter by completion status"]
L["Combined Filter"] --> M["Final Task Collection"]
B --> L
C --> L
D --> L
E --> L
F --> L
style A fill:#f9f,stroke:#333
style B fill:#bbf,stroke:#333
style C fill:#bbf,stroke:#333
style D fill:#bbf,stroke:#333
style E fill:#bbf,stroke:#333
style F fill:#bbf,stroke:#333
style L fill:#f9f,stroke:#333
style M fill:#9f9,stroke:#333
```

**Diagram sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)
- [TaskItemViewModel.cs](file://src/Unlimotion.ViewModel/TaskItemViewModel.cs)

**Section sources**
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)

## Performance Considerations

The search implementation includes several performance optimizations to ensure a responsive user experience. The most significant optimization is the use of throttling, which delays the application of search filters until the user has stopped typing for a specified period.

The default throttle time is set to 600 milliseconds, which strikes a balance between responsiveness and performance. This prevents the application from performing expensive filtering operations on every keystroke, which would degrade performance, especially with large task collections.

Additionally, the search implementation uses reactive programming patterns that efficiently handle changes to the search text and only re-evaluate the filter when necessary. The DistinctUntilChanged operator ensures that the filter is not reapplied when the search text hasn't actually changed.

**Section sources**
- [SearchDefinition.cs](file://src/Unlimotion.ViewModel/SearchDefinition.cs)
- [MainWindowViewModel.cs](file://src/Unlimotion.ViewModel/MainWindowViewModel.cs)

## Conclusion
The search functionality in Unlimotion provides a robust and efficient way for users to find tasks within their task hierarchy. By combining text search with other filtering mechanisms like emoji and date filtering, the application offers a comprehensive task discovery experience. The implementation leverages reactive programming patterns to create a responsive interface while maintaining good performance through throttling and efficient filtering operations. The modular design with the SearchDefinition class allows the search functionality to be easily integrated across different parts of the application.