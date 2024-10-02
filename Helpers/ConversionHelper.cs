namespace RaymarEquipmentInventory.Helpers
{
    public static class ConversionHelper
    {
        public static long DateToUnix(string input)
        {
            // Convert a string to a Unix timestamp
            if (string.IsNullOrEmpty(input))
                return 0;
            else
            {
                var unixTime = new DateTimeOffset(DateTime.Parse(input)).ToUnixTimeSeconds();
                return unixTime;
            }

        }


    }

}
