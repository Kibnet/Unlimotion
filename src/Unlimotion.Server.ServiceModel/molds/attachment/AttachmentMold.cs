using ServiceStack.DataAnnotations;
using System;

namespace Unlimotion.Server.ServiceModel.Molds.Attachment
{
    public class AttachmentMold
    {
        [Description("Идентификатор файла")]
        public string Id { get; set; }

        [Description("Идентификатор Отправителя")]
        public string SenderId { get; set; }

        [Description("Имя файла")]
        public string FileName { get; set; }

        [Description("Время загрузки")]
        public DateTimeOffset UploadDateTime { get; set; }

        [Description("Хеш файла")]
        public string Hash { get; set; }

        [Description("Размер файла")]
        public long Size { get; set; }
    }
}