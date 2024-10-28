namespace RaymarEquipmentInventory.DTOs
{
    public class DocumentType
    {
        public int DocumentTypeId { get; set; } // Primary Key

        public string DocumentTypeName { get; set; } = ""; // Foreign Key
        public string MimeType { get; set; } = ""; // Foreign Key
    }
}
