using System;
using System.Collections.Generic;
using SignalR.EasyUse.Interface;
using System.Threading.Tasks;

namespace Unlimotion.Interface
{
    public interface IChatHub : IServerMethods
    {
        Task SaveTask(TaskItemHubMold hubTask);
        Task UpdateMyDisplayName(string userDispalyName);
        Task Login(string token, string operatingSystem, string ipAddress, string nameVersionClient);
        Task DeleteTasks(List<string> idTasks);
    }
}