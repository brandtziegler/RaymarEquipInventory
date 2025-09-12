using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    //This is all for parts used...
    public class WorkOrderFeesGroup
    {
        public List<WorkOrderFee> WorkOrderFeesList { get; set; } = new List<WorkOrderFee>();
        public List<FeeVisibilityDto>? FeeVisibilityList { get; set; } = new(); // NEW

    }

}
