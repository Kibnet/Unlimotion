using System.Collections.Generic;
using System.Linq;
using System;

namespace Unlimotion.Domain;

public class RepeaterPattern
{
    public RepeaterType Type { get; set; }
    public int Period { get; set; } = 1;
    public bool AfterComplete { get; set; }
    public List<int> Pattern { get; set; } = null!;

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        var y = obj as RepeaterPattern;
        if (y is null) return false;

        return Type == y.Type &&
               Period == y.Period &&
               AfterComplete == y.AfterComplete &&
               (Pattern == y.Pattern || (Pattern != null && y.Pattern != null && Pattern.SequenceEqual(y.Pattern)));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        hash.Add(Period);
        hash.Add(AfterComplete);
        foreach (var item in Pattern ?? [])
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
