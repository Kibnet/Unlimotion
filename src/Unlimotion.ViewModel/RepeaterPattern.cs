using System.Collections.Generic;

namespace Unlimotion.ViewModel;

public class RepeaterPattern
{
    public RepeaterType Type { get; set; }
    public int Period { get; set; } = 1;
    public bool AfterComplete { get; set; }
    public List<int> Pattern { get; set; }
}