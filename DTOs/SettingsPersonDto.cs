namespace RaymarEquipmentInventory.DTOs
{
    public class SettingsPersonDto
    {
        public int PersonID { get; set; }

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        private string _email = "";
        public string Email
        {
            get => _email;
            set => _email = (value ?? "").Trim().ToLowerInvariant();
        }

        public string PhoneOne { get; set; } = "";

        // send/receive canonical role
        public int RoleID { get; set; }

        // display / convenience (optional, but nice)
        public string RoleName { get; set; } = "";

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        // nullable because non-techs won't have these
        public int? TechnicianID { get; set; }
        public int? TechTypeId { get; set; }

        public bool IsTechnician => TechnicianID.HasValue;
    }
}

