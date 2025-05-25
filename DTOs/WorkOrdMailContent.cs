using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrdMailContent
    {
        public int SheetId { get; set; } = 0;
        public int WorkOrderNumber { get; set; } = 0;
        public string CustPath { get; set; } = "";
        public string WorkDescription { get; set; } = "";
    }
}
