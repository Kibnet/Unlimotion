using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using ServiceStack;

using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceModel
{

    [Api("Profile")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/getprofile", "GET", Summary = "Получение профиля")]
    public class GetProfile : IReturn<UserProfileMold>
    {
        [ApiMember(IsRequired = true, Description = "Идентификатор пользователя")]
        public string UserId { get; set; }
    } 

    [Api("Profile")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/setprofile", "POST", Summary = "Отправка профиля")]
    public class SetProfile : IReturn<UserProfileMold>
    {
        [ApiMember(IsRequired = true, Description = "Имя пользователя")]
        public string DisplayName { get; set; }

        [ApiMember(IsRequired = true, Description = "О себе")]
        public string AboutMe { get; set; }
    }
}
