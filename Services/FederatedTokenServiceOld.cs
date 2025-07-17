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
    public class FederatedTokenServiceOld : IFederatedTokenServiceOld
    {
        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _audience;
        private readonly string _serviceAccountEmail;
        private readonly string _scope;
        private readonly string _tokenUrl;
        private readonly string _privateKeyPem;

        public FederatedTokenServiceOld()
        {
            _tenantId = GetRequiredEnv("AZURE_TENANT_ID");
            _clientId = GetRequiredEnv("AZURE_CLIENT_ID");
            _audience = GetRequiredEnv("GOOGLE_POOL_AUDIENCE");
            _serviceAccountEmail = GetRequiredEnv("GOOGLE_SERVICE_ACCOUNT_EMAIL");
            _scope = Environment.GetEnvironmentVariable("GOOGLE_SCOPE") ?? "https://www.googleapis.com/auth/drive";
            _tokenUrl = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URL") ?? "https://sts.googleapis.com/v1/token";
            _privateKeyPem = GetRequiredEnv("AZURE_APP_PRIVATE_KEY");
        }

        private static string GetRequiredEnv(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Missing required environment variable: {key}");
            return value;
        }

        public async Task<string> GetGoogleAccessTokenAsync()
        {
            // Step 1: Get Azure-issued JWT using your federated App Registration
            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { _audience }));
            var jwt = token.Token;

            // Step 2: Exchange it for a Google access token via STS
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


        private string GenerateSignedJwt()
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(_privateKeyPem.ToCharArray());

            var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);

            var now = DateTimeOffset.UtcNow;

            var jwt = new JwtSecurityToken(
                issuer: $"https://login.microsoftonline.com/{_tenantId}/v2.0",
                audience: _audience,
                claims: new[]
                {
        new Claim("sub", _clientId),
        new Claim("jti", Guid.NewGuid().ToString()),
        new Claim("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
                },
                notBefore: now.UtcDateTime,
                expires: now.AddMinutes(10).UtcDateTime,
                signingCredentials: signingCredentials
            );


            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}