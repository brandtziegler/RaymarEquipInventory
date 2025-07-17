using Azure.Core;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Google.Cloud.Iam.Credentials.V1;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;

namespace RaymarEquipmentInventory.Services
{
    public class FederatedTokenService : IFederatedTokenService
    {
        private readonly string _audience;
        private readonly string _scope;
        private readonly string _tokenUrl;

        public FederatedTokenService()
        {
            _audience = GetRequiredEnv("GOOGLE_POOL_AUDIENCE");
            _scope = Environment.GetEnvironmentVariable("GOOGLE_SCOPE") ?? "https://www.googleapis.com/auth/drive";
            _tokenUrl = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URL") ?? "https://sts.googleapis.com/v1/token";
        }

        private static string GetRequiredEnv(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Missing required environment variable: {key}");
            return value;
        }


        public async Task<(bool success, string message, string token)> TestAzureTokenAsync()
        {
            try
            {
                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = "0e0f9219-287b-4543-8621-42b2ba27b22f" // <-- your UAMI Client ID
                });

                var context = new TokenRequestContext(new[]
                {
            "https://management.azure.com/.default"
        });

                var token = await credential.GetTokenAsync(context);

                if (string.IsNullOrWhiteSpace(token.Token))
                {
                    return (false, "Token was returned as null or empty", null);
                }

                return (true, "Azure-issued token acquired successfully", token.Token);
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}", null);
            }
        }

        public async Task<string> GetGoogleAccessTokenAsync()
        {
            var credential = new ManagedIdentityCredential("0e0f9219-287b-4543-8621-42b2ba27b22f");

            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { _audience }));
            var jwt = token.Token;

            var body = new Dictionary<string, string>
    {
        { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
        { "audience", _audience },
        { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" },
        { "requested_token_type", "urn:ietf:params:oauth:token-type:access_token" },
        { "subject_token", jwt },
        { "scope", _scope }
    };

            using var client = new HttpClient();
            var response = await client.PostAsync(_tokenUrl, new FormUrlEncodedContent(body));

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                var exception = new HttpRequestException($"Google token exchange failed: {response.StatusCode}");
                exception.Data["Body"] = err;
                throw exception;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(json);

            if (!result.RootElement.TryGetProperty("access_token", out var tokenElement))
                throw new ApplicationException("access_token missing in STS response");

            var accessToken = tokenElement.GetString();

            if (string.IsNullOrWhiteSpace(accessToken))
                throw new ApplicationException("access_token returned as null or empty");

            return accessToken;
        }


        //public async Task<string> GetGoogleAccessTokenAsync()
        //{
        //    var credential = new DefaultAzureCredential();
        //    var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { _audience }));
        //    var jwt = token.Token;

        //    var body = new Dictionary<string, string>
        //    {
        //        { "grant_type", "urn:ietf:params:oauth:grant-type:token-exchange" },
        //        { "audience", _audience },
        //        { "subject_token_type", "urn:ietf:params:oauth:token-type:jwt" },
        //        { "requested_token_type", "urn:ietf:params:oauth:token-type:access_token" },
        //        { "subject_token", jwt },
        //        { "scope", _scope }
        //    };

        //    using var client = new HttpClient();
        //    var response = await client.PostAsync(_tokenUrl, new FormUrlEncodedContent(body));

        //    if (!response.IsSuccessStatusCode)
        //    {
        //        var err = await response.Content.ReadAsStringAsync();
        //        var exception = new HttpRequestException($"Google token exchange failed: {response.StatusCode}");
        //        exception.Data["Body"] = err;
        //        throw exception;
        //    }

        //    var json = await response.Content.ReadAsStringAsync();
        //    var result = JsonDocument.Parse(json);

        //    if (!result.RootElement.TryGetProperty("access_token", out var tokenElement))
        //        throw new ApplicationException("access_token missing in STS response");

        //    var accessToken = tokenElement.GetString();

        //    if (string.IsNullOrWhiteSpace(accessToken))
        //        throw new ApplicationException("access_token returned as null or empty");

        //    return accessToken;
        //}
    }
}