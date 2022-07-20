using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ServiceStack;
using Unlimotion.Server.Domain;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceInterface
{
    public class SettingsService : Service
    {
        public IAsyncDocumentSession RavenSession { get; set; }
        public IMapper Mapper { get; set; }

        [Authenticate]
        public async Task<UserChatSettings> Get(GetMySettings request)
        {
            var session = Request.ThrowIfUnauthorized();

            var settingsId = session?.UserAuthId + "/settings";

            var typeMessage = await RavenSession.Query<Settings>().FirstOrDefaultAsync(x => x.Id == settingsId);

            if (typeMessage != null)
            {
                var settings = Mapper.Map<UserChatSettings>(typeMessage);
                return settings;
            }
            else
            {
                var settings = new Settings {SendingMessageByEnterKey = true, Id = settingsId };

                await RavenSession.StoreAsync(settings);
                await RavenSession.SaveChangesAsync();

                var settingsTypeMessage = Mapper.Map<UserChatSettings>(settings);
                return settingsTypeMessage;
            }
        }

        [Authenticate]
        public async Task<UserChatSettings> Post(SetSettings request)
        {
            var session = Request.ThrowIfUnauthorized();
            var settingsId = session?.UserAuthId + "/settings";

            var settings = Mapper.Map<Settings>(request);
            settings.Id = settingsId;

            await RavenSession.StoreAsync(settings);
            await RavenSession.SaveChangesAsync();

            var mapped = Mapper.Map<UserChatSettings>(settings);
            return mapped;
        }

        [Authenticate]
        public async Task<LoginHistory> Get(GetLoginAudit request)
        {
            var session = Request.ThrowIfUnauthorized();
            var loginAuditId = session?.UserAuthId + "/loginAudit";
            var сurrentSessionId = session?.Id;

            var history = new LoginHistory
            {
                History = await RavenSession.Advanced.Revisions.GetForAsync<UserLoginAudit>(loginAuditId),
                UniqueSessionUser = сurrentSessionId
            };
            return history;
        }
    }
}