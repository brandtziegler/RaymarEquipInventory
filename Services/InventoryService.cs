using Microsoft.EntityFrameworkCore;
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

        public InventoryService(IQuickBooksConnectionService quickBooksConnectionService)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
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
            string query = "SELECT TOP 1 Name FROM Customers";
            string otherQuery = "SELECT Name, Description, QuantityOnHand, SalesPrice FROM Items WHERE Type = 'Inventory'";
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

                                Description = reader["Description"].ToString(),
                                OnHand = Convert.ToInt32(reader["QuantityOnHand"]),
                                SalesPrice = Convert.ToDecimal(reader["SalesPrice"])
                            });
                        }
                    }
                }

                _quickBooksConnectionService.CloseConnection();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Handle logging or re-throw as needed
            }

            return inventoryParts;
        }

    }

}

