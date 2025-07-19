// Services/DriveAuthService.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System.Diagnostics;
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


        public string GetConfigValue(string key)
        {
            return _config[key] ?? "";
        }

        public async Task<DriveService> GetDriveServiceFromUserTokenAsync()
        {
            var encPath = Path.Combine(AppContext.BaseDirectory, "drive-user-token.json.enc");
            var encryptionPassword = _config["GoogleOAuth:TokenPassword"] ?? "";
            var clientId = _config["GoogleOAuth:ClientId"];
            var clientSecret = _config["GoogleOAuth:ClientSecret"];

            using var decryptProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "openssl",
                    Arguments = $"enc -aes-256-cbc -pbkdf2 -d -salt -pass pass:{encryptionPassword}",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };

            decryptProc.Start();

            using var encryptedStream = new FileStream(encPath, FileMode.Open, FileAccess.Read);
            await encryptedStream.CopyToAsync(decryptProc.StandardInput.BaseStream);
            decryptProc.StandardInput.Close();

            using var decryptedStream = new MemoryStream();
            await decryptProc.StandardOutput.BaseStream.CopyToAsync(decryptedStream);
            decryptedStream.Position = 0;

            var token = await JsonSerializer.DeserializeAsync<Google.Apis.Auth.OAuth2.Responses.TokenResponse>(decryptedStream);

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
    }
}