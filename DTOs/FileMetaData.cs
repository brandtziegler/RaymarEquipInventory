namespace RaymarEquipmentInventory.DTOs
{
    public class FileMetadata
    {
        public string Id { get; set; } = "";
        public string PDFName { get; set; } = "";
        public string fileDescription { get; set; } = "";
        public bool IsTemplateFile { get; set; }
        public string dateLastEdited { get; set; } = "";
        public string lastEditTechName { get; set; } = "";
        public int sheetId { get; set; } = 0;
        public string MimeType { get; set; } = "";
        public string WebViewLink { get; set; } = "";
        public string WebContentLink { get; set; } = "";
    }
}
