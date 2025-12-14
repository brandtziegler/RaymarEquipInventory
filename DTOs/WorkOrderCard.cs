using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrderCard
    {
        public int SheetID { get; set; }
        public int WorkOrderNumber { get; set; }
        public string WorkDescription { get; set; } = "";
        public string WorkOrderStatus { get; set; } = "";
        public DateTime? DateUploaded { get; set; }
        public DateTime? DateTimeCompleted { get; set; }
        public string UnitNo { get; set; } = "";

        public bool HasInvoice { get; set; } = false;   // ✅ default false

        public int? CustomerID { get; set; }
        public string PathToRoot { get; set; } = "";
        public string ParentCustomerName { get; set; } = "";
        public string ChildCustomerName { get; set; } = "";
        public string LastSyncEventType { get; set; } = "";
        public DateTime? LastSyncTimestamp { get; set; }
    }
}
