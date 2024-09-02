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
                            var inventoryIdTest = reader["ID"].ToString();
                            var itmNameTest = reader["Name"].ToString();
                            var manuPartNumTest = reader["PartNumber"].ToString();
                            var descTest = reader["Description"].ToString();
                            var costTest = reader["PurchaseCost"].ToString();
                            var salePriceText = reader["Price"].ToString();
                            var qtyTest = reader["QuantityOnHand"].ToString();
                            //var reorderPointTest = reader["ReorderPoint"].ToString();
                            

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
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Well, ain't that a kick in the teeth: {ex.Message}");
                // Handle logging or re-throw as needed
            }

            return inventoryParts;
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

