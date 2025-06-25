using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;
using Serilog;
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;

namespace RaymarEquipmentInventory.Services
{
    public class WorkOrderService : IWorkOrderService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly ILabourService _labourService;
        private readonly IPartService _partService;
        private readonly IDocumentService _documentService;
        private readonly IBillingService _billingService;
        private readonly ICustomerService _customerService;
        private readonly IInventoryService _inventoryService;
        private readonly ITechnicianService _technicianService;
        private readonly IVehicleService _vehicleService;

        private readonly RaymarInventoryDBContext _context;
        public WorkOrderService(IQuickBooksConnectionService quickBooksConnectionService, 
            RaymarInventoryDBContext context,
            ILabourService labourService, IPartService partService, 
            IDocumentService documentService, IBillingService billingService, ICustomerService customerService,
            IInventoryService inventoryService, ITechnicianService technicianService,
            IVehicleService vehicleService)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _labourService = labourService;
            _partService = partService;
            _documentService = documentService;
            _billingService = billingService;
            _customerService = customerService;
            _inventoryService = inventoryService;
            _technicianService = technicianService;
            _vehicleService = vehicleService;
        }


        public async Task<bool> LaunchWorkOrder(Billing billingInfo)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Step 1: Get the next work order number
                int workOrderNumber = await GetNextWorkOrderNumber();

                // Step 2: Create the WorkOrder record using the generated number
                var newWorkOrder = new WorkOrderSheet
                {
                    WorkOrderNumber = workOrderNumber,
                    DateTimeCreated = DateTime.Now,
                    WorkOrderStatus = "Created",
                    DateTimeCompleted = null,
                    DateTimeStarted = null,
                    // Other initialization steps
                };
                await _context.WorkOrderSheets.AddAsync(newWorkOrder);
                await _context.SaveChangesAsync();


                // Step 3: Create the BillingInformation record and link it to the WorkOrder
                var newBilling = new BillingInformation
                {
                    Pono = billingInfo.PONo,
                    SheetId = newWorkOrder.SheetId, // Link to WorkOrderSheet's SheetID
                    CustomerId = billingInfo.CustomerId,
                    BillingPersonId = billingInfo.TechId,
                    Notes = billingInfo.Notes,
                    UnitNo = billingInfo.UnitNo,
                    WorkLocation = "",
                };
                await _context.BillingInformations.AddAsync(newBilling);
                await _context.SaveChangesAsync();


                // Commit transaction
                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Log.Error($"Error launching work order: {ex.Message}");
                return false;
            }
        }

        // ⏰ grab Eastern Standard Time (covers daylight-savings automatically in Azure)
        private static readonly TimeZoneInfo _eastern =
                TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        private static DateTime EasternNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _eastern);


        public async Task<WorkOrderInsertResult?> UpdateWorkOrderAsync(DTOs.WorkOrdSheet workOrdSheet)
        {
            try
            {
                // Step 1: Find the existing WorkOrderSheet
                var sheet = await _context.WorkOrderSheets
                    .FirstOrDefaultAsync(w => w.SheetId == workOrdSheet.RemoteSheetId);

                if (sheet == null)
                {
                    Log.Warning($"⚠️ No WorkOrderSheet found for SheetId {workOrdSheet.RemoteSheetId}");
                    return null;
                }

                // Step 2: Update editable fields
                sheet.WorkOrderStatus = workOrdSheet.WorkOrderStatus?.Trim() ?? sheet.WorkOrderStatus;
                sheet.DateTimeStarted = workOrdSheet.DateTimeStarted ?? sheet.DateTimeStarted;
                sheet.DateTimeCompleted = workOrdSheet.DateTimeCompleted ?? sheet.DateTimeCompleted;
                sheet.WorkDescription = workOrdSheet.WorkDescription ?? sheet.WorkDescription;
                sheet.CompletedBy = workOrdSheet.TechnicianID;
                sheet.DateUploaded = EasternNow();  // always refresh upload timestamp

                await _context.SaveChangesAsync();

                // Step 3: Insert new sync event
                await _context.WorkOrderSyncEvents.AddAsync(new Models.WorkOrderSyncEvent
                {
                    SheetId = sheet.SheetId,
                    DeviceId = workOrdSheet.DeviceId,
                    EventType = sheet.WorkOrderStatus,
                    Timestamp = EasternNow()
                });

                await _context.SaveChangesAsync();

                // Step 4: Load technician mappings (but don’t change them)
                var techWOs = await _context.TechnicianWorkOrders
                    .Where(t => t.SheetId == sheet.SheetId)
                    .ToListAsync();

                int anchorTechnicianWorkOrderId = techWOs
                    .FirstOrDefault(t => t.TechnicianId == workOrdSheet.TechnicianID)?.TechnicianWorkOrderId ?? 0;

                var technicianMappings = techWOs.Select(t => new TechnicianWorkOrderMapping
                {
                    TechnicianId = t.TechnicianId,
                    TechnicianWorkOrderId = t.TechnicianWorkOrderId
                }).ToList();

                // Step 5: Return same structure as Insert
                return new WorkOrderInsertResult
                {
                    WorkOrderNumber = sheet.WorkOrderNumber,
                    SheetId = sheet.SheetId,
                    TechnicianWorkOrderId = anchorTechnicianWorkOrderId,
                    TechnicianMappings = technicianMappings
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"❌ UpdateWorkOrderAsync failed for SheetID {workOrdSheet.RemoteSheetId}");

                if (ex.InnerException != null)
                    Log.Error(ex.InnerException, "➡ Inner exception during WO update");

                return null;
            }
        }

        public async Task<WorkOrderInsertResult?> InsertWorkOrderAsync(DTOs.WorkOrdSheet workOrdSheet)
        {
            try
            {
              
                // Step 1: Get next W/O number
                var nextNumberRow = await _context.NextWorkOrderNumbers.FirstOrDefaultAsync();
                if (nextNumberRow == null)
                {
                    Log.Error("❌ NextWorkOrderNumber table is empty.");
                    return null;
                }

                int assignedWONumber = nextNumberRow.Wonumber;
                nextNumberRow.Wonumber += 1;
                await _context.SaveChangesAsync();



                // Step 2: Insert WorkOrderSheet
                var newSheet = new Models.WorkOrderSheet
                {
                    WorkOrderNumber = assignedWONumber,
                    DateTimeCreated = workOrdSheet.DateTimeCreated ?? EasternNow(),
                    WorkOrderStatus = workOrdSheet.WorkOrderStatus?.Trim() ?? "",
                    DateTimeStarted = workOrdSheet.DateTimeStarted,
                    DateTimeCompleted = workOrdSheet.DateTimeCompleted,
                    WorkDescription = workOrdSheet.WorkDescription,
                    CompletedBy = workOrdSheet.TechnicianID,
                    DateUploaded = EasternNow()              // 👈 shop time-zone stamp
                };

                await _context.WorkOrderSheets.AddAsync(newSheet);
                await _context.SaveChangesAsync();

                // Step 2½ : write sync-log so this sheet won't redownload
                await _context.WorkOrderSyncEvents.AddAsync(new Models.WorkOrderSyncEvent
                {
                    SheetId = newSheet.SheetId,
                    DeviceId = workOrdSheet.DeviceId,   // e.g. “TECH-7”
                    EventType = workOrdSheet.WorkOrderStatus?.Trim() ?? "",
                    Timestamp = EasternNow()
                });
                await _context.SaveChangesAsync();

                // Step 3: Insert ALL TechnicianWorkOrders
                var allTechs = await _context.Technicians.ToListAsync();
                int anchorTechnicianWorkOrderId = 0;
                var technicianMappings = new List<TechnicianWorkOrderMapping>();

                foreach (var tech in allTechs)
                {
                    var techEntry = new Models.TechnicianWorkOrder
                    {
                        SheetId = newSheet.SheetId,
                        TechnicianId = tech.TechnicianId
                    };

                    await _context.TechnicianWorkOrders.AddAsync(techEntry);
                    await _context.SaveChangesAsync();

                    if (tech.TechnicianId == workOrdSheet.TechnicianID)
                    {
                        anchorTechnicianWorkOrderId = techEntry.TechnicianWorkOrderId;
                    }

                    technicianMappings.Add(new TechnicianWorkOrderMapping
                    {
                        TechnicianId = tech.TechnicianId,
                        TechnicianWorkOrderId = techEntry.TechnicianWorkOrderId
                    });

                    Log.Information($"👷 Inserted TechnicianWorkOrder for TechID {tech.TechnicianId} (TWO_ID {techEntry.TechnicianWorkOrderId})");
                }

                return new WorkOrderInsertResult
                {
                    WorkOrderNumber = assignedWONumber,
                    SheetId = newSheet.SheetId,
                    TechnicianWorkOrderId = anchorTechnicianWorkOrderId,
                    TechnicianMappings = technicianMappings
                };

            }
            catch (Exception ex)
            {


                Log.Error($"❌ InsertWorkOrderAsync failed: {ex.Message}");

                if (ex.InnerException != null)
                    Log.Error($"➡ Inner Exception: {ex.InnerException.Message}");

                return null;
            }
        }

        private async Task<int> GetNextWorkOrderNumber()
        {
            // Fetch the current work order number and increment it
            var workOrderCounter = await _context.WorkOrderCounters.FirstOrDefaultAsync();
            if (workOrderCounter == null)
            {
                throw new InvalidOperationException("WorkOrderCounter record not found.");
            }

            // Increment the work order number
            int newWorkOrderNumber = workOrderCounter.CurrentNumber + 1;
            workOrderCounter.CurrentNumber = newWorkOrderNumber;
            workOrderCounter.LastModified = DateTime.Now;

            // Save the changes to the counter
            _context.WorkOrderCounters.Update(workOrderCounter);
            await _context.SaveChangesAsync();

            return newWorkOrderNumber;
        }


        public async Task UpdateWOStatus(int sheetId, string workOrderStatus, string deviceId)
        {
            try
            {
                // Optional: validate the sheet exists
                var workOrderExists = await _context.WorkOrderSheets
                    .AnyAsync(w => w.SheetId == sheetId);

                if (!workOrderExists)
                {
                    Log.Error($"Work order with SheetID {sheetId} not found.");
                    return;
                }

                // You may want to get the device ID from the current context/token/header/etc.
                var syncEvent = new WorkOrderSyncEvent
                {
                    SheetId = sheetId,
                    EventType = workOrderStatus,
                    Timestamp = DateTime.UtcNow,
                    DeviceId = deviceId // Placeholder: inject or extract this properly
                };

                await _context.WorkOrderSyncEvents.AddAsync(syncEvent);
                await _context.SaveChangesAsync();

                Log.Information($"✅ Sync event added: SheetID={sheetId}, Status={workOrderStatus}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating work order sync status");
                throw;
            }
        }

        public async Task<bool> AddTechToWorkOrder(int techID, int sheetID)
        {

            try
            {
                //1. Does the tech exist?
                var tech = await _context.Technicians.Where(t => t.TechnicianId == techID).Include(o => o.Person)
                    .FirstOrDefaultAsync();

                if (tech == null)
                {
                    Log.Error($"Technician does not exist {techID}. ");
                }
                //2. Does the work order exist?
                var workOrder = await _context.WorkOrderSheets.Where(t => t.SheetId == sheetID).FirstOrDefaultAsync();
                if (workOrder == null)
                {
                    Log.Error($"Work Order does not exist {techID}. ");
                }
                //3. Does the tech already exist on the work order?
                var technicianWorkOrder = await _context.TechnicianWorkOrders
                .Where(t => t.SheetId == sheetID && t.TechnicianId == techID).FirstOrDefaultAsync();

                if (technicianWorkOrder != null)
                {
                    Log.Error($"Technician already exists on work order {techID}. ");
                }

                //4. Add the tech to the work order
                var newTechOnWO = new Models.TechnicianWorkOrder()
                {
                    TechnicianId = techID,
                    SheetId = sheetID
                };

                await _context.TechnicianWorkOrders.AddAsync(newTechOnWO);
                await _context.SaveChangesAsync(); // Save all changes asynchronously

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding tech to work order");
                return false;
            }

        }

        public async Task<bool> DeleteTechWorkOrderById(int technicianWorkOrderID)
        {
            try
            {
                // Find the TechnicianWorkOrder entry by its ID
                var techWorkOrder = await _context.TechnicianWorkOrders.FindAsync(technicianWorkOrderID);

                if (techWorkOrder == null)
                {
                    // If no entry is found, log a warning and return false
                    Log.Warning($"TechnicianWorkOrder with ID {technicianWorkOrderID} not found.");
                    return false;
                }

                // Remove the entry from the context
                _context.TechnicianWorkOrders.Remove(techWorkOrder);

                // Save the changes to the database
                await _context.SaveChangesAsync();

                Log.Information($"TechnicianWorkOrder with ID {technicianWorkOrderID} successfully deleted.");
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception and return false
                Log.Error($"Error deleting TechnicianWorkOrder with ID {technicianWorkOrderID}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteTechFromWorkOrder(int techID, int sheetID)
        {
            try
            {
                // Find the TechnicianWorkOrder entry using the TechnicianID and SheetID
                var techWorkOrder = await _context.TechnicianWorkOrders.FirstOrDefaultAsync(t => t.TechnicianId == techID && t.SheetId == sheetID);
   
                if (techWorkOrder == null)
                {
                    // If no matching entry is found, log a warning and return false
                    Log.Warning($"Technician with ID {techID} not found for WorkOrder with SheetID {sheetID}.");
                    return false;
                }

                // Remove the entry from the context
                _context.TechnicianWorkOrders.Remove(techWorkOrder);

                // Save the changes to the database
                await _context.SaveChangesAsync();

                Log.Information($"Technician with ID {techID} successfully removed from WorkOrder with SheetID {sheetID}.");
                return true;
            }
            catch (Exception ex)
            {
                // Log the exception and return false
                Log.Error($"Error removing technician with ID {techID} from WorkOrder with SheetID {sheetID}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddLbrToWorkOrder(LabourLine labourDTO)
        {
            // Step 1: Check for required fields
            if (labourDTO.TechnicianID == 0 || labourDTO.SheetId == 0 || labourDTO.DateofLabour == null)
            {
                Log.Warning("TechnicianID, SheetID, and DateOfLabour are required fields.");
                return false;
            }

            try
            {
                // Step 2: Retrieve the TechnicianWorkOrderID based on TechnicianID and SheetID
                var technicianWorkOrder = await _context.TechnicianWorkOrders
                    .FirstOrDefaultAsync(t => t.TechnicianId == labourDTO.TechnicianID && t.SheetId == labourDTO.SheetId);

                if (technicianWorkOrder == null)
                {
                    Log.Warning($"TechnicianWorkOrder not found for TechnicianID {labourDTO.TechnicianID} and SheetID {labourDTO.SheetId}.");
                    return false;  // No matching TechnicianWorkOrder entry found
                }

                // Step 3: Map the LabourLine DTO to the Labour entity, now with TechnicianWorkOrderID
                var newLabour = new Labour
                {
                    DateOfLabour = labourDTO.DateofLabour.Value,
                    FlatRateJob = labourDTO.FlatRateJob,
                    FlatRateJobDescription = labourDTO.FlatRateJobDescription ?? string.Empty,
                    TechnicianWorkOrderId = technicianWorkOrder.TechnicianWorkOrderId,
                    WorkDescription = labourDTO.WorkDescription ?? string.Empty
                };

                // Step 4: Add the new labour record to the database
                await _context.Labours.AddAsync(newLabour);
                await _context.SaveChangesAsync();

                Log.Information($"Successfully added labour entry for TechnicianWorkOrderID {technicianWorkOrder.TechnicianWorkOrderId}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding labour to work order: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveLbrFromWorkOrder(int labourID)
        {
            try
            {
                // Step 1: Retrieve the labour entry from the database
                var labourEntry = await _context.Labours.FindAsync(labourID);

                if (labourEntry == null)
                {
                    Log.Warning($"Labour entry with ID {labourID} not found.");
                    return false; // Labour entry does not exist
                }

                // Step 2: Remove the labour entry from the database
                _context.Labours.Remove(labourEntry);
                await _context.SaveChangesAsync();

                Log.Information($"Successfully removed labour entry with ID {labourID} from work order.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Error removing labour entry with ID {labourID}: {ex.Message}");
                return false; // Indicate failure
            }
        }


        public async Task<DTOs.Billing> GetLabourForWorkorder(int sheetID)
        {
            var billing = await _context.BillingInformations
              .Include(t => t.Customer)  // Include the related work order
              .Include(t => t.Customer.Parent)  // Include the related work order
              .Where(t => t.SheetId == sheetID)
              .FirstOrDefaultAsync();

            if (billing == null)
            {
                return null;  // Return null if no matching labour is found
            }

            var billingDTO = new DTOs.Billing()
            {
                BillingID = billing.BillingId,
                SheetId = billing.SheetId,
                CustomerId = billing.Customer.CustomerId,
                CustomerName = billing.Customer.CustomerName,
                ParentCustomerId = billing.Customer.Parent.CustomerId,
                ParentCustomerName = billing.Customer.Parent.CustomerName,
                CustomerQBId = billing.Customer.Id,
                ParentCustomerQBId = billing.Customer.Parent.Id,
                PONo = billing.Pono,
                Notes = billing.Notes,
                UnitNo = billing.UnitNo
                //WorkLocation = billing.WorkLocation
            };
            return billingDTO;

        }

        private async Task<bool> CanDownloadFromCloud(int technicianId)
        {
            return await _context.VwRolesMins
                .Where(r => r.TechnicianId == technicianId)
                .Select(r => r.CanDownloadFromCloud)
                .FirstOrDefaultAsync();
        }

        public async Task<List<WorkOrderCard>> GetWorkOrderCardsAsync(
           DateTime? dateUploadedStart,
           DateTime? dateUploadedEnd,
           DateTime? dateTimeCompletedStart,
           DateTime? dateTimeCompletedEnd,
           int technicianId,
           string syncEventType = "COMPLETE",
           int? customerId = null)
        {
            // 🚨 Permission check (early exit)
            if (!await CanDownloadFromCloud(technicianId))
            {
                Log.Warning($"Technician {technicianId} does not have permission to download work orders.");
                return new List<WorkOrderCard>();
            }

            var query = _context.VwWorkOrderCards.AsQueryable();

            // Case-insensitive sync event type filter
            if (!string.IsNullOrWhiteSpace(syncEventType))
            {
                var syncTypeUpper = syncEventType.Trim().ToUpper();
                query = query.Where(w => w.LastSyncEventType.ToUpper() == syncTypeUpper);
            }

            // Optional date filters
            if (dateUploadedStart.HasValue)
                query = query.Where(w => w.DateUploaded >= dateUploadedStart.Value);

            if (dateUploadedEnd.HasValue)
                query = query.Where(w => w.DateUploaded <= dateUploadedEnd.Value);

            if (dateTimeCompletedStart.HasValue)
                query = query.Where(w => w.DateTimeCompleted >= dateTimeCompletedStart.Value);

            if (dateTimeCompletedEnd.HasValue)
                query = query.Where(w => w.DateTimeCompleted <= dateTimeCompletedEnd.Value);

            if (customerId.HasValue)
                query = query.Where(w => w.CustomerId == customerId.Value);

            // Optional debug logging
            Log.Information($"Technician {technicianId} passed permission check. Filtering WorkOrderCards on LastSyncEventType = {syncEventType}");

            // Project to DTO
            var workOrderCards = await query.Select(w => new WorkOrderCard
            {
                SheetID = w.SheetId,
                WorkOrderNumber = w.WorkOrderNumber,
                WorkDescription = w.WorkDescription,
                WorkOrderStatus = w.WorkOrderStatus,
                DateUploaded = w.DateUploaded,
                DateTimeCompleted = w.DateTimeCompleted,
                UnitNo = w.UnitNo,
                CustomerID = w.CustomerId,
                PathToRoot = w.PathToRoot,
                ParentCustomerName = w.ParentCustomerName,
                ChildCustomerName = w.ChildCustomerName,
                LastSyncEventType = w.LastSyncEventType,
                LastSyncTimestamp = w.LastSyncTimestamp
            }).ToListAsync();

            return workOrderCards;
        }


        public async Task<List<DTOs.WorkOrderFee>> GetFees(int sheetID)
        {
            try
            {
                // Step 1: Get the TechnicianWorkOrderIDs for the given SheetID
                var techWorkOrderIds = await _context.TechnicianWorkOrders
                    .Where(t => t.SheetId == sheetID)
                    .Select(t => t.TechnicianWorkOrderId)
                    .ToListAsync();

                if (!techWorkOrderIds.Any())
                {
                    Log.Warning($"No TechnicianWorkOrders found for SheetID {sheetID}.");
                    return new List<DTOs.WorkOrderFee>();
                }

                // Step 2: Get the fees
                var feeLines = await _context.WorkOrderFees
                    .Where(f => techWorkOrderIds.Contains(f.TechnicianWorkOrderId))
                    .ToListAsync();

                var result = feeLines.Select(f => new DTOs.WorkOrderFee
                {
                    TechnicianWorkOrderID = f.TechnicianWorkOrderId,
                    FlatLabourID = f.FlatLabourId,
                    LabourTypeID = f.LabourTypeId,
                    Qty = f.Qty,
                    WorkDescription = f.WorkDescription
                }).ToList();

                Log.Information($"✅ Retrieved {result.Count} WorkOrderFees for SheetID {sheetID}.");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Failed to get WorkOrderFees for SheetID {sheetID}: {ex.Message}");
                return null;
            }
        }


        public async Task<List<HourlyLbrSummary>> GetLabourLines(int sheetID, int? technicianId = null, int? labourTypeId = null)
        {
            try
            {
                var techWorkOrdersQuery = _context.TechnicianWorkOrders
                    .Where(t => t.SheetId == sheetID);

                if (technicianId.HasValue)
                    techWorkOrdersQuery = techWorkOrdersQuery.Where(t => t.TechnicianId == technicianId.Value);

                var techWorkOrders = await techWorkOrdersQuery.ToListAsync();

                if (!techWorkOrders.Any())
                {
                    Log.Warning($"No TechnicianWorkOrders found for SheetID {sheetID}.");
                    return new List<HourlyLbrSummary>();
                }

                var techWOIds = techWorkOrders.Select(t => t.TechnicianWorkOrderId).ToList();

                var regularLabourQuery = _context.RegularLabours
                    .Where(l => l.TechnicianWorkOrderId.HasValue && techWOIds.Contains(l.TechnicianWorkOrderId.Value));

                if (labourTypeId.HasValue)
                    regularLabourQuery = regularLabourQuery.Where(l => l.LabourTypeId == labourTypeId.Value);

                var regularLabourLines = await regularLabourQuery.ToListAsync();

                var grouped = regularLabourLines
                    .GroupBy(l => new { l.TechnicianWorkOrderId, l.LabourTypeId })
                    .Select(g => new HourlyLbrSummary
                    {
                        TechnicianID = techWorkOrders
                            .First(t => t.TechnicianWorkOrderId == g.Key.TechnicianWorkOrderId).TechnicianId,
                        LabourTypeID = g.Key.LabourTypeId,
                        Labour = g.Select(l => new DTOs.RegularLabourLine
                        {
                            LabourId = l.LabourId,
                            TechnicianWorkOrderID = l.TechnicianWorkOrderId ?? 0,
                            DateOfLabor = l.DateOfLabor.Date,
                            StartLabor = l.StartLabor,
                            FinishLabor = l.FinishLabor.HasValue && l.FinishLabor.Value.Date.Year == 1969 ? null : l.FinishLabor,
                            WorkDescription = l.WorkDescription,
                            TotalHours = l.TotalHours ?? 0,
                            TotalMinutes = l.TotalMinutes ?? 0,
                            TotalOTHours = l.TotalOthours ?? 0,
                            TotalOTMinutes = l.TotalOtminutes ?? 0,
                            LabourTypeID = l.LabourTypeId
                        }).ToList()
                    })
                    .ToList();

                Log.Information($"✅ Grouped {grouped.Sum(g => g.Labour.Count)} labour rows for SheetID {sheetID} into {grouped.Count} tech/type combos.");
                return grouped;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Failed to group RegularLabour for SheetID {sheetID}: {ex.Message}");
                return null;
            }
        }




        public async Task LogFailedSync(int sheetId, string reason)
        {
            try
            {
                await _context.FailedSyncLogs.AddAsync(new FailedSyncLog
                {
                    SheetId = sheetId,
                    Reason = reason,
                    Timestamp = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Failed to log sync failure for SheetID {sheetId}: {ex.Message}");
            }
        }

        public async Task<DTOs.BillingMin?> GetBillingMin(int sheetID)
        {
            try
            {
                var result = await _context.BillingInformations
                    .Where(b => b.SheetId == sheetID)
                    .Join(_context.WorkOrderSheets,
                        b => b.SheetId,
                        w => w.SheetId,
                        (b, w) => new DTOs.BillingMin
                        {
                            SheetId = b.SheetId,
                            CustomerId = b.CustomerId ?? 0,
                            PONo = b.Pono ?? "",
                            Kilometers = b.Kilometers ?? 0,
                            UnitNo = b.UnitNo ?? "",
                            JobSiteCity = b.JobSiteCity ?? "",
                            WorkDescription = w.WorkDescription ?? "",
                            CustPath = b.CustPath ?? "",
                            WorkOrderStatus = w.WorkOrderStatus ?? "",
                            DateUploaded = w.DateUploaded,
                            CompletedBy = w.CompletedBy
                        })
                    .FirstOrDefaultAsync();

                if (result == null)
                    Console.WriteLine($"⚠️ No BillingMin found for SheetID {sheetID}");

                return result;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ BillingMin fetch failed for SheetID {sheetID}: {ex.Message}");
                return null;
            }
        }


        public async Task<List<DTOs.TravelLog>> GetMileage(int sheetID)
        {
            try
            {
                var results = await _context.MileageAndTimes
                    .Where(m => m.SheetId == sheetID)
                    .Select(m => new DTOs.TravelLog
                    {
                        MilageTimeID = m.MilageTimeId,
                        SheetId = m.SheetId,
                        DateOfTravel = m.DateOfMileageTime,
                        StartTravel = m.StartTravel,
                        FinishTravel = m.FinishTravel,
                        StartOdometerKm = m.StartOdometerKm ?? 0,
                        FinishOdometerKm = m.FinishOdometerKm ?? 0,
                        TotalDistance = m.TotalDistance ?? 0,
                        TotalHours = m.TimeTotalHrs ?? 0,
                        TotalMinutes = m.TimeTotalMin ?? 0,
                        TotalOTHours = m.TotalOthours ?? 0,
                        TotalOTMinutes = m.TotalOtminutes ?? 0,
                        IsOvertime = m.IsOvertime,
                        SegmentNumber = m.SegmentNumber,
                        TimeTotalMin = m.TimeTotalMin
                    })
                    .ToListAsync();

                Console.WriteLine($"✅ Retrieved {results.Count} mileage records for SheetID {sheetID}.");
                return results;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Failed to retrieve mileage for SheetID {sheetID}: {ex.Message}");
                return new List<DTOs.TravelLog>(); // graceful fallback
            }
        }



        public async Task<List<DTOs.PartsUsed>> GetPartsUsed(int sheetID)
        {
            try
            {
                var woPartsUsedDetails = await _context.PartsUseds
                    .Where(p => p.SheetId == sheetID)
                    .ToListAsync();

                if (woPartsUsedDetails == null || !woPartsUsedDetails.Any())
                {
                    Log.Warning($"No parts found for WorkOrder with SheetID {sheetID}.");
                    return new List<DTOs.PartsUsed>();
                }

                var woPartsUsedDetailDTOs = new List<DTOs.PartsUsed>();

                foreach (var p in woPartsUsedDetails)
                {
                    var dto = new DTOs.PartsUsed
                    {
                        PartUsedId = p.PartUsedId,
                        SheetId = p.SheetId,
                        QtyUsed = p.QtyUsed,
                        Notes = p.Notes,
                        InventoryID = p.InventoryId,
                        PartNumber = p.PartNumber,
                        Description = p.Description,
                        UploadDate = p.UploadDate,
                        UploadedBy = p.UploadedBy,
                        Deleted = p.Deleted ?? false,
                        PartsDocs = new List<DTOs.PartsDocument>() // Init list
                    };

                    // Pull Inventory info if applicable
                    if (p.InventoryId.HasValue)
                    {
                        var inventory = await _context.InventoryData
                            .Where(i => i.InventoryId == p.InventoryId)
                            .FirstOrDefaultAsync();

                        if (inventory != null)
                        {
                            dto.InventoryData = new DTOs.InventoryData
                            {
                                InventoryId = inventory.InventoryId.ToString(),
                                ItemName = inventory.ItemName,
                                ManufacturerPartNumber = inventory.ManufacturerPartNumber,
                                QuickBooksInvId = inventory.QuickBooksInvId,
                                Description = inventory.Description,
                                Cost = inventory.Cost,
                                SalesPrice = inventory.SalesPrice,
                                ReorderPoint = inventory.ReorderPoint,
                                OnHand = inventory.OnHand,
                                AverageCost = inventory.AverageCost,
                                IncomeAccountId = inventory.IncomeAccountId,
                                InventoryDocuments = new List<DTOs.InventoryDocument>() // still leave empty for now
                            };
                        }
                    }

                    // Pull PartDocuments
                    var partDocs = await _context.PartsDocuments
                        .Where(d => d.PartUsedId == p.PartUsedId)
                        .ToListAsync();

                    foreach (var doc in partDocs)
                    {
                        dto.PartsDocs.Add(new DTOs.PartsDocument
                        {
                            PartsDocumentId = doc.PartsDocumentId,
                            PartUsedId = doc.PartUsedId,
                            FileName = doc.FileName,
                            FileUrl = doc.FileUrl,
                            Description = doc.Description,
                            UploadedBy = doc.UploadedBy,
                            UploadedDate = doc.UploadDate
                        });
                    }

                    woPartsUsedDetailDTOs.Add(dto);
                }

                Log.Information($"✅ Retrieved {woPartsUsedDetailDTOs.Count} parts for WorkOrder with SheetID {sheetID}.");
                return woPartsUsedDetailDTOs;
            }
            catch (Exception ex)
            {
                Log.Error($"❌ Error retrieving parts for SheetID {sheetID}: {ex.Message}");
                return null;
            }
        }


        public async Task<DTOs.WorkOrder> GetWorkOrder(int sheetID)
        {
            try
            {
                // Step 1: Retrieve the basic work order details from the database
                var workOrderEntity = await _context.WorkOrderSheets
                    .FirstOrDefaultAsync(w => w.SheetId == sheetID);

                if (workOrderEntity == null)
                {
                    Log.Warning($"Work order with SheetID {sheetID} not found.");
                    return null; // Could return null or throw an exception based on your design
                }

                // Step 2: Map the WorkOrderSheet fields to the WorkOrder DTO
                var workOrderDto = new DTOs.WorkOrder
                {
                    SheetId = workOrderEntity.SheetId,
                    WorkOrderNumber = workOrderEntity.WorkOrderNumber,
                    WorkOrderStatus = workOrderEntity.WorkOrderStatus,
                    DateTimeCreated = workOrderEntity.DateTimeCreated,
                    DateTimeStarted = workOrderEntity.DateTimeStarted,
                    DateTimeCompleted = workOrderEntity.DateTimeCompleted
                };


                // Step 3: Retrieve the billing information for the work order

                var billing = await _context.BillingInformations
                    .Include(t => t.Customer)  // Include the related work order
                    .Include(t => t.Customer.Parent)  // Include the related work order
                    .Where(t => t.SheetId == sheetID)
                    .FirstOrDefaultAsync();

                if (billing == null)
                {
                    Log.Warning($"No bill attached with Work Order {workOrderDto.WorkOrderNumber}!");
                    return null; // Could return null or throw an exception based on your design
                }

                if (billing.Customer == null)
                {
                    Log.Warning($"No customer attached with Work Order {workOrderDto.WorkOrderNumber}!");
                    return null; // Could return null or throw an exception based on your design
                }
                workOrderDto.Customer = billing.Customer;
                workOrderDto.ParentCustomer = billing.Customer.Parent;

                workOrderDto.WOBilling = new DTOs.Billing();
                workOrderDto.WOBilling.BillingID = billing.BillingId;
                workOrderDto.WOBilling.SheetId = billing.SheetId;
                workOrderDto.WOBilling.PONo = billing.Pono;
                workOrderDto.WOBilling.Notes = billing.Notes;
                workOrderDto.WOBilling.UnitNo = billing.UnitNo;
                //workOrderDto.WOBilling.WorkLocation = billing.WorkLocation;

                var labourLines = await _context.Labours.Include(t => t.TechnicianWorkOrder)
                    .Where(t => t.TechnicianWorkOrder.SheetId == sheetID).ToListAsync();

                workOrderDto.LabourLines = _labourService.GetLabourByWorkOrder(sheetID).Result;
                //workOrderDto.PartsUsed = _partService.GetPartsByWorkOrder(sheetID,1,1000, null, "", null, null, "").Result;
                workOrderDto.Technicians = _technicianService.GetTechsByWorkOrder(sheetID).Result;
                workOrderDto.Documents = _documentService.GetDocumentsByWorkOrder(sheetID).Result;
                workOrderDto.VehicleTravelLogs = _vehicleService.GetTripLog(sheetID).Result;
                workOrderDto.PartsUsed = _partService.GetPartsByWorkOrder(sheetID, 1, 1000, null, null, null, null, "", "").Result;
                //workOrderDto.VehicleTravelLogs = 

                //workOrderDto.VehicleTravelLogs = .(sheetID).Result;


                Log.Information($"Successfully retrieved work order with SheetID {sheetID}.");
                return workOrderDto;
            }
            catch (Exception ex)
            {
                Log.Error($"Error retrieving work order with SheetID {sheetID}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<DTOs.WorkOrderBriefDetails>> GetWorkOrderBriefDetails()
        {

                var woBriefDetails = await _context.VwWorkOrdBriefDetails.ToListAsync();
                var woBriefDetailDTOs = woBriefDetails.Select(briefdetail => new WorkOrderBriefDetails
                {
                    SheetID = briefdetail.SheetId,
                    WorkOrderNumber = briefdetail.WorkOrderNumber,
                    DateTimeCompleted = briefdetail.DateTimeCompleted,
                    DateTimeCreated = briefdetail.DateTimeCreated,
                    DateTimeStarted = briefdetail.DateTimeStarted,
                    WorkLocation = briefdetail.WorkLocation,
                    WorkOrderStatus = briefdetail.WorkOrderStatus,
                    WorkOrderType = briefdetail.WorkOrderType,
                    PONo = briefdetail.Pono,
                    CustomerName = briefdetail.CustomerName,
                    IconName = briefdetail.IconName,
                    HexColor = briefdetail.HexColor,
                    TypeHexColor = briefdetail.TypeHexColor, 
                    TypeIconName = briefdetail.TypeIconName,
                    ParentName = briefdetail.ParentName,
                    FullAddress = briefdetail.FullAddress,
                    VehicleName = briefdetail.VehicleName,
                }).ToList();


                return woBriefDetailDTOs;
  
           
        }

                public async Task<bool> RemovePartFromWorkOrder(int partUsedId, int sheetId)
        {
            try
            {
                // Step 1: Find the PartsUsed entity by PartUsedId and SheetId
                var partToRemove = await _context.PartsUseds
                    .FirstOrDefaultAsync(p => p.PartUsedId == partUsedId && p.SheetId == sheetId);

                if (partToRemove == null)
                {
                    // Part not found
                    Log.Warning($"Part with PartUsedId {partUsedId} and SheetId {sheetId} not found.");
                    return false;
                }

                // Step 2: Remove the part from the database
                _context.PartsUseds.Remove(partToRemove);
                await _context.SaveChangesAsync();

                Log.Information($"Part with PartUsedId {partUsedId} successfully removed from Work Order {sheetId}.");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error removing part from work order: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> RemoveBillFromWorkOrder(int billingId, int sheetId)
        {
            try
            {
                // Step 1: Find the PartsUsed entity by PartUsedId and SheetId
                var billToRemove = await _context.BillingInformations
                    .FirstOrDefaultAsync(p => p.BillingId == billingId && p.SheetId == sheetId);

                if (billToRemove == null)
                {
                    // Part not found
                    Log.Warning($"Bill with BillikngID {billingId} and SheetId {sheetId} not found.");
                    return false;
                }

                // Step 2: Remove the part from the database
                _context.BillingInformations.Remove(billToRemove);
                await _context.SaveChangesAsync();

                Log.Information($"Bill with Billing ID {billingId} successfully removed from Work Order {sheetId}.");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error removing part from work order: {ex.Message}");
                return false;
            }
        }



        public async Task<bool> AddPartToWorkOrder(DTOs.PartsUsed partsUsedDto)
        {
            // Start by checking for required fields in the DTO
            if (partsUsedDto.SheetId == null || partsUsedDto.InventoryID == 0 || partsUsedDto.QtyUsed == null)
            {
                Log.Warning("Failed to add part: SheetID, InventoryId, and QtyUsed are required fields.");
                return false;
            }

            try
            {
                // Step 1: Map the DTO to the PartsUsed entity
                var partUsedEntity = new Models.PartsUsed
                {
                    SheetId = partsUsedDto.SheetId.Value,
                    InventoryId = partsUsedDto.InventoryID,
                    QtyUsed = partsUsedDto.QtyUsed.Value,
                    Notes = partsUsedDto.Notes ?? string.Empty  // Optional, use empty string if null
                };

                // Step 2: Add the part to the database
                await _context.PartsUseds.AddAsync(partUsedEntity);
                await _context.SaveChangesAsync();

                Log.Information($"Successfully added part with InventoryID {partsUsedDto.InventoryID} to Work Order {partsUsedDto.SheetId}");
                return true;
            }
            catch (Exception ex)
            {
                // Log error and return failure
                Log.Error($"Error adding part to work order: {ex.Message}");
                return false;
            }
        }


    }

}

