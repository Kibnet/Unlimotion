using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Unlimotion.ViewModel;

public sealed class TaskTreeExpansionStateStore : IDisposable
{
    public const string DefaultFileName = "TaskTreeExpansionState.json";
    public static readonly TimeSpan DefaultSaveThrottle = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string? _filePath;
    private readonly TimeSpan _saveThrottle;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string> _readAllText;
    private readonly Action<string, string> _writeAllText;
    private readonly Timer _saveTimer;
    private readonly Dictionary<string, HashSet<string>> _expandedTaskIdsByTree;
    private bool _dirty;
    private bool _disposed;
    private bool _isPersistenceEnabled;
    private int _saveVersion;

    public TaskTreeExpansionStateStore(
        string? filePath,
        bool loadPersistedState,
        TimeSpan? saveThrottle = null,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null,
        Action<string, string>? writeAllText = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        _saveThrottle = NormalizeThrottle(saveThrottle);
        _fileExists = fileExists ?? File.Exists;
        _readAllText = readAllText ?? File.ReadAllText;
        _writeAllText = writeAllText ?? WriteAllTextAtomically;
        _isPersistenceEnabled = loadPersistedState;
        _expandedTaskIdsByTree = loadPersistedState ? LoadState() : new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        _saveTimer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public static string? GetDefaultPath(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return null;
        }

        var fullConfigPath = Path.GetFullPath(configPath);
        var configDirectory = Path.GetDirectoryName(fullConfigPath);
        return string.IsNullOrWhiteSpace(configDirectory)
            ? Path.Combine(Environment.CurrentDirectory, DefaultFileName)
            : Path.Combine(configDirectory, DefaultFileName);
    }

    public bool? GetExpansionState(string treeName, string? taskId)
    {
        if (string.IsNullOrWhiteSpace(treeName) || string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        lock (_gate)
        {
            return _expandedTaskIdsByTree.TryGetValue(treeName, out var expandedTaskIds) &&
                   expandedTaskIds.Contains(taskId)
                ? true
                : null;
        }
    }

    public void SetExpansionState(string treeName, string? taskId, bool isExpanded, bool persist)
    {
        if (string.IsNullOrWhiteSpace(treeName) || string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        lock (_gate)
        {
            var expandedTaskIds = GetOrCreateTreeState(treeName);
            var changed = isExpanded
                ? expandedTaskIds.Add(taskId)
                : expandedTaskIds.Remove(taskId);

            if (changed && persist)
            {
                ScheduleSaveLocked();
            }
        }
    }

    public void SetPersistenceEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || _isPersistenceEnabled == enabled)
            {
                return;
            }

            _isPersistenceEnabled = enabled;
            if (!enabled)
            {
                CancelPendingSaveLocked();
            }
        }
    }

    public void Flush()
    {
        string? filePath;
        string? json;
        int saveVersion;

        lock (_gate)
        {
            if (!_dirty || !_isPersistenceEnabled || string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            filePath = _filePath;
            json = JsonSerializer.Serialize(CreateSnapshotLocked(), JsonOptions);
            saveVersion = _saveVersion;
        }

        var writeSucceeded = false;
        try
        {
            _writeAllText(filePath, json);
            writeSucceeded = true;
        }
        catch
        {
            // Persistence of UI expansion state must never block the task workflow.
        }

        lock (_gate)
        {
            if (writeSucceeded)
            {
                if (saveVersion == _saveVersion)
                {
                    _dirty = false;
                }

                return;
            }

            if (_isPersistenceEnabled && !string.IsNullOrWhiteSpace(_filePath))
            {
                _dirty = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Flush();
        _saveTimer.Dispose();
    }

    private void ScheduleSaveLocked()
    {
        if (_disposed || !_isPersistenceEnabled || string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        _dirty = true;
        _saveVersion++;
        _saveTimer.Change(_saveThrottle, Timeout.InfiniteTimeSpan);
    }

    private void CancelPendingSaveLocked()
    {
        _dirty = false;
        _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private HashSet<string> GetOrCreateTreeState(string treeName)
    {
        if (_expandedTaskIdsByTree.TryGetValue(treeName, out var expandedTaskIds))
        {
            return expandedTaskIds;
        }

        expandedTaskIds = new HashSet<string>(StringComparer.Ordinal);
        _expandedTaskIdsByTree[treeName] = expandedTaskIds;
        return expandedTaskIds;
    }

    private PersistedExpansionState CreateSnapshotLocked()
    {
        return new PersistedExpansionState
        {
            Version = 1,
            Trees = _expandedTaskIdsByTree
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList(),
                    StringComparer.Ordinal)
        };
    }

    private Dictionary<string, HashSet<string>> LoadState()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }

        try
        {
            if (!_fileExists(_filePath))
            {
                return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            }

            var json = _readAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            }

            var persistedState = JsonSerializer.Deserialize<PersistedExpansionState>(json, JsonOptions);
            return NormalizeState(persistedState);
        }
        catch
        {
            return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, HashSet<string>> NormalizeState(PersistedExpansionState? persistedState)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (persistedState?.Trees == null)
        {
            return result;
        }

        foreach (var (treeName, taskIds) in persistedState.Trees)
        {
            if (string.IsNullOrWhiteSpace(treeName))
            {
                continue;
            }

            result[treeName] = taskIds?
                .Where(taskId => !string.IsNullOrWhiteSpace(taskId))
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        }

        return result;
    }

    private static void WriteAllTextAtomically(string filePath, string content)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, fullPath, true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best-effort cleanup only; the caller handles the original write failure.
        }
    }

    private static TimeSpan NormalizeThrottle(TimeSpan? saveThrottle)
    {
        if (saveThrottle == null || saveThrottle.Value < TimeSpan.Zero)
        {
            return DefaultSaveThrottle;
        }

        return saveThrottle.Value;
    }

    private sealed class PersistedExpansionState
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, List<string>> Trees { get; set; } = new(StringComparer.Ordinal);
    }
}
