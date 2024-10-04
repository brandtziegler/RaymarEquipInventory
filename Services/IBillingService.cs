﻿using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface IBillingService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        //Task<DTOs.PartsUsed> GetlabourById(int partID);

        Task<DTOs.Billing> GetLabourForWorkorder(int workOrderID);


        //Task<List<Tech>> GetAllParts();

    }
}
