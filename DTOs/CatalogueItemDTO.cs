namespace RaymarEquipmentInventory.DTOs
{
    public sealed class CatalogItemDto
    {
        public string? ListID { get; set; }
        public string? Name { get; set; }
        public string? FullName { get; set; }
        public string? EditSequence { get; set; }
        public decimal? SalesPrice { get; set; }
        public decimal? PurchaseCost { get; set; }
        public string? SalesDesc { get; set; }
        public string? PurchaseDesc { get; set; }
        public DateTime? TimeModified { get; set; }
        public string Type { get; set; } = "Service";
        public bool? IsActive { get; set; }
    }


}
