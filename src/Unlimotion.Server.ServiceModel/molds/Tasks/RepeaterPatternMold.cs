using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds.Tasks;

[Description("Шаблон повторения")]
public class RepeaterPatternMold
{
    public RepeaterPatternMold()
    {
        Pattern = new List<int>();
    }

    [Description("Тип повторения")]
    public RepeaterTypeMold Type { get; set; }

    [Description("Период")]
    public int Period { get; set; } = 1;

    [Description("Отсчитывать после завершения")]
    public bool AfterComplete { get; set; }

    [Description("Индексы")]
    public List<int> Pattern { get; set; }
}