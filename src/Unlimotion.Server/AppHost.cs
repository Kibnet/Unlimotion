using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Funq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using ServiceStack;
using ServiceStack.Api.OpenApi;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Validation;
using Unlimotion.Server.ServiceInterface;

namespace Unlimotion.Server
{
    public class AppHost : AppHostBase
    {
        private readonly IAppSettings _settings;
        private readonly IWebHostEnvironment _env;
        /// <summary>
        /// Base constructor requires a Name and Assembly where web service implementation is located
        /// </summary>
        public AppHost(IAppSettings settings, IWebHostEnvironment env)
            : base("Unlimotion", typeof(AuthService).Assembly)
        {
            _settings = settings;
            _env = env;
        }

        /// <summary>
        /// Application specific configuration
        /// This method should initialize any IoC resources utilized by your web service classes.
        /// </summary>
        public override void Configure(Container container)
        {
            ServiceExceptionHandlers.Add((httpReq, request, exception) =>
            {
                httpReq.Items["__exception"] = exception;
                return new HttpError { Status = exception.ToStatusCode() };
            });

            //Handle Unhandled Exceptions occurring outside of Services
            //E.g. Exceptions during Request binding or in filters:
            UncaughtExceptionHandlers.Add((httpReq, res, operationName, exception) =>
            {
                //var logger = NLog.LogManager.GetCurrentClassLogger();
                //var builder = logger.Error().Exception(exception);
                //builder = builder.Property("operationName", operationName);
                //logger.Log(builder.LogEventInfo);
                if (res.StatusCode == 200)
                    res.StatusCode = exception.ToStatusCode();
                res.EndRequest();
            });
            
            //container.RegisterAutoWiredAs<MagicCodeRepository, IMagicCodeRepository>().ReusedWithin(ReuseScope.Request);
            
            //Validation
            //container.RegisterAllValidators<GetCheck>();
            Plugins.Add(new ValidationFeature());

            var hostConfig = new HostConfig
            {
                IgnoreFormatsInMetadata = new HashSet<string> { "xml", "csv", "jsv" },
                DefaultContentType = MimeTypes.Json,
            };
            hostConfig.MapExceptionToStatusCode.Add(typeof(CryptographicException), 401);
            SetConfig(hostConfig);

            Plugins.Add(new PostmanFeature
            {
                DefaultLabelFmt = new List<string> { "route", " - ", "type:english" },
            });
            Plugins.Add(new CorsFeature(allowedHeaders: "Content-Type,Authorization,x-client-version"));
            
            var privateKey = _settings.GetString("Security:PrivateKeyXml");//Получение закрытого ассиметричного ключа из конфига

            var requireSecureConnectionStr = _settings.GetString("Security:RequireSecureConnection");
            bool.TryParse(requireSecureConnectionStr, out var requireSecureConnection);

            var jwtprovider = new JwtAuthProvider(_settings)
            {
                RequireSecureConnection = requireSecureConnection,//требуется защищённое соединение
                HashAlgorithm = "RS512",
                PrivateKeyXml = privateKey,
                EncryptPayload = true,//шифрование токена
                ExpireTokensInDays = 1,//срок жизни токена
                ExpireRefreshTokensIn = TimeSpan.FromDays(30),
                AllowInQueryString = true,//токен можно передавать в параметрах как ?ss-tok
                AllowInFormData = true,//токен можно передавать в теле как ss-tok
                PopulateSessionFilter = (session, o, arg3) =>
                {
                    if (o.TryGetValue("session", out var sessionid))
                    {
                        if (session is AuthUserSession ses)
                            ses.Id = sessionid;
                    }
                }
            };
            if (_env.IsDevelopment())//при отладке
            {
                jwtprovider.RequireSecureConnection = false;//выключить требование защищённого соединения
            }

            jwtprovider.ServiceRoutes?.Clear();//Отключаю стандартные методы
            var authFeature = new AuthFeature(() => new AuthUserSession(),
                new IAuthProvider[]
                {
                    jwtprovider,
                })
            {
                IncludeAssignRoleServices = false,
                IncludeAuthMetadataProvider = false,
                IncludeRegistrationService = false,
            };
            authFeature.ServiceRoutes.Clear();//Отключаю стандартные методы
            Plugins.Add(authFeature);

            Plugins.Add(new OpenApiFeature { UseBearerSecurity = true });
        }
    }
}