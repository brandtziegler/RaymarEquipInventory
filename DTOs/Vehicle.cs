namespace RaymarEquipmentInventory.DTOs
{
    public class Vehicle
    {
        public string SamsaraVehicleID { get; set; } = "";
        public string VehicleName { get; set; } = "";
        public string VehicleVIN { get; set; } = "";
        public bool isActive { get; set; } = false;

        public Double? CurXLocation { get; set; } = 0;
        public Double? CurYLocation { get; set; } = 0;
      
    }
}
