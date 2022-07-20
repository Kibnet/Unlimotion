using System;
using System.Collections.Generic;
using SignalR.EasyUse.Interface;
using System.Threading.Tasks;

namespace Unlimotion.Interface
{
    public interface IChatHub : IServerMethods
    {
        Task SendMessage(HubMessage hubMessage);
        Task UpdateMessage(HubEditedMessage hubEditedMessage);
        Task UpdateMyDisplayName(string userDispalyName);
        Task Login(string token, string operatingSystem, string ipAddress, string nameVersionClient);
        Task DeleteMessagesForMe(List<string> idMessages);
        Task CleanChatForMe(string chatId);
    }
}