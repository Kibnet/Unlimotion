using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds.Tasks;

[Description("Тип повторения")]
public enum RepeaterTypeMold
{
    [Description("Никакой")]
    None,

    [Description("По дням")]
    Daily,

    [Description("По неделям")]
    Weekly,

    [Description("По месяцам")]
    Monthly,

    [Description("По годам")]
    Yearly,
}