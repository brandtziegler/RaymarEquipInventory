namespace RaymarEquipmentInventory.DTOs
{
    public sealed class InventoryItemDto
    {
        public string? ListID { get; set; }
        public string? FullName { get; set; }
        public string? EditSequence { get; set; }
        public decimal? QuantityOnHand { get; set; }
        public decimal? SalesPrice { get; set; }
        public decimal? PurchaseCost { get; set; }
        public DateTime? TimeModified { get; set; }
    }
}
