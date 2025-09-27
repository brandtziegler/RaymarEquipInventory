using RaymarEquipmentInventory.DTOs;
using System;
using static RaymarEquipmentInventory.Services.DriveUploaderService;

namespace RaymarEquipmentInventory.Services
{
    public interface IDriveUploaderService
    {


        Task ClearImageFolderAsync(string custPath, string workOrderId);
        Task UpdateFileUrlInPartsDocumentAsync(string fileName, string fileId, string extension, string workOrderId);

        Task UpdateFileUrlInPDFDocumentAsync(PDFUploadRequest request);
        Task<List<DTOs.FileMetadata>> ListFileUrlsAsync(int sheetId, int? labourTypeId, List<string> tags);

        Task<DTOs.GoogleDriveFolderDTO> PrepareGoogleDriveFoldersAsync(string custPath, string workOrderId);
        Task<List<FileUpload>> UploadFilesAsync(List<IFormFile> files, string workOrderId, string workOrderFolderId, string pdfFolderId, string imagesFolderId);
        Task<string?> BackupDatabaseToGoogleDriveAsync(CancellationToken ct = default);

        Task<ReceiptConfirm> ParseReceiptsBuildCsvAsync(List<IFormFile> files, CancellationToken ct = default);

        Task<(MemoryStream Csv, ReceiptConfirm Confirm)> ParseReceiptsAndReturnCsvAsync(List<IFormFile> files, CancellationToken ct = default);
        Task<List<DTOs.FileMetadata>> ListActiveFileUrlsAsync(int sheetId);
        UploadPlan PlanBlobRoutingFromClient(
            IEnumerable<(string FileName, string? Kind)> files,
            string workOrderId,
            string? workOrderFolderId,
            string? imagesFolderId,
            string? pdfFolderId,
            string? batchId);

        UploadPlan PlanBlobRouting(
             List<IFormFile> files,
             string workOrderId,
             string workOrderFolderId,
             string imagesFolderId,
             string pdfFolderId,
             string? batchId = null);

        Task ClearAndUploadBatchFromBlobAsync(ProcessBatchArgs args, CancellationToken ct = default);

        Task UpdateFolderIdsInPartsDocumentAsync(
        string fileName,
        string extension,
        string workOrderId,
        string workOrderFolderId,
        string expenseFolderId,
        string imagesFolderId,
        string blobPath,
        CancellationToken ct = default);

        Task ClearImageFolderNewAsync(string imagesFolderId);

        Task DeleteBatchBlobsAsync(
   IEnumerable<PlannedFileInfo> files, // args.Files
   string workOrderId,
   string batchId,
   CancellationToken ct);


        Task<PDFSyncResult> SyncTemplatesToSqlAsync(CancellationToken ct = default);

        // NEW: Hangfire-safe wrapper (no params, no CancellationToken)
        Task<PDFSyncResult> SyncTemplatesToSqlJob();

        Task ParseReceiptBatchFromBlobAndEmailAsync(
    ProcessBatchArgs args,
    string toEmail,
    CancellationToken ct = default);
        Task UpdateFolderIdsInPDFDocumentAsync(
            string fileName,
            string extension,
            string workOrderId,
            string workOrderFolderId,
            string pdfFolderId,
            string blobPath,
            CancellationToken ct = default);

        Task UploadFileToBlobAsync(
    string containerName,
    string blobPath,
    IFormFile file,
    CancellationToken ct = default);
    };





}