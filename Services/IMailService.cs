using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IMailService    
    {


        Task<MailBatchResult> SendWorkOrderEmailsAsync(WorkOrdMailContentBatch dto, CancellationToken ct = default);

    }
}
