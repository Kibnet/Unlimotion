using System.Net;
using ServiceStack;
using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceModel
{
    [Api("Auth")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.NotFound, "Пара пользователя и пароля не найдена", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.ServiceUnavailable, "Сервис авторизации недоступен", ResponseType = typeof(void))]
    [Route("/password/login", "POST", Summary = "Логин с паролем", Notes = "Получение JWT токена через логин и пароль. Если пароля нет, то не пустит.")]
    public class AuthViaPassword : IReturn<TokenResult>
    {
        [ApiMember(IsRequired = true, Description = "Логин")]
        public string Login { get; set; }

        [ApiMember(IsRequired = true, Description = "Пароль")]
        public string Password { get; set; }
        
        [ApiMember(IsRequired = false, Description = "Секунд до истечения токена доступа, положительное число")]
        public long? AccessTokenExpirationPeriod { get; set; }

        [ApiMember(IsRequired = false, Description = "Секунд до истечения токена обновления, положительное число")]
        public long? RefreshTokenExpirationPeriod { get; set; }
    }

    [Api("Auth")]
    [Authenticate]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.Unauthorized, "Пользователь не авторизован", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.NotFound, "Пользователь не найден", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.Conflict, "Пароль уже задан", ResponseType = typeof(void))]
    [Route("/password", "POST", Summary = "Задать пароль", Notes = "Задание пароля если он не задан.")]
    public class CreatePassword : IReturn<PasswordChangeResult>
    {
        [ApiMember(IsRequired = true, Description = "Новый пароль")]
        public string NewPassword { get; set; }
    }

    [Api("Auth")]
    [Authenticate]
    [ApiResponse(HttpStatusCode.Unauthorized, "Токен неверный или сессия закрыта", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.NotFound, "Пользователь не найдены", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.ServiceUnavailable, "Сервис авторизации недоступен", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [Route("/token/refresh", "POST", Summary = "Обновление токена", Notes = "Получение новой пары токенов с помощью Refresh токена")]
    public class PostRefreshToken : IReturn<TokenResult>
    {
        [ApiMember(IsRequired = false, Description = "Секунд до истечения токена доступа, положительное число")]
        public long? AccessTokenExpirationPeriod { get; set; }

        [ApiMember(IsRequired = false, Description = "Секунд до истечения токена обновления, положительное число")]
        public long? RefreshTokenExpirationPeriod { get; set; }
    }
    
    [Api("Auth")]
    [Authenticate]
    [ApiResponse(HttpStatusCode.Unauthorized, "Токен неверный или сессия закрыта", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.NotFound, "Пользователь не найден", ResponseType = typeof(void))]
    [Route("/me", "GET", Summary = "Получить сведения своего профиля", Notes = "Возвращаются актуальные сведения из базы данных")]
    public class GetMyProfile : IReturn<MyUserProfileMold> { }

    [Api("Auth")]
    [ApiResponse(HttpStatusCode.BadRequest, "Неверно составлен запрос", ResponseType = typeof(void))]
    [ApiResponse(HttpStatusCode.ServiceUnavailable, "Сервис авторизации недоступен", ResponseType = typeof(void))]
    [Route("/register", "POST", Summary = "Создать пользователя", Notes = "Получение JWT токена через регистрацию нового пользователя.")]
    public class RegisterNewUser : IReturn<TokenResult>
    {
        [ApiMember(IsRequired = true, Description = "Логин")]
        public string Login { get; set; }

        [ApiMember(IsRequired = true, Description = "Пароль")]
        public string Password { get; set; }

        [ApiMember(IsRequired = false, Description = "Имя пользователя")]
        public string UserName { get; set; }

    }

}
