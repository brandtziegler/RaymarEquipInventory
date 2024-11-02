namespace RaymarEquipmentInventory.DTOs
{
    public class InventoryForDropdown
    {
        public string? QuickBooksInvId { get; set; }
        public int QtyAvailable { get; set; } = 0;

        public string? PartNumber { get; set; }
        private string? _itemName;

        // Original ItemName with custom setter to clean data on intake
        public string? ItemName
        {
            get => _itemName;
            set
            {
                // Clean the input string: Remove unwanted characters, trim whitespace, etc.
                _itemName = value != null ? CleanItemName(value) : null;
            }
        }

        public string ItemNameWithPartNum => $"{PartNumber}-{ItemName}";

        // Method to clean up the item name-small change.
        private string CleanItemName(string rawName)
        {
            return rawName
                .Replace("\\\"", " in.")               // Replace escaped inches with "in."
                .Replace(" ,", ",")                    // Remove spaces before commas
                .Replace("  ", " ")                    // Replace double spaces with single spaces
                .Trim()                                // Trim leading and trailing whitespace
                .Replace(" , ", ", ")                  // Standardize comma spacing
                .Replace(" .", ".")                    // Remove space before periods
                .Replace(", ,", ",")                   // Handle any accidental double commas
                .Replace("\"", " in.");                // Replace any remaining inches symbol with word
        }

    }
}
