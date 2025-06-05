using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IDriveUploaderService
    {
        Task UploadFilesAsync(List<IFormFile> files, string custPath, string workOrderId);
        List<string> VerifyAndSplitPrivateKey();
    }
}