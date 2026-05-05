using System.Collections.ObjectModel;
using System.Diagnostics;
using DynamicData;
using Unlimotion;
using Unlimotion.Domain;
using Unlimotion.TaskTree;
using Unlimotion.ViewModel;
using Unlimotion.Views.Graph;

var iterations = ReadIntArgument(args, "--iterations", 5);
var warmup = ReadIntArgument(args, "--warmup", 2);

var scenarios = new[]
{
    RoadmapScenario.CreateLayeredChain("small-chain", layerCount: 8, width: 8, shortcutSpan: 3),
    RoadmapScenario.CreateLayeredChain("dense-shortcuts", layerCount: 10, width: 18, shortcutSpan: 4),
    RoadmapScenario.CreateLayeredTree("tree-with-blocks", depth: 5, branchFactor: 4, blockFanOut: 3)
};

Console.WriteLine($"Roadmap graph performance baseline: warmup={warmup}, iterations={iterations}");
Console.WriteLine("Scenario\tNodes\tConnections\tMeanMs\tMinMs\tMaxMs\tAllocatedKB");

foreach (var scenario in scenarios)
{
    for (var index = 0; index < warmup; index++)
    {
        Consume(RoadmapGraphBuilder.Build(scenario.Roots));
    }

    var elapsed = new double[iterations];
    var allocated = new long[iterations];
    var checksum = 0;

    for (var index = 0; index < iterations; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var projection = RoadmapGraphBuilder.Build(scenario.Roots);
        stopwatch.Stop();
        var afterAllocated = GC.GetAllocatedBytesForCurrentThread();

        elapsed[index] = stopwatch.Elapsed.TotalMilliseconds;
        allocated[index] = afterAllocated - beforeAllocated;
        checksum += Consume(projection);
    }

    var mean = elapsed.Average();
    var allocatedMean = allocated.Average() / 1024d;
    Console.WriteLine(
        string.Join(
            '\t',
            scenario.Name,
            scenario.TaskCount,
            scenario.ConnectionCount,
            mean.ToString("F2"),
            elapsed.Min().ToString("F2"),
            elapsed.Max().ToString("F2"),
            allocatedMean.ToString("F1")));

    GC.KeepAlive(checksum);
}

static int ReadIntArgument(string[] args, string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var value))
    {
        return fallback;
    }

    return value;
}

static int Consume(RoadmapGraphProjection projection)
{
    return projection.Nodes.Count * 397 + projection.Connections.Count * 17;
}

internal sealed class RoadmapScenario
{
    private RoadmapScenario(
        string name,
        ReadOnlyObservableCollection<TaskWrapperViewModel> roots,
        int taskCount,
        int connectionCount)
    {
        Name = name;
        Roots = roots;
        TaskCount = taskCount;
        ConnectionCount = connectionCount;
    }

    public string Name { get; }

    public ReadOnlyObservableCollection<TaskWrapperViewModel> Roots { get; }

    public int TaskCount { get; }

    public int ConnectionCount { get; }

    public static RoadmapScenario CreateLayeredChain(
        string name,
        int layerCount,
        int width,
        int shortcutSpan)
    {
        var storage = new StubTaskStorage();
        var tasks = new TaskItemViewModel[layerCount][];
        for (var layer = 0; layer < layerCount; layer++)
        {
            tasks[layer] = new TaskItemViewModel[width];
            for (var index = 0; index < width; index++)
            {
                tasks[layer][index] = CreateTask(
                    $"{name}-{layer}-{index}",
                    $"{name} layer {layer} task {index}",
                    storage);
            }
        }

        var connectionCount = 0;
        for (var layer = 0; layer < layerCount - 1; layer++)
        {
            for (var index = 0; index < width; index++)
            {
                var targets = new List<TaskItemViewModel> { tasks[layer + 1][index] };

                if (index > 0)
                {
                    targets.Add(tasks[layer + 1][index - 1]);
                }

                if (index + 1 < width)
                {
                    targets.Add(tasks[layer + 1][index + 1]);
                }

                for (var span = 2; span <= shortcutSpan && layer + span < layerCount; span++)
                {
                    targets.Add(tasks[layer + span][index]);
                }

                connectionCount += targets.Count;
                tasks[layer][index].ApplyRelations(
                    Array.Empty<TaskItemViewModel>(),
                    Array.Empty<TaskItemViewModel>(),
                    targets,
                    Array.Empty<TaskItemViewModel>());
            }
        }

        return new RoadmapScenario(
            name,
            CreateRootWrappers(tasks.SelectMany(layer => layer)),
            tasks.Sum(layer => layer.Length),
            connectionCount);
    }

    public static RoadmapScenario CreateLayeredTree(
        string name,
        int depth,
        int branchFactor,
        int blockFanOut)
    {
        var storage = new StubTaskStorage();
        var allTasks = new List<TaskItemViewModel>();
        var root = CreateTask($"{name}-root", $"{name} root", storage);
        var rootWrapper = CreateWrapper(root);
        var currentLayer = new List<TaskWrapperViewModel> { rootWrapper };
        allTasks.Add(root);

        var connectionCount = 0;
        for (var layer = 1; layer <= depth; layer++)
        {
            var nextLayer = new List<TaskWrapperViewModel>();
            foreach (var parentWrapper in currentLayer)
            {
                var children = new List<TaskWrapperViewModel>();
                for (var index = 0; index < branchFactor; index++)
                {
                    var child = CreateTask(
                        $"{name}-{layer}-{allTasks.Count}",
                        $"{name} child {layer} {index}",
                        storage);
                    allTasks.Add(child);
                    var childWrapper = CreateWrapper(child);
                    children.Add(childWrapper);
                    nextLayer.Add(childWrapper);
                    connectionCount++;
                }

                SetChildren(parentWrapper, children);
            }

            currentLayer = nextLayer;
        }

        for (var index = 0; index < allTasks.Count; index++)
        {
            var targets = new List<TaskItemViewModel>();
            for (var offset = 1; offset <= blockFanOut && index + offset < allTasks.Count; offset++)
            {
                targets.Add(allTasks[index + offset]);
            }

            if (targets.Count == 0)
            {
                continue;
            }

            connectionCount += targets.Count;
            allTasks[index].ApplyRelations(
                Array.Empty<TaskItemViewModel>(),
                Array.Empty<TaskItemViewModel>(),
                targets,
                Array.Empty<TaskItemViewModel>());
        }

        return new RoadmapScenario(
            name,
            CreateRootWrappersFromWrappers(rootWrapper),
            allTasks.Count,
            connectionCount);
    }

    private static TaskItemViewModel CreateTask(string id, string title, ITaskStorage storage)
    {
        return new TaskItemViewModel(
            new TaskItem
            {
                Id = id,
                Title = title,
                BlocksTasks = new List<string>(),
                BlockedByTasks = new List<string>(),
                ContainsTasks = new List<string>(),
                ParentTasks = new List<string>()
            },
            storage,
            () => false);
    }

    private static ReadOnlyObservableCollection<TaskWrapperViewModel> CreateRootWrappers(
        IEnumerable<TaskItemViewModel> tasks)
    {
        return CreateRootWrappersFromWrappers(tasks.Select(task => CreateWrapper(task)).ToArray());
    }

    private static ReadOnlyObservableCollection<TaskWrapperViewModel> CreateRootWrappersFromWrappers(
        params TaskWrapperViewModel[] wrappers)
    {
        return new ReadOnlyObservableCollection<TaskWrapperViewModel>(
            new ObservableCollection<TaskWrapperViewModel>(wrappers));
    }

    private static TaskWrapperViewModel CreateWrapper(
        TaskItemViewModel task,
        params TaskWrapperViewModel[] children)
    {
        var wrapper = new TaskWrapperViewModel(null!, task, new TaskWrapperActions());
        SetChildren(wrapper, children);
        return wrapper;
    }

    private static void SetChildren(
        TaskWrapperViewModel wrapper,
        IReadOnlyList<TaskWrapperViewModel> children)
    {
        foreach (var child in children)
        {
            child.Parent = wrapper;
        }

        wrapper.SubTasks = new ReadOnlyObservableCollection<TaskWrapperViewModel>(
            new ObservableCollection<TaskWrapperViewModel>(children));
    }
}

internal sealed class StubTaskStorage : ITaskStorage
{
    public SourceCache<TaskItemViewModel, string> Tasks { get; } = new(task => task.Id);

    public ITaskRelationsIndex Relations { get; } = new TaskRelationsIndex();

    public TaskTreeManager TaskTreeManager => throw new NotSupportedException();

    public event EventHandler<EventArgs>? Initiated
    {
        add { }
        remove { }
    }

    public Task Init() => Task.CompletedTask;

    public Task<TaskItemViewModel> Add(TaskItemViewModel? currentTask = null, bool isBlocked = false) =>
        throw new NotSupportedException();

    public Task<TaskItemViewModel> AddChild(TaskItemViewModel currentTask) =>
        throw new NotSupportedException();

    public Task<bool> Delete(TaskItemViewModel change, bool deleteInStorage = true) =>
        throw new NotSupportedException();

    public Task<bool> Delete(TaskItemViewModel change, TaskItemViewModel parent) =>
        throw new NotSupportedException();

    public Task<TaskItemViewModel> Update(TaskItemViewModel change) =>
        throw new NotSupportedException();

    public Task<TaskItemViewModel> Update(TaskItem change) =>
        throw new NotSupportedException();

    public Task<TaskItemViewModel> Clone(TaskItemViewModel change, params TaskItemViewModel[]? additionalParents) =>
        throw new NotSupportedException();

    public Task<bool> CopyInto(TaskItemViewModel change, TaskItemViewModel[]? additionalParents) =>
        throw new NotSupportedException();

    public Task<bool> MoveInto(TaskItemViewModel change, TaskItemViewModel[] additionalParents, TaskItemViewModel? currentTask) =>
        throw new NotSupportedException();

    public Task<bool> Unblock(TaskItemViewModel taskToUnblock, TaskItemViewModel blockingTask) =>
        throw new NotSupportedException();

    public Task<bool> Block(TaskItemViewModel change, TaskItemViewModel currentTask) =>
        throw new NotSupportedException();

    public Task RemoveParentChildConnection(TaskItemViewModel parent, TaskItemViewModel child) =>
        throw new NotSupportedException();
}
