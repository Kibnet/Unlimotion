using System.Net;
using ServiceStack;
using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceModel
{
    [Api("LoginAudit")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/loginAudit", "GET", Summary = "Получение истории входов", Notes = "Все факты входа пользователя")]
    public class GetLoginAudit : IReturn<LoginHistory> { }
}