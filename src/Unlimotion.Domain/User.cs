using System;

namespace Unlimotion.Domain
{
    public class User
    {
        public string Id { get; set; } = null!;
        public string Login { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string AboutMe { get; set; } = null!;
        public DateTimeOffset RegisteredTime { get; set; }

        public override string ToString()
        {
            return $"{Id};{Login}";
        }
    }

    public class UserSecret
    {
        public string Id { get; set; } = null!;
        public string Password { get; set; } = null!;
        public byte[] Salt { get; set; } = null!;
    }
}
