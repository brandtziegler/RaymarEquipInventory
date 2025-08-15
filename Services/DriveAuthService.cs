using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RaymarEquipmentInventory.Services
{
    public class DriveAuthService : IDriveAuthService
    {
        private readonly IConfiguration _config;

        public DriveAuthService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<DriveService> GetDriveServiceFromUserTokenAsync()
        {
            try
            {
                // 🔐 Production-safe: uses injected config values
                var encPath = Path.Combine(AppContext.BaseDirectory, "drive-user-token.json.enc");

                var password = Environment.GetEnvironmentVariable("GoogleOAuth__TokenPassword") ?? ""
                    ?? throw new InvalidOperationException("Missing config: GoogleOAuth:TokenPassword");

                var clientId = Environment.GetEnvironmentVariable("GoogleOAuth__ClientId") 
                    ?? throw new InvalidOperationException("Missing config: GoogleOAuth:ClientId");

                var clientSecret = Environment.GetEnvironmentVariable("GoogleOAuth__ClientSecret")
                    ?? throw new InvalidOperationException("Missing config: GoogleOAuth:ClientSecret");

                using var encryptedStream = new FileStream(encPath, FileMode.Open, FileAccess.Read);
                using var decrypted = DecryptOpenSslAes256(encryptedStream, password);

                var token = await JsonSerializer.DeserializeAsync<TokenResponse>(decrypted);

                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    },
                    Scopes = new[] { DriveService.ScopeConstants.Drive }
                });

                var credential = new UserCredential(flow, "user", token);

                return new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "TaskFuelUploader"
                });
            }
            catch (Exception ex)
            {
                // Let it bubble up but with rich context
                throw new Exception("Failed to initialize Google Drive service. See inner exception for details.", ex);
            }
        }


        private static Stream DecryptOpenSslAes256(Stream inputStream, string password)
        {
            using var ms = new MemoryStream();
            inputStream.CopyTo(ms);
            var encrypted = ms.ToArray();

            // OpenSSL salt header: "Salted__" + 8-byte salt
            if (encrypted.Length < 16 || Encoding.ASCII.GetString(encrypted, 0, 8) != "Salted__")
                throw new InvalidDataException("Invalid OpenSSL salt header.");

            var salt = encrypted.Skip(8).Take(8).ToArray();
            var cipherBytes = encrypted.Skip(16).ToArray();

            using var keyDerive = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            var key = keyDerive.GetBytes(32);
            var iv = keyDerive.GetBytes(16);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var cipherStream = new MemoryStream(cipherBytes);
            using var cryptoStream = new CryptoStream(cipherStream, decryptor, CryptoStreamMode.Read);
            var output = new MemoryStream();
            cryptoStream.CopyTo(output);
            output.Position = 0;
            return output;
        }
    }
}
