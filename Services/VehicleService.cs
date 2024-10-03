using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using RaymarEquipmentInventory.Settings.YourApiProject.Settings;
using System.Reflection.PortableExecutable;
using System;
using System.Net.Http.Headers;
using RaymarEquipmentInventory.Helpers;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace RaymarEquipmentInventory.Services
{
    public class VehicleService : IVehicleService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        private readonly HttpClient _httpClient;
        private readonly SamsaraApiConfig _config;
        private readonly string _bearerToken;

        public VehicleService(HttpClient httpClient, IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context,  SamsaraApiConfig config)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _config = config;
            _httpClient = httpClient;
            _bearerToken = Environment.GetEnvironmentVariable("SAMSARA_API_TOKEN"); // Fetching from environment variable
        }


        public async Task UpdateOrInsertVehiclesAsync(List<Vehicle> vehicleList)
        {

            if (vehicleList == null || vehicleList.Count == 0)
            {
                return; // If there’s nothing to update or insert, we just move on.
            }

            try
            {
                var newVehicleCount = 0;
                foreach (var vehicle in vehicleList)
                {
                    var mappedVehicle = MapDtoToModel(vehicle);
                    var existingVehicle = await _context.VehicleData
                        .FirstOrDefaultAsync(i => i.SamsaraVehicleId == vehicle.SamsaraVehicleID); // Using async for efficiency

                    if (existingVehicle != null)
                    {
                        // Update the existing record
                        existingVehicle.VehicleName = vehicle.VehicleName;
                        existingVehicle.VehicleVin = vehicle.VehicleVIN;
                        // Update in the context
                        _context.VehicleData.Update(existingVehicle);
                    }
                    else
                    {
                        // Insert new record into the context
                        await _context.VehicleData.AddAsync(mappedVehicle);
                        newVehicleCount++;
                    }
                }

                await _context.SaveChangesAsync(); // Save all changes asynchronously
            }
            catch (Exception ex)
            {
                // Log the exception - make sure you replace this with your actual logging framework
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }


        public async Task UpdateVehicleLog(Int32 SheetID, Int32 VehicleID)
        {
            try
            {
                var existingVehicleWO = await _context.VehicleWorkOrders
                    .FirstOrDefaultAsync(i => i.SheetId == SheetID && i.VehicleId == VehicleID); // Using async for efficiency

                var vehicle = await _context.VehicleData
                    .FirstOrDefaultAsync(i => i.VehicleId == VehicleID); // Using async for efficiency

                var workOrder = await _context.WorkOrderSheets
                    .FirstOrDefaultAsync(i => i.SheetId == SheetID); // Using async for efficiency

                if (vehicle != null && workOrder != null)
                {
                    var samsaraID = vehicle.SamsaraVehicleId;
                    var startTime = Convert.ToDateTime(workOrder.DateTimeStarted);  // Placeholder for workOrder.DateTimeStarted
                    var endTime = Convert.ToDateTime(workOrder.DateTimeCompleted);
                    var utcStartTime = startTime.ToUniversalTime();
                    var utcEndTime = endTime.ToUniversalTime();

                    // Convert the local time to UTC first, then to Unix time (milliseconds)
                    var startMs = new DateTimeOffset(utcStartTime).ToUnixTimeMilliseconds();
                    var endMs = new DateTimeOffset(utcEndTime).ToUnixTimeMilliseconds();

                    // Create the request URL with parameters
                    var requestUrl = $"{_config.BaseUrl}/v1/fleet/trips?vehicleId={samsaraID}&startMs={startMs}&endMs={endMs}";

                    // Create the request message
                    var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);

                    // Send the request
                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read the JSON response
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var tripResponse = JsonSerializer.Deserialize<TripResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        var trips = tripResponse?.Trips
                            .Select(tripData => new TripLog(
                                vehicle.SamsaraVehicleId,
                                VehicleID,
                                SheetID,
                                tripData.StartMs,
                                tripData.EndMs,
                                tripData.StartLocation ?? "Unknown", // Safeguard against null
                                tripData.EndLocation ?? "Unknown",  // Safeguard against null
                                tripData.StartOdometer,
                                tripData.EndOdometer
                            ))
                            .ToList();

                        // 1. Create a Vehicle Work Order in the database and fill it with VehicleID and SheetID
                        var newVehicleWO = new VehicleWorkOrder
                        {
                            VehicleId = VehicleID,
                            SheetId = SheetID
                        };
                        await _context.VehicleWorkOrders.AddAsync(newVehicleWO);
                        await _context.SaveChangesAsync(); // Save all changes asynchronously

                        // Now loop through trips and insert them into VehicleTravelLog
                        foreach (var trip in trips)
                        {
                            var newTrip = new VehicleTravelLog
                            {
                                VehicleId = trip.VehicleID,
                                DateTimeStart = trip.StartDateTime,
                                KmatStart = (decimal?)trip.StartKM,
                                StartingLocation = trip.StartLocation,
                                DateTimeEnd = trip.EndDateTime,
                                EndingLocation = trip.EndLocation,
                                KmatEnd = (decimal?)trip.EndKM,
                                VehicleWorkOrderId = newVehicleWO.VehicleWorkOrderId,
                                TotalKms = (decimal?)trip.DistanceKM
                            };

                            await _context.VehicleTravelLogs.AddAsync(newTrip);
                        }
                        await _context.SaveChangesAsync(); // Save all changes asynchronously

                        // For now, return the JSON string (you could also parse it as needed)
                        Console.WriteLine("Samsara API Response:");
                        Console.WriteLine(jsonResponse);
                    }
                    else
                    {
                        // Handle error response
                        Console.WriteLine($"Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                    }
                }
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP-specific errors like connectivity issues
                Console.WriteLine($"HTTP Error: {httpEx.Message}");
            }
            catch (JsonException jsonEx)
            {
                // Handle errors related to JSON parsing
                Console.WriteLine($"JSON Parsing Error: {jsonEx.Message}");
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database-specific exceptions
                Console.WriteLine($"Database Update Error: {dbEx.Message}");
            }
            catch (Exception ex)
            {
                // Handle any other general errors
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }
        }


        public class TripResponse
        {
            public List<TripData> Trips { get; set; }
        }

        public class TripData
        {
            public long StartMs { get; set; }
            public long EndMs { get; set; }

            public string StartLocation { get; set; }
            public string EndLocation { get; set; }

            public long StartOdometer { get; set; }
            public long EndOdometer { get; set; }
            public long DistanceMeters { get; set; }
        }




        private Models.VehicleDatum MapDtoToModel(DTOs.Vehicle curVehicle)
        {
            return new Models.VehicleDatum
            {
                SamsaraVehicleId = curVehicle.SamsaraVehicleID,
                VehicleName = curVehicle.VehicleName,
                VehicleVin = curVehicle.VehicleVIN,
                // Map additional fields as necessary
            };
        }
   

    }

}

