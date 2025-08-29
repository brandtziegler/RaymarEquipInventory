namespace RaymarEquipmentInventory.DTOs
{
    public class FlatLabour
    {
        public int FlatLabourId { get; set; }
        public int LabourTypeId { get; set; }
        public string LabourName { get; set; } = "";
        public string LabourDescription { get; set; } = "";
        public bool IsActive { get; set; } = false;
        public decimal Price { get; set; }
    }
}
