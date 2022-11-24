﻿using Autodesk.Forge;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Http;
using forgeSample.Controllers;
namespace forgeSample.Controllers
{
    public class OAuthController : ApiController
    { 
        private static dynamic InternalToken { get; set; }
        private static dynamic PublicToken { get; set; }
        /// <summary>
        /// Get access token with public (viewables:read) scope
        /// </summary>
        [HttpGet]
        [Route("api/forge/oauth/token")]
        public async Task<dynamic> GetPublicAsync()
        {
            if (PublicToken == null || PublicToken.ExpiresAt < DateTime.UtcNow)
            {
                PublicToken = await Credentials.Get2LeggedTokenAsync(new Scope[] { Scope.ViewablesRead });
                PublicToken.ExpiresAt = DateTime.UtcNow.AddSeconds(PublicToken.expires_in);
            }
            return PublicToken;
        }

       

        [HttpGet]
        [Route("api/forge/oauth")]
        public async Task<AccessToken> GetPublicTokenAsync()
        {
            Credentials credentials = await Credentials.FromSessionAsync();

            if (credentials == null)
            {
                var msg = new HttpResponseMessage(HttpStatusCode.Unauthorized) { ReasonPhrase = "No credentials were available at this session!" };
                throw new HttpResponseException(msg);
            }

            // return the public (viewables:read) access token
            return new AccessToken()
            {
                access_token = credentials.TokenPublic,
                expires_in = (int)credentials.ExpiresAt.Subtract(DateTime.Now).TotalSeconds
            };
        }


        /// <summary>
        /// Response for GetPublicToken
        /// </summary>
        public struct AccessToken
        {
            public string access_token { get; set; }
            public int expires_in { get; set; }
        }

        [HttpGet]
        [Route("api/forge/oauth/signout")]
        public HttpResponseMessage Singout()
        {
            // finish the session
            HttpContext.Current.Session.Abandon();

            // redirect to root
            HttpResponseMessage res = Request.CreateResponse(HttpStatusCode.Moved /* rorce redirect */);
            res.Headers.Location = new Uri("/", UriKind.Relative); // back to / (root, default)
            return res;
        }

        [HttpGet]
        [Route("api/forge/oauth/url")]
        public string GetOAuthURL()
        {
            // prepare the sign in URL
            Scope[] scopes = { Scope.DataRead };
            ThreeLeggedApi _threeLeggedApi = new ThreeLeggedApi();
            string oauthUrl = _threeLeggedApi.Authorize(
              Credentials.GetAppSetting("FORGE_CLIENT_ID"),
              oAuthConstants.CODE,
              Credentials.GetAppSetting("FORGE_CALLBACK_URL"),
              new Scope[] { Scope.DataRead, Scope.ViewablesRead });

            return oauthUrl;
        }

        [HttpGet]
         [Route("api/forge/callback/oauth")] // see Web.Config FORGE_CALLBACK_URL variable
        public async Task<HttpResponseMessage> OAuthCallbackAsync(string code)
        {
            // create credentials form the oAuth CODE
            Credentials credentials = await Credentials.CreateFromCodeAsync(code);

            // redirect to root
            HttpResponseMessage res = Request.CreateResponse(HttpStatusCode.Moved /* rorce redirect */);
            res.Headers.Location = new Uri("/", UriKind.Relative); // back to / (root, default)

            return res;
        }
    }

    /// <summary>
    /// Store data in session
    /// </summary>
    public class Credentials
    {
        private Credentials() { }
        private static dynamic InternalToken { get; set; }
        private static dynamic PublicToken { get; set; }
        public string TokenInternal { get; set; }
        public string TokenPublic { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Perform the OAuth authorization via code
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static async Task<Credentials> CreateFromCodeAsync(string code)
        {
            ThreeLeggedApi oauth = new ThreeLeggedApi();

            dynamic credentialInternal = await oauth.GettokenAsync(
              GetAppSetting("FORGE_CLIENT_ID"), GetAppSetting("FORGE_CLIENT_SECRET"),
              oAuthConstants.AUTHORIZATION_CODE, code, GetAppSetting("FORGE_CALLBACK_URL"));

            dynamic credentialPublic = await oauth.RefreshtokenAsync(
              GetAppSetting("FORGE_CLIENT_ID"), GetAppSetting("FORGE_CLIENT_SECRET"),
              "refresh_token", credentialInternal.refresh_token, new Scope[] { Scope.ViewablesRead });

            Credentials credentials = new Credentials();
            credentials.TokenInternal = credentialInternal.access_token;
            credentials.TokenPublic = credentialPublic.access_token;
            credentials.RefreshToken = credentialPublic.refresh_token;
            credentials.ExpiresAt = DateTime.Now.AddSeconds(credentialInternal.expires_in);

            HttpContext.Current.Session.Add("ForgeCredentials", JsonConvert.SerializeObject(credentials));

            return credentials;
        }

        /// <summary>
        /// Restore the credentials from the session object, refresh if needed
        /// </summary>
        /// <returns></returns>
        public static async Task<Credentials> FromSessionAsync()
        {
            if (HttpContext.Current.Session == null || HttpContext.Current.Session["ForgeCredentials"] == null)
                return null;

            Credentials credentials = JsonConvert.DeserializeObject<Credentials>(HttpContext.Current.Session["ForgeCredentials"].ToString());
            if (credentials.ExpiresAt < DateTime.Now) await credentials.RefreshAsync();
            return credentials;
        }
        public static async Task<dynamic> Get2LeggedTokenAsync(Scope[] scopes)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            string grantType = "client_credentials";
            dynamic bearer = await oauth.AuthenticateAsync(
              GetAppSetting("FORGE_CLIENT_ID"),
              GetAppSetting("FORGE_CLIENT_SECRET"),
              grantType,
              scopes);
            return bearer;
        }


        public static  async Task<dynamic> GetInternalAsync()
        {
            if (InternalToken == null || InternalToken.ExpiresAt < DateTime.UtcNow)
            {
                InternalToken = await Get2LeggedTokenAsync(new Scope[] { Scope.BucketCreate, Scope.BucketRead, Scope.DataRead, Scope.DataCreate });
                InternalToken.ExpiresAt = DateTime.UtcNow.AddSeconds(InternalToken.expires_in);
            }

            return InternalToken;
        }
        /// <summary>
        /// Refresh the credentials (internal & external)
        /// </summary>
        /// <returns></returns>
        private async Task RefreshAsync()
        {
            ThreeLeggedApi oauth = new ThreeLeggedApi();

            dynamic credentialInternal = await oauth.RefreshtokenAsync(
              GetAppSetting("FORGE_CLIENT_ID"), GetAppSetting("FORGE_CLIENT_SECRET"),
              "refresh_token", RefreshToken, new Scope[] { Scope.DataRead, Scope.ViewablesRead });

            dynamic credentialPublic = await oauth.RefreshtokenAsync(
              GetAppSetting("FORGE_CLIENT_ID"), GetAppSetting("FORGE_CLIENT_SECRET"),
              "refresh_token", credentialInternal.refresh_token, new Scope[] { Scope.ViewablesRead });

            Credentials credentials = new Credentials();
            credentials.TokenInternal = credentialInternal.access_token;
            credentials.TokenPublic = credentialPublic.access_token;
            credentials.RefreshToken = credentialPublic.refresh_token;
            credentials.ExpiresAt = DateTime.Now.AddSeconds(credentialInternal.expires_in);
        }

        /// <summary>
        /// Reads appsettings from web.config
        /// </summary>
        public static string GetAppSetting(string settingKey)
        {
            return WebConfigurationManager.AppSettings[settingKey];
        }
    }
}
