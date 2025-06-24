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
using Google.Apis.Download;
using Microsoft.Data.SqlClient;
using Google.Apis.Upload;

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

        //public async Task<List<DTOs.FileMetadata>> ListFileUrlsAsync()
        //{
        //    try
        //    {
        //        Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
        //        Log.Information($"Machine Local Time: {DateTime.Now:O}");

        //        // Set up credentials (as you're already doing)
        //        string GetEnv(string key)
        //        {
        //            var value = Environment.GetEnvironmentVariable(key);
        //            if (string.IsNullOrWhiteSpace(value))
        //                throw new InvalidOperationException($"Missing required environment variable: {key}");
        //            return value;
        //        }

        //        var privateKeyLines = Enumerable.Range(1, 28)
        //            .Select(i => Environment.GetEnvironmentVariable($"GOOGLE_PRIVATE_KEY_{i}"))
        //            .Where(line => !string.IsNullOrWhiteSpace(line))
        //            .ToList();

        //        if (privateKeyLines.Count != 28)
        //            throw new InvalidOperationException($"Expected 28 lines of private key, but got {privateKeyLines.Count}.");

        //        var privateKeyCombined = string.Join("\n", privateKeyLines);
        //        var credential = new ServiceAccountCredential(
        //            new ServiceAccountCredential.Initializer(GetEnv("GOOGLE_CLIENT_EMAIL"))
        //            {
        //                ProjectId = GetEnv("GOOGLE_PROJECT_ID"),
        //                Scopes = new[] { DriveService.ScopeConstants.Drive }
        //            }.FromPrivateKey(privateKeyCombined)
        //        );

        //        var driveService = new DriveService(new BaseClientService.Initializer
        //        {
        //            HttpClientInitializer = credential,
        //            ApplicationName = "TaskFuelUploader"
        //        });

        //        // 1️⃣ Build the query
        //        var listRequest = driveService.Files.List();
        //        listRequest.Q = "'1drVKdt4x6KRV5UuLImHRkfcARfOo0PJ9' in parents and trashed=false";

        //        /* 2️⃣ Ask Drive for the extra metadata you want to expose */
        //        listRequest.Fields =
        //            "files(" +
        //                "id," +
        //                "name," +
        //                "description," +
        //                "modifiedTime," +                      // ⬅️ NEW
        //                "lastModifyingUser(displayName)," +    // ⬅️ NEW
        //                "mimeType," +
        //                "webContentLink," +
        //                "webViewLink" +
        //            ")";
        //        // 3️⃣ Execute
        //        var result = await listRequest.ExecuteAsync();

        //        if (result.Files == null || result.Files.Count == 0)
        //        {
        //            Log.Information("No files found in the folder.");
        //            return new List<DTOs.FileMetadata>();
        //        }
        //        var localZone = TimeZoneInfo.Local;

        //        /* 4️⃣ Map Google API objects to your DTO */
        //        var fileMetaDataList = result.Files.Select(file => new DTOs.FileMetadata
        //        {
        //            Id = file.Id,
        //            PDFName = file.Name,
        //            fileDescription = file.Description ?? string.Empty,   // populate the UI blurb
        //            dateLastEdited = file.ModifiedTimeDateTimeOffset.HasValue
        //            ? TimeZoneInfo.ConvertTime(file.ModifiedTimeDateTimeOffset.Value, localZone)
        //                .ToString("o")
        //            : string.Empty,
        //            lastEditTechName = file.LastModifyingUser?.DisplayName ?? "Unknown",
        //            MimeType = file.MimeType,
        //            WebContentLink = file.WebContentLink,                 // direct download
        //            WebViewLink = file.WebViewLink,                   // browser preview
        //            sheetId = 0
        //            // SheetId stays 0 by default
        //        }).ToList();

        //        return fileMetaDataList;
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "Error occurred while listing files");
        //        throw;
        //    }
        //}

        public async Task<List<DTOs.FileMetadata>> ListFileUrlsAsync(int sheetId)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");

                string GetEnv(string key)
                {
                    var value = Environment.GetEnvironmentVariable(key);
                    if (string.IsNullOrWhiteSpace(value))
                        throw new InvalidOperationException($"Missing required environment variable: {key}");
                    return value;
                }

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

                var listRequest = driveService.Files.List();
                listRequest.Q = "'1drVKdt4x6KRV5UuLImHRkfcARfOo0PJ9' in parents and trashed=false";
                listRequest.Fields = "files(id,name,description,modifiedTime,lastModifyingUser(displayName),mimeType,webContentLink,webViewLink)";

                var result = await listRequest.ExecuteAsync();
                if (result.Files == null || result.Files.Count == 0)
                {
                    Log.Information("No files found in the TechPDFs folder.");
                    return new List<DTOs.FileMetadata>();
                }

                var localZone = TimeZoneInfo.Local;
                var templates = result.Files.Select(file => new DTOs.FileMetadata
                {
                    Id = file.Id,
                    PDFName = file.Name,
                    fileDescription = file.Description ?? string.Empty,
                    dateLastEdited = file.ModifiedTimeDateTimeOffset.HasValue
                        ? TimeZoneInfo.ConvertTime(file.ModifiedTimeDateTimeOffset.Value, localZone).ToString("o")
                        : string.Empty,
                    lastEditTechName = file.LastModifyingUser?.DisplayName ?? "Unknown",
                    MimeType = file.MimeType,
                    WebContentLink = file.WebContentLink,
                    WebViewLink = file.WebViewLink,
                    sheetId = sheetId
                }).ToList();

                var filledDocs = await _context.Pdfdocuments
                    .Where(p => p.SheetId == sheetId)
                    .ToListAsync();

                if (sheetId != 0)
                {
                    foreach (var filled in filledDocs)
                    {
                        var match = templates.FirstOrDefault(t => t.PDFName == filled.FileName);
                        if (match != null)
                        {
                            match.WebContentLink = filled.FileUrl;
                            match.WebViewLink = filled.FileUrl;
                            match.fileDescription = string.IsNullOrWhiteSpace(filled.Description) ? match.fileDescription : filled.Description;
                            match.dateLastEdited = filled.UploadDate.ToString("o");
                            match.lastEditTechName = filled.UploadedBy ?? match.lastEditTechName;
                        }
                    }
                }


                return templates;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occurred while listing files");
                throw;
            }
        }

        public async Task UpdateFileUrlInPDFDocumentAsync(PDFUploadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.FileId))
                {
                    Log.Warning($"⚠️ Skipping update for '{request.FileName}' — fileId is null or empty.");
                    return;
                }

                string downloadUrl = $"https://drive.google.com/uc?export=download&id={request.FileId}";

                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == Convert.ToInt32(request.WorkOrderId))
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync();

                if (sheetId == null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {request.WorkOrderId}. Skipping update for '{request.FileName}'.");
                    return;
                }

                var existingPdf = await _context.Pdfdocuments
                    .FirstOrDefaultAsync(p => p.FileName == request.FileName && p.SheetId == sheetId);

                if (existingPdf != null)
                {
                    existingPdf.FileUrl = downloadUrl;
                    existingPdf.UploadedBy = request.UploadedBy;
                    existingPdf.Description = request.Description;
                    existingPdf.UploadDate = DateTime.UtcNow;
                    existingPdf.DriveFileId = request.FileId;

                    Log.Information($"🔁 Updated existing PDFDocument for '{request.FileName}' (SheetID: {sheetId})");
                }
                else
                {
                    _context.Pdfdocuments.Add(new Pdfdocument
                    {
                        SheetId = sheetId.Value,
                        FileName = request.FileName,
                        FileUrl = downloadUrl,
                        UploadedBy = request.UploadedBy,
                        Description = request.Description,
                        UploadDate = DateTime.UtcNow,
                        DriveFileId = request.FileId
                    });

                    Log.Information($"➕ Inserted new PDFDocument for '{request.FileName}' (SheetID: {sheetId})");
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception while updating PDFDocument for '{request.FileName}' (WO#: {request.WorkOrderId})");
            }
        }


        public async Task UpdateFileUrlInPartsDocumentAsync(string fileName, string fileId, string extension, string workOrderId)
        {
            try
            {
               
                if (string.IsNullOrWhiteSpace(fileId))
                {
                    Log.Warning($"⚠️ Skipping update for '{fileName}' — fileId is null or empty.");
                    return;
                }

                if (extension is not (".jpg" or ".jpeg" or ".png"))
                {
                    Log.Warning($"⚠️ Skipping update for '{fileName}' — unsupported extension '{extension}'.");
                    return;
                }

                string downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

                // Get SheetID from WorkOrderNumber
                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == Convert.ToInt32(workOrderId))
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync();

                if (sheetId == null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {workOrderId}. Skipping update for '{fileName}'.");
                    return;
                }

                // Get all PartUsedIds for this sheet
                var partUsedIds = await _context.PartsUseds
                    .Where(p => p.SheetId == sheetId.Value)
                    .Select(p => p.PartUsedId)
                    .ToListAsync();

                // Find matching PartsDocument
                var doc = await _context.PartsDocuments
                    .FirstOrDefaultAsync(p => p.FileName == fileName && partUsedIds.Contains(p.PartUsedId));

                if (doc == null)
                {
                    Log.Warning($"⚠️ No matching PartsDocument found for '{fileName}' tied to SheetID: {sheetId}");
                    return;
                }

                doc.FileUrl = downloadUrl;
                await _context.SaveChangesAsync();

                Log.Information($"🖼️ Updated FileUrl for '{fileName}' → (SheetID: {sheetId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception occurred while updating FileUrl for '{fileName}' (WO#: {workOrderId})");
            }
        }

        public async Task<List<FileUpload>> UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");
                List<FileUpload> newUploads = new List<FileUpload>();   
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
                //TechPDFs is 1drVKdt4x6KRV5UuLImHRkfcARfOo0PJ9....we will make another service endpoint soon to use that for DOWNLOADS
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
                        var uploadResult = await upload.UploadAsync();
                        
                        if (uploadResult.Status == UploadStatus.Completed)
                        {
                            newUploads.Add(new FileUpload
                            {
                                FileName = file.FileName,
                                Extension = ext,
                                ResponseBodyId = upload.ResponseBody?.Id,
                                WorkOrderId = workOrderId
                            });
                            //await UpdateFileUrlInPartsDocumentAsync(file.FileName, upload.ResponseBody?.Id, ext, workOrderId);
                            Log.Information($"✅ Uploaded file: {file.FileName}");
                        }

                        Log.Information($"✅ Uploaded file: {file.FileName}");
                    }
                    catch (Exception fileEx)
                    {
                        Log.Warning(fileEx, $"⚠️ Failed to upload file: {file.FileName}");
                        // Continue with the next file
                    }
                }

                Log.Information("🎯 All uploads complete.");
                return newUploads;
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

