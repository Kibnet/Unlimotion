using System.ComponentModel;

namespace Unlimotion.Server.ServiceModel.Molds
{
    [Description("Результат изменения пароля")]
    public class PasswordChangeResult
    {
        [Description("Результат")]
        public ChangeEnum Result { get; set; }

        public enum ChangeEnum
        {
            [Description("Создан")]
            Created,
            [Description("Изменён")]
            Changed,
        }
    }
}