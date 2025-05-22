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

        public DateTime? DateLabourStarted { get; set; }  // optional — can derive
        public DateTime? DateLabourFinished { get; set; } // optional — can derive

        public string WorkDescription { get; set; } = "";

        public int TotalHours { get; set; }
        public int TotalMinutes { get; set; }
        public int TotalOTHours { get; set; }
        public int TotalOTMinutes { get; set; }

        public int LabourTypeID { get; set; }
    }

}
