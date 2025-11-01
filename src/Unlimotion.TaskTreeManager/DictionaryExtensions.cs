
using System.Collections.Generic;
using Unlimotion.Domain;

namespace Unlimotion.TaskTree;

public static class DictionaryExtensions
{
    public static void AddOrUpdate<TKey,TValue>(this IDictionary<TKey,TValue> source, TKey key, TValue value)
    {
        source[key] = value;
    }
    public static void AddOrUpdateRange<TKey,TValue>(this IDictionary<TKey,TValue> source, IDictionary<TKey,TValue> values)
    {
        foreach (var value in values)
        {
            source[value.Key] = value.Value;
        }        
    }
    
    public static void AddOrUpdate(this IDictionary<string,TaskItem> source, TaskItem item)
    {
        source[item.Id] = item;
    }
    
    public static void AddOrUpdateRange(this IDictionary<string,TaskItem> source, IEnumerable<TaskItem> items)
    {
        foreach (var item in items)
        {
            source[item.Id] = item;
        }        
    }
}