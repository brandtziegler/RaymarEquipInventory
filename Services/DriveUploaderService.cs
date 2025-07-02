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
        public async Task ClearImageFolderAsync(string custPath, string workOrderId)
        {
            // 🔐 Environment variable loader
            string GetEnv(string key)
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"Missing required environment variable: {key}");
                return value;
            }

            // 🔑 Build credential
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

            Log.Information("🧭 Resolving folder path for image cleanup...");

            string rootFolderId = "1adqdzJVDVqdMB6_MSuweBYG8nlr4ASVk";
            string[] pathSegments = custPath.Split('>');
            string? currentParentId = rootFolderId;

            foreach (var segment in pathSegments)
            {
                currentParentId = await TryResolveFolderAsync(segment.Trim(), currentParentId, driveService);
                if (currentParentId == null)
                {
                    Log.Warning($"❌ Folder segment '{segment}' not found. Aborting clear.");
                    return;
                }
            }

            var workOrderFolderId = await TryResolveFolderAsync(workOrderId, currentParentId, driveService);
            if (workOrderFolderId == null)
            {
                Log.Warning($"❌ Work order folder '{workOrderId}' not found. Nothing to clear.");
                return;
            }

            var imagesFolderId = await TryResolveFolderAsync("Images", workOrderFolderId, driveService);
            if (imagesFolderId == null)
            {
                Log.Warning("📭 'Images' folder not found. Nothing to clear.");
                return;
            }

            Log.Information("🧼 Fetching files in 'Images' folder for deletion...");

            var listRequest = driveService.Files.List();
            listRequest.Q = $"'{imagesFolderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name, owners)";
            var fileList = await listRequest.ExecuteAsync();

            if (fileList.Files.Count == 0)
            {
                Log.Information("📭 No files found to delete in 'Images' folder.");
                return;
            }

            foreach (var file in fileList.Files)
            {
                try
                {
                    var ownerEmail = file.Owners?.FirstOrDefault()?.EmailAddress ?? "unknown";

                    if (!ownerEmail.Contains("taskfuel-uploader"))
                    {
                        Log.Warning($"🚫 Skipping file not owned by service account: {file.Name} (Owner: {ownerEmail})");
                        continue;
                    }

                    await driveService.Files.Delete(file.Id).ExecuteAsync();
                    Log.Information($"🗑️ Deleted image file: {file.Name} (ID: {file.Id})");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"⚠️ Failed to delete image file: {file.Name}");
                }
            }

            Log.Information($"✅ Image folder cleanup complete. {fileList.Files.Count} file(s) processed.");
        }

        public async Task<DTOs.GoogleDriveFolderDTO> PrepareGoogleDriveFoldersAsync(string custPath, string workOrderId)
        {
            try
            {
                Log.Information($"📁 Preparing Google Drive folders for {custPath} → WorkOrder {workOrderId}");

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

                string rootFolderId = "1adqdzJVDVqdMB6_MSuweBYG8nlr4ASVk"; // Adjust if needed
                string[] pathSegments = custPath.Split('>');
                string currentParentId = rootFolderId;

                foreach (var segment in pathSegments)
                {
                    currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
                }

                string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
                string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
                string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);

                Log.Information($"📂 Folder prep complete → WO: {workOrderFolderId}, PDFs: {pdfFolderId}, Images: {imagesFolderId}");

                return new DTOs.GoogleDriveFolderDTO
                {
                    WorkOrderFolderId = workOrderFolderId,
                    PdfFolderId = pdfFolderId,
                    ImagesFolderId = imagesFolderId
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Error in PrepareGoogleDriveFoldersAsync.");
                throw;
            }
        }


        public async Task<List<FileUpload>> UploadFilesAsync(
    List<IFormFile> files,
    string workOrderId,
    string workOrderFolderId,
    string pdfFolderId,
    string imagesFolderId)
        {
            try
            {
                Log.Information($"📦 Uploading {files.Count} file(s) to pre-created Google Drive folders...");
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");

                List<FileUpload> newUploads = new();

                // Setup Drive Auth
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

                        // Only delete matching PDFs to avoid duplicates
                        if (ext == ".pdf")
                        {
                            var checkExisting = driveService.Files.List();
                            checkExisting.Q = $"name = '{file.FileName}' and '{targetFolderId}' in parents and trashed = false";
                            checkExisting.Fields = "files(id, name)";
                            var existing = await checkExisting.ExecuteAsync();

                            foreach (var match in existing.Files)
                            {
                                Log.Information($"🗑️ Deleting existing PDF: {match.Name} (ID: {match.Id})");
                                await driveService.Files.Delete(match.Id).ExecuteAsync();
                            }
                        }

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

                            Log.Information($"✅ Uploaded: {file.FileName}");
                        }
                    }
                    catch (Exception fileEx)
                    {
                        Log.Warning(fileEx, $"⚠️ Failed to upload file: {file.FileName}");
                    }
                }

                Log.Information("🎯 All uploads complete.");
                return newUploads;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Error during file upload.");
                throw;
            }
        }
        public async Task<List<FileUpload>> UploadFilesSingleAsync(List<IFormFile> files, string custPath, string workOrderId)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");
                List<FileUpload> newUploads = new List<FileUpload>();

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

                bool imageFolderCleared = false;

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

                        // 🧼 Clean-up only for PDFs
                        if (ext == ".pdf")
                        {
                            var checkExisting = driveService.Files.List();
                            checkExisting.Q = $"name = '{file.FileName}' and '{targetFolderId}' in parents and trashed = false";
                            checkExisting.Fields = "files(id, name)";
                            var existing = await checkExisting.ExecuteAsync();

                            foreach (var match in existing.Files)
                            {
                                Log.Information($"🗑️ Deleting existing PDF: {match.Name} (ID: {match.Id})");
                                await driveService.Files.Delete(match.Id).ExecuteAsync();
                            }
                        }

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
                throw;
            }
            catch (GoogleApiException apiEx)
            {
                Log.Error(apiEx, "❌ Google API error.");
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Unexpected error in UploadFilesAsync.");
                throw;
            }
        }



        private async Task<string?> TryResolveFolderAsync(string folderName, string parentId, DriveService driveService)
        {
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            var result = await listRequest.ExecuteAsync();

            return result.Files.FirstOrDefault()?.Id;
        }



        private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            // 🔍 Step 1: Check SQL cache only — no validation
            var cached = await _context.GoogleDriveFolders
                .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderId == parentId);

            if (cached != null)
            {
                Log.Information($"📦 SQL Cache Hit: {folderName} under {parentId} (ID: {cached.FolderId})");
                return cached.FolderId;
            }

            // 🔍 Step 2: Check Drive
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            var result = await listRequest.ExecuteAsync();

            if (result.Files.Count > 0)
            {
                var existingId = result.Files[0].Id;
                Log.Information($"📁 Found in Drive: {folderName} under {parentId} (ID: {existingId})");

                _context.GoogleDriveFolders.Add(new GoogleDriveFolder
                {
                    FolderName = folderName,
                    ParentFolderId = parentId,
                    FolderId = existingId,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return existingId;
            }

            // 🕒 Step 3: Delay and retry once
            await Task.Delay(750);
            var retryList = driveService.Files.List();
            retryList.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            retryList.Fields = "files(id)";
            var retryResult = await retryList.ExecuteAsync();

            if (retryResult.Files.Count > 0)
            {
                var foundAfterDelay = retryResult.Files[0].Id;
                Log.Warning($"⚠️ Found after delay: {folderName} (ID: {foundAfterDelay})");

                _context.GoogleDriveFolders.Add(new GoogleDriveFolder
                {
                    FolderName = folderName,
                    ParentFolderId = parentId,
                    FolderId = foundAfterDelay,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return foundAfterDelay;
            }

            // 🆕 Step 4: Create and cache
            Log.Information($"📂 Creating new folder: {folderName} under {parentId}");

            var newFolder = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            };

            var createRequest = driveService.Files.Create(newFolder);
            createRequest.Fields = "id";
            var created = await createRequest.ExecuteAsync();

            Log.Information($"✅ Created folder: {folderName} (ID: {created.Id})");

            _context.GoogleDriveFolders.Add(new GoogleDriveFolder
            {
                FolderName = folderName,
                ParentFolderId = parentId,
                FolderId = created.Id,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return created.Id;
        }



        private async Task<string> EnsureFolderBackupExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            // First attempt: check if folder already exists
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            var result = await listRequest.ExecuteAsync();

            if (result.Files.Count > 0)
            {
                Log.Information($"📁 Found existing folder '{folderName}' under parent '{parentId}' (ID: {result.Files[0].Id})");
                return result.Files[0].Id;
            }

            // Wait briefly for any in-flight folders being created in parallel
            await Task.Delay(750);

            // Second check after delay (Google Drive is eventually consistent)
            var retryList = driveService.Files.List();
            retryList.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            retryList.Fields = "files(id)";
            var retryResult = await retryList.ExecuteAsync();

            if (retryResult.Files.Count > 0)
            {
                Log.Warning($"⚠️ Found folder '{folderName}' after delay — race avoided (ID: {retryResult.Files[0].Id})");
                return retryResult.Files[0].Id;
            }

            // Still nothing — safe to create
            Log.Information($"📂 Creating folder '{folderName}' under parent '{parentId}'");

            var newFolder = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            };

            var createRequest = driveService.Files.Create(newFolder);
            createRequest.Fields = "id";
            var created = await createRequest.ExecuteAsync();

            Log.Information($"✅ Created folder '{folderName}' (ID: {created.Id})");
            return created.Id;
        }

    }

}

