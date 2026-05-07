using SignalR.EasyUse.Interface;

namespace Unlimotion.Interface
{
    public class UpdateUserDisplayName : IClientMethod
    {
        public string Id { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string UserLogin { get; set; } = null!;
    }
}
