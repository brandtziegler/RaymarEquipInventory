using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{

    //This is all for parts used...
    public class PartsUsed
    {
        public int PartUsedId { get; set; }

        public int InventoryId { get; set; }

        public int? QtyUsed { get; set; }

        public int? SheetId { get; set; }


        public string? Notes { get; set; } = string.Empty;

        public InventoryData InventoryData { get; set; } = new InventoryData();
    }
}
