namespace RaymarEquipmentInventory.DTOs
{

    public class PDFUploadRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string FileId { get; set; } = string.Empty;
        public string WorkOrderId { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = "iPad App";
        public string Description { get; set; } = string.Empty;
    }

}
