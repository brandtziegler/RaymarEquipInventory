using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class RegularLabourLine
    {
        public int LabourId { get; set; }
        public int TechnicianWorkOrderID { get; set; }
        public DateTime DateOfLabor { get; set; }
        public DateTime? StartLabor { get; set; }
        public DateTime? FinishLabor { get; set; }

        public string WorkDescription { get; set; } = "";

        public int TotalHours { get; set; } = 0;
        public int TotalMinutes { get; set; } = 0;
        public int TotalOTHours { get; set; } = 0;
        public int TotalOTMinutes { get; set; } = 0;

        public int LabourTypeID { get; set; } = 1;
    }

}
