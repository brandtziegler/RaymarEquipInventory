using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class WorkOrderBriefDetails
    {
        public int SheetID { get; set; } = 0;
        public int WorkOrderNumber { get; set; } = 0;
        public DateTime? DateTimeCreated { get; set; }
        public DateTime? DateTimeCompleted { get; set; }
        public DateTime? DateTimeStarted { get; set; }
        public string WorkOrderStatus { get; set; } = "";
        public string WorkOrderType { get; set; } = "";
        public string TypeIconName { get; set; } = "";
        public string TypeHexColor { get; set; } = "";
        public string WorkLocation { get; set; } = "";
        public string PONo { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ParentName { get; set; } = "";
        public string IconName { get; set; } = "";
        public string HexColor { get; set; } = "";
        public string FullAddress { get; set; } = "";
        public string VehicleName { get; set; } = "";

        public List<Tech> Techs{ get; set; } = new List<Tech>();

    }
}
