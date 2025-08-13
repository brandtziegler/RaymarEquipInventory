using System;
using System.Collections.Generic;
namespace RaymarEquipmentInventory.DTOs
{
    public class ReceiptCsvRow
    {
        public string MerchantName { get; set; } = "";
        public string City { get; set; } = "";
        public string ReceiptType { get; set; } = "";   // "Restaurant", "Supplies", "Fuel", "Unknown"
        public decimal SubTotal { get; set; }
        public decimal TotalTax { get; set; }
        public decimal Total { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string CardNumberMasked { get; set; } = "";
        public string Items { get; set; } = "";         // NEW: comma-joined list
    }

}
