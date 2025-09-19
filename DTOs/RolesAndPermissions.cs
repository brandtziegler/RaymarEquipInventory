namespace RaymarEquipmentInventory.DTOs
{
    public class RolesAndPermissions
    {
        public int TechnicianID { get; set; }
        public int PersonID { get; set; }
        public int RolePermissionID { get; set; }
        public bool CanDownloadFromCloud { get; set; }
        public bool CanUploadToCloud { get; set; }
        public bool CanApprove { get; set; }
        public bool CanAssignTech { get; set; }

        public bool CanExportInvoice { get; set; }
    }
}
