
using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IQBWCRequestBuilder
    {
        // First “hello” request (super light)
        string BuildCompanyQuery();

        // ---------- Inventory (ItemInventory) ----------
        string BuildItemInventoryStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc,
            string[]? includeRetElements = null);

        string BuildItemInventoryContinue(
            string iteratorId,
            int pageSize,
            string[]? includeRetElements = null);

        // ---------- Customers / Jobs ----------
        string BuildCustomerStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc,
            string[]? includeRetElements = null);

        string BuildCustomerContinue(
            string iteratorId,
            int pageSize,
            string[]? includeRetElements = null);

        // ---------- ItemService ----------
        string BuildItemServiceStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc);

        string BuildItemServiceContinue(
            string iteratorId,
            int pageSize);

        // ============================================================
        // NEW: Non-Inventory / Other-Charge / Sales-Tax / Tax-Group
        // ============================================================

        // ---------- ItemNonInventory ----------
        string BuildItemNonInventoryStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc);

        string BuildItemNonInventoryContinue(
            string iteratorId,
            int pageSize);

        // ---------- ItemOtherCharge ----------
        string BuildItemOtherChargeStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc);

        string BuildItemOtherChargeContinue(
            string iteratorId,
            int pageSize);

        // ---------- ItemSalesTax ----------
        string BuildItemSalesTaxStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc);

        string BuildItemSalesTaxContinue(
            string iteratorId,
            int pageSize);

        // ---------- ItemSalesTaxGroup ----------
        string BuildItemSalesTaxGroupStart(
            int pageSize,
            bool activeOnly,
            string? fromModifiedIso8601Utc);

        string BuildItemSalesTaxGroupContinue(
            string iteratorId,
            int pageSize);

        string BuildInvoiceAdd(InvoiceAddPayload payload);
    }

   

}
