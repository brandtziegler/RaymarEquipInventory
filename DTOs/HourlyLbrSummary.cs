using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class HourlyLbrSummary
    {
        public int TechnicianID { get; set; }
        public int LabourTypeID { get; set; } = 1;
        public List<RegularLabourLine> Labour { get; set; } = new List<RegularLabourLine>();
       
    }

}
