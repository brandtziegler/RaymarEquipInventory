﻿using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ILabourService    
    {
        //Task<Vehicle> GetVehicleById(int id);
        //Task<Vehicle> GetVehicleBySamsaraId(string samsaraID);

        //Task<List<Vehicle>> GetAllVehicles();

        //Task<DTOs.PartsUsed> GetlabourById(int partID);

        Task<List<DTOs.LabourLine>> GetLabourByWorkOrder(Int32 workOrderID);

        Task<LabourLine> GetLabourById(Int32 labourID);

        //Task<List<Tech>> GetAllParts();

    }
}