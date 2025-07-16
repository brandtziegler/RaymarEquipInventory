using Azure.Core;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public class TokenExchangeService : ITokenExchangeService
    {
        private readonly ILogger<TokenExchangeService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public TokenExchangeService(ILogger<TokenExchangeService> logger, IHttpClientFactory httpClientFactory)
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

            var azureCred = new DefaultAzureCredential();
            var requestContext = new TokenRequestContext(new[] { audience });
            var azureToken = await azureCred.GetTokenAsync(requestContext);

            var client = _httpClientFactory.CreateClient();

            var body = new Dictionary<string, string>
            {
                { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
                { "audience", audience },
                { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" },
                { "subject_token", azureToken.Token },
                { "scope", scope }
            };

            var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(body));

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("WIF Token Exchange failed: {Status} - {Error}", response.StatusCode, err);
                throw new ApplicationException("Google token exchange failed");
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