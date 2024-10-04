namespace RaymarEquipmentInventory.DTOs
{
    public class UploadDocument
    {
        public int DocumentID { get; set; }       // Unique ID of the document
        public int SheetID { get; set; }          // WorkOrderSheet to which the document belongs
        public string FileName { get; set; } = "";   // Name of the file
        public string FileType { get; set; } = "";     // Document type (e.g., "PDF", "JPG")
        public string FileURL { get; set; } = "";     // URL where the document is stored (Azure blob storage)
        public DateTime UploadDate { get; set; }  // When it was uploaded
    }
}
