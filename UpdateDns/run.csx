using System.Net;
using System.Configuration;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    string ip = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "ip", true) == 0)
        .Value;

    IPAddress ipAddress;

    if (ip == null || !IPAddress.TryParse(ip, out ipAddress))
    {
        return req.CreateResponse(HttpStatusCode.BadRequest);
    }

    log.Info("IP update request received: " + ip);

    // Build the service credentials and DNS management client
    var tenantId = ConfigurationManager.AppSettings["TenantId"];
    var clientId = ConfigurationManager.AppSettings["AppId"];
    var secret = ConfigurationManager.AppSettings["AppSecret"];
    var subscriptionId = ConfigurationManager.AppSettings["SubscriptionId"];
    var resourceGroupName = ConfigurationManager.AppSettings["ResourceGroupName"];
    var zoneName = ConfigurationManager.AppSettings["ZoneName"];
    var recordSetName = "@";

    var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, secret);
    var dnsClient = new DnsManagementClient(serviceCreds);
    dnsClient.SubscriptionId = subscriptionId;

    var recordSet = dnsClient.RecordSets.Get(resourceGroupName, zoneName, recordSetName, RecordType.A);

    // Add a new record to the local object.  Note that records in a record set must be unique/distinct
    var currentIp = recordSet.ARecords[0].Ipv4Address;
    if (currentIp != ip)
    {
        recordSet.ARecords[0].Ipv4Address = ip;
        
        // Update the record set in Azure DNS
        // Note: ETAG check specified, update will be rejected if the record set has changed in the meantime
        recordSet = await dnsClient.RecordSets.CreateOrUpdateAsync(resourceGroupName, zoneName, recordSetName, RecordType.A, recordSet, recordSet.Etag);

        log.Info("IP changed from " + currentIp + " to " + ip);

        return req.CreateResponse(HttpStatusCode.OK);
    }

    return req.CreateResponse(HttpStatusCode.Accepted);
}
