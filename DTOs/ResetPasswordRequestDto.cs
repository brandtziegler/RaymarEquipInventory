namespace RaymarEquipmentInventory.DTOs
{
   public class ResetPasswordRequestDto
    {
        public string Email { get; set; } = "";
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

}
