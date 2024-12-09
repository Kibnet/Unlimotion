using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using ServiceStack;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds;

namespace Unlimotion.Server.ServiceInterface
{
    public class SettingsService : Service
    {
        public IAsyncDocumentSession RavenSession { get; set; }

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