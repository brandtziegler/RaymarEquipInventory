﻿using RaymarEquipmentInventory.DTOs;

namespace RaymarEquipmentInventory.Services
{
    public interface ICustomerService
    {
        Task<List<CustomerData>> GetCustomersFromQuickBooksAsnyc(bool doUpdate = false);

        //Task UpdateOrInsertCustomersAsync(List<CustomerData> customerDataList); // Declares the async task method

    }
}