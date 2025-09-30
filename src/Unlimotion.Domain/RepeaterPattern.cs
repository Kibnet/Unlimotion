using System.Collections.Generic;
using System.Linq;

namespace Unlimotion.Domain;

public class RepeaterPattern
{
    public RepeaterType Type { get; set; }
    public int Period { get; set; } = 1;
    public bool AfterComplete { get; set; }
    public List<int> Pattern { get; set; }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        var y = obj as RepeaterPattern;
        if (this is null || y is null) return false;

        return this.Type == y.Type &&
               this.Period == y.Period &&
               this.AfterComplete == y.AfterComplete &&
               (this.Pattern == y.Pattern || (this.Pattern != null && y.Pattern != null && this.Pattern.SequenceEqual(y.Pattern)));
    }
}