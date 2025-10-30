
using System.Collections.Generic;

namespace Unlimotion.TaskTree;

public class AutoUpdatingDictionary<TKey, TValue>
{
    private readonly Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();

    public void AddOrUpdate(TKey key, TValue value)
    {
        _dictionary[key] = value;
    }
    public void AddOrUpdateRange(Dictionary<TKey, TValue> values)
    {
        foreach (var value in values)
        {
            _dictionary[value.Key] = value.Value;
        }        
    }
    
    public Dictionary<TKey, TValue> Dict => _dictionary;

    public void Clear()
    {
        _dictionary.Clear();
    }
}