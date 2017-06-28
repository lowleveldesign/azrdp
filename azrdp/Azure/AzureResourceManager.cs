using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop.Azure
{
    sealed class AzureException : Exception
    {
        public AzureException(HttpStatusCode httpCode, string message)
            : base($"{message} (http: {httpCode})") { }
    }

    sealed class AzureTooManyRequestsException : Exception
    {
    }

    sealed class AzureResourceManager
    {
        private const string ApiVersion = "2017-03-01";
        private const string JsonContentType = "application/json";
        private readonly AuthHelper auth = new AuthHelper();
        private readonly string subscriptionId;
        private readonly bool verboseLogging;

        public AzureResourceManager(string subscriptionId, bool verboseLogging)
        {
            this.subscriptionId = subscriptionId;
            this.verboseLogging = verboseLogging;
        }

        public string SubscriptionId
        {
            get { return subscriptionId; }
        }

        public async Task AuthenticateWithPrompt()
        {
            await auth.AcquireTokens();
        }

        public async Task<bool> HeadAsync(string azureRelativeUri, CancellationToken cancellationToken)
        {
            using (var client = await CreateAzureRestClient()) {
                using (var response = await client.GetAsync(PrepareAzureRestUrl(azureRelativeUri), cancellationToken)) {
                    if (response.StatusCode == HttpStatusCode.NoContent ||
                        response.StatusCode == HttpStatusCode.OK) {
                        return true;
                    }
                    Debug.Assert(response.StatusCode == HttpStatusCode.NotFound);
                    return false;
                }
            }
        }

        public async Task<JToken> GetAsync(string azureRelativeUri, CancellationToken cancellationToken)
        {
            using (var client = await CreateAzureRestClient()) {
                using (var response = await client.GetAsync(PrepareAzureRestUrl(azureRelativeUri), cancellationToken)) {
                    return await ParseResponse(response);
                }
            }
        }

        public async Task<JToken> PutAsync(string azureRelativeUri, string content, CancellationToken cancellationToken)
        {
            while (true) {
                var httpContent = new StringContent(content, Encoding.UTF8, JsonContentType);
                try {
                    using (var client = await CreateAzureRestClient()) {
                        using (var response = await client.PutAsync(PrepareAzureRestUrl(azureRelativeUri),
                            httpContent, cancellationToken)) {
                            return await ParseResponse(response);
                        }
                    }
                } catch (AzureTooManyRequestsException) {
                    Trace.TraceWarning("WARNING: retrying the request: {0}", azureRelativeUri);
                } finally {
                    httpContent.Dispose();
                }
            }
        }

        public async Task DeleteAsync(string azureRelativeUri, CancellationToken cancellationToken)
        {
            using (var client = await CreateAzureRestClient()) {
                using (var response = await client.DeleteAsync(PrepareAzureRestUrl(azureRelativeUri), cancellationToken)) {
                    if (response.StatusCode == HttpStatusCode.Accepted ||
                        response.StatusCode == HttpStatusCode.NoContent) {
                        return;
                    }
                    var msg = await response.Content.ReadAsStringAsync();
                    throw new AzureException(response.StatusCode, msg);
                }
            }
        }

        private async Task<HttpClient> CreateAzureRestClient()
        {
            var client = verboseLogging ? new HttpClient(new ARMClient.HttpLoggingHandler(
                new HttpClientHandler(), true)) : new HttpClient();
            var token = await auth.GetToken(subscriptionId);
            client.DefaultRequestHeaders.Add("Authorization", token.CreateAuthorizationHeader());
            client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent.Value);
            client.DefaultRequestHeaders.Add("Accept", Constants.JsonContentType);

            // FIXME: should I include this?
            //if (Utils.IsRdfe(uri)) {
            //    client.DefaultRequestHeaders.Add("x-ms-version", "2013-10-01");
            //}

            client.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());

            return client;
        }

        private Uri PrepareAzureRestUrl(string azureRelativeUri)
        {
            var uri = Utils.EnsureAbsoluteUri(azureRelativeUri);

            if (uri.Query == null || uri.Query.IndexOf("api-version=", StringComparison.OrdinalIgnoreCase) < 0) {
                var uriBuilder = new UriBuilder(uri);
                if (uriBuilder.Query != null && uriBuilder.Query.Length > 1) {
                    uriBuilder.Query = uriBuilder.Query.Substring(1) + $"&api-version={ApiVersion}";
                } else {
                    uriBuilder.Query = $"api-version={ApiVersion}";
                }
                uri = uriBuilder.Uri;
            }

            return uri;
        }

        private async Task<JToken> ParseResponse(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.Created) {
                using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync())) {
                    return JToken.ReadFrom(new JsonTextReader(reader));
                }
            }
            if ((int)response.StatusCode == 429) {
                throw new AzureTooManyRequestsException();
            }
            var msg = await response.Content.ReadAsStringAsync();
            throw new AzureException(response.StatusCode, msg);
        }
    }
}
