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
            Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
            Log.Information($"Machine Local Time: {DateTime.Now:O}");

            var jsonObject = new
            {
                type = Environment.GetEnvironmentVariable("GOOGLE_TYPE"),
                project_id = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID"),
                private_key_id = Environment.GetEnvironmentVariable("GOOGLE_PRIVATE_KEY_ID"),
                private_key = Environment.GetEnvironmentVariable("GOOGLE_PRIVATE_KEY")?.Replace("\\n", "\n"),
                client_email = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_EMAIL"),
                client_id = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID"),
                auth_uri = Environment.GetEnvironmentVariable("GOOGLE_AUTH_URI"),
                token_uri = Environment.GetEnvironmentVariable("GOOGLE_TOKEN_URI"),
                auth_provider_x509_cert_url = Environment.GetEnvironmentVariable("GOOGLE_AUTH_CERT_URL"),
                client_x509_cert_url = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_CERT_URL"),
                universe_domain = Environment.GetEnvironmentVariable("GOOGLE_UNIVERSE_DOMAIN"),
            };

            var json = JsonConvert.SerializeObject(jsonObject);

            var credential = GoogleCredential.FromJson(json)
                .CreateScoped(DriveService.ScopeConstants.Drive);

            var driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "TaskFuelUploader"
            });

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

            foreach (var file in files)
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
            }
        }

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

