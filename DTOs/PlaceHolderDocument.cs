namespace RaymarEquipmentInventory.DTOs
{
    public class PlaceHolderDocument
    {
        public int FileId { get; set; } // Primary Key

        public DocumentType DocType { get; set; } = new DTOs.DocumentType(); // Type of document

        public string FileName { get; set; } = ""; // Name of the file

        public string FileURL { get; set; } = ""; // URL to the file

        public DateTime UploadDate { get; set; } // Date the file was uploaded

        public string UploadedBy { get; set; } = "";// Who uploaded the file
    }
}
