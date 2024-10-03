namespace RaymarEquipmentInventory.Helpers
{
    public static class StringHelper
    {
        public static string CleanString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove non-printable characters and trim whitespace
            return new string(input.Where(c => !char.IsControl(c)).ToArray()).Trim();
        }

        // Method to safely parse decimals
        public static decimal? ParseDecimal(object input)
        {
            if (input == null || input == DBNull.Value)
                return null;

            if (decimal.TryParse(input.ToString(), out decimal result))
                return result;

            return null; // Or return a default value if needed
        }

        // Method to safely parse integers
        public static int? ParseInt(object input)
        {
            if (input == null || input == DBNull.Value)
                return null;

            if (int.TryParse(input.ToString(), out int result))
                return result;

            return null; // Or return a default value if needed
        }

        public static string? NullIfEmpty(string input)
        {
            return string.IsNullOrEmpty(input) ? null : input;
        }

        public static string? NullIfWhiteSpace(string input)
        {
            return string.IsNullOrWhiteSpace(input) ? null : input;
        }
    }
}
