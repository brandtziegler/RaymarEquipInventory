using Azure.Core;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Iam.Credentials.V1;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public class TokenExchangeServiceOld : ITokenExchangeService
    {
        private readonly ILogger<TokenExchangeServiceOld> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public TokenExchangeServiceOld(ILogger<TokenExchangeServiceOld> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<string> GetGoogleAccessTokenAsync()
        {
            string audience = Environment.GetEnvironmentVariable("GOOGLE_POOL_AUDIENCE")
                ?? throw new InvalidOperationException("GOOGLE_POOL_AUDIENCE is not set");
            string serviceAccountEmail = Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_EMAIL")
                ?? throw new InvalidOperationException("GOOGLE_SERVICE_ACCOUNT_EMAIL is not set");
            string scope = Environment.GetEnvironmentVariable("GOOGLE_SCOPE")
                ?? "https://www.googleapis.com/auth/drive";
            string tokenUrl = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URL")
                ?? "https://sts.googleapis.com/v1/token";

            var credential = new ManagedIdentityCredential(
                clientId: Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
            );
            var tokenRequestContext = new TokenRequestContext(
                new[] { "https://management.azure.com/.default" }
            );
            var azureToken = await credential.GetTokenAsync(tokenRequestContext);

            var client = _httpClientFactory.CreateClient();
            var body = new Dictionary<string, string>
    {
        { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
        { "audience", audience },
        { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" },
        { "requested_token_type", "urn:ietf:params:oauth:token-type:access_token" },
        { "subject_token", azureToken.Token },
        { "scope", scope }
    };

            var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(body));

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("Google token exchange failed: {Status} - {Error}", response.StatusCode, err);
                throw new ApplicationException($"Google token exchange failed: {response.StatusCode}\n{err}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);
            if (!result.RootElement.TryGetProperty("access_token", out var tokenElement))
            {
                _logger.LogError("access_token property not found in STS response: {Json}", json);
                throw new ApplicationException("access_token missing in STS response");
            }

            var accessToken = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _logger.LogError("access_token was null or empty in STS response");
                throw new ApplicationException("access_token returned as null or empty");
            }

            return accessToken;
        }


        //        public async Task<string> GetGoogleAccessTokenAsync()
        //        {
        //            string azureAudience = "https://management.azure.com/.default"; // Azure supports this
        //            string gcpAudience = Environment.GetEnvironmentVariable("GOOGLE_POOL_AUDIENCE")
        //                ?? throw new InvalidOperationException("GOOGLE_POOL_AUDIENCE is not set");
        //            string serviceAccountEmail = Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_EMAIL")
        //                ?? throw new InvalidOperationException("GOOGLE_SERVICE_ACCOUNT_EMAIL is not set");
        //            string scope = Environment.GetEnvironmentVariable("GOOGLE_SCOPE")
        //                ?? "https://www.googleapis.com/auth/drive";
        //            string tokenUrl = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URL")
        //                ?? "https://sts.googleapis.com/v1/token";

        //            var azureCred = new DefaultAzureCredential();
        //            var requestContext = new TokenRequestContext(
        //                new[] { "https://management.azure.com/.default" },
        //                tenantId: "eacb26db-086d-4556-874b-167804328df6"
        //            );
        //            var azureToken = await azureCred.GetTokenAsync(requestContext);

        //            var client = _httpClientFactory.CreateClient();

        //            var body = new Dictionary<string, string>
        //{
        //    { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
        //    { "audience", gcpAudience },
        //    { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" },
        //    { "requested_token_type", "urn:ietf:params:oauth:token-type:access_token" },
        //    { "subject_token", azureToken.Token },
        //    { "scope", scope }
        //};


        //            var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(body));

        //            if (!response.IsSuccessStatusCode)
        //            {
        //                var err = await response.Content.ReadAsStringAsync();
        //                _logger.LogError("🔥 Google STS token exchange failed!\nStatus: {StatusCode}\nBody: {ErrorBody}", response.StatusCode, err);
        //                throw new ApplicationException($"Google token exchange failed: {response.StatusCode}\n{err}");
        //            }

        //            var json = await response.Content.ReadAsStringAsync();
        //            var result = JsonDocument.Parse(json);
        //            if (!result.RootElement.TryGetProperty("access_token", out var tokenElement))
        //            {
        //                _logger.LogError("access_token property not found in STS response: {Json}", json);
        //                throw new ApplicationException("access_token missing in STS response");
        //            }

        //            var accessToken = tokenElement.GetString();
        //            if (string.IsNullOrWhiteSpace(accessToken))
        //            {
        //                _logger.LogError("access_token was null or empty in STS response");
        //                throw new ApplicationException("access_token returned as null or empty");
        //            }

        //            return accessToken;
        //        }

        //    public async Task<string> GetGoogleAccessTokenAsync()
        //    {
        //        string azureAudience = "https://management.azure.com/.default"; // Azure supports this
        //        string gcpAudience = Environment.GetEnvironmentVariable("GOOGLE_POOL_AUDIENCE")
        //            ?? throw new InvalidOperationException("GOOGLE_POOL_AUDIENCE is not set");
        //        string serviceAccountEmail = Environment.GetEnvironmentVariable("GOOGLE_SERVICE_ACCOUNT_EMAIL")
        //            ?? throw new InvalidOperationException("GOOGLE_SERVICE_ACCOUNT_EMAIL is not set");
        //        string scope = Environment.GetEnvironmentVariable("GOOGLE_SCOPE")
        //            ?? "https://www.googleapis.com/auth/drive";
        //        string tokenUrl = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URL")
        //            ?? "https://sts.googleapis.com/v1/token";

        //        var azureCred = new DefaultAzureCredential();
        //        var requestContext = new TokenRequestContext(new[] { azureAudience });
        //        var azureToken = await azureCred.GetTokenAsync(requestContext);

        //        var client = _httpClientFactory.CreateClient();

        //        var body = new Dictionary<string, string>
        //{
        //    { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
        //    { "audience", gcpAudience },
        //    { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" },
        //    { "subject_token", azureToken.Token },
        //    { "scope", scope }
        //};

        //        var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(body));

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            var err = await response.Content.ReadAsStringAsync();
        //            _logger.LogError("WIF Token Exchange failed: {Status} - {Error}", response.StatusCode, err);
        //            throw new ApplicationException("Google token exchange failed");
        //        }

        //        var json = await response.Content.ReadAsStringAsync();
        //        var result = JsonDocument.Parse(json);
        //        if (!result.RootElement.TryGetProperty("access_token", out var tokenElement))
        //        {
        //            _logger.LogError("access_token property not found in STS response: {Json}", json);
        //            throw new ApplicationException("access_token missing in STS response");
        //        }

        //        var accessToken = tokenElement.GetString();
        //        if (string.IsNullOrWhiteSpace(accessToken))
        //        {
        //            _logger.LogError("access_token was null or empty in STS response");
        //            throw new ApplicationException("access_token returned as null or empty");
        //        }

        //        return accessToken;
        //    }


        public async Task<DriveService> GetDriveServiceAsync()
        {
            var token = await GetGoogleAccessTokenAsync();

            var credential = GoogleCredential.FromAccessToken(token);
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "TaskFuelUploader"
            });
        }
    }
}