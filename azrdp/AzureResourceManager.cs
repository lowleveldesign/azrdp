using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop
{
    sealed class AzureException : Exception
    {
        public AzureException(HttpStatusCode httpCode, string message) 
            : base($"{message} (http: {httpCode})") { }
    }

    sealed class AzureResourceManager
    {
        private const string ApiVersion = "2017-05-10";
        private readonly AuthHelper auth = new AuthHelper();

        public AzureResourceManager() { }

        public async Task AuthenticateWithPrompt()
        {
            Logger.Log.TraceEvent(TraceEventType.Verbose, 0, "Acquiring user credentials");
            await auth.AcquireTokens();
        }

        public async Task<JToken> GetAsync(string azureRelativeUri, CancellationToken cancellationToken)
        {
            var uri = Utils.EnsureAbsoluteUri(azureRelativeUri);

            using (var client = new HttpClient())
            {
                var tokenId = auth.DumpTokenCache().First();  // FIXME make it nicer
                var token = await auth.GetToken(tokenId);
                client.DefaultRequestHeaders.Add("Authorization", token.CreateAuthorizationHeader());
                client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent.Value);
                client.DefaultRequestHeaders.Add("Accept", Constants.JsonContentType);

                if (Utils.IsRdfe(uri))
                {
                    client.DefaultRequestHeaders.Add("x-ms-version", "2013-10-01");
                }

                client.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());

                if (uri.Query == null || uri.Query.IndexOf("api-version=", StringComparison.OrdinalIgnoreCase) < 0) {
                    var uriBuilder = new UriBuilder(uri);
                    if (uriBuilder.Query != null && uriBuilder.Query.Length > 1) {
                        uriBuilder.Query = uriBuilder.Query.Substring(1) + $"&api-version={ApiVersion}";
                    } else {
                        uriBuilder.Query = $"api-version={ApiVersion}";
                    }
                    uri = uriBuilder.Uri;
                }

                using (var response = await client.GetAsync(uri, cancellationToken)) {
                    if (response.StatusCode != HttpStatusCode.OK) {
                        var msg = await response.Content.ReadAsStringAsync();
                        throw new AzureException(response.StatusCode, msg);
                    }
                    using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync())) {
                        return JToken.ReadFrom(new JsonTextReader(reader));
                    }
                }
            }
        }

    }
}
