using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using System.Net.Http.Headers;
using RaymarEquipmentInventory.Settings.YourApiProject.Settings;
using System.Text.Json;


namespace RaymarEquipmentInventory.Services
{
    public class SamsaraApiService : ISamsaraApiService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        private readonly HttpClient _httpClient;
        private readonly SamsaraApiConfig _config;
        public SamsaraApiService(HttpClient httpClient, RaymarInventoryDBContext context, IQuickBooksConnectionService quickBooksConnectionService, SamsaraApiConfig config)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _config = config;
            _httpClient = httpClient;
        }


        public async Task<Vehicle> GetVehicleByID(string vehicleId = "")
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/fleet/vehicles/{vehicleId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.BearerToken);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode(); // Throws if status code is not 2xx

            var jsonResponse = await response.Content.ReadAsStringAsync();

            // Deserialize JSON directly into a single VehicleData object
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var vehicleData = JsonSerializer.Deserialize<SingleVehicleDataResponse>(jsonResponse, options)?.Data;

            var vehicle = new Vehicle
            {
                SamsaraVehicleID = vehicleData.Id,
                VehicleName = vehicleData.Name,
                VehicleVIN = vehicleData.Vin
            };

            // Map to a single Vehicle object
            return vehicle;
        }

        public async Task<List<Vehicle>> GetAllVehicles()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/fleet/vehicles");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.BearerToken);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode(); // Throws if status code is not 2xx

            var jsonResponse = await response.Content.ReadAsStringAsync();

            // Deserialize the JSON response and map it to a List<Vehicle>
            return ParseVehicleResponse(jsonResponse);
        }

        private List<Vehicle> ParseVehicleResponse(string jsonResponse)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Deserialize JSON into an anonymous type containing the "data" array
            var vehiclesData = JsonSerializer.Deserialize<VehicleDataResponse>(jsonResponse, options)?.Data;

            // Use LINQ's Select to map to the List<Vehicle>
            var vehicles = vehiclesData?
                .Select(vehicleData => new Vehicle
                {
                    SamsaraVehicleID = vehicleData.Id,
                    VehicleName = vehicleData.Name,
                    VehicleVIN = vehicleData.Vin
                })
                .ToList();

            return vehicles ?? new List<Vehicle>(); // Return an empty list if vehiclesData is null
        }


        private class SingleVehicleDataResponse
        {
            public VehicleData Data { get; set; }
        }
        // Class representing the "data" array in the response
        private class VehicleDataResponse
        {
            public List<VehicleData> Data { get; set; }
        }

        private class VehicleData
        {
            public string Id { get; set; }     // Corresponds to JSON "id"
            public string Name { get; set; }   // Corresponds to JSON "name"
            public string Vin { get; set; }    // Corresponds to JSON "vin"
        }
    }

  

}

