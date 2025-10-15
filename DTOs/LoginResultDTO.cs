namespace RaymarEquipmentInventory.DTOs
{
    public class LoginResultDto
    {
        public bool Success { get; set; }
        public string? UUID { get; set; }
        public string? Email { get; set; }
        public int PersonID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; }
        public string? Salt { get; set; }
        public string? PasswordHash { get; set; }
        public string Message { get; set; } = "";
    }
}
