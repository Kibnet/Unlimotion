using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;

namespace Unlimotion.Views;

internal static class DragDataFormats
{
    private static readonly ConcurrentDictionary<string, object> Values = new();

    public static InMemoryDragDataTransfer CreateTransfer<T>(DataFormat<string> format, T value)
        where T : class
    {
        var transfer = new InMemoryDragDataTransfer();
        transfer.Add(DataTransferItem.Create(format, transfer.Track(value)));
        return transfer;
    }

    public static bool TryGetValue<T>(
        IDataTransfer dataTransfer,
        DataFormat<string> format,
        [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = null;

        var key = dataTransfer.TryGetValue(format);
        if (key == null ||
            !Values.TryGetValue(key, out var rawValue) ||
            rawValue is not T typedValue)
        {
            return false;
        }

        value = typedValue;
        return true;
    }

    internal static void Register(string key, object value)
    {
        Values[key] = value;
    }

    internal static void Unregister(string key)
    {
        Values.TryRemove(key, out _);
    }
}

internal sealed class InMemoryDragDataTransfer : IDataTransfer
{
    private readonly DataTransfer inner = new();
    private readonly List<string> keys = [];

    public IReadOnlyList<DataFormat> Formats => inner.Formats;

    public IReadOnlyList<IDataTransferItem> Items => inner.Items;

    public void Add(DataTransferItem item)
    {
        inner.Add(item);
    }

    public string Track(object value)
    {
        var key = Guid.NewGuid().ToString("N");
        DragDataFormats.Register(key, value);
        keys.Add(key);
        return key;
    }

    public void Dispose()
    {
        ((IDisposable)inner).Dispose();

        foreach (var key in keys)
        {
            DragDataFormats.Unregister(key);
        }

        keys.Clear();
    }
}
