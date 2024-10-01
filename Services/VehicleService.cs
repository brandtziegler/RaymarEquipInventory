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

            var existingVehicleWO = await _context.VehicleWorkOrders
                .FirstOrDefaultAsync(i => i.SheetId == SheetID && i.VehicleId == VehicleID); // Using async for efficiency

            var vehicle = await _context.VehicleData
                .FirstOrDefaultAsync(i => i.VehicleId == VehicleID); // Using async for efficiency

            var workOrder = await _context.WorkOrderSheets
                .FirstOrDefaultAsync(i => i.SheetId == SheetID); // Using async for efficiency

            if (vehicle != null && workOrder != null)
            {
                var samsaraID = vehicle.SamsaraVehicleId;
                var startTime = workOrder.DateTimeStarted;
                var endTime = workOrder.DateTimeCompleted;
                var vehicleId = vehicle.SamsaraVehicleId;

                var startMs = new DateTimeOffset(startTime).ToUnixTimeMilliseconds();
                var endMs = new DateTimeOffset(endTime).ToUnixTimeMilliseconds();
              
                // Create the request URL with parameters
                var requestUrl = $"/v1/fleet/trips?vehicleId={vehicleId}&startMs={startMs}&endMs={endMs}";

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

                    // For now, return the JSON string (you could also parse it as needed)
                    Console.WriteLine("Samsara API Response:");
                    Console.WriteLine(jsonResponse);

                    // If needed, you could further deserialize the JSON into a C# object
                    // var trips = JsonSerializer.Deserialize<TripResponse>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    // Handle error response
                    Console.WriteLine($"Error: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                }

            }

    


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
        private static string CleanString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove non-printable characters and trim whitespace
            return new string(input.Where(c => !char.IsControl(c)).ToArray()).Trim();
        }

        // Method to safely parse decimals
        private static decimal? ParseDecimal(object input)
        {
            if (input == null || input == DBNull.Value)
                return null;

            if (decimal.TryParse(input.ToString(), out decimal result))
                return result;

            return null; // Or return a default value if needed
        }

        // Method to safely parse integers
        private static int? ParseInt(object input)
        {
            if (input == null || input == DBNull.Value)
                return null;

            if (int.TryParse(input.ToString(), out int result))
                return result;

            return null; // Or return a default value if needed
        }

    }

}

