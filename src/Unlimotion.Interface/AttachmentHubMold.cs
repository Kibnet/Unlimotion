using System;

namespace Unlimotion.Interface
{
    public class AttachmentHubMold
    {
        public string Id { get; set; } = null!;
        public string SenderId { get; set; } = null!;
        public string FileName { get; set; } = null!;
        public DateTimeOffset UploadDateTime { get; set; }
        public string Hash { get; set; } = null!;
        public long Size { get; set; }
    }
}
