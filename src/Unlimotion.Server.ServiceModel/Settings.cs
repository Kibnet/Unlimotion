using System.Net;
using ServiceStack;
using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceModel
{
    [Api("Settings")]
    [ApiResponse(HttpStatusCode.Unauthorized, "Токен неверный или сессия закрыта", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.ServiceUnavailable, "Сервис авторизации недоступен", ResponseType = typeof(void))]
    [Route("/settings", "GET", Summary = "Получить настройки пользователя", Notes = "Возвращаются актуальные сведения из базы данных")]
    public class GetMySettings : IReturn<UserChatSettings> { }

    [Api("Settings")]
    [ApiResponse(HttpStatusCode.Unauthorized, "Токен неверный или сессия закрыта", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.ServiceUnavailable, "Сервис авторизации недоступен", ResponseType = typeof(void))]
    [Route("/settings", "POST", Summary = "Отправка настроек", Notes = "Сохранение изменений пользователя в базе данных")]
    public class SetSettings : IReturn<UserChatSettings>
    {
        [ApiMember(Description = "Настройка отправки сообщения пользователем")]
        public bool SendingMessageByEnterKey { set; get; }
    }
}
