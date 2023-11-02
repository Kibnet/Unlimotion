using System;
using System.Collections.Generic;
using System.Net;
using ServiceStack;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion.Server.ServiceModel
{
    [Api("Task")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/tasks", "GET", Summary = "Получение задач", Notes = "Подгрузка задач с паджинацией")]
    public class GetTasks : IReturn<TaskItemPage>
    {
        [ApiMember(IsRequired = false, Description = "Время задачи после которой получать")]
        public DateTimeOffset? BeforePostTime { get; set; }
        
        [ApiMember(IsRequired = false, Description = "Размер страницы результатов")]
        public int? PageSize { get;set; }
    }

    [Api("Task")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/tasks/{Id}", "GET", Summary = "Получение задачи", Notes = "Подгрузка задачи по идентификатору")]
    public class GetTask : IReturn<TaskItemMold>
    {
        [ApiMember(IsRequired = true, Description = "Идентификатор задачи")]
        public string Id { get; set; }
    }

    [Api("Task")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/tasks", "GET", Summary = "Получение списка задач", Notes = "Получение списка задач")]
    public class GetAllTasks: IReturn<TaskItemPage>
    {}

    [Api("Task")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/tasks/bulk", "POST", Summary = "Массовая загрузка списка задач", Notes = "Массовая загрузка списка задач")]
    public class BulkInsertTasks : IReturnVoid
    {
        [ApiMember(IsRequired = true, Description = "Список задач")]
        public List<TaskItemMold> Tasks { get; set; }
    }
}
