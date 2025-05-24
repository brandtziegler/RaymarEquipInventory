using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrderFee
    {
        public int TechnicianWorkOrderID { get; set; }
        public int FlatLabourID { get; set; }
        public int LabourTypeID { get; set; }
        public int Qty { get; set; }
        public string WorkDescription { get; set; } = string.Empty;
    }

}
