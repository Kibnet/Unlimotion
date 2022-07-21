using System.Collections.Generic;

namespace Unlimotion.Interface;

public class RepeaterPatternHubMold
{
    public RepeaterTypeHubMold Type { get; set; }
    public int Period { get; set; } = 1;
    public bool AfterComplete { get; set; }
    public List<int> Pattern { get; set; }
}