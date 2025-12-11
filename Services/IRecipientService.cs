using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RaymarEquipmentInventory.Services
{
    public interface IRecipientService
    {
        /// <summary>
        /// Returns the final recipient list for a Work Order email,
        /// based on Settings tables plus any ad-hoc extras.
        /// </summary>
        Task<IReadOnlyList<string>> GetWorkOrderRecipientsAsync(
            int sheetId,
            int workOrderNumber,
            IEnumerable<string>? extraEmails = null,
            CancellationToken ct = default);
    }
}