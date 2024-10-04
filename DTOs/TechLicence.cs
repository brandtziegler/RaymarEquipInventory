namespace RaymarEquipmentInventory.DTOs
{
    public class TechLicence
    {
        public int LicenseID { get; set; }
        public string LicenseName { get; set; } = "";
        public int TechnicianID { get; set; } 

        public DateTime? IssuedDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string LicenceUrl { get; set; } = "";

      
    }
}
