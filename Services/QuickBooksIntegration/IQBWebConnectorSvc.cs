using Microsoft.Data.SqlClient;
using System.ServiceModel;
using RaymarEquipmentInventory.DTOs; // RaymarInventoryDBContext

namespace RaymarEquipmentInventory.Services
{
    // QBWC expects these method names/signatures
    [ServiceContract]
    public interface IQBWebConnectorSvc
    {
        [OperationContract]
        string[] authenticate(string strUserName, string strPassword);

        [OperationContract]
        string clientVersion(string strVersion);

        [OperationContract]
        string serverVersion();

        [OperationContract]
        string getLastError(string ticket);

        [OperationContract]
        string closeConnection(string ticket);

        // Typical QBWC signature (company file path may be blank)
        [OperationContract]
        string sendRequestXML(string ticket, string strCompanyFileName, string qbXMLCountry, int qbXMLMajorVers, int qbXMLMinorVers);

        [OperationContract]
        int receiveResponseXML(string ticket, string response, string hresult, string message);
    }
}
