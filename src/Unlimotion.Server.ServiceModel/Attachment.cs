using System.IO;
using ServiceStack;
using Unlimotion.Server.ServiceModel.Molds.Attachment;
using System.Net;

namespace Unlimotion.Server.ServiceModel
{
    [Api("Attachment")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/attachments", "POST", Summary = "Отправка файла на сервер", Notes = "Отправка файла на сервер")]
    public class SetAttachment : IReturn<AttachmentMold>
    { }

    [Api("Attachment")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/attachments/{id}", "GET", Summary = "Получение файла с сервера", Notes = "Получение файла с сервера")]
    public class GetAttachment : IReturn<Stream>
    {
        [ApiMember(IsRequired = true, Description = "Идентификатор файла")]
        public string Id { get; set; }
    }
}
