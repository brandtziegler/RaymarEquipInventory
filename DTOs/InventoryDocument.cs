﻿namespace RaymarEquipmentInventory.DTOs
{
    public class InventoryDocument
    {
        public int InventoryDocumentId { get; set; } // Primary Key

        public int InventoryId { get; set; } // Foreign Key

        public int DocumentTypeId { get; set; } // ID of the document type

        public string FileName { get; set; } = ""; // Name of the file

        public string FileURL { get; set; } = ""; // URL to the file

        public DateTime UploadDate { get; set; } // Date the file was uploaded

        public string UploadedBy { get; set; } = "";// Who uploaded the file
    }
}
