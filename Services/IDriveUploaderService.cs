using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IDriveUploaderService
    {


        Task ClearImageFolderAsync(string custPath, string workOrderId);
        Task UpdateFileUrlInPartsDocumentAsync(string fileName, string fileId, string extension, string workOrderId);

        Task UpdateFileUrlInPDFDocumentAsync(PDFUploadRequest request);
        Task<List<DTOs.FileMetadata>> ListFileUrlsAsync(int sheetId);

        Task<DTOs.GoogleDriveFolderDTO> PrepareGoogleDriveFoldersAsync(string custPath, string workOrderId);
        Task<List<FileUpload>> UploadFilesAsync(List<IFormFile> files, string workOrderId, string workOrderFolderId, string pdfFolderId, string imagesFolderId);

    }
}