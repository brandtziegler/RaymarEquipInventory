using System.Data.Odbc;

namespace RaymarEquipmentInventory.Services
{
    public interface IQuickBooksConnectionService
    {
        OdbcConnection GetConnection();
        void OpenConnection();
        void CloseConnection();
    }
}