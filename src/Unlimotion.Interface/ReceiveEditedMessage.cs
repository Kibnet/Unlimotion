using System;

namespace Unlimotion.Interface
{
    public class ReceiveEditedMessage : ReceiveMessage
    {
        public DateTimeOffset LastEditTime { get; set; }
    }
}