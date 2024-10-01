namespace RaymarEquipmentInventory.DTOs
{
    public class TripLog
    {
        public string SamsaraVehicleID { get; set; } = "";
        public Int32 VehicleID { get; set; }
        public DateTime? TripStartTime { get; set; } = null;
        public DateTime? TripEndTime { get; set; } = null;
        public Double? TripStartOdometer { get; set; } = 0;
        public Double? TripEndOdometer { get; set; } = 0;
        public string StartingLocation { get; set; } = "";
        public string EndingLocation { get; set; } = "";

        public Int32 VehicleTravelID { get; set; } = 0;

    }
}
