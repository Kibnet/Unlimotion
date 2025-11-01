using System;
using ServiceStack.DataAnnotations;

namespace Unlimotion.Server.ServiceModel.Molds.Attachment
{
    [Description("Файл")]
    public class AttachmentMold
    {
        public AttachmentMold()
        {
            Id = string.Empty;
            SenderId = string.Empty;
            FileName = string.Empty;
            Hash = string.Empty;
        }

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