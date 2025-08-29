namespace RaymarEquipmentInventory.DTOs
{
    public class LabourType
    {
        public int LabourTypeId { get; set; }
        public string LabourTypeDescription { get; set; } = "";
        public bool IsActive { get; set; } = false;
        public List<FlatLabour> FlatLabours { get; set; } = new();
    }
}
