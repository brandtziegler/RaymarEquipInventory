namespace RaymarEquipmentInventory.Settings
{
    namespace YourApiProject.Settings
    {
        public class SamsaraApiConfig
        {
            public string BaseUrl { get; set; }
            //public string BearerToken { get; set; }

            public SamsaraApiConfig(string baseUrl)
            {
                BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
                //BearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
            }
        }
    }
}
