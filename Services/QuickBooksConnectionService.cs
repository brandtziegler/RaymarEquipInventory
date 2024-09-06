
using Microsoft.Extensions.Configuration;
using System.Data.Odbc;


namespace RaymarEquipmentInventory.Services
{
    public class QuickBooksConnectionService : IQuickBooksConnectionService
    {
        private readonly IConfiguration _configuration;
        private OdbcConnection? _connection;

        public QuickBooksConnectionService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public OdbcConnection GetConnection()
        {
            if (_connection == null)
            {
                var connectionString = _configuration.GetConnectionString("QuickBooksODBCConnection");
                _connection = new OdbcConnection(connectionString);
            }
            return _connection;
        }

        public void OpenConnection()
        {
            if (_connection == null)
            {
                GetConnection();
            }

            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                _connection.Open();
            }
        }

        public void CloseConnection()
        {
            if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
            {
                _connection.Close();
            }
        }
    }
}