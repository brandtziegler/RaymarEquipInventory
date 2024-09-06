namespace RaymarEquipmentInventory.DTOs
{
    public class InventoryForDropdown
    {
        public string? QuickBooksInvId { get; set; }
        public string? ItemNameWithPartNum { get; set; }
        public int QtyAvailable { get; set; } = 0;

    }
}
