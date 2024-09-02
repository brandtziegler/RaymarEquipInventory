﻿using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;


namespace RaymarEquipmentInventory.Services
{
    public class InventoryService : IInventoryService
    {

        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        public InventoryService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
        }
        public string GetProductById(int id)
        {
            // Imagine this method actually does something useful.
            return $"Product details for product ID: {id}";
        }

        public string GetAllProducts()
        {
            // Again, let's pretend this returns actual product data.
            return "Returning all products... because why not?";
        }

        public List<InventoryData> GetInventoryPartsFromQuickBooks()
        {
            var inventoryParts = new List<InventoryData>();
           
            string otherQuery = "SELECT ID, Name, PartNumber, Description, PurchaseCost, Price, QuantityOnHand, ReorderPoint FROM Items WHERE Type = 'Inventory'";
            try
            {
                _quickBooksConnectionService.OpenConnection();
                using (var cmd = new OdbcCommand(otherQuery, _quickBooksConnectionService.GetConnection()))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            inventoryParts.Add(new InventoryData
                            {
                                InventoryId = CleanString(reader["ID"].ToString()),
                                ItemName = CleanString(reader["Name"].ToString()),
                                ManufacturerPartNumber = CleanString(reader["PartNumber"].ToString()),
                                Description = CleanString(reader["Description"].ToString()),
                                Cost = ParseDecimal(reader["PurchaseCost"] ?? 0),
                                SalesPrice = ParseDecimal(reader["Price"] ?? 0),
                                ReorderPoint = ParseInt(reader["ReorderPoint"]),
                                OnHand = ParseInt(reader["QuantityOnHand"] ?? 0),
                            });

                        }
                    }

                    _quickBooksConnectionService.CloseConnection();

                    foreach (var inventoryPart in inventoryParts)
                    {
                        var mappedInventory = MapDtoToModel(inventoryPart);
                        var existingInventory = _context.InventoryData.FirstOrDefault(i => i.QuickBooksInvId == inventoryPart.InventoryId);
                        if (existingInventory != null)
                        {
                            // Update existing record
                            existingInventory.ItemName = inventoryPart.ItemName;
                            existingInventory.QuickBooksInvId = inventoryPart.InventoryId;
                            existingInventory.ManufacturerPartNumber = inventoryPart.ManufacturerPartNumber;
                            existingInventory.Description = inventoryPart.Description;
                            existingInventory.Cost = inventoryPart.Cost;
                            existingInventory.SalesPrice = inventoryPart.SalesPrice;
                            existingInventory.ReorderPoint = inventoryPart.ReorderPoint;
                            existingInventory.OnHand = inventoryPart.OnHand;

                            // Update additional fields as necessary
                            _context.InventoryData.Update(existingInventory);
                        }
                        else
                        {
                            // Insert new record
                            _context.InventoryData.Add(mappedInventory);
                        }

                        //We are going to try to either insert records into _context.InventoryData or update _context.InventoryData
                        //depending on if the InventoryId is already found in QuickBooksInvId

                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Handle logging or re-throw as needed
            }
            _context.SaveChanges();
            return inventoryParts;
        }

        private Models.InventoryDatum MapDtoToModel(DTOs.InventoryData inventoryPart)
        {
            return new Models.InventoryDatum
            {
                QuickBooksInvId = inventoryPart.InventoryId, // Map the ID
                ItemName = inventoryPart.ItemName,
                ManufacturerPartNumber = inventoryPart.ManufacturerPartNumber,
                Description = inventoryPart.Description,
                Cost = inventoryPart.Cost,
                SalesPrice = inventoryPart.SalesPrice,
                ReorderPoint = inventoryPart.ReorderPoint,
                OnHand = inventoryPart.OnHand
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

