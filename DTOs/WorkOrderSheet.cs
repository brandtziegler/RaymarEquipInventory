using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrdSheet
    {
        public int SheetId { get; set; } = 0;
        public string? DeviceId { get; set; }
        public int TechnicianID { get; set; } = 8;
        public int WorkOrderNumber { get; set; } = 0;
        public DateTime? DateTimeCreated { get; set; }
        public string WorkOrderStatus { get; set; } = "";
        public DateTime? DateTimeStarted { get; set; }
        public DateTime? DateTimeCompleted { get; set; }
        public string WorkDescription { get; set; } = "";
    }
}
