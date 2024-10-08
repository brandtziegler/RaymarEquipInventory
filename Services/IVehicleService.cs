﻿using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IVehicleService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        Task UpdateOrInsertVehiclesAsync(List<Vehicle> vehicleList); // Declares the async task method

        Task UpdateVehicleLog(int SheetID, int VehicleID); // Declares the async task method

        Task<List<TripLog>>GetTripLog(int SheetID); // Declares the async task method
    }
}
