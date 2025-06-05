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
using System.Text;

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


        public List<string> VerifyAndSplitPrivateKey()
        {
            var raw = Environment.GetEnvironmentVariable("GOOGLE_PRIVATE_KEY");

            if (string.IsNullOrWhiteSpace(raw))
            {
                Log.Error("🛑 GOOGLE_PRIVATE_KEY is null or empty.");
                return null;
            }

            Log.Information("🔍 Raw private key (first 80 chars): " + raw.Substring(0, Math.Min(80, raw.Length)));

            var cleaned = raw.Replace("\\n", "\n");

            // Validate delimiters
            if (!cleaned.StartsWith("-----BEGIN PRIVATE KEY-----") ||
                !cleaned.Trim().EndsWith("-----END PRIVATE KEY-----"))
            {
                Log.Error("❌ Cleaned key is missing BEGIN/END delimiters.");
                return null;
            }

            // Validate by trying to create a credential
            try
            {
                var credential = new ServiceAccountCredential(
                    new ServiceAccountCredential.Initializer("test@your-project.iam.gserviceaccount.com")
                    {
                        Scopes = new[] { DriveService.ScopeConstants.Drive }
                    }.FromPrivateKey(cleaned)
                );

                Log.Information("🎯 Credential creation succeeded.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "💥 Credential creation failed.");
                return null;
            }

            // Split into individual lines
            var lines = cleaned.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

            Log.Information($"✅ Private key validated and split. Line count: {lines.Count}");

            return lines;
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

   
              // Rebuild private key from split env vars
                var privateKeyLines = Enumerable.Range(1, 28)
                    .Select(i => Environment.GetEnvironmentVariable($"GOOGLE_PRIVATE_KEY_{i}"))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                if (privateKeyLines.Count != 28)
                    throw new InvalidOperationException($"Expected 28 lines of private key, but got {privateKeyLines.Count}.");

                var privateKeyCombined = string.Join("\n", privateKeyLines);


                var credential = new ServiceAccountCredential(
                    new ServiceAccountCredential.Initializer(GetEnv("GOOGLE_CLIENT_EMAIL"))
                    {
                        ProjectId = GetEnv("GOOGLE_PROJECT_ID"),
                        Scopes = new[] { DriveService.ScopeConstants.Drive }
                    }.FromPrivateKey(privateKeyCombined)
                );


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

