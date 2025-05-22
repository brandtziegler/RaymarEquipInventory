namespace RaymarEquipmentInventory.DTOs
{
    public class HourlyLabourType
    {
        public int LabourTypeID { get; set; }  // Primary Key
        public string LabourTypeDescription { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }
}
