using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;
using Serilog;
using Microsoft.AspNetCore.Server.IISIntegration;
using Google.Apis.Drive.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Newtonsoft.Json;
using Google;

namespace RaymarEquipmentInventory.Services
{
    public class DriveUploaderService : IDriveUploaderService
    {
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;

        public DriveUploaderService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }

        public async Task UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");

                // Gather environment variables with basic null checking
                string GetEnv(string key)
                {
                    var value = Environment.GetEnvironmentVariable(key);
                    if (string.IsNullOrWhiteSpace(value))
                        throw new InvalidOperationException($"Missing required environment variable: {key}");
                    return value;
                }

                var serviceType = GetEnv("GOOGLE_TYPE");
                var projectID = GetEnv("GOOGLE_PROJECT_ID");
                var privateKeyID = GetEnv("GOOGLE_PRIVATE_KEY_ID");
                var privateKey = GetEnv("GOOGLE_PRIVATE_KEY");
                var clientEmail = GetEnv("GOOGLE_CLIENT_EMAIL");
                var clientID = GetEnv("GOOGLE_CLIENT_ID");
                var authURI = GetEnv("GOOGLE_AUTH_URI");
                var tokenURI = GetEnv("GOOGLE_TOKEN_URI");
                var authProviderCertUrl = GetEnv("GOOGLE_AUTH_CERT_URL");
                var clientCertUrl = GetEnv("GOOGLE_CLIENT_CERT_URL");
                var universeDomain = GetEnv("GOOGLE_UNIVERSE_DOMAIN");

                var json = $@"
        {{
          ""type"": ""{serviceType}"",
          ""project_id"": ""{projectID}"",
          ""private_key_id"": ""{privateKeyID}"",
          ""private_key"": ""{privateKey}"",
          ""client_email"": ""{clientEmail}"",
          ""client_id"": ""{clientID}"",
          ""auth_uri"": ""{authURI}"",
          ""token_uri"": ""{tokenURI}"",
          ""auth_provider_x509_cert_url"": ""{authProviderCertUrl}"",
          ""client_x509_cert_url"": ""{clientCertUrl}"",
          ""universe_domain"": ""{universeDomain}""
        }}";

                Log.Information("Creating GoogleCredential from environment variables...");
                var credential = GoogleCredential.FromJson(json).CreateScoped(DriveService.ScopeConstants.Drive);

                var driveService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "TaskFuelUploader"
                });

                Log.Information("Ensuring folder structure exists...");
                string rootFolderId = "1adqdzJVDVqdMB6_MSuweBYG8nlr4ASVk";
                string[] pathSegments = custPath.Split('>');
                string currentParentId = rootFolderId;

                foreach (var segment in pathSegments)
                {
                    currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
                }

                string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
                string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
                string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);

                Log.Information($"Uploading {files.Count} file(s)...");

                foreach (var file in files)
                {
                    try
                    {
                        string ext = Path.GetExtension(file.FileName).ToLower();
                        string targetFolderId = ext switch
                        {
                            ".pdf" => pdfFolderId,
                            ".jpg" or ".jpeg" or ".png" => imagesFolderId,
                            _ => workOrderFolderId
                        };

                        using var stream = file.OpenReadStream();

                        var metadata = new Google.Apis.Drive.v3.Data.File
                        {
                            Name = file.FileName,
                            Parents = new List<string> { targetFolderId }
                        };

                        var upload = driveService.Files.Create(metadata, stream, file.ContentType);
                        upload.Fields = "id, webViewLink";
                        await upload.UploadAsync();

                        Log.Information($"✅ Uploaded file: {file.FileName}");
                    }
                    catch (Exception fileEx)
                    {
                        Log.Warning(fileEx, $"⚠️ Failed to upload file: {file.FileName}");
                        // Continue with the next file
                    }
                }

                Log.Information("🎯 All uploads complete.");
            }
            catch (InvalidOperationException envEx)
            {
                Log.Error(envEx, "❌ Missing environment configuration.");
                throw; // Rethrow so the caller knows it's a config issue
            }
            catch (GoogleApiException apiEx)
            {
                Log.Error(apiEx, "❌ Google API error.");
                throw; // Possibly log/report to external service if needed
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Unexpected error in UploadFilesAsync.");
                throw;
            }
        }


        //public async Task UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId)
        //{
        //    Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
        //    Log.Information($"Machine Local Time: {DateTime.Now:O}");

        //    var serviceType = Environment.GetEnvironmentVariable("GOOGLE_TYPE");
        //    var projectID = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID");
        //    var privateKeyID = Environment.GetEnvironmentVariable("GOOGLE_PRIVATE_KEY_ID");
        //    var privateKey = Environment.GetEnvironmentVariable("GOOGLE_PRIVATE_KEY")?.Replace("\n", "\\n");
        //    var clientEmail = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_EMAIL");
        //    var clientID = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        //    var authURI = Environment.GetEnvironmentVariable("GOOGLE_AUTH_URI");
        //    var tokenURI = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URI");
        //    var authProvidderCertUrl = Environment.GetEnvironmentVariable("GOOGLE_AUTH_PROVIDER_CERT_URL");
        //    var clientCertUrl = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_CERT_URL");
        //    var universeDomain = Environment.GetEnvironmentVariable("GOOGLE_UNIVERSE_DOMAIN");

        //    var json = $@"
        //    {{
        //      ""type"": ""{serviceType}"", ""project_id"": ""{projectID}"", ""private_key_id"": ""{privateKeyID}"", ""private_key"": ""{privateKey}"",
        //      ""client_email"": ""{clientEmail}"", ""client_id"": ""{clientID}"", ""auth_uri"": ""{authURI}"", ""token_uri"": ""{tokenURI}"",
        //      ""auth_provider_x509_cert_url"": ""{authProvidderCertUrl}"", ""client_x509_cert_url"": ""{clientCertUrl}"", ""universe_domain"": ""{universeDomain}""
        //    }}";

        //    var credential = GoogleCredential.FromJson(json)
        //        .CreateScoped(DriveService.ScopeConstants.Drive);

        //    var driveService = new DriveService(new BaseClientService.Initializer
        //    {
        //        HttpClientInitializer = credential,
        //        ApplicationName = "TaskFuelUploader"
        //    });

        //    string rootFolderId = "1adqdzJVDVqdMB6_MSuweBYG8nlr4ASVk";
        //    string[] pathSegments = custPath.Split('>');
        //    string currentParentId = rootFolderId;

        //    foreach (var segment in pathSegments)
        //    {
        //        currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
        //    }

        //    string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
        //    string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
        //    string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);

        //    foreach (var file in files)
        //    {
        //        string ext = Path.GetExtension(file.FileName).ToLower();
        //        string targetFolderId = ext switch
        //        {
        //            ".pdf" => pdfFolderId,
        //            ".jpg" or ".jpeg" or ".png" => imagesFolderId,
        //            _ => workOrderFolderId
        //        };

        //        using var stream = file.OpenReadStream();

        //        var metadata = new Google.Apis.Drive.v3.Data.File
        //        {
        //            Name = file.FileName,
        //            Parents = new List<string> { targetFolderId }
        //        };

        //        var upload = driveService.Files.Create(metadata, stream, file.ContentType);
        //        upload.Fields = "id, webViewLink";
        //        await upload.UploadAsync();
        //    }
        //}

        private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            var result = await listRequest.ExecuteAsync();

            if (result.Files.Count > 0)
                return result.Files[0].Id;

            var newFolder = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            };

            var createRequest = driveService.Files.Create(newFolder);
            createRequest.Fields = "id";
            var created = await createRequest.ExecuteAsync();

            return created.Id;
        }
    }

}

