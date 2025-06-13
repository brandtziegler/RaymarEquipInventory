namespace RaymarEquipmentInventory.DTOs
{
    public class RolesAndPermissions
    {
        public int TechnicianID { get; set; }
        public int PersonID { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int RoleID { get; set; }
        public string RoleName { get; set; } = "";
        public bool CanDownloadFromCloud { get; set; }
        public bool CanUploadToCloud { get; set; }
        public bool CanApprove { get; set; }
        public bool CanAssignTech { get; set; }
    }
}
