using RaymarEquipmentInventory.Models;

namespace RaymarEquipmentInventory.DTOs
{
    public class TravelLog
    {
        public int SheetId { get; set; } // FK to TechnicianWorkOrder
        public DateTime DateOfTravel { get; set; }

        public DateTime StartTravel { get; set; }
        public DateTime FinishTravel { get; set; }

        public int StartOdometerKm { get; set; }
        public int FinishOdometerKm { get; set; }

        public int TotalDistance { get; set; }

        public int TotalHours { get; set; }
        public int TotalMinutes { get; set; }

        public int TotalOTHours { get; set; }
        public int TotalOTMinutes { get; set; }

        public bool IsOvertime { get; set; }

        public int? SegmentNumber { get; set; } // Optional — set if multiple per day
    }

}
