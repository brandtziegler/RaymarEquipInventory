namespace RaymarEquipmentInventory.DTOs
{
    public class InventoryChangesResponse
    {
        public string Cursor { get; set; } = "";
        public bool HasMore { get; set; } = false;
        public List<InventoryChangeDto> Upserts { get; set; } = new List<InventoryChangeDto>();
    }
}
