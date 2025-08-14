using RaymarEquipmentInventory.DTOs;
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

        UploadPlan PlanBlobRouting(
             List<IFormFile> files,
             string workOrderId,
             string workOrderFolderId,
             string imagesFolderId,
             string pdfFolderId,
             string? batchId = null);


        Task UpdateFolderIdsInPartsDocumentAsync(
        string fileName,
        string extension,
        string workOrderId,
        string workOrderFolderId,
        string imagesFolderId,
        string blobPath,
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
    }
}