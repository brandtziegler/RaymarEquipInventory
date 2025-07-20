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
using Google.Apis.Drive.v3.Data; // 👈 This is where `Permission` lives
using Google.Cloud.Iam.Credentials.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Auth;
using System.Diagnostics;

namespace RaymarEquipmentInventory.Services
{
    public class DriveUploaderService : IDriveUploaderService
    {
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        private readonly IDriveAuthService _authService;
        private readonly IConfiguration _config;
        public DriveUploaderService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context, IDriveAuthService authService, IConfiguration config)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _authService = authService;
            _config = config;
        }


    

        public async Task<List<DTOs.FileMetadata>> ListFileUrlsAsync(int sheetId)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");

                // 🔐 Use OAuth2 via DriveAuthService
                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                var listRequest = driveService.Files.List();
                var templatesFolderId = _config["GoogleDrive:TemplatesFolderId"]?? throw new InvalidOperationException("Missing config: GoogleDrive:TemplatesFolderId");

                listRequest.Q = $"'{templatesFolderId}' in parents and trashed=false";
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
                            match.fileDescription = string.IsNullOrWhiteSpace(filled.Description)
                                ? match.fileDescription
                                : filled.Description;
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
            var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

            Log.Information("🧭 Resolving folder path for image cleanup...");

            string rootFolderId = _config["GoogleDrive:RootFolderId"]
            ?? throw new InvalidOperationException("Missing required config: GoogleDrive:RootFolderId");

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

                    // ⚠️ With OAuth, you may not need to check this anymore.
                    // But if you're still enforcing only-your-files deletion:
                    //if (!ownerEmail.Contains("raymarequip@gmail.com"))
                    //{
                    //    Log.Warning($"🚫 Skipping file not owned by authorized user: {file.Name} (Owner: {ownerEmail})");
                    //    continue;
                    //}

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
            var debugLog = new List<string>();
            try
            {
                debugLog.Add($"📁 Preparing Google Drive folders for {custPath} → WorkOrder {workOrderId}");
                debugLog.Add($"⏱ Machine UTC: {DateTime.UtcNow:O}");
                debugLog.Add($"🧠 Local Time: {DateTime.Now:O}");

                debugLog.Add("🔐 Using OAuth2 encrypted token via DriveAuthService");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                debugLog.Add("🚀 DriveService initialized");

                string rootFolderId = _config["GoogleDrive:RootFolderId"]
                 ?? throw new InvalidOperationException("Missing required config: GoogleDrive:RootFolderId");
                string[] pathSegments = custPath.Split('>');
                string currentParentId = rootFolderId;

                foreach (var segment in pathSegments)
                {
                    debugLog.Add($"📂 EnsureFolderExists: {segment.Trim()} under {currentParentId}");
                    currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
                }

                await driveService.Permissions.Create(new Permission
                {
                    Type = "user",
                    Role = "writer",
                    EmailAddress = _config["GoogleDrive:SharedEmail"]
                }, currentParentId).ExecuteAsync();

                debugLog.Add($"👥 Granted 'writer' to email.");

                string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
                debugLog.Add($"📁 WorkOrder folder created: {workOrderFolderId}");

                string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
                debugLog.Add($"📁 PDFs folder created: {pdfFolderId}");

                string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);
                debugLog.Add($"📁 Images folder created: {imagesFolderId}");

                return new GoogleDriveFolderDTO
                {
                    WorkOrderFolderId = workOrderFolderId,
                    PdfFolderId = pdfFolderId,
                    ImagesFolderId = imagesFolderId,
                    stupidLogErrors = debugLog
                };
            }
            catch (Exception ex)
            {
                debugLog.Add($"💥 EXCEPTION: {ex.Message}");
                debugLog.Add($"🧵 STACK TRACE: {ex.StackTrace}");
                Log.Error(ex, "❌ Error in PrepareGoogleDriveFoldersAsync.");

                return new GoogleDriveFolderDTO
                {
                    WorkOrderFolderId = "",
                    PdfFolderId = "",
                    ImagesFolderId = "",
                    stupidLogErrors = debugLog,
                    HasCriticalError = true
                };
            }
        }
     

        private static string GetMimeTypeFromExtension(string filename)
        {
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }

        public async Task<List<FileUpload>> UploadFilesAsync(
      List<IFormFile> files,
      string workOrderId,
      string workOrderFolderId,
      string pdfFolderId,
      string imagesFolderId)
        {
            var newUploads = new List<FileUpload>();
            var encFilePath = Path.Combine(AppContext.BaseDirectory, "raymaroutgoing.json.enc");

            try
            {
                Log.Information($"📦 Uploading {files.Count} file(s) to Google Drive...");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                foreach (var file in files)
                {
                    var logDump = new FileUpload
                    {
                        FileName = file.FileName,
                        Extension = Path.GetExtension(file.FileName)?.ToLower() ?? "",
                        WorkOrderId = workOrderId,
                        stupidLogErrors = new List<string>()
                    };

                    try
                    {
                        string ext = logDump.Extension;
                        string targetFolderId = ext switch
                        {
                            ".pdf" => pdfFolderId,
                            ".jpg" or ".jpeg" or ".png" => imagesFolderId,
                            _ => workOrderFolderId
                        };

                        logDump.stupidLogErrors.Add($"📦 Routing to folder ID: {targetFolderId}");

                        using var stream = file.OpenReadStream();
                        logDump.stupidLogErrors.Add($"📄 File: {file.FileName} | Length: {file.Length}");

                        if (ext == ".pdf")
                        {
                            var checkExisting = driveService.Files.List();
                            checkExisting.Q = $"name = '{file.FileName}' and '{targetFolderId}' in parents and trashed = false";
                            checkExisting.Fields = "files(id, name)";
                            var existing = await checkExisting.ExecuteAsync();

                            logDump.stupidLogErrors.Add($"🧹 Found {existing.Files.Count} matching PDF(s) to delete");

                            foreach (var match in existing.Files)
                            {
                                logDump.stupidLogErrors.Add($"🗑️ Deleting: {match.Name} (ID: {match.Id})");
                                await driveService.Files.Delete(match.Id).ExecuteAsync();
                            }
                        }

                        var metadata = new Google.Apis.Drive.v3.Data.File
                        {
                            Name = file.FileName,
                            Parents = new List<string> { targetFolderId }
                        };

                        string contentType = string.IsNullOrWhiteSpace(file.ContentType)
                            ? GetMimeTypeFromExtension(file.FileName)
                            : file.ContentType;

                        stream.Position = 0;

                        var uploadReq = driveService.Files.Create(metadata, stream, contentType);
                        uploadReq.Fields = "id, webViewLink";

                        var progress = await uploadReq.UploadAsync();
                        logDump.stupidLogErrors.Add($"📥 Upload status: {progress.Status}");

                        if (uploadReq.ResponseBody == null)
                        {
                            logDump.stupidLogErrors.Add("⚠️ ResponseBody is null after upload");
                        }
                        else
                        {
                            logDump.ResponseBodyId = uploadReq.ResponseBody.Id;
                            logDump.stupidLogErrors.Add($"📎 File ID: {uploadReq.ResponseBody.Id}");
                            logDump.stupidLogErrors.Add($"🔗 WebViewLink: {uploadReq.ResponseBody.WebViewLink}");
                        }

                        if (progress.Exception != null)
                        {
                            logDump.stupidLogErrors.Add($"🚨 Google API Exception: {progress.Exception.Message}");
                            logDump.stupidLogErrors.Add($"🧵 {progress.Exception.StackTrace}");
                        }

                        if (progress.Status == UploadStatus.Completed)
                        {
                            logDump.stupidLogErrors.Add($"✅ Upload completed: {file.FileName}");
                        }
                        else
                        {
                            logDump.stupidLogErrors.Add($"⚠️ Upload incomplete: {progress.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        var err = $"💥 File error: {ex.Message}";
                        logDump.stupidLogErrors.Add(err);
                        logDump.stupidLogErrors.Add($"🧵 Stack: {ex.StackTrace}");
                        Log.Warning(ex, err);
                    }

                    newUploads.Add(logDump);
                }

                return newUploads;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Fatal error during UploadFilesAsync");
                throw;
            }
        }




        private async Task<string?> TryResolveFolderAsync(string folderName, string parentId, DriveService driveService)
        {
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            listRequest.SupportsAllDrives = true;
            listRequest.IncludeItemsFromAllDrives = true;

            try
            {
                var result = await listRequest.ExecuteAsync();
                return result.Files.FirstOrDefault()?.Id;
            }
            catch (GoogleApiException ex)
            {
                Log.Warning(ex, $"⚠️ Failed to resolve folder '{folderName}' under '{parentId}'");
                return null;
            }
        }


        private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            var cached = await _context.GoogleDriveFolders
                .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderId == parentId);

            if (cached != null)
            {
                Log.Information($"📦 SQL Cache Hit: {folderName} under {parentId} (ID: {cached.FolderId})");
                return cached.FolderId;
            }

            // Helper to list folder from Drive
            async Task<string?> TryFindFolderOnDriveAsync(string name, string parent)
            {
                var request = driveService.Files.List();
                request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{name}' and '{parent}' in parents and trashed=false";
                request.Fields = "files(id)";
                request.SupportsAllDrives = true;
                request.IncludeItemsFromAllDrives = true;

                try
                {
                    var response = await request.ExecuteAsync();
                    return response.Files.FirstOrDefault()?.Id;
                }
                catch (Google.GoogleApiException ex)
                {
                    Log.Warning($"🔁 Drive API list failed for '{name}' under '{parent}': {ex.Message}");
                    return null;
                }
            }

            // Step 1: Try immediately
            var existingId = await TryFindFolderOnDriveAsync(folderName, parentId);
            if (existingId != null)
            {
                Log.Information($"📁 Found in Drive: {folderName} under {parentId} (ID: {existingId})");
                await CacheFolderAsync(folderName, parentId, existingId);
                return existingId;
            }

            // Step 2: Retry with jitter
            var retryDelay = new Random().Next(500, 850); // jittered retry
            Log.Debug($"🕒 Retrying after {retryDelay}ms delay for '{folderName}'");
            await Task.Delay(retryDelay);

            var retryId = await TryFindFolderOnDriveAsync(folderName, parentId);
            if (retryId != null)
            {
                Log.Warning($"⚠️ Found after retry: {folderName} (ID: {retryId})");
                await CacheFolderAsync(folderName, parentId, retryId);
                return retryId;
            }

            // Step 3: Create
            Log.Information($"📂 Creating new folder: {folderName} under {parentId}");

            var newFolder = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            };

            try
            {
                var createRequest = driveService.Files.Create(newFolder);
                createRequest.Fields = "id";
                createRequest.SupportsAllDrives = true;

                var created = await createRequest.ExecuteAsync();
                Log.Information($"✅ Created folder: {folderName} (ID: {created.Id})");

                await CacheFolderAsync(folderName, parentId, created.Id);
                return created.Id;
            }
            catch (Google.GoogleApiException ex)
            {
                Log.Error(ex, $"❌ Failed to create folder '{folderName}' under '{parentId}': {ex.Message}");
                throw;
            }
        }

        // Small helper to DRY up caching
        private async Task CacheFolderAsync(string name, string parentId, string folderId)
        {
            _context.GoogleDriveFolders.Add(new GoogleDriveFolder
            {
                FolderName = name,
                ParentFolderId = parentId,
                FolderId = folderId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
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
            await driveService.Permissions.Create(new Permission
            {
                Type = "user",
                Role = "writer",
                EmailAddress = "taskfuel.files@gmail.com"
            }, created.Id).ExecuteAsync();
            return created.Id;
        }

    }

}

