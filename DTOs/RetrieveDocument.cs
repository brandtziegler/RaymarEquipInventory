namespace RaymarEquipmentInventory.DTOs
{
    public class RetrieveDocument
    {

        public int DocumentID { get; set; }       // Unique ID of the document
        public int SheetID { get; set; }
        public string FileName { get; set; } = "";     // Name of the file
        public string FileType { get; set; } = "";     // Document type (e.g., "PDF", "JPG")
        public string FileURL { get; set; } = "";     // URL where the document is stored (Azure blob storage)
       
        public string DocumentType { get; set; } = "";     // Document type (e.g., "PDF", "JPG")    
        public DateTime UploadDate { get; set; }  // W


    }
}
