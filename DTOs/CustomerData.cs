namespace RaymarEquipmentInventory.DTOs
{
    public class CustomerData
    {
        public int CustomerID { get; set; } = 0;  
        public string ID { get; set; } = "";
        public string ParentID { get; set; } = "";
        public string Name { get; set; } = "";
        public string ParentName { get; set; } = "";
        public string FullAddress { get; set; } = "";
        public string Company { get; set; } = "";
        public string FullName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string UnitNumber { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Notes { get; set; } = "";
        public string JobStatus { get; set; } = "";
        public string JobStartDate { get; set; } = "";
        public string JobProjectedEndDate { get; set; } = "";
        public string JobDescription { get; set; } = "";
        public string JobType { get; set; } = "";
        public string JobTypeId { get; set; } = "";
      
        public string Description { get; set; } = "";

        public bool IsActive { get; set; } = false;
        public int SubLevelId { get; set; } = 0;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;   // when we last touched this row
        public string UpdateType { get; set; } = "A";                  // "A"=Added, "U"=Updated
        public List<CustomerData> Children { get; set; } = new List<CustomerData>();



    }
}
