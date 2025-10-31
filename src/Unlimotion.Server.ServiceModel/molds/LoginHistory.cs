using System.Collections.Generic;
using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Страница истории входа")]
    public class LoginHistory
    {
        public LoginHistory()
        {
            History = new List<UserLoginAudit>();
            UniqueSessionUser = string.Empty;
        }

        [Description("История входа")]
        public List<UserLoginAudit> History { get; set; }
        [Description("Уникальный идентификатор сессии")]
        public string UniqueSessionUser { get; set; }
    }
}
