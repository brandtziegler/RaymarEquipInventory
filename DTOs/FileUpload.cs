namespace RaymarEquipmentInventory.DTOs
{
    public class FileUpload
    {

        public string FileName { get; set; } = "";
        public string ResponseBodyId { get; set; } = "";
        public string Extension { get; set; } = "";
        public string WorkOrderId { get; set; } = "";
        public List<string> stupidLogErrors { get; set; } = new List<string>();
    }
}
