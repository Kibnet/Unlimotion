using AutoMapper;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using ServiceStack;
using Unlimotion.Server.Domain;
using Unlimotion.Server.ServiceModel;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.Documents.BulkInsert;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion.Server.ServiceInterface
{
    public class TaskService : Service
    {
        public IAsyncDocumentSession RavenSession { get; set; }
        public IDocumentStore DocumentStore { get; set; }
        public IMapper Mapper { get; set; }
        private const string TaskPrefix = "TaskItem/";

        [Authenticate]
        public async Task<TaskItemPage> Get(GetAllTasks request)
        {
            var session = Request.ThrowIfUnauthorized();
            var uid = session.UserAuthId;
            var tasks = await RavenSession.Query<TaskItem>()
                .Where(chat => chat.UserId == uid)
                .ToListAsync();
            return new TaskItemPage
            {
                Tasks = tasks.Select(e => Mapper.Map<TaskItemMold>(e)).ToList()
            };
        }

        [Authenticate]
        public async Task<TaskItemMold> Get(GetTask request)
        {
            var session = Request.ThrowIfUnauthorized();
            var task = await RavenSession.Query<TaskItem>()
                .Where(chat => chat.Id == request.Id)
                .FirstAsync();
            return Mapper.Map<TaskItemMold>(task);
        }
        
        [Authenticate]
        public async Task Post(BulkInsertTasks request)
        {
            var session = Request.ThrowIfUnauthorized();
            var uid = session.UserAuthId;
            var tasks = request.Tasks.Select(e => Mapper.Map<TaskItem>(e)).ToList();
            using var insert = DocumentStore.BulkInsert();
            foreach (var task in tasks)
            {
                task.UserId = uid;
                task.Id = $"{TaskPrefix}{task.Id}";
                if (task.BlocksTasks != null)
                {
                    task.BlocksTasks = task.BlocksTasks.Select(s => $"{TaskPrefix}{s}").ToList();
                }
                if (task.ContainsTasks != null)
                {
                    task.ContainsTasks = task.ContainsTasks.Select(s => $"{TaskPrefix}{s}").ToList();
                }
                await insert.StoreAsync(task);
            }
        }
    }
}