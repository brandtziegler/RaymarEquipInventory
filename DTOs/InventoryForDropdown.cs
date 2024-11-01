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

        // Method to clean up the item name
        private string CleanItemName(string rawName)
        {
            return rawName
                .Replace("\\\"", "\"")                // Convert escaped inches \" to actual "
                .Replace(" ,", ",")                   // Remove spaces before commas
                .Replace("  ", " ")                   // Replace double spaces with single spaces
                .Trim()                               // Trim leading and trailing whitespace
                .Replace(" , ", ", ")                 // Standardize comma spacing
                .Replace(" .", ".")                   // Remove space before periods
                .Replace(" ,", ",")                   // Remove spaces before commas
                .Replace(", ,", ",")                  // Handle any accidental double commas
                .Replace(" ,", ",")                   // Fix any lingering space-comma issues
                .Replace(", ,", ",")                  // Catch double commas, just in case
                .Replace("\"", " in.");            // Replace inches symbol with word
        }

    }
}
