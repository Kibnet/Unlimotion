using System;

namespace Unlimotion.Interface
{
    public class AttachmentHubMold
    {
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string FileName { get; set; }
        public DateTimeOffset UploadDateTime { get; set; }
        public string Hash { get; set; }
        public long Size { get; set; }
    }
}
