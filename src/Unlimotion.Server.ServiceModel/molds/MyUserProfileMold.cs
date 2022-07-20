using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Мой профиль пользователя")]
    public class MyUserProfileMold : UserProfileMold
    {
        [Description("Наличие пароля")]
        public bool IsPasswordSetted { get; set; }
    }
}