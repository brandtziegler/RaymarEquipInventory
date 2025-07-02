using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{
    public class GoogleDriveFolderDTO
    {
        public int SheetID {get; set; } = 0;
        public string WorkOrderFolderId { get; set; } = string.Empty;
        public string PdfFolderId { get; set; } = string.Empty;
        public string ImagesFolderId { get; set; } = string.Empty;
    }
}
