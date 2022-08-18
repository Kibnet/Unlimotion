using System.Net;
using ServiceStack;
using ServiceStack.Web;

namespace Unlimotion.Server.ServiceInterface
{
    public static class RequestExtensions
    {
        public static AuthUserSession ThrowIfUnauthorized(this IRequest webRequest)
        {
            var session = webRequest.SessionAs<AuthUserSession>();
            var uid = session.UserAuthId;
            if (uid == null)
                throw new HttpError(HttpStatusCode.Unauthorized);
            return session;
        }
    }
}