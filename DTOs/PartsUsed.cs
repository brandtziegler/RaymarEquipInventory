using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{

    //This is all for parts used...
    public class PartsUsed
    {
        public int PartUsedId { get; set; }

        public int? QtyUsed { get; set; }

        public string? Notes { get; set; } = string.Empty;

        public int? SheetId { get; set; }

        public int? InventoryID { get; set; }

        public string? PartNumber { get; set; } = string.Empty;

        public string? Description { get; set; } = string.Empty;
        

        public DateTime? UploadDate { get; set; }


        public string? UploadedBy { get; set; } = string.Empty;


        public InventoryData InventoryData { get; set; } = new InventoryData();

        public bool Deleted { get; set; }
    }
}
