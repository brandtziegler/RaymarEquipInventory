using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{

    //This is all for parts used...
    public class PartsUsed
    {
        public int PartUsedId { get; set; }

        public int? QtyUsed { get; set; }

        public string? QuickBooksInvId { get; set; } = string.Empty;

        public string? Notes { get; set; } = string.Empty;  
    }
}
