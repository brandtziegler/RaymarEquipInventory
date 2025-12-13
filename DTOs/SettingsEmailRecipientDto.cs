namespace RaymarEquipmentInventory.DTOs
{
    /// <summary>
    /// Settings payload for EmailNotificationRecipients.
    /// </summary>
    public class SettingsEmailRecipientDto
    {
        public int Id { get; set; }
        public int NotificationTypeId { get; set; }

        private string _notificationCode = "";
        /// <summary>
        /// WORK_ORDER | INVOICE (normalized to UPPER + trimmed)
        /// </summary>
        public string NotificationCode
        {
            get => _notificationCode;
            set => _notificationCode = (value ?? "").Trim().ToUpperInvariant();
        }

        private string _emailAddress = "";
        public string EmailAddress
        {
            get => _emailAddress;
            set => _emailAddress = (value ?? "").Trim().ToLowerInvariant();
        }

        private string _displayName = "";
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = (value ?? "").Trim();
        }

        public bool IsActive { get; set; }
        public bool IsDefault { get; set; } // maps to "Primary Recipient"
    }
}

