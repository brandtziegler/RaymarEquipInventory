namespace RaymarEquipmentInventory.DTOs
{
    public class TripLog
    {
        public string SamsaraVehicleID { get; set; } = "";
        public int VehicleID { get; set; }

        public int SheetID { get; set; }

        public long StartMs { get; set; }           // Start time in Unix milliseconds
        public long EndMs { get; set; }             // End time in Unix milliseconds

        // Convert Unix time to the system's local time zone for start time
        public DateTime StartDateTime => ConvertToLocalTime(DateTimeOffset.FromUnixTimeMilliseconds(StartMs).UtcDateTime);

        // Convert Unix time to local time, or set to January 1, 2100 if endMs is max value
        public DateTime EndDateTime => EndMs == long.MaxValue
            ? new DateTime(2100, 1, 1)  // Set the end time to Jan 1, 2100 if it's max
            : ConvertToLocalTime(DateTimeOffset.FromUnixTimeMilliseconds(EndMs).UtcDateTime);

        public string StartLocation { get; set; }    // Start address from JSON
        public string EndLocation { get; set; }      // End address from JSON

        public double StartKM { get; set; }         // Start odometer converted to kilometers
        public double EndKM { get; set; }           // End odometer converted to kilometers
        public double DistanceKM { get; set; }      // Distance traveled in kilometers

        public int VehicleTravelID { get; set; } = 0;

        public TripLog(string samsaraID, int vehicleID, int sheetID, long startMs, long endMs, string startLocation, string endLocation, long startOdometer, long endOdometer)
        {
            SamsaraVehicleID = samsaraID;
            VehicleID = vehicleID;
            SheetID = sheetID;
            StartMs = startMs;
            EndMs = endMs;
            StartLocation = startLocation;
            EndLocation = endLocation;

            // Convert odometers from meters to kilometers
            StartKM = ConvertToKilometers(startOdometer);
            EndKM = ConvertToKilometers(endOdometer);

            // Convert distance from meters to kilometers
            DistanceKM = EndKM - StartKM;
        }

        public Vehicle Vehicle { get; set; } = new Vehicle();

        // Helper method to convert meters/odometer to kilometers
        private double ConvertToKilometers(long meters)
        {
            return meters / 1000.0;
        }

        // Helper method to convert from UTC to the system's local time zone
        private DateTime ConvertToLocalTime(DateTime utcDateTime)
        {
            TimeZoneInfo localZone = TimeZoneInfo.Local;  // Dynamically get the server's time zone
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, localZone);
        }
    }
}


