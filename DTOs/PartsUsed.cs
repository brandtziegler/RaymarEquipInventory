using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{
    public class PartsUsed
    {
        public int PartUsedId { get; set; }

        public int QtyUsed { get; set; }

        public string QuickBooksInvId { get; set; }

        public string Notes { get; set; }
    }
}
