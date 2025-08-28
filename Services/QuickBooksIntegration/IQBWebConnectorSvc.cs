using Microsoft.Data.SqlClient;
using System.ServiceModel;
using RaymarEquipmentInventory.DTOs; // RaymarInventoryDBContext

namespace RaymarEquipmentInventory.Services
{
    [ServiceContract(Namespace = "http://developer.intuit.com/")]
    public interface IQBWebConnectorSvc
    {
        [OperationContract]
        string clientVersion(string strVersion);

        [OperationContract]
        string serverVersion();

        [OperationContract]
        string[] authenticate(string strUserName, string strPassword);

        // NOTE: add strHCPResponse as the 2nd parameter
        [OperationContract]
        string sendRequestXML(string ticket, string strHCPResponse,
                              string strCompanyFileName, string qbXMLCountry,
                              int qbXMLMajorVers, int qbXMLMinorVers);

        [OperationContract]
        int receiveResponseXML(string ticket, string response, string hresult, string message);

        // NEW: must exist
        [OperationContract]
        string connectionError(string ticket, string hresult, string message);

        [OperationContract]
        string getLastError(string ticket);

        [OperationContract]
        string closeConnection(string ticket);
    }
}
