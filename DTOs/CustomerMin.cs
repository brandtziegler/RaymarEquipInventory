namespace RaymarEquipmentInventory.DTOs
{
    public class CustomerMin
    {
        public string ID { get; set; } = "";
        public string? ParentID { get; set; }
        public string? CustomerName { get; set; }
        public string? ParentName { get; set; }
        public string? FullAddress { get; set; }
        public DateTime LastUpdated { get; set; }   // UTC
    }
}
