using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using ServiceStack;
using ServiceStack.Auth;
using Unlimotion.Domain;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceInterface
{
    public class AuthService : Service
    {
        private const string RefreshPermission = "refresh";
        private const string UserPrefix = "User/";
        private const string SecretPostfix = "/secret";

        public IAsyncDocumentSession RavenSession { get; set; }
        public IMapper Mapper { get; set; }

        #region Методы эндпоинтов
        public async Task<TokenResult> Post(RegisterNewUser request)
        {
            var login = request.Login.ToLowerInvariant();

            var user = await GetUserByLogin(login);
            if (user != null)
                throw new HttpError(HttpStatusCode.BadRequest, "Пользователь с таким логином уже существует");
            if (string.IsNullOrWhiteSpace(request.Password))
                throw new HttpError(HttpStatusCode.BadRequest, "Пароль не может быть пустым");

            user = await CreateUser(login, request.UserName, request.Password );

            var tokenResult = await GenerateToken(user);

            return tokenResult;
        }
        
        public async Task<TokenResult> Post(AuthViaPassword request)
        {
            var login = request.Login.ToLowerInvariant();

            var user = await GetUserByLogin(login);
            if (user == null)
                throw new HttpError(HttpStatusCode.NotFound, "User is not found");

            var secret = await GetUserSecret(user.Id);

            if (string.IsNullOrWhiteSpace(secret?.Password))
                throw new HttpError(HttpStatusCode.NotFound, "Password is not set");

            if (secret.Password != Hashing.CreateHashPassword(request.Password, secret.Salt))
                throw new HttpError(HttpStatusCode.NotFound, "Password is wrong");

            var customAccessExpire = (request.AccessTokenExpirationPeriod.HasValue && request.AccessTokenExpirationPeriod >= 0)
                ? TimeSpan.FromSeconds(request.AccessTokenExpirationPeriod.Value) : (TimeSpan?)null;
            var customRefreshExpire = (request.RefreshTokenExpirationPeriod.HasValue && request.RefreshTokenExpirationPeriod >= 0)
                ? TimeSpan.FromSeconds(request.RefreshTokenExpirationPeriod.Value) : (TimeSpan?)null;

            var tokenResult = await GenerateToken(user, customAccessExpire, customRefreshExpire);

            return tokenResult;
        }

        [Authenticate]
        public async Task<TokenResult> Post(PostRefreshToken request)
        {
            var session = Request.ThrowIfUnauthorized();
            var uid = session.UserAuthId;

            User user = await GetUserById(uid);

            if (user == null)
                throw new HttpError(HttpStatusCode.NotFound, $"User {uid} is not found");

            var customAccessExpire =
                (request.AccessTokenExpirationPeriod.HasValue && request.AccessTokenExpirationPeriod >= 0)
                    ? TimeSpan.FromSeconds(request.AccessTokenExpirationPeriod.Value)
                    : (TimeSpan?)null;

            var customRefreshExpire =
                (request.RefreshTokenExpirationPeriod.HasValue && request.RefreshTokenExpirationPeriod >= 0)
                    ? TimeSpan.FromSeconds(request.RefreshTokenExpirationPeriod.Value)
                    : (TimeSpan?)null;

            var tokenResult = await GenerateToken(user, customAccessExpire, customRefreshExpire);

            if (tokenResult == null)
                throw new HttpError(HttpStatusCode.Conflict, "Roles is not setted");

            return tokenResult;
        }

        [Authenticate]
        public async Task<PasswordChangeResult> Post(CreatePassword request)
        {
            var session = Request.ThrowIfUnauthorized();

            var uid = session?.UserAuthId;
            if (uid == null)
                throw new HttpError(HttpStatusCode.Unauthorized);

            var user = await GetUserById(uid);
            if (user == null)
                throw new HttpError(HttpStatusCode.NotFound);

            var secret = await GetUserSecret(uid);
            if (secret.Password != null)
                throw new HttpError(HttpStatusCode.Conflict);

            secret.Password = request.NewPassword;

            await RavenSession.StoreAsync(secret);
            await RavenSession.SaveChangesAsync();

            return new PasswordChangeResult { Result = PasswordChangeResult.ChangeEnum.Created };
        }

        [Authenticate]
        public async Task<MyUserProfileMold> Get(GetMyProfile request)
        {
            var session = Request.ThrowIfUnauthorized();

            var uid = session?.UserAuthId;
            if (uid == null)
                throw new HttpError(HttpStatusCode.Unauthorized);

            var me = await GetUserById(uid);
            if (me == null)
                throw new HttpError(HttpStatusCode.NotFound);

            var secret = await GetUserSecret(uid);

            var profile = Mapper.Map<MyUserProfileMold>(me);
            profile.IsPasswordSetted = secret?.Password != null;

            return profile;
        }

        #endregion

        #region Внутренние методы
        /// <summary>
        /// Генерация пары токенов для пользователя
        /// </summary>
        /// <returns>Пара токенов: Access и Refresh</returns>
        internal async Task<TokenResult> GenerateToken(User user, TimeSpan? customAccessExpire = null, TimeSpan? customRefreshExpire = null)
        {
            //var user = await UserRepository.GetWithIncludesAsync(uid);
            var sessionId = Guid.NewGuid().ToString();

            var token = new TokenResult();

            var jwtProvider = (JwtAuthProvider)AuthenticateService.GetAuthProvider("jwt");
            if (jwtProvider?.PublicKey == null)
                throw new HttpError(HttpStatusCode.ServiceUnavailable, "AuthProvider does not work");

            var defaultAccessExpire = jwtProvider.ExpireTokensIn;
            //Можно запросить время жизни токена не больше чем 'defaultAccessExpire'
            var accExpSpan = (customAccessExpire ?? defaultAccessExpire) >= defaultAccessExpire
                ? defaultAccessExpire
                : customAccessExpire.Value;

            var body = JwtAuthProvider.CreateJwtPayload(new AuthUserSession
            {
                UserAuthId = user.Id,
                CreatedAt = DateTime.UtcNow,
                DisplayName = user.Login,
            },
                issuer: jwtProvider.Issuer, expireIn: accExpSpan);

            body["useragent"] = Request.UserAgent;
            body["session"] = sessionId;

            token.AccessToken = JwtAuthProvider.CreateEncryptedJweToken(body, jwtProvider.PublicKey.Value);
            token.ExpireTime = DateTimeOffset.Now.Add(accExpSpan);


            var defaultRefreshExpire = jwtProvider.ExpireRefreshTokensIn;
            var refExpSpan = (customRefreshExpire ?? defaultRefreshExpire) >= defaultRefreshExpire
                ? defaultRefreshExpire
                : customRefreshExpire.Value;

            var refreshBody = JwtAuthProvider.CreateJwtPayload(new AuthUserSession
            {
                UserAuthId = user.Id,
                DisplayName = user.Login,
                CreatedAt = DateTime.UtcNow,
                Permissions = new List<string> { RefreshPermission },
            },
                issuer: jwtProvider.Issuer, expireIn: refExpSpan);

            refreshBody["useragent"] = Request.UserAgent;
            refreshBody["session"] = body["session"];

            token.RefreshToken = JwtAuthProvider.CreateEncryptedJweToken(refreshBody, jwtProvider.PublicKey.Value);

            return token;
        }

        private async Task<UserSecret> GetUserSecret(string uid)
        {
            var secret = await RavenSession.LoadAsync<UserSecret>(uid + SecretPostfix);
            return secret;
        }       
        
        private async Task<User> GetUserById(string uid)
        {
            return await RavenSession.LoadAsync<User>(uid, b => b.IncludeDocuments(uid + SecretPostfix));
        }

        private async Task<User> GetUserByLogin(string login)
        {
            var user = await RavenSession.Query<User>().Where(x => x.Login == login).Include(x => x.Id + SecretPostfix)
                .FirstOrDefaultAsync();
            return user;
        }

        private async Task<User> CreateUser(string login, string userName, string password = null)
        {
            var uid = $"{UserPrefix}{Guid.NewGuid()}";
            var user = new User
            {
                Id = uid,
                Login = login,
                DisplayName = String.IsNullOrWhiteSpace(userName)? null: userName,
                RegisteredTime = DateTimeOffset.UtcNow,
            };
            await RavenSession.StoreAsync(user);

            if (password!=null)
            {
                byte[] salt = Hashing.CreateSalt();

                var secret = new UserSecret
                {
                    Id = uid+SecretPostfix,
                    Salt = salt,
                    Password = Hashing.CreateHashPassword(password, salt),
                };
                await RavenSession.StoreAsync(secret);
            }
            
            await RavenSession.SaveChangesAsync();
            return user;
        }

        #endregion
    }
}
