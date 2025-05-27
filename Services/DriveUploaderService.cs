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

namespace RaymarEquipmentInventory.Services
{
    public class DriveUploaderService : IDriveUploaderService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        private readonly DriveService _driveService;
        public DriveUploaderService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "service-account.json");
            if (!System.IO.File.Exists(path))
            {
                throw new Exception($"service-account.json NOT FOUND at {path}");
            }

            var credential = GoogleCredential.FromFile(path)
                .CreateScoped(DriveService.ScopeConstants.Drive);

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "TaskFuelUploader"
            });
        }

        public async Task UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId)
        {
            string rootFolderId = "1adqdzJVDVqdMB6_MSuweBYG8nlr4ASVk";

            // Customer path: "Consbec>Site1" — already nicely formed
            string[] pathSegments = custPath.Split('>');

            string currentParentId = rootFolderId;

            foreach (var segment in pathSegments)
            {
                currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId);
            }

            string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId);

            string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId);
            string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId);

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

                var upload = _driveService.Files.Create(metadata, stream, file.ContentType);
                upload.Fields = "id, webViewLink";
                await upload.UploadAsync();
            }
        }


        private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId)
        {
            var listRequest = _driveService.Files.List();
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

            var createRequest = _driveService.Files.Create(newFolder);
            createRequest.Fields = "id";
            var created = await createRequest.ExecuteAsync();

            return created.Id;
        }







    }

}

