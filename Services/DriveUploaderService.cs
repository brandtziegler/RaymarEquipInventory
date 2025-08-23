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
using Microsoft.SqlServer.Dac;  // DacFx (ExportBacpac)

using Azure.Core;
using System.Globalization;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;        // new SDK
using CsvHelper;                             // for CSV (step 7 later)
using SixLabors.ImageSharp;                  // image load/resize
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Google.Apis.Http;
using Azure.Storage;
using System.Collections.Concurrent;
using Hangfire;


namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService : IDriveUploaderService
    {
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        private readonly IDriveAuthService _authService;
        private readonly IConfiguration _config;
        private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;
        private readonly IReceiptLexicon _lex;

        public DriveUploaderService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context, IDriveAuthService authService, 
            IConfiguration config, System.Net.Http.IHttpClientFactory httpClientFactory, IReceiptLexicon lexicon)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _authService = authService;
            _config = config;
            _httpClientFactory = httpClientFactory;
            _lex = lexicon; // <— store it
        }



        ///START OF BLOB SUBMISSION CODE.

        public string GenerateBatchId()
        {
            // compact, sortable-ish, globally unique
            return $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        }

        // Image/receipt helpers
        private static bool IsImage(IFormFile file)
        {
            var ct = file.ContentType?.ToLowerInvariant() ?? "";
            if (ct.StartsWith("image/")) return true;

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".heic" or ".webp";
        }

        // Your rule: receipt images are prefixed with 25_
        private static bool IsReceiptImage(string fileName) =>
            fileName.Contains("TRAVEL", StringComparison.OrdinalIgnoreCase);

        // PDF helper
        private static bool IsPdf(IFormFile file)
        {
            var ct = file.ContentType?.ToLowerInvariant() ?? "";
            if (ct == "application/pdf" || ct.Contains("pdf")) return true;

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            return ext == ".pdf";
        }

        private (string container, string blobPrefix) DecideBlobRoutingCore(
            IFormFile file,
            string workOrderId,
            string batchId)
        {
            var fileName = file.FileName ?? string.Empty;

            // Containers from config, with safe fallbacks
            var receiptContainer = Environment.GetEnvironmentVariable("BlobContainer_Receipts") ?? "";
            var partsContainer = Environment.GetEnvironmentVariable("BlobContainer_Parts") ?? "";
            var pdfContainer = Environment.GetEnvironmentVariable("BlobContainer_PDFs") ?? "";

            string container;

            if (IsPdf(file))
            {
                container = pdfContainer;
            }
            else if (IsImage(file) && IsReceiptImage(fileName))
            {
                container = receiptContainer;
            }
            else
            {
                container = partsContainer;
            }

            // <container>/<workOrderId>/<batchId>/<originalFileName>
            // (container is passed separately to the SDK; blob name is the prefix below)
            var blobPrefix = $"{workOrderId}/{batchId}/{fileName}";

            return (container, blobPrefix);
        }

        // DTOs used only for planning/preview

        public record UploadPlan(
    string BatchId,
    string WorkOrderId,
    string WorkOrderFolderId,
    string ImagesFolderId,
    string PdfFolderId,
    List<PlannedFileInfo> Files);

        public UploadPlan PlanBlobRoutingFromClient(
            IEnumerable<(string FileName, string? Kind)> files,
            string workOrderId,
            string? workOrderFolderId,
            string? imagesFolderId,
            string? pdfFolderId,
            string? batchId)
        {
            var fakeFormFiles = files
                .Select(f => (IFormFile)new FakeLightFormFile(f.FileName))
                .ToList(); // <-- List<IFormFile>

            return PlanBlobRouting(fakeFormFiles, workOrderId, workOrderFolderId, imagesFolderId, pdfFolderId, batchId);
        }


        private sealed class FakeLightFormFile : IFormFile
        {
            public FakeLightFormFile(string fileName) { FileName = fileName; }
            public string FileName { get; }
            // The rest are unused; provide minimal stubs
            public string ContentType => "application/octet-stream";
            public string ContentDisposition => string.Empty;
            public IHeaderDictionary Headers => new HeaderDictionary();
            public long Length => 0;
            public string Name => FileName;
            public void CopyTo(Stream target) => throw new NotSupportedException();
            public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Stream OpenReadStream() => Stream.Null;
        }
        public UploadPlan PlanBlobRouting(
            List<IFormFile> files,
            string workOrderId,
            string workOrderFolderId,
            string imagesFolderId,
            string pdfFolderId,
            string? batchId = null)
        {
            var id = string.IsNullOrWhiteSpace(batchId) ? GenerateBatchId() : batchId.Trim();
            var results = new List<PlannedFileInfo>(files?.Count ?? 0);

            foreach (var f in files ?? Enumerable.Empty<IFormFile>())
            {
                var (container, blobPath) = DecideBlobRoutingCore(f, workOrderId, id);
                results.Add(new PlannedFileInfo(
                    f.FileName,
                    (container ?? "").Trim().ToLowerInvariant(),
                    (blobPath ?? "").Replace('\\', '/').TrimStart('/').Replace("//", "/")
                ));
            }

            return new UploadPlan(id, workOrderId, workOrderFolderId, imagesFolderId, pdfFolderId, results);
        }


        public async Task UploadFileToBlobAsync(
                 string containerName,
                 string blobPath,
                 IFormFile file,
                 CancellationToken ct = default)
        {
            // ---- Stopwatch for perf telemetry
            var sw = Stopwatch.StartNew();

            // ---- Clients (container cached for perf)
            var connStr = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                         ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING missing.");

            containerName = (containerName ?? "").Trim().ToLowerInvariant();
            blobPath = (blobPath ?? "").Replace('\\', '/').TrimStart('/');

            var containerClient = _containerCache.GetOrAdd(containerName, name =>
            {
                var svc = new BlobServiceClient(connStr, new BlobClientOptions
                {
                    Retry =
            {
                Mode = RetryMode.Exponential,
                MaxRetries = 5,
                Delay = TimeSpan.FromMilliseconds(300),
                MaxDelay = TimeSpan.FromSeconds(5)
            }
                });
                var c = svc.GetBlobContainerClient(name);
                c.CreateIfNotExists(PublicAccessType.None, cancellationToken: ct);
                return c;
            });

            var blobClient = containerClient.GetBlobClient(blobPath);

            // ---- Content headers
            string contentType = !string.IsNullOrWhiteSpace(file.ContentType)
                ? file.ContentType
                : GetMimeTypeFromExtension(file.FileName) ?? "application/octet-stream";

            // ---- Stream upload (no server-side image compression)
            await using var contentStream = file.OpenReadStream();

            // If you rerun tests often, overwrite avoids 409 conflicts.
            // Easiest way while still using TransferOptions: delete if exists, then upload with options.
            await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);

            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                TransferOptions = new StorageTransferOptions
                {
                    // Tune these; 4–8 concurrency and 4 MiB chunks are good starters for App Service.
                    MaximumConcurrency = 4,
                    InitialTransferSize = 4 * 1024 * 1024,
                    MaximumTransferSize = 4 * 1024 * 1024
                }
            };

            Response<BlobContentInfo> resp = await blobClient.UploadAsync(contentStream, options, ct);

            sw.Stop();

            // ---- Nice telemetry: MB, MB/s, and Azure request id
            double mb = file.Length / (1024.0 * 1024.0);
            double mbps = mb / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            string reqId = resp.GetRawResponse()?.Headers.TryGetValue("x-ms-request-id", out var v) == true ? v : "?";

            Log.Information("✅ Uploaded {Container}/{Path} ({SizeMB:n2} MB) in {Ms} ms ({MBps:n2} MB/s) [req {ReqId}]",
                containerName, blobPath, mb, sw.ElapsedMilliseconds, mbps, reqId);
        }

        // Cache container clients so we don’t CreateIfNotExists on every file
        private static readonly ConcurrentDictionary<string, BlobContainerClient> _containerCache
            = new(StringComparer.OrdinalIgnoreCase);



        // --- Parsing helpers --------------------------------

        private ReceiptCsvRow MapParsedToCsvRow(AnalyzeResult result, out bool needsReview)
        {
            var doc = result.Documents.FirstOrDefault();
            var raw = result.Content ?? string.Empty;
            needsReview = false;
           
            // --- Basic fields -------------------------------------------------------
            var merchantRaw = GetFieldContentCI(doc, "MerchantName");
            // Normalize to help vendor overrides like "CANADIAN TIRE #123"

            var merchant = NormalizeMerchant(merchantRaw);

            var address = GetFieldContentCI(doc, "MerchantAddress");
            var city = ExtractCityFromAddressSmart(address);
            var typedByModel = GetFieldContentCI(doc, "ReceiptType"); // may be flaky

            var subTotal = ParseCurrency(GetFieldContentCI(doc, "SubTotal"));
            var totalTax = ParseCurrency(GetFieldContentCI(doc, "TotalTax"));
            var total = ParseCurrency(GetFieldContentCI(doc, "Total"));
            var txDate = ParseDate(GetFieldContentCI(doc, "TransactionDate"));

            // Derive subtotal if missing (common case when only Total/Tax are present)
            if (subTotal == 0 && total > 0 && totalTax >= 0)
            {
                var computed = total - totalTax;
                if (computed > 0) subTotal = computed;
            }

            // Strict masked last-4 (only when "************1234" style is present)
            var last4 = ExtractMaskedLast4Strict(raw, out _);

            // --- Items (structured or fallback “desc + price” lines) ----------------
            var items = ExtractItems(doc, raw);

            // --- Decide category, then filter items by category ---------------------
            // 1) Try vendor override using contains-match to handle store numbers etc.
            string? overrideCat = null;
            foreach (var kvp in _lex.VendorCategoryOverrides)
            {
                if (merchant.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideCat = kvp.Value;
                    break;
                }
            }

            // 2) If no override, use your heuristics (items + merchant hints)
            var inferred = overrideCat ?? InferCategory(merchant, items);

            // 3) Fallback to model’s type only if our inference is Unknown/empty
            var finalType = string.IsNullOrWhiteSpace(inferred) || inferred.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                            ? (string.IsNullOrWhiteSpace(typedByModel) ? "Unknown" : typedByModel)
                            : inferred;

            // 4) Now filter noisy lines based on the decided category (allow/stop lists)
            var filteredItems = FilterItemsByCategory(items, finalType);
            var itemsJoined = string.Join(", ", filteredItems);

            // --- Review heuristics ---------------------------------------------------
            if (string.IsNullOrWhiteSpace(merchant) || total <= 0) needsReview = true;
            if (subTotal > 0 && totalTax == 0 && !NearlyEqual(total, subTotal)) needsReview = true;
            if (subTotal > 0 && totalTax > 0 && !NearlyEqual(subTotal + totalTax, total)) needsReview = true;

            return new ReceiptCsvRow
            {
                MerchantName = merchantRaw, // preserve original casing in CSV
                City = city,
                ReceiptType = finalType,
                SubTotal = subTotal,
                TotalTax = totalTax,
                Total = total,
                TransactionDate = txDate,
                CardNumberMasked = last4,
                Items = itemsJoined
            };
        }
        public async Task<ReceiptConfirm> ParseReceiptsBuildCsvAsync(List<IFormFile> files, CancellationToken ct = default)
        {
            var confirm = new ReceiptConfirm
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ReceivedCount = files.Count,
                CsvFileName = $"Receipts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
            };

            var client = CreateDocIntelClient();
            var modelId = GetModelId();

            // Throttle to respect F0: <= 20 calls/min. Simple: 1 at a time, ~3s spacing.
            const int minMsBetweenCalls = 3100;

            var rows = new List<ReceiptCsvRow>();
            var lastCall = DateTime.MinValue;

            foreach (var file in files)
            {
                try
                {
                    // 3) Normalize (HEIC->JPG, resize/compress if > 4MB)
                    await RespectRateAsync(lastCall, minMsBetweenCalls, ct);
                    using var normalized = await NormalizeImageAsync(file, ct);

                    // 4) Call Document Intelligence
                    lastCall = DateTime.UtcNow;
                    var parsed = await AnalyzeReceiptAsync(client, modelId, normalized, ct);

                    // 5) Post-process into one CSV row
                    var row = MapParsedToCsvRow(parsed, out bool needsReview);
                    if (needsReview) confirm.NeedsReviewCount++;
                    rows.Add(row);
                    confirm.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    confirm.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            // (You’ll generate CSV in step 7; for now just compute size when you write it)
            using var csvStream = await BuildCsvAsync(rows, confirm.CsvFileName, ct);
            confirm.CsvSizeBytes = csvStream.Length;

            // TODO: email or return csvStream to caller depending on your controller design
            // (You can stash csvStream in temp storage or return as FileStreamResult)

            return confirm;
        }


        public async Task ParseReceiptBatchFromBlobAndEmailAsync(
    ProcessBatchArgs args,
    string toEmail,
    CancellationToken ct = default)
        {
            if (args is null) throw new ArgumentNullException(nameof(args));
            // Blob client
            var connStr = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                          ?? throw new InvalidOperationException("AzureStorage:ConnectionString missing");
            var blobService = new Azure.Storage.Blobs.BlobServiceClient(connStr);

            // Doc Intelligence
            var client = CreateDocIntelClient();
            var modelId = GetModelId();

            var rows = new List<ReceiptCsvRow>();
            var confirm = new ReceiptConfirm
            {
                BatchId = args.BatchId,
                ReceivedCount = args.Files?.Count ?? 0,
                CsvFileName = $"Receipts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
            };

            // Respect free-tier rate limits
            const int minMsBetweenCalls = 3100;
            var lastCall = DateTime.MinValue;

            // Pull only receipt images from args.Files (controller already filtered, this is a safety net)
            var receiptFiles = (args.Files ?? Enumerable.Empty<PlannedFileInfo>())
                .Where(f => f.Container.Equals(Environment.GetEnvironmentVariable("BlobContainer_Receipts"), StringComparison.OrdinalIgnoreCase))
                .Where(f => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(f.FileName).ToLowerInvariant()))
                .ToList();

            foreach (var pf in receiptFiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var container = blobService.GetBlobContainerClient(pf.Container);
                    var blob = container.GetBlobClient(pf.BlobPath);

                    if (!await blob.ExistsAsync(ct))
                    {
                        confirm.Errors.Add($"Missing blob: {pf.Container}/{pf.BlobPath}");
                        continue;
                    }

                    await using var src = await blob.OpenReadAsync(cancellationToken: ct);

                    // optional: normalize JPEGs before sending to DI to save DI bandwidth/time
                    Stream diStream;
                    var ext = Path.GetExtension(pf.FileName).ToLowerInvariant();
                    if (ext is ".jpg" or ".jpeg")
                    {
                        // reuse your helper
                        // Create a faux IFormFile wrapper around blob stream if needed,
                        // or just pass through src; DI handles large images fine.
                        diStream = src;
                    }
                    else
                    {
                        diStream = src;
                    }

                    await RespectRateAsync(lastCall, minMsBetweenCalls, ct);
                    lastCall = DateTime.UtcNow;

                    var parsed = await AnalyzeReceiptAsync(client, modelId, diStream, ct);

                    var row = MapParsedToCsvRow(parsed, out bool needsReview);
                    if (needsReview) confirm.NeedsReviewCount++;
                    rows.Add(row);
                    confirm.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    confirm.Errors.Add($"{pf.FileName}: {ex.Message}");
                }
            }

            // Build CSV in-memory
            var csvStream = await BuildCsvAsync(rows, confirm.CsvFileName, ct);
            confirm.CsvSizeBytes = csvStream.Length;
            csvStream.Position = 0;

            // Email it
            await SendCsvEmailViaResendAsync(
                subject: $"WO #{args.WorkOrderId} — Receipts CSV (Batch {args.BatchId})",
                htmlBody: $"<p>Attached are parsed receipts for Work Order <strong>#{args.WorkOrderId}</strong>, batch <code>{args.BatchId}</code>.</p>" +
                          $"<p>Processed: {confirm.ProcessedCount}, Needs review: {confirm.NeedsReviewCount}.</p>",
                attachmentName: confirm.CsvFileName,
                csvStream: csvStream,
                ct: ct);

            Log.Information("📨 Sent receipts CSV email for WO {WO}, batch {Batch}. Rows={Rows}, Review={Review}",
                args.WorkOrderId, args.BatchId, rows.Count, confirm.NeedsReviewCount);
        }

        // in DriveUploaderService
        public async Task<(MemoryStream Csv, ReceiptConfirm Confirm)> ParseReceiptsAndReturnCsvAsync(List<IFormFile> files, CancellationToken ct = default)
        {
            var confirm = new ReceiptConfirm
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ReceivedCount = files.Count,
                CsvFileName = $"Receipts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
            };

            var client = CreateDocIntelClient();
            var modelId = GetModelId();

            const int minMsBetweenCalls = 3100;
            var rows = new List<ReceiptCsvRow>();
            var lastCall = DateTime.MinValue;

            foreach (var file in files)
            {
                try
                {
                    await RespectRateAsync(lastCall, minMsBetweenCalls, ct);
                    using var normalized = await NormalizeImageAsync(file, ct);

                    lastCall = DateTime.UtcNow;
                    var parsed = await AnalyzeReceiptAsync(client, modelId, normalized, ct);

                    var row = MapParsedToCsvRow(parsed, out bool needsReview);
                    if (needsReview) confirm.NeedsReviewCount++;
                    rows.Add(row);
                    confirm.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    confirm.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            var csvStream = await BuildCsvAsync(rows, confirm.CsvFileName, ct); // <- DO NOT dispose
            confirm.CsvSizeBytes = csvStream.Length;

            return (csvStream, confirm);
        }





        ///END  OF DOCUMENT INTELLIGENCE CODE

        //BRING OVER ALL FILES FROM TEMPLATES FOLDER START
        public async Task<PDFSyncResult> SyncTemplatesToSqlAsync(CancellationToken ct = default)
        {
            var drive = await _authService.GetDriveServiceFromUserTokenAsync();

            var folderId =
                Environment.GetEnvironmentVariable("GoogleDrive__TemplatesFolderId")
                ?? _config["GoogleDrive:TemplatesFolderId"]
                ?? throw new InvalidOperationException("Missing config: GoogleDrive:TemplatesFolderId");

            // ---- 1) Pull all files in the folder (paged) ----
            var files = new List<Google.Apis.Drive.v3.Data.File>();
            string? pageToken = null;

            do
            {
                var req = drive.Files.List();
                req.Q = $"'{folderId}' in parents and trashed=false and mimeType != 'application/vnd.google-apps.folder'";
                req.Fields = "nextPageToken, files(id,name,description,modifiedTime,md5Checksum,size,mimeType,webContentLink,webViewLink,lastModifyingUser/displayName,parents)";
                req.PageSize = 1000;
                req.PageToken = pageToken;

                var resp = await req.ExecuteAsync(ct);
                if (resp.Files != null) files.AddRange(resp.Files);
                pageToken = resp.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            var nowUtc = DateTimeOffset.UtcNow;

            // ---- 2) Local lookups ----
            var existing = await _context.DriveFileMetadata
                .Where(d => d.FolderId == folderId)
                .ToListAsync(ct);

            var byId = existing.ToDictionary(d => d.DriveFileId, d => d, StringComparer.Ordinal);

            static string Norm(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

            var tagByName = await _context.Pdftags
                .ToDictionaryAsync(t => Norm(t.FileName), t => t.Id, ct);

            static DateTimeOffset? ToDto(DateTime? dt)
                => dt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null;

            int ins = 0, upd = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // ---- 3) Upserts ----
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f.Id)) continue;
                seen.Add(f.Id);

                if (byId.TryGetValue(f.Id, out var row))
                {
                    // update
                    if (!string.IsNullOrEmpty(f.Name)) row.Name = f.Name;
                    if (!string.IsNullOrEmpty(f.MimeType)) row.MimeType = f.MimeType;
                    if (!string.IsNullOrEmpty(f.Description)) row.Description = f.Description;

                    var mod = ToDto(f.ModifiedTime);
                    if (mod.HasValue) row.ModifiedTime = mod;

                    if (f.Size.HasValue) row.SizeBytes = f.Size;
                    if (!string.IsNullOrEmpty(f.Md5Checksum)) row.Md5Checksum = f.Md5Checksum;
                    if (!string.IsNullOrEmpty(f.WebViewLink)) row.WebViewLink = f.WebViewLink;
                    if (!string.IsNullOrEmpty(f.WebContentLink)) row.WebContentLink = f.WebContentLink;
                    if (!string.IsNullOrEmpty(f.LastModifyingUser?.DisplayName))
                        row.LastModUser = f.LastModifyingUser.DisplayName;

                    row.IsTemplateFile = true;
                    row.IsTrashed = false;
                    row.LastSeenAt = nowUtc;

                    if (row.PdfTagId == null && tagByName.TryGetValue(Norm(row.Name), out var tagId))
                        row.PdfTagId = tagId;

                    upd++;
                }
                else
                {
                    var newRow = new DriveFileMetadatum
                    {
                        DriveFileId = f.Id,
                        Name = f.Name ?? string.Empty,
                        MimeType = f.MimeType ?? string.Empty,
                        FolderId = folderId,
                        Description = f.Description,
                        ModifiedTime = ToDto(f.ModifiedTime),
                        SizeBytes = f.Size,
                        Md5Checksum = f.Md5Checksum,
                        WebViewLink = f.WebViewLink,
                        WebContentLink = f.WebContentLink,
                        LastModUser = f.LastModifyingUser?.DisplayName,
                        IsTemplateFile = true,
                        IsTrashed = false,
                        LastSeenAt = nowUtc,
                        PdfTagId = tagByName.TryGetValue(Norm(f.Name), out var tagId) ? tagId : (int?)null
                    };

                    _context.DriveFileMetadata.Add(newRow);
                    ins++;
                }
            }

            // ---- 4) Soft-delete anything not seen this run ----
            int trashed = 0;
            foreach (var row in existing)
            {
                if (!seen.Contains(row.DriveFileId) && row.IsTrashed == false)
                {
                    row.IsTrashed = true;
                    row.LastSeenAt = nowUtc;
                    trashed++;
                }
            }

            await _context.SaveChangesAsync(ct);

            Log.Information("Templates sync: {ins} inserted, {upd} updated, {trashed} trashed", ins, upd, trashed);

            return new PDFSyncResult
            {
                Inserted = ins,
                Updated = upd,
                Trashed = trashed.ToString(),   // matches your current DTO
                RanAtUtc = nowUtc
            };
        }


        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public Task<PDFSyncResult> SyncTemplatesToSqlJob()
    => SyncTemplatesToSqlAsync(CancellationToken.None);
        //BRING OVER ALL FILES FROM TEMPLATES FOLDER END

        public async Task<List<DTOs.FileMetadata>> ListFileUrlsAsync(int sheetId, int? labourTypeId, List<string> tags)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                var listRequest = driveService.Files.List();
                var templatesFolderId = Environment.GetEnvironmentVariable("GoogleDrive__TemplatesFolderId") ?? _config["GoogleDrive:TemplatesFolderId"] 
                    ?? throw new InvalidOperationException("Missing config: GoogleDrive:TemplatesFolderId");

                listRequest.Q = $"'{templatesFolderId}' in parents and trashed=false and mimeType != 'application/vnd.google-apps.folder'";
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

                // 🧩 Step 1: Filter by LabourTypeID
                if (labourTypeId.HasValue)
                {
                    var matchingFileNames = await _context.Pdftags
                        .Where(p => p.LabourTypeId == labourTypeId.Value)
                        .Select(p => p.FileName)
                        .ToListAsync();

                    templates = templates
                        .Where(t => matchingFileNames.Contains(t.PDFName, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                // 🧩 Step 2: Filter by tags parsed from Categories: before Notes:
                if (tags != null && tags.Any())
                {
                    templates = templates.Where(t =>
                    {
                        var description = t.fileDescription ?? string.Empty;
                        string categoriesPart = string.Empty;

                        var startIndex = description.IndexOf("Categories:", StringComparison.OrdinalIgnoreCase);
                        if (startIndex >= 0)
                        {
                            var afterCategories = description.Substring(startIndex + "Categories:".Length);

                            // ✅ Fix: strip off any trailing Notes: section, even if no comma
                            var notesIndex = afterCategories.IndexOf("Notes:", StringComparison.OrdinalIgnoreCase);
                            if (notesIndex >= 0)
                                afterCategories = afterCategories.Substring(0, notesIndex);

                            categoriesPart = afterCategories.Trim();
                        }

                        if (string.IsNullOrWhiteSpace(categoriesPart))
                            return false;

                        var fileTags = categoriesPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        return fileTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                    }).ToList();
                }

                // 🧩 Step 3: Merge filled file data for sheetId
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

        public async Task UpdateFolderIdsInPDFDocumentAsync(
            string fileName,
            string extension,
            string workOrderId,
            string workOrderFolderId,
            string pdfFolderId,
            string blobPath,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Log.Warning("⚠️ Skipping PDF folder-id update — fileName is empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(workOrderId))
                {
                    Log.Warning("⚠️ Skipping PDF folder-id update — workOrderId is empty.");
                    return;
                }

                if (!int.TryParse(workOrderId, out var woNumber))
                {
                    Log.Warning($"⚠️ workOrderId '{workOrderId}' is not a valid integer WorkOrderNumber.");
                    return;
                }

                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == woNumber)
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync(ct);

                if (sheetId is null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {workOrderId}. Skipping '{fileName}'.");
                    return;
                }

                var doc = await _context.Pdfdocuments
                    .Where(p => p.FileName == fileName && p.SheetId == sheetId)
                    .OrderByDescending(p => p.PdfdocumentId)
                    .FirstOrDefaultAsync(ct);

                if (doc is null)
                {
                    Log.Warning($"⚠️ No PDFDocument found for '{fileName}' tied to SheetID: {sheetId}.");
                    return;
                }

                doc.WorkOrderFolderId = workOrderFolderId;
                doc.PdffolderId = pdfFolderId;
                doc.AzureBlobPath = blobPath;

                await _context.SaveChangesAsync(ct);

                Log.Information($"📄 Stamped folder IDs and blobPath for PDF '{fileName}' (WO#: {workOrderId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception while updating PDF folder IDs for '{fileName}' (WO#: {workOrderId})");
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

        public async Task UpdateFolderIdsInPartsDocumentAsync(
            string fileName,
            string extension,
            string workOrderId,
            string workOrderFolderId,
            string expenseFolderId,
            string imagesFolderId,
            string blobPath,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Log.Warning("⚠️ Skipping folder-id update — fileName is empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(workOrderId))
                {
                    Log.Warning("⚠️ Skipping folder-id update — workOrderId is empty.");
                    return;
                }

                if (!int.TryParse(workOrderId, out var woNumber))
                {
                    Log.Warning($"⚠️ workOrderId '{workOrderId}' is not a valid integer WorkOrderNumber.");
                    return;
                }

                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == woNumber)
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync(ct);

                if (sheetId is null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {workOrderId}. Skipping '{fileName}'.");
                    return;
                }

                var partUsedIds = await _context.PartsUseds
                    .Where(p => p.SheetId == sheetId.Value)
                    .Select(p => p.PartUsedId)
                    .ToListAsync(ct);

                if (partUsedIds.Count == 0)
                {
                    Log.Warning($"⚠️ No PartsUseds found for SheetID: {sheetId}. Skipping '{fileName}'.");
                    return;
                }

                var doc = await _context.PartsDocuments
                    .Where(p => p.FileName == fileName && partUsedIds.Contains(p.PartUsedId))
                    .OrderByDescending(p => p.PartsDocumentId)
                    .FirstOrDefaultAsync(ct);

                if (doc is null)
                {
                    Log.Warning($"⚠️ No PartsDocument found for '{fileName}' tied to SheetID: {sheetId}.");
                    return;
                }

                doc.WorkOrderFolderId = workOrderFolderId;
                doc.ExpensesFolderId = expenseFolderId;
                if (extension is ".jpg" or ".jpeg" or ".png")
                doc.ImagesFolderId = imagesFolderId;
            
                doc.AzureBlobPath = blobPath;

                await _context.SaveChangesAsync(ct);

                Log.Information($"📁 Stamped folder IDs and blobPath for '{fileName}' (WO#: {workOrderId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception while updating folder IDs for '{fileName}' (WO#: {workOrderId})");
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


        public async Task ClearImageFolderNewAsync(string imagesFolderId)
        {
            var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

            Log.Information("🧼 Fetching files in 'Images' folder for deletion...");

            var listRequest = driveService.Files.List();
            listRequest.Q = $"'{imagesFolderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name)";
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


        public async Task ClearImageFolderAsync(string custPath, string workOrderId)
        {
            var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

            Log.Information("🧭 Resolving folder path for image cleanup...");

            string rootFolderId = Environment.GetEnvironmentVariable("GoogleDrive__RootFolderId") ?? _config["GoogleDrive:RootFolderId"]
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
            //Prepare Google Drive folders...
            var debugLog = new List<string>();
            try
            {
                debugLog.Add($"📁 Preparing Google Drive folders for {custPath} → WorkOrder {workOrderId}");
                debugLog.Add($"⏱ Machine UTC: {DateTime.UtcNow:O}");
                debugLog.Add($"🧠 Local Time: {DateTime.Now:O}");

                debugLog.Add("🔐 Using OAuth2 encrypted token via DriveAuthService");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                debugLog.Add("🚀 DriveService initialized");

                string rootFolderId = Environment.GetEnvironmentVariable("GoogleDrive__RootFolderId") ?? ""
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
                    EmailAddress = Environment.GetEnvironmentVariable("GoogleDrive__SharedEmail") ?? ""
                }, currentParentId).ExecuteAsync();

                debugLog.Add($"👥 Granted 'writer' to email.");

                string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
                debugLog.Add($"📁 WorkOrder folder created: {workOrderFolderId}");

                string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
                debugLog.Add($"📁 PDFs folder created: {pdfFolderId}");

                string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);
                debugLog.Add($"📁 Images folder created: {imagesFolderId}");

                string expenseTrackingFolderId = await EnsureFolderExistsAsync("Expense Tracking", workOrderFolderId, driveService);
                debugLog.Add($"📁 Expense Tracking folder created: {expenseTrackingFolderId}");

                return new GoogleDriveFolderDTO
                {
                    WorkOrderFolderId = workOrderFolderId,
                    PdfFolderId = pdfFolderId,
                    ImagesFolderId = imagesFolderId,
                    ExpenseTrackingFolderId = expenseTrackingFolderId,
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
                    ExpenseTrackingFolderId = "",
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


        //private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        //{
        //    var cached = await _context.GoogleDriveFolders
        //        .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderId == parentId);

        //    if (cached != null)
        //    {
        //        Log.Information($"📦 SQL Cache Hit: {folderName} under {parentId} (ID: {cached.FolderId})");
        //        return cached.FolderId;
        //    }

        //    // Helper to list folder from Drive
        //    async Task<string?> TryFindFolderOnDriveAsync(string name, string parent)
        //    {
        //        var request = driveService.Files.List();
        //        request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{name}' and '{parent}' in parents and trashed=false";
        //        request.Fields = "files(id)";
        //        request.SupportsAllDrives = true;
        //        request.IncludeItemsFromAllDrives = true;

        //        try
        //        {
        //            var response = await request.ExecuteAsync();
        //            return response.Files.FirstOrDefault()?.Id;
        //        }
        //        catch (Google.GoogleApiException ex)
        //        {
        //            Log.Warning($"🔁 Drive API list failed for '{name}' under '{parent}': {ex.Message}");
        //            return null;
        //        }
        //    }

        //    // Step 1: Try immediately
        //    var existingId = await TryFindFolderOnDriveAsync(folderName, parentId);
        //    if (existingId != null)
        //    {
        //        Log.Information($"📁 Found in Drive: {folderName} under {parentId} (ID: {existingId})");
        //        await CacheFolderAsync(folderName, parentId, existingId);
        //        return existingId;
        //    }

        //    // Step 2: Retry with jitter
        //    var retryDelay = new Random().Next(500, 850); // jittered retry
        //    Log.Debug($"🕒 Retrying after {retryDelay}ms delay for '{folderName}'");
        //    await Task.Delay(retryDelay);

        //    var retryId = await TryFindFolderOnDriveAsync(folderName, parentId);
        //    if (retryId != null)
        //    {
        //        Log.Warning($"⚠️ Found after retry: {folderName} (ID: {retryId})");
        //        await CacheFolderAsync(folderName, parentId, retryId);
        //        return retryId;
        //    }

        //    // Step 3: Create
        //    Log.Information($"📂 Creating new folder: {folderName} under {parentId}");

        //    var newFolder = new Google.Apis.Drive.v3.Data.File
        //    {
        //        Name = folderName,
        //        MimeType = "application/vnd.google-apps.folder",
        //        Parents = new List<string> { parentId }
        //    };

        //    try
        //    {
        //        var createRequest = driveService.Files.Create(newFolder);
        //        createRequest.Fields = "id";
        //        createRequest.SupportsAllDrives = true;

        //        var created = await createRequest.ExecuteAsync();
        //        Log.Information($"✅ Created folder: {folderName} (ID: {created.Id})");

        //        await CacheFolderAsync(folderName, parentId, created.Id);
        //        return created.Id;
        //    }
        //    catch (Google.GoogleApiException ex)
        //    {
        //        Log.Error(ex, $"❌ Failed to create folder '{folderName}' under '{parentId}': {ex.Message}");
        //        throw;
        //    }
        //}

        public async Task DeleteBatchBlobsAsync(
            IEnumerable<PlannedFileInfo> files,
            string workOrderId,
            string batchId,
            CancellationToken ct)
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "";
            var blobService = new Azure.Storage.Blobs.BlobServiceClient(conn);

            var prefix = $"{workOrderId}/{batchId}/";
            var containers = files.Select(f => f.Container).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var containerName in containers)
            {
                try
                {
                    var container = blobService.GetBlobContainerClient(containerName);
                    int delCount = 0;

                    await foreach (var blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
                    {
                        await container.DeleteBlobIfExistsAsync(
                            blob.Name,
                            Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots,
                            cancellationToken: ct);
                        delCount++;
                    }

                    Log.Information("🧹 Deleted {Count} blobs from {Container} with prefix {Prefix}",
                        delCount, containerName, prefix);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ DeleteBatchBlobsAsync: failed on container {Container}", containerName);
                }
            }

            Log.Information("🧹 Finished deleting batch {BatchId} under WO {WO}", batchId, workOrderId);
        }


        public async Task ClearAndUploadBatchFromBlobAsync(ProcessBatchArgs args, CancellationToken ct = default)
        {
            if (args is null) throw new ArgumentNullException(nameof(args));
            if (args.Files is null || args.Files.Count == 0)
            {
                Log.Information("ClearAndUploadBatchFromBlobAsync: no files in batch {BatchId}.", args.BatchId);
                return;
            }

            Log.Information("🚚 Begin batch {BatchId} for WO {WO} (files: {Count})",
                args.BatchId, args.WorkOrderId, args.Files.Count);

            // 1) Always clear Drive Images folder first (if provided)
            if (!string.IsNullOrWhiteSpace(args.ImagesFolderId))
            {
                try
                {
                    Log.Information("🧼 Clearing Images folder for WO {WO}…", args.WorkOrderId);
                    await ClearImageFolderNewAsync(args.ImagesFolderId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "⚠️ Failed to clear Images folder; continuing with uploads.");
                }
            }

            // 2) Clients
            var drive = await _authService.GetDriveServiceFromUserTokenAsync();


            var conn = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            var blobService = new Azure.Storage.Blobs.BlobServiceClient(conn);

            // 3) Move each file: Blob → Drive → DB
            foreach (var pf in args.Files)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var container = blobService.GetBlobContainerClient(pf.Container);
                    var blob = container.GetBlobClient(pf.BlobPath);

                    if (!await blob.ExistsAsync(ct))
                    {
                        Log.Warning("🫥 Blob missing: {Container}/{Path}", pf.Container, pf.BlobPath);
                        continue;
                    }

                    using var mem = new MemoryStream();

                    await using (var src = await blob.OpenReadAsync(cancellationToken: ct))
                    {
                        await src.CopyToAsync(mem, ct);
                    }
                    mem.Position = 0;

                    var ext = Path.GetExtension(pf.FileName).ToLowerInvariant();
                    var targetFolderId = ext switch
                    {
                        ".jpg" or ".jpeg" or ".png" => args.ImagesFolderId,
                        ".pdf" => args.PdfFolderId,        // or args.ExpensesFolderId if that’s your policy
                        _ => args.WorkOrderFolderId,
                    };

                    if (string.IsNullOrWhiteSpace(targetFolderId))
                    {
                        Log.Warning("⚠️ No Drive folder ID for {File}. Skipping.", pf.FileName);
                        continue;
                    }

                    var meta = new Google.Apis.Drive.v3.Data.File
                    {
                        Name = pf.FileName,
                        Parents = new List<string> { targetFolderId }
                    };

                    var contentType = GetMimeTypeFromExtension(pf.FileName) ?? "application/octet-stream";

                    mem.Position = 0;
                    var req = drive.Files.Create(meta, mem, contentType);
                    req.Fields = "id, webViewLink";
                    var prog = await req.UploadAsync(ct);

                    var driveFileId = req.ResponseBody?.Id;
                    Log.Information("⬆️  Drive upload {Status} for {File} → Folder {Folder}, Id {Id}",
                        prog.Status, pf.FileName, targetFolderId, driveFileId ?? "(null)");
                
                    // DB updates
                    if (!string.IsNullOrEmpty(driveFileId))
                    {
                        if (ext is ".jpg" or ".jpeg" or ".png")
                        {
                            await UpdateFileUrlInPartsDocumentAsync(
                                pf.FileName, driveFileId, ext, args.WorkOrderId);
                        }
                        else if (ext == ".pdf")
                        {
                            await UpdateFileUrlInPDFDocumentAsync(new PDFUploadRequest
                            {
                                FileName = pf.FileName,
                                FileId = driveFileId,
                                WorkOrderId = args.WorkOrderId,
                                UploadedBy = "Hangfire Job",
                                Description = ""
                            });
                        }
                    }

                
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "🔥 Failed moving {File} from Blob to Drive", pf.FileName);
                }
            }


            //await DeleteBatchBlobsAsync(blobService, args.Files, args.WorkOrderId, args.BatchId, ct);

            Log.Information("🧹 Deleted blobs for batch {BatchId} under {WO}/", args.BatchId, args.WorkOrderId);
            Log.Information("✅ Batch {BatchId} (WO {WO}) clear+upload complete.", args.BatchId, args.WorkOrderId);
        }

        // inside class DriveUploaderService …
        public async Task<string?> BackupDatabaseToGoogleDriveAsync(CancellationToken ct = default)
        {
            // 1) Target Drive folder (env var first, then appsettings fallback)
            var driveFolderId = Environment.GetEnvironmentVariable("GOOGLE_DBBackups")
                ?? _config["GoogleDrive:DBBackupsFolderId"];

            if (string.IsNullOrWhiteSpace(driveFolderId))
                throw new InvalidOperationException("Missing GOOGLE_DBBackups (or GoogleDrive:DBBackupsFolderId).");

            // 2) Connection string from EF context
            var connString = _context.Database.GetDbConnection().ConnectionString;
            var csb = new SqlConnectionStringBuilder(connString);
            var dbName = csb.InitialCatalog ?? "Database";
            var server = csb.DataSource ?? "server";

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{dbName}_{stamp}.bacpac";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            Log.Information("Starting .bacpac export for {Db} on {Server} -> {Path}", dbName, server, tempPath);

            try
            {
                // 3) Export .bacpac (DacFx is sync; run on background thread)
                await Task.Run(() =>
                {
                    var dac = new DacServices(connString);
                    dac.Message += (s, e) => Log.Information("DacFx: {Msg}", e.Message);
                    dac.ExportBacpac(tempPath, dbName);
                }, ct);

                var size = new FileInfo(tempPath).Length;
                Log.Information("Bacpac created: {Path} ({Size} bytes)", tempPath, size);

                // 4) Auth to Drive using your existing OAuth2 user token
                DriveService drive = await _authService.GetDriveServiceFromUserTokenAsync();

                // 5) Upload (resumable)
                var meta = new Google.Apis.Drive.v3.Data.File
                {
                    Name = fileName,
                    Parents = new[] { driveFolderId }
                };

                using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var upload = drive.Files.Create(meta, fs, "application/octet-stream");
                upload.Fields = "id,name,parents,size,createdTime,webViewLink";
                upload.ChunkSize = ResumableUpload.MinimumChunkSize * 8; // ~8MB chunks

                Log.Information("Uploading {File} to Drive folder {Folder}…", fileName, driveFolderId);
                var result = await upload.UploadAsync(ct);

                if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
                {
                    Log.Error(result.Exception, "Google Drive upload failed with status {Status}", result.Status);
                    return null;
                }

                var created = upload.ResponseBody;
                Log.Information("✅ Backup uploaded. fileId={FileId} size={Size}", created.Id, created.Size);

                return created.Id;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupDatabaseToGoogleDriveAsync failed.");
                return null;
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath))
                        System.IO.File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    Log.Warning(cleanupEx, "Failed to remove temp {Path}", tempPath);
                }
            }
        }

        private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            // 1) Check cache, but verify it still has the right parent
            var cached = await _context.GoogleDriveFolders
                .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderId == parentId);

            if (cached != null)
            {
                try
                {
                    var g = driveService.Files.Get(cached.FolderId);
                    g.Fields = "id,parents,trashed";
                    var meta = await g.ExecuteAsync();

                    // stale/trashed? purge and fallthrough to lookup/create
                    if (meta == null || meta.Trashed == true)
                    {
                        _context.GoogleDriveFolders.Remove(cached);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        var parents = meta.Parents ?? new List<string>();
                        if (!parents.Contains(parentId))
                        {
                            // 💡 auto-correct drift: move to the intended parent
                            var upd = driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), cached.FolderId);
                            upd.AddParents = parentId;
                            if (parents.Count > 0) upd.RemoveParents = string.Join(",", parents);
                            upd.SupportsAllDrives = true;
                            await upd.ExecuteAsync();

                            cached.ParentFolderId = parentId;
                            await _context.SaveChangesAsync();
                        }
                        return cached.FolderId;
                    }
                }
                catch (Google.GoogleApiException ex) when ((int)ex.HttpStatusCode == 404)
                {
                    // cache points to a deleted/missing folder → purge & continue
                    _context.GoogleDriveFolders.Remove(cached);
                    await _context.SaveChangesAsync();
                }
            }

            // 2) Look for an existing folder *under this parent ID*
            async Task<string?> TryFindFolderOnDriveAsync(string name, string parent)
            {
                var escaped = name.Replace("'", "\\'");
                var request = driveService.Files.List();
                request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{escaped}' and '{parent}' in parents and trashed=false";
                request.Fields = "files(id,parents)";
                request.SupportsAllDrives = true;
                request.IncludeItemsFromAllDrives = true;

                var response = await request.ExecuteAsync();
                return response.Files.FirstOrDefault()?.Id;
            }

            var existingId = await TryFindFolderOnDriveAsync(folderName, parentId);
            if (existingId != null)
            {
                await CacheFolderAsync(folderName, parentId, existingId);
                return existingId;
            }

            // 3) Create new under the intended parent
            var create = driveService.Files.Create(new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            });
            create.Fields = "id,parents";
            create.SupportsAllDrives = true;

            var created = await create.ExecuteAsync();
            await CacheFolderAsync(folderName, parentId, created.Id);
            return created.Id;
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

