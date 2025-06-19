using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IDriveUploaderService
    {
        Task<List<FileUpload>> UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId);

        Task UpdateFileUrlInPartsDocumentAsync(string fileName, string fileId, string extension, string workOrderId);
        Task<List<DTOs.FileMetadata>> ListFileUrlsAsync();
        List<string> VerifyAndSplitPrivateKey();
    }
}