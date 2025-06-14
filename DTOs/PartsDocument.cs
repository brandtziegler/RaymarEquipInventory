using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{

    //This is all for parts used...
    public class PartsDocument
    {
        public int PartUsedId { get; set; }
        public int PartsDocumentId { get; set; }
        public string? FileName { get; set; } = string.Empty;
        public string? Description { get; set; } = string.Empty;

        public string? UploadedBy { get; set; } = string.Empty;
        public DateTime? UploadedDate { get; set; }

    }
}
