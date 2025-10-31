using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Профиль пользователя")]
    public class UserProfileMold
    {
        public UserProfileMold()
        {
            Id = string.Empty;
            Login = string.Empty;
            DisplayName = string.Empty;
            AboutMe = string.Empty;
        }

        [Description("Идентификатор пользователя")]
        public string Id { get; set; }

        [Description("Псевдоним пользователя")]
        public string Login { get; set; }

        [Description("Имя пользователя")]
        public string DisplayName { get; set; }

        [Description("О себе")]
        public string AboutMe { get; set; }
    }
}