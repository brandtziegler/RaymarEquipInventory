using RaymarEquipmentInventory.Models;

public partial class PartsUsed
{
    public int PartUsedId { get; set; }

    public int QtyUsed { get; set; }

    // Use required keyword for non-nullable properties
    public required string QuickBooksInvId { get; set; } = string.Empty;

    public required string Notes { get; set; } = string.Empty;

    public int? SheetId { get; set; }

    public virtual InventoryDatum? QuickBooksInv { get; set; }

    public virtual WorkOrderSheet? Sheet { get; set; }
}