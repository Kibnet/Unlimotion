using System.Collections.Generic;
using System.Threading.Tasks;
using SignalR.EasyUse.Interface;

namespace Unlimotion.Interface
{
    public interface IChatHub : IServerMethods
    {
        Task<string> SaveTask(TaskItemHubMold hubTask);
        Task UpdateMyDisplayName(string userDispalyName);
        Task Login(string token, string operatingSystem, string ipAddress, string nameVersionClient);
        Task DeleteTasks(List<string> idTasks);
    }
}