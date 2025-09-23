

namespace RaymarEquipmentInventory.Services
{
    public interface IQBWCRequestBuilder
    {
        // First “hello” request (super light)
        string BuildCompanyQuery();

        // Start/Continue inventory paging
        string BuildItemInventoryStart(int pageSize, bool activeOnly, string? fromModifiedIso8601Utc, string[]? includeRetElements = null);
        string BuildItemInventoryContinue(string iteratorId, int pageSize, string[]? includeRetElements = null);

        public string BuildCustomerContinue(
    string iteratorId,
    int pageSize,
    string[]? includeRetElements = null);

        public string BuildCustomerStart(
    int pageSize,
    bool activeOnly,
    string? fromModifiedIso8601Utc,
    string[]? includeRetElements = null);


        public string BuildItemServiceStart(
    int pageSize,
    bool activeOnly,
    string? fromModifiedIso8601Utc);

        public string BuildItemServiceContinue(
    string iteratorId,
    int pageSize);

    }


}