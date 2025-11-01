using System.IO;
using System.Net;
using ServiceStack;
using Unlimotion.Server.ServiceModel.Molds.Attachment;

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
        public GetAttachment()
        {
            Id = string.Empty;
        }

        [ApiMember(IsRequired = true, Description = "Идентификатор файла")]
        public string Id { get; set; }
    }
}
