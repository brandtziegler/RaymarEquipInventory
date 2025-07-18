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

        //public async Task<List<DTOs.FileMetadata>> ListFileUrlsWIFAsync(int sheetId)
        //{
        //    try
        //    {
        //        Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
        //        Log.Information($"Machine Local Time: {DateTime.Now:O}");

        //        // ✅ Use ADC for WIF/Workspace auth
        //        var credential = await GoogleCredential
        //            .GetApplicationDefaultAsync()
        //            .ConfigureAwait(false);

        //        if (credential.IsCreateScopedRequired)
        //        {
        //            credential = credential.CreateScoped(DriveService.ScopeConstants.Drive);
        //        }

        //        var driveService = new DriveService(new BaseClientService.Initializer
        //        {
        //            HttpClientInitializer = credential,
        //            ApplicationName = "TaskFuelUploader"
        //        });

        //        // ✅ New PDF folder ID (TechPDFs)
        //        var listRequest = driveService.Files.List();
        //        listRequest.Q = "'1RzjU4YStj2oxtbMd5i2nbAv8yBvb5w5f' in parents and trashed=false";
        //        listRequest.Fields = "files(id,name,description,modifiedTime,lastModifyingUser(displayName),mimeType,webContentLink,webViewLink)";
        //        listRequest.SupportsAllDrives = true;
        //        listRequest.IncludeItemsFromAllDrives = true;

        //        var result = await listRequest.ExecuteAsync();
        //        if (result.Files == null || result.Files.Count == 0)
        //        {
        //            Log.Information("No files found in the TechPDFs folder.");
        //            return new List<DTOs.FileMetadata>();
        //        }

        //        var localZone = TimeZoneInfo.Local;
        //        var templates = result.Files.Select(file => new DTOs.FileMetadata
        //        {
        //            Id = file.Id,
        //            PDFName = file.Name,
        //            fileDescription = file.Description ?? string.Empty,
        //            dateLastEdited = file.ModifiedTimeDateTimeOffset.HasValue
        //                ? TimeZoneInfo.ConvertTime(file.ModifiedTimeDateTimeOffset.Value, localZone).ToString("o")
        //                : string.Empty,
        //            lastEditTechName = file.LastModifyingUser?.DisplayName ?? "Unknown",
        //            MimeType = file.MimeType,
        //            WebContentLink = file.WebContentLink,
        //            WebViewLink = file.WebViewLink,
        //            sheetId = sheetId
        //        }).ToList();

        //        var filledDocs = await _context.Pdfdocuments
        //            .Where(p => p.SheetId == sheetId)
        //            .ToListAsync();

        //        if (sheetId != 0)
        //        {
        //            foreach (var filled in filledDocs)
        //            {
        //                var match = templates.FirstOrDefault(t => t.PDFName == filled.FileName);
        //                if (match != null)
        //                {
        //                    match.WebContentLink = filled.FileUrl;
        //                    match.WebViewLink = filled.FileUrl;
        //                    match.fileDescription = string.IsNullOrWhiteSpace(filled.Description) ? match.fileDescription : filled.Description;
        //                    match.dateLastEdited = filled.UploadDate.ToString("o");
        //                    match.lastEditTechName = filled.UploadedBy ?? match.lastEditTechName;
        //                }
        //            }
        //        }

        //        return templates;
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

            string rootFolderId = "1ZFWivpkVhCF11yogNMRWV6zp23hwwDT7";
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

                //var privateKeyLines = Enumerable.Range(1, 28)
                //    .Select(i => Environment.GetEnvironmentVariable($"GOOGLE_PRIVATE_KEY_{i}"))
                //    .Where(line => !string.IsNullOrWhiteSpace(line))
                //    .ToList();


                var privateKeyLines = Enumerable.Range(1, 28)
    .Select(i => Environment.GetEnvironmentVariable($"GOOGLE_PRIVATE_KEY_{i}"))
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

                string rootFolderId = "1Hd4opYT_bV_JbQzMoEzDMxPdWGnjFXTl"; // Adjust if needed - "1ZFWivpkVhCF11yogNMRWV6zp23hwwDT7"
                string[] pathSegments = custPath.Split('>');
                string currentParentId = rootFolderId;
                await Task.Delay(500);
                foreach (var segment in pathSegments)
                {
                    currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
                }
                await driveService.Permissions.Create(new Permission
                {
                    Type = "user",
                    Role = "writer", // or "reader" if you prefer
                    EmailAddress = "taskfuel.files@gmail.com"
                }, currentParentId).ExecuteAsync();

                await Task.Delay(500);

                string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
                await Task.Delay(500);
                string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
                await Task.Delay(500);
                string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);
                await Task.Delay(500);

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
        //public async Task<GoogleDriveFolderDTO> PrepareGoogleDriveFoldersAsync(string custPath, string workOrderId)
        //{
        //    var dto = new GoogleDriveFolderDTO();

        //    try
        //    {
        //        Log.Information($"\ud83d\udcc1 Preparing Google Drive folders for {custPath} \u2192 WorkOrder {workOrderId}");

        //        // Log key env vars
        //        var envVars = new[]
        //        {
        //    "GOOGLE_CLOUD_PROJECT",
        //    "GOOGLE_WORKLOAD_IDENTITY_POOL",
        //    "GOOGLE_WORKLOAD_IDENTITY_PROVIDER",
        //    "GOOGLE_IMPERSONATE_SERVICE_ACCOUNT"
        //};

        //        foreach (var key in envVars)
        //        {
        //            string val = Environment.GetEnvironmentVariable(key) ?? "(null)";
        //            dto.stupidLogErrors.Add($"{key} = {val}");
        //        }

        //        // Load base credential from WIF
        //        GoogleCredential sourceCredential;
        //        try
        //        {
        //            sourceCredential = await GoogleCredential
        //                .GetApplicationDefaultAsync()
        //                .ConfigureAwait(false);

        //            dto.stupidLogErrors.Add($"[Source Cred Type] {sourceCredential.GetType().FullName}");
        //        }
        //        catch (Exception ex)
        //        {
        //            dto.stupidLogErrors.Add($"\u274c Failed to load base credential: {ex.Message}");
        //            Log.Error(ex, "\u274c Failed to load base WIF credential.");
        //            return dto;
        //        }

        //        // Impersonate the service account manually
        //        GoogleCredential impersonatedCredential;
        //        try
        //        {
        //            var saEmail = Environment.GetEnvironmentVariable("GOOGLE_IMPERSONATE_SERVICE_ACCOUNT") ?? "";
        //            dto.stupidLogErrors.Add($"[Impersonating SA] {saEmail}");

        //            var iamClient = new IAMCredentialsClientBuilder
        //            {
        //                TokenAccessMethod = ((ITokenAccess)sourceCredential.UnderlyingCredential).GetAccessTokenForRequestAsync
        //            }.Build();

        //            var tokenResponse = iamClient.GenerateAccessToken(new GenerateAccessTokenRequest
        //            {
        //                Name = $"projects/-/serviceAccounts/{saEmail}",
        //                Scope = { DriveService.Scope.Drive },
        //                Lifetime = Duration.FromTimeSpan(TimeSpan.FromMinutes(10))
        //            });

        //            impersonatedCredential = GoogleCredential.FromAccessToken(tokenResponse.AccessToken);
        //            dto.stupidLogErrors.Add($"[Token (manual WIF)] {tokenResponse.AccessToken.Substring(0, 30)}...");
        //        }
        //        catch (Exception ex)
        //        {
        //            dto.stupidLogErrors.Add($"\u274c Impersonation failed: {ex.Message}");
        //            Log.Error(ex, "\u274c Failed to impersonate SA.");
        //            return dto;
        //        }

        //        // Set up Drive API
        //        var driveService = new DriveService(new BaseClientService.Initializer
        //        {
        //            HttpClientInitializer = impersonatedCredential,
        //            ApplicationName = "TaskFuelUploader"
        //        });

        //        string raymarRootId = "1V13UNyx-eQE7ec24-Z3wPOinQyiNR-Ty";
        //        string[] pathSegments = custPath.Split('>');
        //        string currentParentId = raymarRootId;

        //        // Walk folder path
        //        foreach (var segment in pathSegments)
        //        {
        //            try
        //            {
        //                currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
        //            }
        //            catch (Exception segEx)
        //            {
        //                dto.stupidLogErrors.Add($"\u274c Failed creating folder segment '{segment}': {segEx.Message}");
        //                Log.Error(segEx, $"\u274c Folder segment failure: {segment}");
        //                return dto;
        //            }
        //        }

        //        // Final folder structure
        //        try
        //        {
        //            dto.WorkOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
        //            dto.PdfFolderId = await EnsureFolderExistsAsync("PDFs", dto.WorkOrderFolderId, driveService);
        //            dto.ImagesFolderId = await EnsureFolderExistsAsync("Images", dto.WorkOrderFolderId, driveService);
        //        }
        //        catch (Exception finalEx)
        //        {
        //            dto.stupidLogErrors.Add($"\u274c Final folder structure failed: {finalEx.Message}");
        //            Log.Error(finalEx, "\u274c Final folder creation step failed.");
        //        }

        //        Log.Information($"\ud83d\udcc2 Folder prep complete \u2192 WO: {dto.WorkOrderFolderId}, PDFs: {dto.PdfFolderId}, Images: {dto.ImagesFolderId}");
        //    }
        //    catch (Exception ex)
        //    {
        //        var err = $"\ud83d\udd25 UNHANDLED EXCEPTION: {ex.Message}";
        //        Log.Error(ex, err);
        //        dto.stupidLogErrors.Add(err);
        //    }

        //    return dto;
        //}


        //public async Task<GoogleDriveFolderDTO> PrepareGoogleDriveFoldersAsync(string custPath, string workOrderId)
        //{
        //    var dto = new GoogleDriveFolderDTO();

        //    try
        //    {
        //        Log.Information($"📁 Preparing Google Drive folders for {custPath} → WorkOrder {workOrderId}");

        //        // Log env vars
        //        var envVars = new[]
        //        {
        //    "GOOGLE_CLOUD_PROJECT",
        //    "GOOGLE_WORKLOAD_IDENTITY_POOL",
        //    "GOOGLE_WORKLOAD_IDENTITY_PROVIDER",
        //    "GOOGLE_IMPERSONATE_SERVICE_ACCOUNT"
        //};

        //        foreach (var key in envVars)
        //        {
        //            string val = Environment.GetEnvironmentVariable(key) ?? "(null)";
        //            dto.stupidLogErrors.Add($"{key} = {val}");
        //        }

        //        // Grab default credential
        //        var credential = await GoogleCredential
        //            .GetApplicationDefaultAsync()
        //            .ConfigureAwait(false);

        //        dto.stupidLogErrors.Add($"[Cred Type] {credential.GetType().FullName}");

        //        if (credential.IsCreateScopedRequired)
        //        {
        //            credential = credential.CreateScoped(DriveService.ScopeConstants.Drive);
        //            dto.stupidLogErrors.Add("[Scope Applied] DriveService.ScopeConstants.Drive");
        //        }

        //        try
        //        {
        //            var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        //            dto.stupidLogErrors.Add($"[Token Preview] {token?.Substring(0, 30)}...");
        //        }
        //        catch (Exception tokenEx)
        //        {
        //            dto.stupidLogErrors.Add($"[Token Failure] {tokenEx.Message}");
        //        }

        //        // Set up the Drive service
        //        var driveService = new DriveService(new BaseClientService.Initializer
        //        {
        //            HttpClientInitializer = credential,
        //            ApplicationName = "TaskFuelUploader"
        //        });

        //        string raymarRootId = "1V13UNyx-eQE7ec24-Z3wPOinQyiNR-Ty";
        //        string[] pathSegments = custPath.Split('>');
        //        string currentParentId = raymarRootId;

        //        foreach (var segment in pathSegments)
        //        {
        //            currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
        //        }

        //        string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
        //        string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
        //        string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);

        //        Log.Information($"📂 Folder prep complete → WO: {workOrderFolderId}, PDFs: {pdfFolderId}, Images: {imagesFolderId}");

        //        dto.WorkOrderFolderId = workOrderFolderId;
        //        dto.PdfFolderId = pdfFolderId;
        //        dto.ImagesFolderId = imagesFolderId;
        //        return dto;
        //    }
        //    catch (Exception ex)
        //    {
        //        var err = $"❌ EXCEPTION: {ex.Message}";
        //        Log.Error(ex, err);
        //        dto.stupidLogErrors.Add(err);
        //        return dto;
        //    }
        //}

        //public async Task<List<FileUpload>> UploadFilesNEWAsync(
        //   List<IFormFile> files,
        //   string workOrderId,
        //   string workOrderFolderId,
        //   string pdfFolderId,
        //   string imagesFolderId)
        //{
        //    var newUploads = new List<FileUpload>();

        //    try
        //    {
        //        Log.Information($"📦 Uploading {files.Count} file(s) to pre-created Google Drive folders...");
        //        Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
        //        Log.Information($"Machine Local Time: {DateTime.Now:O}");

        //        foreach (var f in files)
        //        {
        //            Log.Information($"📂 Incoming file: {f.FileName} ({f.Length} bytes, ContentType: {f.ContentType})");
        //        }

        //        // ✅ Use Workspace Application Default Credentials
        //        GoogleCredential credential = await GoogleCredential
        //            .GetApplicationDefaultAsync()
        //            .ConfigureAwait(false);

        //        if (credential.IsCreateScopedRequired)
        //        {
        //            credential = credential.CreateScoped(DriveService.ScopeConstants.Drive);
        //        }

        //        var driveService = new DriveService(new BaseClientService.Initializer
        //        {
        //            HttpClientInitializer = credential,
        //            ApplicationName = "TaskFuelUploader"
        //        });

        //        foreach (var file in files)
        //        {
        //            var fileLog = new FileUpload
        //            {
        //                FileName = file.FileName,
        //                Extension = Path.GetExtension(file.FileName).ToLower(),
        //                WorkOrderId = workOrderId,
        //                stupidLogErrors = new List<string>()
        //            };

        //            try
        //            {
        //                string ext = fileLog.Extension;
        //                string targetFolderId = ext switch
        //                {
        //                    ".pdf" => pdfFolderId,
        //                    ".jpg" or ".jpeg" or ".png" => imagesFolderId,
        //                    _ => workOrderFolderId
        //                };

        //                fileLog.stupidLogErrors.Add($"➡️ Target Folder ID: {targetFolderId}");

        //                try
        //                {
        //                    // ✅ SupportsAllDrives fix
        //                    var folderCheck = driveService.Files.Get(targetFolderId);
        //                    folderCheck.SupportsAllDrives = true;
        //                    var folder = await folderCheck.ExecuteAsync();

        //                    fileLog.stupidLogErrors.Add($"✅ Verified folder exists: {folder.Name} (ID: {folder.Id})");
        //                    Log.Information($"✅ Verified folder exists: {folder.Name} (ID: {folder.Id})");
        //                }
        //                catch (Exception folderEx)
        //                {
        //                    fileLog.stupidLogErrors.Add($"❌ Folder check failed: {folderEx.Message}");
        //                    fileLog.stupidLogErrors.Add($"🧱 Stack: {folderEx.StackTrace}");
        //                    Log.Error(folderEx, $"🔥 Folder check failed for {targetFolderId}");
        //                    newUploads.Add(fileLog);
        //                    continue;
        //                }

        //                if (ext == ".pdf")
        //                {
        //                    var checkExisting = driveService.Files.List();
        //                    checkExisting.Q = $"name = '{file.FileName}' and '{targetFolderId}' in parents and trashed = false";
        //                    checkExisting.Fields = "files(id, name)";
        //                    checkExisting.SupportsAllDrives = true;
        //                    checkExisting.IncludeItemsFromAllDrives = true;
        //                    var existing = await checkExisting.ExecuteAsync();

        //                    foreach (var match in existing.Files)
        //                    {
        //                        fileLog.stupidLogErrors.Add($"🗑️ Deleting existing PDF: {match.Name} (ID: {match.Id})");

        //                        var deleteReq = driveService.Files.Delete(match.Id);
        //                        deleteReq.SupportsAllDrives = true;
        //                        await deleteReq.ExecuteAsync();
        //                    }
        //                }

        //                using var stream = file.OpenReadStream();
        //                fileLog.stupidLogErrors.Add($"📥 Stream opened. Length: {stream.Length}");

        //                var metadata = new Google.Apis.Drive.v3.Data.File
        //                {
        //                    Name = file.FileName,
        //                    Parents = new List<string> { targetFolderId }
        //                };

        //                var upload = driveService.Files.Create(metadata, stream, file.ContentType);
        //                upload.Fields = "id, webViewLink";
        //                upload.SupportsAllDrives = true;

        //                fileLog.stupidLogErrors.Add($"🚀 Starting upload... ContentType: {file.ContentType}");

        //                UploadStatus uploadStatus = UploadStatus.NotStarted;

        //                try
        //                {
        //                    var progress = await upload.UploadAsync();
        //                    uploadStatus = progress.Status;
        //                    fileLog.stupidLogErrors.Add($"📤 Upload status: {uploadStatus}");

        //                    if (progress.Exception != null)
        //                    {
        //                        fileLog.stupidLogErrors.Add($"❌ Google Drive exception: {progress.Exception.GetType().Name} - {progress.Exception.Message}");
        //                        fileLog.stupidLogErrors.Add($"🧱 Stack Trace: {progress.Exception.StackTrace}");
        //                        Log.Error(progress.Exception, $"🔥 Upload failed for {file.FileName}");
        //                    }
        //                }
        //                catch (Exception uploadEx)
        //                {
        //                    fileLog.stupidLogErrors.Add($"🔥 Upload try/catch: {uploadEx.GetType().Name} - {uploadEx.Message}");
        //                    fileLog.stupidLogErrors.Add($"🧱 Stack Trace: {uploadEx.StackTrace}");
        //                    Log.Error(uploadEx, $"🔥 Upload exception for {file.FileName}");
        //                    uploadStatus = UploadStatus.Failed;
        //                }

        //                if (uploadStatus == UploadStatus.Completed)
        //                {
        //                    fileLog.ResponseBodyId = upload.ResponseBody?.Id;
        //                    fileLog.stupidLogErrors.Add($"✅ Upload completed. File ID: {fileLog.ResponseBodyId}");
        //                    newUploads.Add(fileLog);
        //                }
        //                else
        //                {
        //                    fileLog.stupidLogErrors.Add($"⚠️ Upload not completed. Status: {uploadStatus}");
        //                    newUploads.Add(fileLog);
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                fileLog.stupidLogErrors.Add($"🔥 OUTER EXCEPTION: {ex.GetType().Name} - {ex.Message}");
        //                fileLog.stupidLogErrors.Add($"STACK: {ex.StackTrace}");
        //                Log.Error(ex, $"🔥 Upload wrapper failed for {file.FileName}");
        //                newUploads.Add(fileLog);
        //            }
        //        }

        //        Log.Information("🎯 All uploads complete.");
        //        return newUploads;
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "🔥 Total failure during UploadFilesAsync()");
        //        throw;
        //    }
        //}
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
                string rootFolderId = "1Hd4opYT_bV_JbQzMoEzDMxPdWGnjFXTl";
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

