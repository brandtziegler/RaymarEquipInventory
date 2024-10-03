namespace RaymarEquipmentInventory.DTOs
{
    public class Tech
    {
        public int TechnicianID { get; set; } 
        public string Notes { get; set; } = "";
        public decimal HourlyRate { get; set; } = 0;

        public Person Person { get; set; } = new Person();

        public List<TechLicence> TechLicences { get; set; } = new List<TechLicence>();

    }
}
