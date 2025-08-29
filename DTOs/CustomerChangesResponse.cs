namespace RaymarEquipmentInventory.DTOs
{
    public class CustomerChangesResponse
    {
        public List<CustomerMin> Items { get; set; } = new();
        public int Count { get; set; }
        public bool HasMore { get; set; }
        public string ServerWatermark { get; set; } = ""; // ISO 8601 UTC
    }
}
