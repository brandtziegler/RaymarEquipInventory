using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IDriveUploaderService
    {
        Task UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId);

        Task<List<DTOs.FileMetadata>> ListFileUrlsAsync();
        List<string> VerifyAndSplitPrivateKey();
    }
}