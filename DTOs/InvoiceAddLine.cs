namespace RaymarEquipmentInventory.DTOs
{
    public sealed class InvoiceAddLine
    {
        public string? ItemListID { get; set; }
        public string? Desc { get; set; }
        public decimal? Qty { get; set; }
        public decimal? Rate { get; set; }
        public string? ClassRef { get; set; }
        public DateTime? ServiceDate { get; set; }
        public bool? IsTaxable { get; set; }  // optional; if null we omit SalesTaxCodeRef
    }

    public sealed class InvoiceAddPayload
    {
        public string CustomerListID { get; set; } = "";
        public string RefNumber { get; set; } = "";
        public DateTime TxnDate { get; set; }
        public string? PONumber { get; set; }
        public string? Memo { get; set; }
        public string? ItemSalesTaxRefListID { get; set; }  // e.g., HST ListID
        public List<InvoiceAddLine> Lines { get; set; } = new();
    }
}
