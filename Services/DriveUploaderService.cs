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

            var json = @"
{
""type"": ""service_account"",
  ""project_id"": ""taskfueluploader"",
  ""private_key_id"": ""4392043e7c33832fd20fd3bd75a1038e4b62122b"",
  ""private_key"": ""-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQDB9Y1MCzFQfSFn\nv17mIBiu3FrTuEUtyDFwMkAuHdE0p/6nXbu7t091H6cb8iJFZRYDWUkfgNlPs3Np\nJcCU/VvubeZWLOUM4xlKnF5cp0fT4YFbcLVJkAmgPNXX8EVMKpMyaM4cLlbbFolW\n5IeImL2LDY5NWzzUsdMr8Mce43zid4MvoCN/ltfUd/5nzqrZU+T/B6BFuFBh0gAY\neOTjK7UxAvo/Xm+4m6+s38ShOdwz7HwNH1hRyAC+PYoFDUNGxfaaU19Hg/f87Mvn\nMmiPIsoPwMYqzqIyZR+DO7/xF03Ci1e77bMpSABkyEQkzpA+GF36yW880UKslfR8\nT3iE2nV9AgMBAAECggEAEqBoAiBGC16E9X9rl+qHrE1FnLUC0b96vfVZdARRib7D\nZiPKil+zjgIs7HJfp63qRAaQTunzFKQdZoDlYnmFWVutBeQYWBk8Hc3wAvJeo8Kx\nh0xG4KpoPj7xY2Qz53lkOvBVMOAjX7VlmZZnTK8sbrztyFCFgkCUkND18mmy3dWk\n1srLqqWP3DE6WLB7zUQcBA4r/XAwGjFRjsTnYIVnY1l+icYTKawPDO1qSLbK7Ar5\nEvx773IXPy9MYY6tdJ7Jj8F5yuIWwbRCxTmMf6GfocxCvbGJwcC01Ju44utuN717\nn0NkkqBUV9VvtxxCOYPuR9gR00QFZpEFudk7K7pw4QKBgQD3f7WEgC8ucbAEL06n\nyVUYsNBPRGJfE62l4tc726wLBeBQaoMGmTSSBeMXLr+psDc4mCnRNNP8e98Ue8ja\nu4CzNoGOaFYwJYhj9XNKvBXBXRhUUXijkXSl7p46efStH0j8wjxSipwOqDvEsApj\nIN9FKUiP15x9rJB/E/VIiywwHQKBgQDInw+wJ6I2pAfoPt9lVX4cQZlfW+7CCQcb\nWjsKcIAqq/VyffW9g4lLWf145k6mHswG15q7zPQ7hpb5iIhdtliRAbrKr0nceN9R\nTIfr1lxpRjt0KL43hYP6/HzWx5aWTJzacRBkMDOblGQVwm/Sih1VjkNaxSe58Wlv\nzLhj+vUc4QKBgG9ziwIH1zdK6sB3rSvRdgiQVr3bRZEbA29YHyRNX8P2+XQ9ApPO\nGeZH0GN4IccAG13Y57vV1kA0Z9iJhYE6PlJ1kRHX2jgELs8UkL5uxD029uXBaln9\n/lFaitY6ZPwwwmVP8moZEP1otMF1pLfO7bvvQ0XDCi1tsAQJsMLiKRvhAoGAVNKT\nsnn/ZrTRtwsmLtUHvfCqZRwchaOFgrYSsmZAeko8O40wIlGD8fz5Y22UoT3yK45r\nGK4eMTDFknl8loqrRZwCmwGj6/ibCuedrEP0zHnqV0GGszjbRXoNWk4GyENaKi2V\nrZaHq2cBLgYIe27z2iGNLsqe8ko0txVKfNM1YWECgYEAhrnIgFC5sxhSqHwUeBFc\nnknDpflGgiyDlNsKhZ4l/N29ZSS33jWORtMqX1de7rV5ejmGSz2mVXSOCHxM6R8e\nJKfD6SL/HoQRgJKLN89nNxVDNERlOUSdFesaQFq/IOL+wAkuAbikjQUKmAA7eSPE\n/M10De6ZYS6Z9wX4flu+FcM=\n-----END PRIVATE KEY-----\n"",
  ""client_email"": ""taskfuel-uploader@taskfueluploader.iam.gserviceaccount.com"",
  ""client_id"": ""111742076022927988339"",
  ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
  ""token_uri"": ""https://oauth2.googleapis.com/token"",
  ""auth_provider_x509_cert_url"": ""https://www.googleapis.com/oauth2/v1/certs"",
  ""client_x509_cert_url"": ""https://www.googleapis.com/robot/v1/metadata/x509/taskfuel-uploader%40taskfueluploader.iam.gserviceaccount.com"",
  ""universe_domain"": ""googleapis.com""
}";

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

