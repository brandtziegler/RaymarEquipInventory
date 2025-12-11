using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IMailService    
    {


        Task<MailBatchResult> SendInvoiceEmailsAsync(WorkOrdMailContentBatch dto, CancellationToken ct = default);

        Task<MailBatchResult> SendWorkOrderNotificationEmailsAsync(WorkOrdMailContentBatch dto, CancellationToken ct = default);

    }
}
