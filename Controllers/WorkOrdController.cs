using Azure.Core;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Mvc;
using RaymarEquipmentInventory.DTOs;
using RaymarEquipmentInventory.Services;
using Serilog;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Net.Mail;
using Hangfire;





namespace RaymarEquipmentInventory.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkOrdController : Controller
    {
        private readonly IWorkOrderService _workOrderService;
        private readonly ITechnicianService _technicianService;
   
        //private readonly IFederatedTokenService _federatedTokenService;
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ISamsaraApiService _samsaraApiService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDriveUploaderService _driveUploaderService;
        private readonly IDriveAuthService _driveAuthService;
        private readonly IConfiguration _configuration;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> FolderLocks = new();
        private readonly IBackgroundJobClient _jobs;

        public WorkOrdController(IWorkOrderService workOrderService, 
            IQuickBooksConnectionService quickBooksConnectionService, ITechnicianService technicianService, 
            ISamsaraApiService samsaraApiService, 
            IHttpClientFactory httpClientFactory, IDriveUploaderService driveUploaderService, IDriveAuthService driveAuthService, IBackgroundJobClient jobs)
        {
            _workOrderService = workOrderService;
            _quickBooksConnectionService = quickBooksConnectionService;
            _samsaraApiService = samsaraApiService;
            _technicianService = technicianService;
            _httpClientFactory = httpClientFactory;
            _driveUploaderService = driveUploaderService;
            _jobs = jobs;
           
            _driveAuthService = driveAuthService;
            //_federatedTokenService = federatedTokenService;

        }


        [HttpPost("LaunchWorkOrder")]
        public async Task<IActionResult> LaunchWorkOrder([FromBody] Billing billingDto)
        {
            try
            {
                // Call your service to create the work order and attach billing information. Test adding a small comment.
                var result = await _workOrderService.LaunchWorkOrder(billingDto);

                if (!result)
                {
                    return BadRequest("Unable to launch work order with the provided billing information.");
                }

                return Ok("Work order launched successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

     
        [HttpGet("ping")]
        public IActionResult Ping() => Ok("Connected");

        [HttpPost("InsertUpdateWorkOrder")]
        public async Task<IActionResult> InsertUpdateWorkOrder([FromBody] DTOs.WorkOrdSheet workOrdDto)
        {

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                WorkOrderInsertResult? result;

                if (workOrdDto.RemoteSheetId == 0)
                {
                    result = await _workOrderService.InsertWorkOrderAsync(workOrdDto);
                    if (result == null)
                        return StatusCode(500, "InsertWorkOrder returned null (see logs).");
                }
                else
                {
                    result = await _workOrderService.UpdateWorkOrderAsync(workOrdDto);
                    if (result == null)
                        return StatusCode(500, "UpdateWorkOrderAsync returned null (see logs).");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InsertWorkOrder failed");
                return BadRequest(ex.ToString());
            }
        }


        [HttpPost("ClearImagesFolder")]
        public async Task<IActionResult> ClearImagesFolder([FromQuery] string imagesFolderId)
        {
            if (string.IsNullOrWhiteSpace(imagesFolderId))
                return BadRequest("imagesFolderId is required.");

            try
            {
                await _driveUploaderService.ClearImageFolderNewAsync(imagesFolderId);
                return Ok(new { message = "Images folder cleared." });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to clear images folder.");
                return StatusCode(500, new { message = "Clear failed", error = ex.Message });
            }
        }


        [HttpPost("ClearAppFiles")]
        public async Task<IActionResult> ClearAppFiles([FromQuery] string custPath, [FromQuery] string workOrderId)
        {
            try
            {
                await _driveUploaderService.ClearImageFolderAsync(custPath, workOrderId);
                return Ok(new { message = "Folder cleared." });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to clear image folder.");
                return StatusCode(500, new { message = "Clear failed", error = ex.Message });
            }
        }

        [HttpPost("parse-csv")]
        [Produces("text/csv")]
        public async Task<IActionResult> ParseReceiptsCsv([FromForm] List<IFormFile> files, CancellationToken ct)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files uploaded.");

            var (csv, confirm) = await _driveUploaderService.ParseReceiptsAndReturnCsvAsync(files, ct);

            // Optional: surface metadata in headers
            Response.Headers["X-Receipt-BatchId"] = confirm.BatchId;
            Response.Headers["X-ProcessedCount"] = confirm.ProcessedCount.ToString();
            Response.Headers["X-NeedsReview"] = confirm.NeedsReviewCount.ToString();

            // Return downloadable CSV
            return File(csv, "text/csv", confirm.CsvFileName);
        }

        [HttpPost("parse-receipts")]
        public async Task<IActionResult> ParseReceipts([FromForm] List<IFormFile> files, CancellationToken ct)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files uploaded.");

            var confirm = await _driveUploaderService.ParseReceiptsBuildCsvAsync(files, ct);

            // Option A: JSON only
            return Ok(confirm);

            // Option B: return CSV directly
            // var csvStream = await _driveUploaderService.ParseReceiptsAndReturnCsvAsync(files, ct);
            // return File(csvStream, "text/csv", confirm.CsvFileName);
        }

        [HttpPost("BackupDb")]
        public async Task<IActionResult> BackupDb(CancellationToken ct)
        {
            try
            {
                var fileId = await _driveUploaderService.BackupDatabaseToGoogleDriveAsync(ct);

                if (string.IsNullOrWhiteSpace(fileId))
                    return StatusCode(500, new { message = "Backup failed (no fileId returned)" });

                return Ok(new
                {
                    message = "Backup completed",
                    fileId
                });
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "❌ BackupDb failed.");
                return StatusCode(500, new { message = "Backup failed", error = ex.Message });
            }
        }


        [HttpPost("PrepareDriveFolders")]
        public async Task<IActionResult> PrepareDriveFolders(
            [FromQuery] string custPath,
            [FromQuery] string workOrderId,
            [FromQuery] int sheetID)
        {
            //prepares drive folders.
            if (string.IsNullOrWhiteSpace(custPath) || string.IsNullOrWhiteSpace(workOrderId))
            {
                return BadRequest(new { message = "custPath and workOrderId are required." });
            }

            var key = custPath.Split('>').First().Trim().ToLower(); // lock on root customer only
            var semaphore = FolderLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            GoogleDriveFolderDTO folderResult = new();

            try
            {
                await semaphore.WaitAsync();

                folderResult = await _driveUploaderService.PrepareGoogleDriveFoldersAsync(custPath, workOrderId);
                folderResult.SheetID = sheetID;

                if (folderResult.HasCriticalError)
                {
                    return StatusCode(500, folderResult);
                }

                return Ok(folderResult);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Controller-level failure for {custPath} / {workOrderId}");

                folderResult.SheetID = sheetID;
                folderResult.stupidLogErrors.Add($"🔥 Controller exception: {ex.Message}");

                return StatusCode(500, folderResult); // Still return DTO with logs
            }
            finally
            {
                semaphore.Release();
            }
        }

        [HttpPost("UploadToTestFolder")]
        public async Task<IActionResult> UploadToTestFolder(List<IFormFile> files)
        {
            const string testFolderId = "14XRoPlis41OQZ2_WtshCGhMEVKmQOqMa";

            var results = await _driveUploaderService.UploadFilesAsync(
                files,
                workOrderId: "TEST123",
                workOrderFolderId: testFolderId,
                pdfFolderId: testFolderId,
                imagesFolderId: testFolderId
            );

            return Ok(results);
        }



        [HttpPost("UploadAndParseToJSON")]
        public IActionResult UploadAndParseToJSON(
    List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files received.");

            var result = new List<object>();

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file.FileName).ToLower();

                if (ext is ".jpg" or ".jpeg" or ".png")
                {
                    result.Add(new
                    {
                        File = file.FileName,
                        Extension = ext,
                        Status = "✅ Accepted (image/receipt assumed)"
                    });
                }
                else
                {
                    result.Add(new
                    {
                        File = file.FileName,
                        Extension = ext,
                        Status = "⚠️ Rejected (unsupported file type)"
                    });
                }
            }

            return Ok(new
            {
                Message = "File(s) received. No processing done yet.",
                FileResults = result
            });
        }


        //SENDING TO BLOB ONLY - START
        // ===== 1) UPLOAD — micro-batches, no enqueue =====
        [RequestSizeLimit(200 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
        [Consumes("multipart/form-data")]
        [HttpPost("UploadAppFilesToAzureBlob")]
        public async Task<IActionResult> UploadAppFilesToAzureBlob(
            List<IFormFile> files,
            [FromQuery] string workOrderId,
            [FromQuery] string workOrderFolderId,
            [FromQuery] string pdfFolderId,
            [FromQuery] string expensesFolderId,
            [FromQuery] string imagesFolderId,
            [FromQuery] int? maxConcurrency,          // optional; default 6
            CancellationToken ct,
            [FromQuery] bool failFast = true,         // skip rest if uploads fail
            [FromQuery] string? testPrefix = null,    // e.g. "tests/dev"
            [FromQuery] string? batchId = null        // client keeps this constant across micro-batches
        )
        {
            if (files is null || files.Count == 0) return BadRequest("No files received.");
            if (string.IsNullOrWhiteSpace(workOrderId)) return BadRequest("workOrderId is required.");

            var swTotal = Stopwatch.StartNew();

            // 1) Plan (force plan to use client-supplied batchId, if provided)
            var swPlan = Stopwatch.StartNew();
            var plan = _driveUploaderService.PlanBlobRouting(
                files, workOrderId, workOrderFolderId, imagesFolderId, pdfFolderId, batchId);

            swPlan.Stop();

            // Apply optional testPrefix
            string? prefix = string.IsNullOrWhiteSpace(testPrefix) ? null
                            : $"{testPrefix.Trim().Trim('/')}/{plan.BatchId}";
            if (!string.IsNullOrEmpty(prefix))
            {
                foreach (var pf in plan.Files)
                    pf.BlobPath = $"{prefix}/{pf.BlobPath}".Replace("//", "/");
            }

            // 2 & 3) Stamp folder IDs
            var swStamp = Stopwatch.StartNew();
            foreach (var p in plan.Files)
            {
                var ext = Path.GetExtension(p.FileName)?.ToLowerInvariant() ?? string.Empty;
                if (ext is ".jpg" or ".jpeg" or ".png")
                {
                    await _driveUploaderService.UpdateFolderIdsInPartsDocumentAsync(
                        p.FileName, ext, workOrderId, workOrderFolderId, expensesFolderId, imagesFolderId, p.BlobPath, ct);
                }
                else if (ext == ".pdf")
                {
                    await _driveUploaderService.UpdateFolderIdsInPDFDocumentAsync(
                        p.FileName, ext, workOrderId, workOrderFolderId, pdfFolderId, p.BlobPath, ct);
                }
                else
                {
                    Log.Warning("Skipping unsupported file type for folder stamping: {File}", p.FileName);
                }
            }
            swStamp.Stop();

            // 4) Upload with bounded parallelism
            var swUpload = Stopwatch.StartNew();
            var degree = Math.Max(1, Math.Min(maxConcurrency ?? 6, 16));
            var gate = new SemaphoreSlim(degree);

            var nameQueues = files
                .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new Queue<IFormFile>(g), StringComparer.OrdinalIgnoreCase);

            var uploadResults = new List<(string file, bool ok, string? err, long ms)>(plan.Files.Count);
            var tasks = new List<Task>(plan.Files.Count);

            foreach (var p in plan.Files)
            {
                if (!nameQueues.TryGetValue(p.FileName, out var q) || q.Count == 0)
                {
                    uploadResults.Add((p.FileName, false, "File not in request payload (or duplicate underflow)", 0));
                    continue;
                }
                var formFile = q.Dequeue();
                tasks.Add(UploadWithGateAsync(gate, p, formFile, uploadResults, ct));
            }

            await Task.WhenAll(tasks);
            swUpload.Stop();

            var anyUploadFail = uploadResults.Any(r => !r.ok);
            if (failFast && anyUploadFail)
            {
                swTotal.Stop();
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    plan.WorkOrderId,
                    plan.BatchId,
                    plannedCount = plan.Files.Count,
                    uploadedOk = uploadResults.Count(r => r.ok),
                    uploadedFailed = uploadResults.Where(r => !r.ok).Select(r => new { r.file, r.err, r.ms }),
                    enqueued = false,
                    finalizeAvailable = true, // client can still call finalize afterwards
                    timingsMs = new
                    {
                        plan = swPlan.ElapsedMilliseconds,
                        stamp = swStamp.ElapsedMilliseconds,
                        upload = swUpload.ElapsedMilliseconds,
                        total = swTotal.ElapsedMilliseconds
                    },
                    maxConcurrencyUsed = degree,
                    testPrefixApplied = prefix
                });
            }

            swTotal.Stop();

            return Accepted(new
            {
                plan.WorkOrderId,
                plan.BatchId,
                plannedCount = plan.Files.Count,
                uploadedOk = uploadResults.Count(r => r.ok),
                uploadedFailed = uploadResults.Where(r => !r.ok).Select(r => new { r.file, r.err, r.ms }),
                enqueued = false,
                finalizeAvailable = true,
                timingsMs = new
                {
                    plan = swPlan.ElapsedMilliseconds,
                    stamp = swStamp.ElapsedMilliseconds,
                    upload = swUpload.ElapsedMilliseconds,
                    total = swTotal.ElapsedMilliseconds
                },
                maxConcurrencyUsed = degree,
                testPrefixApplied = prefix
            });

            // helper
            async Task UploadWithGateAsync(
                SemaphoreSlim s,
                PlannedFileInfo planned,
                IFormFile formFile,
                List<(string file, bool ok, string? err, long ms)> results,
                CancellationToken token)
            {
                await s.WaitAsync(token);
                var sw = Stopwatch.StartNew();
                try
                {
                    await _driveUploaderService.UploadFileToBlobAsync(
                        (planned.Container ?? "").Trim().ToLowerInvariant(),
                        (planned.BlobPath ?? "").Replace('\\', '/').TrimStart('/'),
                        formFile,
                        token);

                    lock (results) results.Add((planned.FileName, true, null, sw.ElapsedMilliseconds));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Error(ex, "Upload failed for {Container}/{Path}", planned.Container, planned.BlobPath);
                    lock (results) results.Add((planned.FileName, false, ex.Message, sw.ElapsedMilliseconds));
                }
                finally
                {
                    sw.Stop();
                    s.Release();
                }
            }
        }


        //FINALIZE BATCH - START
        // ===== 2) FINALIZE — discover all blobs for batch, enqueue jobs =====
        // ========= FINALIZE (tolerant of both prefix layouts) =========
        [HttpPost("FinalizeWorkOrderBatch")]
        public async Task<IActionResult> FinalizeWorkOrderBatch(
            [FromQuery] string workOrderId,
            [FromQuery] string batchId,                // same value RN used during uploads
            [FromQuery] string workOrderFolderId,
            [FromQuery] string pdfFolderId,
            [FromQuery] string expensesFolderId,
            [FromQuery] string imagesFolderId,
            [FromQuery] string? testPrefix,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(workOrderId)) return BadRequest("workOrderId is required.");
            if (string.IsNullOrWhiteSpace(batchId)) return BadRequest("batchId is required.");

            var key = $"{workOrderId}:{batchId}".ToLowerInvariant();

            // Simple idempotency (single instance)
            if (FinalizeGate.IsFinalized(key))
                return Conflict(new { message = "Batch already finalized.", workOrderId, batchId });

            if (!FinalizeGate.TryBegin(key))
                return Conflict(new { message = "Finalize already in progress for this batch. Try again shortly.", workOrderId, batchId });

            try
            {
                var sw = Stopwatch.StartNew();

                var containers = GetConfiguredContainers(HttpContext.RequestServices);
                if (containers.Count == 0)
                    return Problem("No blob containers configured. Define BlobContainer_* or AzureStorage:Containers.*");

                var tp = string.IsNullOrWhiteSpace(testPrefix) ? null : testPrefix!.Trim().Trim('/');
                var prefixes = BuildCandidatePrefixes(workOrderId, batchId, tp);

                var discovered = await DiscoverBatchFilesAsync(workOrderId, batchId, containers, prefixes, ct);

                discovered = discovered
                    .GroupBy(f => $"{f.Container}|{f.BlobPath}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(f => f.Container).ThenBy(f => f.BlobPath, StringComparer.Ordinal)
                    .ToList();

                if (discovered.Count == 0)
                {
                    return NotFound(new
                    {
                        message = "No blobs found for this workOrderId/batchId.",
                        workOrderId,
                        batchId,
                        prefixesTried = prefixes
                    });
                }

                var args = new ProcessBatchArgs(
                    workOrderId,
                    batchId,
                    workOrderFolderId,
                    imagesFolderId,
                    pdfFolderId,
                    expensesFolderId,
                    discovered);

                var receiptFiles = discovered
                    .Where(f => f.Container.Equals("images-receipts", StringComparison.OrdinalIgnoreCase))
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f.FileName);
                        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                var uploadJobId = _jobs.Enqueue(() =>
                    _driveUploaderService.ClearAndUploadBatchFromBlobAsync(args, CancellationToken.None));

                string? parseJobId = null;
                if (receiptFiles.Count > 0)
                {
                    var defaultTo = Environment.GetEnvironmentVariable("Receipt_Receiver_1") ?? "brandt@brandtziegler.com";
                    parseJobId = _jobs.ContinueJobWith(uploadJobId, () =>
                        _driveUploaderService.ParseReceiptBatchFromBlobAndEmailAsync(
                            args, defaultTo, CancellationToken.None));
                }

                FinalizeGate.TryMarkFinalized(key);
                sw.Stop();

                HttpContext.Response.Headers["x-workorder-id"] = workOrderId;
                HttpContext.Response.Headers["x-batch-id"] = batchId;
                HttpContext.Response.Headers["x-discovered-count"] = discovered.Count.ToString();
                HttpContext.Response.Headers["x-finalize-ms"] = sw.ElapsedMilliseconds.ToString();

                return Accepted(new
                {
                    workOrderId,
                    batchId,
                    discoveredCount = discovered.Count,
                    willParseReceipts = receiptFiles.Count > 0,
                    uploadJobId,
                    parseJobId,
                    prefixesUsed = prefixes
                });
            }
            finally
            {
                FinalizeGate.End(key);
            }
        }

        // ===== Helpers =====

        static class FinalizeGate
        {
            private static readonly ConcurrentDictionary<string, byte> InProgress = new(StringComparer.OrdinalIgnoreCase);
            private static readonly ConcurrentDictionary<string, byte> Finalized = new(StringComparer.OrdinalIgnoreCase);

            public static bool TryBegin(string key) => InProgress.TryAdd(key, 0);
            public static void End(string key) => InProgress.TryRemove(key, out _);
            public static bool IsFinalized(string key) => Finalized.ContainsKey(key);
            public static bool TryMarkFinalized(string key) => Finalized.TryAdd(key, 0);
        }

        // Reads containers from env/appsettings (BlobContainer_* or AzureStorage:Containers)
        private static IReadOnlyList<string> GetConfiguredContainers(IServiceProvider services)
        {
            var cfg = services.GetRequiredService<IConfiguration>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static void Add(HashSet<string> s, string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return;
                v = v.Trim().ToLowerInvariant();
                if (!v.Any(char.IsWhiteSpace)) s.Add(v);
            }

            foreach (DictionaryEntry de in Environment.GetEnvironmentVariables())
                if (de.Key is string k && de.Value is string v &&
                    k.StartsWith("BlobContainer_", StringComparison.OrdinalIgnoreCase)) Add(set, v);

            foreach (var kv in cfg.AsEnumerable())
                if (kv.Key.StartsWith("BlobContainer_", StringComparison.OrdinalIgnoreCase)) Add(set, kv.Value);

            foreach (var child in cfg.GetSection("AzureStorage:Containers").GetChildren())
                Add(set, child.Value);

            return set.ToList();
        }

        // Build both possible prefix shapes so discovery works either way
        private static List<string> BuildCandidatePrefixes(string workOrderId, string batchId, string? testPrefix)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(testPrefix))
            {
                // No testPrefix
                list.Add($"{workOrderId}/{batchId}/");
            }
            else
            {
                var tp = testPrefix.Trim().Trim('/');
                // Preferred (after fixing upload)
                list.Add($"{tp}/{workOrderId}/{batchId}/");
                // Legacy shape you currently have (testPrefix already had batchId appended)
                list.Add($"{tp}/{batchId}/{workOrderId}/{batchId}/");
            }
            return list;
        }

        private async Task<List<PlannedFileInfo>> DiscoverBatchFilesAsync(
            string workOrderId,
            string batchId,
            IEnumerable<string> containers,
            IEnumerable<string> prefixes,
            CancellationToken ct)
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            var svc = new BlobServiceClient(conn);

            var results = new List<PlannedFileInfo>();

            foreach (var c in containers.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var containerName = (c ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(containerName)) continue;

                var cont = svc.GetBlobContainerClient(containerName);

                foreach (var pfx in prefixes)
                {
                    Log.Debug("🔎 Scanning [{Container}] with prefix [{Prefix}]", containerName, pfx);

                    await foreach (var item in cont.GetBlobsAsync(prefix: pfx, cancellationToken: ct))
                    {
                        var blobName = item.Name;
                        var fileName = Path.GetFileName(blobName);
                        results.Add(new PlannedFileInfo(fileName, containerName, blobName));
                    }
                }
            }

            return results;
        }    //FINALIZE BATCH - END
        //SENDING TO BLOB ONLY - END
        [HttpPost("UploadAppFilesToAzureBlob_Full")]
        public async Task<IActionResult> UploadAppFilesToAzureBlob_Full(
            List<IFormFile> files,
            [FromQuery] string workOrderId,
            [FromQuery] string workOrderFolderId,
            [FromQuery] string pdfFolderId,
            [FromQuery] string expensesFolderId,
            [FromQuery] string imagesFolderId,
            [FromQuery] int? maxConcurrency,          // optional; default 6
            CancellationToken ct,
            [FromQuery] bool failFast = true,         // skip enqueue if uploads fail
            [FromQuery] string? testPrefix = null,     // e.g. "tests/dev"
            [FromQuery] bool finalizeBatches = false
        )
        {
            if (files is null || files.Count == 0) return BadRequest("No files received.");
            if (string.IsNullOrWhiteSpace(workOrderId)) return BadRequest("workOrderId is required.");

            var swTotal = Stopwatch.StartNew();

            // -------------------------
            // 1) PLAN THE ROUTE
            // -------------------------
            var swPlan = Stopwatch.StartNew();
            var plan = _driveUploaderService.PlanBlobRouting(
                files, workOrderId, workOrderFolderId, imagesFolderId, pdfFolderId);
            swPlan.Stop();

            // Optional hermetic isolation: prefix blob paths with testPrefix + batch
            string? prefix = string.IsNullOrWhiteSpace(testPrefix) ? null
                            : $"{testPrefix.Trim().Trim('/')}/{plan.BatchId}";
            if (!string.IsNullOrEmpty(prefix))
            {
                foreach (var pf in plan.Files)
                {
                    pf.BlobPath = $"{prefix}/{pf.BlobPath}".Replace("//", "/");
                }
            }

            // -------------------------
            // 2 & 3) STAMP FOLDER IDs
            // -------------------------
            var swStamp = Stopwatch.StartNew();
            foreach (var p in plan.Files)
            {
                var ext = Path.GetExtension(p.FileName)?.ToLowerInvariant() ?? string.Empty;
                if (ext is ".jpg" or ".jpeg" or ".png")
                {
                    await _driveUploaderService.UpdateFolderIdsInPartsDocumentAsync(
                        fileName: p.FileName,
                        extension: ext,
                        workOrderId: workOrderId,
                        workOrderFolderId: workOrderFolderId,
                        expenseFolderId: expensesFolderId,
                        imagesFolderId: imagesFolderId,
                        blobPath: p.BlobPath,
                        ct: ct);
                }
                else if (ext == ".pdf")
                {
                    await _driveUploaderService.UpdateFolderIdsInPDFDocumentAsync(
                        fileName: p.FileName,
                        extension: ext,
                        workOrderId: workOrderId,
                        workOrderFolderId: workOrderFolderId,
                        pdfFolderId: pdfFolderId,
                        blobPath: p.BlobPath,
                        ct: ct);
                }
                else
                {
                    Log.Warning("Skipping unsupported file type for folder stamping: {File}", p.FileName);
                }
            }
            swStamp.Stop();

            // -------------------------
            // 4) UPLOADS with bounded parallelism (duplicate-safe)
            // -------------------------
            var swUpload = Stopwatch.StartNew();
            var degree = Math.Max(1, Math.Min(maxConcurrency ?? 6, 16)); // clamp 1..16
            var gate = new SemaphoreSlim(degree);

            // Map filename -> queue of IFormFile (handles duplicates deterministically)
            var nameQueues = files
                .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new Queue<IFormFile>(g), StringComparer.OrdinalIgnoreCase);

            var uploadResults = new List<(string file, bool ok, string? err, long ms)>(plan.Files.Count);
            var tasks = new List<Task>(plan.Files.Count);

            foreach (var p in plan.Files)
            {
                if (!nameQueues.TryGetValue(p.FileName, out var q) || q.Count == 0)
                {
                    uploadResults.Add((p.FileName, false, "File not in request payload (or duplicate underflow)", 0));
                    continue;
                }
                var formFile = q.Dequeue();
                tasks.Add(UploadWithGateAsync(gate, p, formFile, uploadResults, ct));
            }

            await Task.WhenAll(tasks);
            swUpload.Stop();

            var anyUploadFail = uploadResults.Any(r => !r.ok);

            if (failFast && anyUploadFail)
            {
                swTotal.Stop();
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    plan.WorkOrderId,
                    plan.BatchId,
                    plannedCount = plan.Files.Count,
                    uploadedOk = uploadResults.Count(r => r.ok),
                    uploadedFailed = uploadResults.Where(r => !r.ok).Select(r => new { r.file, r.err, r.ms }),
                    enqueued = false,
                    skippedPostBatch = true,
                    timingsMs = new
                    {
                        plan = swPlan.ElapsedMilliseconds,
                        stamp = swStamp.ElapsedMilliseconds,
                        upload = swUpload.ElapsedMilliseconds,
                        total = swTotal.ElapsedMilliseconds
                    },
                    maxConcurrencyUsed = degree,
                    testPrefixApplied = prefix
                });
            }

            // -------------------------
            // 5) Build args, detect receipts, enqueue jobs
            // -------------------------
            var args = new ProcessBatchArgs(
                workOrderId,
                plan.BatchId,
                workOrderFolderId,
                imagesFolderId,
                pdfFolderId,
                expensesFolderId,
                plan.Files.Select(f =>
                    new PlannedFileInfo(
                        f.FileName,
                        (f.Container ?? "").Trim().ToLowerInvariant(),
                        (f.BlobPath ?? "").Replace('\\', '/').TrimStart('/'))
                ).ToList()
            );

            // Identify receipt images (then chain parse after upload)
            var receiptFiles = plan.Files
                .Where(f => f.Container.Equals("images-receipts", StringComparison.OrdinalIgnoreCase))
                .Where(f =>
                {
                    var ext = Path.GetExtension(f.FileName);
                    return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();


            string uploadJobId;
            string? parseJobId = null;


            // Always enqueue ClearAndUpload first
            uploadJobId = _jobs.Enqueue(() =>
                _driveUploaderService.ClearAndUploadBatchFromBlobAsync(args, CancellationToken.None));

            if (receiptFiles.Count > 0)
            {
                // Chain parse/email AFTER upload finishes
                parseJobId = _jobs.ContinueJobWith(uploadJobId, () =>
                    _driveUploaderService.ParseReceiptBatchFromBlobAndEmailAsync(
                        args, "carrieziegler1973@gmail.com", CancellationToken.None));

                Log.Information("☁️ Enqueued upload job {UploadJobId} then 🧾 parse/email job {ParseJobId} for Batch {BatchId}",
                    uploadJobId, parseJobId, plan.BatchId);
            }
            else
            {
                Log.Information("☁️ Enqueued upload job {UploadJobId} (no receipts) for Batch {BatchId}",
                    uploadJobId, plan.BatchId);
            }

            swTotal.Stop();

            // Background work queued → 202 Accepted
            return Accepted(new
            {
                plan.WorkOrderId,
                plan.BatchId,
                plannedCount = plan.Files.Count,
                uploadedOk = uploadResults.Count(r => r.ok),
                uploadedFailed = uploadResults.Where(r => !r.ok).Select(r => new { r.file, r.err, r.ms }),
                enqueued = true,
                uploadJobId,
                parseJobId,
                willParseReceipts = receiptFiles.Count > 0,
                timingsMs = new
                {
                    plan = swPlan.ElapsedMilliseconds,
                    stamp = swStamp.ElapsedMilliseconds,
                    upload = swUpload.ElapsedMilliseconds,
                    total = swTotal.ElapsedMilliseconds
                },
                maxConcurrencyUsed = degree,
                testPrefixApplied = prefix
            });

            // -------------------------
            // local helper
            // -------------------------
            async Task UploadWithGateAsync(
                SemaphoreSlim s,
                PlannedFileInfo planned,
                IFormFile formFile,
                List<(string file, bool ok, string? err, long ms)> results,
                CancellationToken token)
            {
                await s.WaitAsync(token);
                var sw = Stopwatch.StartNew();
                try
                {
                    await _driveUploaderService.UploadFileToBlobAsync(
                        planned.Container,
                        planned.BlobPath,
                        formFile,
                        token);

                    lock (results) results.Add((planned.FileName, true, null, sw.ElapsedMilliseconds));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Error(ex, "Upload failed for {Container}/{Path}", planned.Container, planned.BlobPath);
                    lock (results) results.Add((planned.FileName, false, ex.Message, sw.ElapsedMilliseconds));
                }
                finally
                {
                    sw.Stop();
                    s.Release();
                }
            }
        }


        [HttpPost("UploadAppFiles")]
        public async Task<IActionResult> UploadAppFiles(
            List<IFormFile> files,
            [FromQuery] string workOrderId,
            [FromQuery] string workOrderFolderId,
            [FromQuery] string pdfFolderId,
            [FromQuery] string imagesFolderId)
        {
            List<FileUpload> uploads;

            try
            {
                uploads = await _driveUploaderService.UploadFilesAsync(
                    files,
                    workOrderId,
                    workOrderFolderId,
                    pdfFolderId,
                    imagesFolderId
                );

                foreach (var upload in uploads)
                {
                    try
                    {
                        if (upload.Extension is ".jpg" or ".jpeg" or ".png")
                        {
                            await _driveUploaderService.UpdateFileUrlInPartsDocumentAsync(
                                upload.FileName,
                                upload.ResponseBodyId,
                                upload.Extension,
                                upload.WorkOrderId
                            );
                            upload.stupidLogErrors.Add("✅ DB update: parts document link set.");
                        }
                        else if (upload.Extension is ".pdf")
                        {
                            await _driveUploaderService.UpdateFileUrlInPDFDocumentAsync(new PDFUploadRequest
                            {
                                FileName = upload.FileName,
                                FileId = upload.ResponseBodyId,
                                WorkOrderId = upload.WorkOrderId,
                                UploadedBy = "iPad App",
                                Description = string.Empty
                            });
                            upload.stupidLogErrors.Add("✅ DB update: PDF document link set.");
                        }
                        else
                        {
                            var msg = $"⚠️ Skipping unsupported file type: {upload.FileName} ({upload.Extension})";
                            Log.Warning(msg);
                            upload.stupidLogErrors.Add(msg);
                        }
                    }
                    catch (Exception dbEx)
                    {
                        var errMsg = $"❌ Failed DB update for {upload.FileName}: {dbEx.Message}";
                        Log.Error(dbEx, errMsg);
                        upload.stupidLogErrors.Add(errMsg);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Entire UploadAppFiles operation failed");
                return StatusCode(500, new { message = "Upload failed", error = ex.Message });
            }

            return Ok(uploads);
        }
        [HttpPost("ListPDFFiles")]
        public async Task<IActionResult> ListPDFFiles(int sheetId, int? labourTypeID)
        {
            try
            {
                var fileMetadataList = await _driveUploaderService.ListFileUrlsAsync(sheetId, labourTypeID, null);
                if (fileMetadataList == null || !fileMetadataList.Any())
                {
                    return NotFound("No files found");
                }
                return Ok(fileMetadataList);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occurred while listing PDF files");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }


        [HttpPost("ListPDFFilesByTag")]
        public async Task<IActionResult> ListPDFFilesByTag(int sheetId, [FromBody] List<string> tags)
        {
            try
            {
                var fileMetadataList = await _driveUploaderService.ListFileUrlsAsync(sheetId, null, tags);
                if (fileMetadataList == null || !fileMetadataList.Any())
                {
                    return NotFound("No files found");
                }
                return Ok(fileMetadataList);
            }
            catch (Exception ex)
            {
                #if DEBUG
                                return StatusCode(500, $"Drive Error: {ex.Message} | {ex.InnerException?.Message}");
                #else
                        Log.Error(ex, "Error occurred while listing PDF files");
                        return StatusCode(500, "An error occurred while processing your request.");
                #endif
            }
        }




        [HttpPost("SendWorkOrderEmail")]
        public async Task<IActionResult> SendWorkOrderEmail([FromBody] DTOs.WorkOrdMailContent dto)
        {
            var resendKey = "re_exsqgshN_HidHMnaoQHNwGn7gn6yy6RbW";

            // ✅ Email format sanity check...........
            try
            {
                var _ = new MailAddress(dto.EmailAddress);
            }
            catch (FormatException)
            {
                return BadRequest("Invalid email format.");
            }

            var email = new
            {
                from = "service@taskfuel.app",
                to = dto.EmailAddress,
                subject = $"Work Order #{dto.WorkOrderNumber} for {dto.CustPath} Uploaded",
                html = $@"
                    <h2>Work Order Synced</h2>
                    <p><strong>Customer Path:</strong> {dto.CustPath}</p>
                    <p><strong>Description:</strong> {dto.WorkDescription}</p>
                    <p><strong>Work Order #{dto.WorkOrderNumber}</strong> is now live in Google Drive & Azure SQL.</p>
                    <p>You can view the uploaded files at this address:<br>
                    <a href='https://drive.google.com/drive/folders/1NmfDJ7Gyig9MhtQO29ZGqB4q1OfKMtBo'>
                        View WO#{dto.WorkOrderNumber} Files on Google Drive for  {dto.CustPath}
                    </a></p>
                  <p><em>Need access? Use the Raymar Google account already shared with this folder.</em></p>
                  <p><em>If you're not sure of the password, call me directly.</em></p>"
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", resendKey);

                var response = await client.PostAsJsonAsync("https://api.resend.com/emails", email);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("❌ Email failed: " + body);
                    return StatusCode((int)response.StatusCode, body);
                }

                Console.WriteLine("✅ Email sent.");
                return Ok("Email sent.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 Exception: " + ex.Message);
                return StatusCode(500, "Internal error: " + ex.Message);
            }
        }

        [HttpPost("SendWorkOrderEmailTwo")]
        public async Task<IActionResult> SendWorkOrderEmailTwo([FromBody] DTOs.WorkOrdMailContent dto)
        {
            var resendKey = "re_exsqgshN_HidHMnaoQHNwGn7gn6yy6RbW";
            //New Controller endpoint. ...new deploy.....
            // ✅ Email format sanity check
            try
            {
                var _ = new MailAddress(dto.EmailAddress);
            }
            catch (FormatException)
            {
                return BadRequest("Invalid email format.");
            }

            var email = new
            {
                from = "service@taskfuel.app",
                to = dto.EmailAddress,
                subject = $"Work Order #{dto.WorkOrderNumber} for {dto.CustPath} Uploaded",
                html = $@"
                    <h2>Work Order Synced</h2>
                    <p><strong>Customer Path:</strong> {dto.CustPath}</p>
                    <p><strong>Description:</strong> {dto.WorkDescription}</p>
                    <p><strong>Work Order #{dto.WorkOrderNumber}</strong> is now live in Google Drive & Azure SQL.</p>
                    <p>You can view the uploaded files at this address:<br>
                    <a href='https://drive.google.com/drive/folders/1NmfDJ7Gyig9MhtQO29ZGqB4q1OfKMtBo'>
                        View WO#{dto.WorkOrderNumber} Files on Google Drive for  {dto.CustPath}
                    </a></p>
                  <p><em>Need access? Use the Raymar Google account already shared with this folder.</em></p>
                  <p><em>If you're not sure of the password, call me directly.</em></p>"
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", resendKey);

                var response = await client.PostAsJsonAsync("https://api.resend.com/emails", email);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("❌ Email failed: " + body);
                    return StatusCode((int)response.StatusCode, body);
                }

                Console.WriteLine("✅ Email sent.");
                return Ok("Email sent.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 Exception: " + ex.Message);
                return StatusCode(500, "Internal error: " + ex.Message);
            }
        }

        [HttpGet("GetWorkOrder")]
        public async Task<IActionResult> GetWorkOrder(int sheetID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.GetWorkOrder(sheetID);

                if (result == null)
                {
                    return BadRequest($"Unable to retrieve work order. Sheet ID {sheetID} does not exist.");
                }

                return Ok("Work order retrieved successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving work order Sheet ID {sheetID}:  {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpGet("GetLabourLines")]
        public async Task<IActionResult> GetLabourLines(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetLabourLines(sheetID);

                if (result == null)
                {
                    return NotFound($"No labour found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving labour used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving labour.");
            }
        }


        [HttpGet("GetFees")]
        public async Task<IActionResult> GetFees(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetFees(sheetID);

                if (result == null)
                {
                    return NotFound($"No parts used found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving parts used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving parts used.");
            }
        }

        [HttpGet("GetMileage")]
        public async Task<IActionResult> GetMileage(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetMileage(sheetID);

                if (result == null)
                {
                    return NotFound($"No mileage used found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving mileage used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving mileage.");
            }
        }

        [HttpGet("GetPartsUsed")]
        public async Task<IActionResult> GetPartsUsed(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetPartsUsed(sheetID);

                if (result == null)
                {
                    return NotFound($"No parts used found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving parts used for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving parts used.");
            }
        }

        [HttpGet("DownloadWorkOrderHours")]
        public async Task<IActionResult> DownloadWorkOrderHours(
        [FromQuery] int sheetId,
        [FromQuery] int? technicianId = null,
        [FromQuery] int? labourTypeId = null)
        {
            try
            {
                var result = await _workOrderService.GetLabourLines(sheetId, technicianId, labourTypeId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                await _workOrderService.LogFailedSync(sheetId, ex.Message);
                return StatusCode(500, "Could not download work order.");
            }
        }

        [HttpGet("DownloadWorkOrder/{sheetId}")]
        public async Task<IActionResult> DownloadWorkOrder(int sheetId)
        {
            try
            {
                var workOrdNumber = await _workOrderService.GetWorkOrderNumber(sheetId);
                var result = new WorkOrderDetails
                {
                    WorkOrderNumber = workOrdNumber,
                    SheetId = sheetId,
                    TechWorkOrder = await _technicianService.GetTechsByWorkOrder(sheetId),
                    Billing = await _workOrderService.GetBillingMin(sheetId) ?? new BillingMin(),
                    Parts = await _workOrderService.GetPartsUsed(sheetId),
                    Fees = await _workOrderService.GetFees(sheetId),
                    MileageAndTime = await _workOrderService.GetMileage(sheetId),
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                await _workOrderService.LogFailedSync(sheetId, ex.Message);
                return StatusCode(500, "Could not download work order.");
            }
        }


        [HttpGet("GetBilling")]
        public async Task<IActionResult> GetBilling(int sheetID)
        {
            try
            {
                var result = await _workOrderService.GetBillingMin(sheetID);

                if (result == null)
                {
                    return NotFound($"No billing found for Sheet ID {sheetID}.");
                }

                return Ok(result); // ✅ Return the full JSON list
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving billing for Sheet ID {sheetID}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving billing.");
            }
        }


        [HttpGet("GetWorkOrderCards")]
        public async Task<IActionResult> GetWorkOrderCards(
            [FromQuery] DateTime? dateUploadedStart,
            [FromQuery] DateTime? dateUploadedEnd,
            [FromQuery] DateTime? dateTimeCompletedStart,
            [FromQuery] DateTime? dateTimeCompletedEnd,
            [FromQuery] int technicianID,
            [FromQuery] string workOrderStatus = "COMPLETE",
            [FromQuery] int? customerId = null)
        {
            try
            {
                var result = await _workOrderService.GetWorkOrderCardsAsync(
                    dateUploadedStart,
                    dateUploadedEnd,
                    dateTimeCompletedStart,
                    dateTimeCompletedEnd,
                    technicianID, 
                    workOrderStatus,
                    customerId
                );

                // ✅ Return 200 even if the list is empty
                return Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving work order cards: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving work order cards.");
            }
        }

        //[HttpGet("GetWorkOrderBriefDetails")]
        //public async Task<IActionResult> GetWorkOrderBriefDetails()
        //{
        //    try
        //    {
        //        // Call your service to create the work order and attach billing information
        //        var result = await _workOrderService.GetWorkOrderBriefDetails();

        //        foreach (var item in result)
        //        {
        //            item.Techs = await _technicianService.GetTechsByWorkOrder(item.SheetID);

        //        }

        //        if (result == null)
        //        {
        //            return BadRequest($"Unable to retrieve work orders.");
        //        }

        //        return Ok(result);
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error($"Error retrieving work orders:  {ex.Message}");
        //        return StatusCode(500, "An error occurred while launching the work orders.");
        //    }
        //}

        [HttpPost("RemoveBillFromWorkOrder")]
        public async Task<IActionResult> RemoveBillFromWorkOrder(int billID, int sheetId)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.RemoveBillFromWorkOrder(billID, sheetId);

                if (!result)
                {
                    return BadRequest("Unable to remove bill from work order");
                }

                return Ok("Bill removed from work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing bill {billID} from work order {sheetId}: {ex.Message}");
                return StatusCode(500, $"An error occurred while removing the bill {billID} from the work order {sheetId}.");
            }
        }

        [HttpPost("UpdateWOStatus")]
        public async Task<IActionResult> UpdateWOStatus(int sheetId, string workOrderStatus, string deviceId)
        {
            try
            {
                await _workOrderService.UpdateWOStatus(sheetId, workOrderStatus, deviceId);

                return Ok("Work order status updated.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating WO status to {workOrderStatus} for SheetID {sheetId}: {ex.Message}");
                return StatusCode(500, "An error occurred while updating the work order status.");
            }
        }

        [HttpPost("RemoveLbrFromWorkOrder")]
        public async Task<IActionResult> RemoveLbrFromWorkOrder(int lbrID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.RemoveLbrFromWorkOrder(lbrID);

                if (!result)
                {
                    return BadRequest("Unable to remove labour from work order");
                }

                return Ok("Labour removed from work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing labour {lbrID} from work order: {ex.Message}");
                return StatusCode(500, $"An error occurred while removing the labour line:  {lbrID} from the work order.");
            }
        }

        [HttpPost("AddPartToWorkorder")]
        public async Task<IActionResult> AddPartToWorkorder([FromBody] PartsUsed partDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.AddPartToWorkOrder(partDTO);

                if (!result)
                {
                    return BadRequest("Unable to add part to work order");
                }

                return Ok("Part added to work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpPost("RemovePartFromWorkorder")]
        public async Task<IActionResult> RemovePartFromWorkOrder(int partID, int sheetId)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.RemovePartFromWorkOrder(partID, sheetId);

                if (!result)
                {
                    return BadRequest("Unable to add part to work order");
                }

                return Ok("Part added to work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpPost("AddLbrToWorkOrder")]
        public async Task<IActionResult> AddLbrToWorkOrder([FromBody] LabourLine labourDTO)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.AddLbrToWorkOrder(labourDTO);

                if (!result)
                {
                    return BadRequest("Unable to add part to work order");
                }

                return Ok("Part added to work order successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Error launching work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }
        [HttpPost("AddTechToWorkOrder")]
        public async Task<IActionResult> AddTechToWorkOrder(int techID, int sheetID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.AddTechToWorkOrder(techID, sheetID);

                if (!result)
                {
                    return BadRequest("Unable to add technician to work order");
                }

                return Ok("Technician added successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding tech ID {techID} to work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

        [HttpPost("DeleteTechFromWorkOrder")]
        public async Task<IActionResult> DeleteTechFromWorkOrder(int techID, int sheetID)
        {
            try
            {
                // Call your service to create the work order and attach billing information
                var result = await _workOrderService.DeleteTechFromWorkOrder(techID, sheetID);

                if (!result)
                {
                    return BadRequest("Unable to remove technician to work order");
                }

                return Ok("Technician removed successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing tech ID {techID} from work order: {ex.Message}");
                return StatusCode(500, "An error occurred while launching the work order.");
            }
        }

    }
       
}
