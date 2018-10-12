
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Net;
using System.Configuration;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using System.Linq;
using System.Net.Http;
using System;

namespace azure_functions_dyndns
{
    public static class UpdateDns
    {
        [FunctionName("UpdateDns")]
        public static async System.Threading.Tasks.Task<HttpResponseMessage> RunAsync([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequest req, TraceWriter log)
        {
            string ip = req.GetQueryParameterDictionary()
                .FirstOrDefault(q => string.Compare(q.Key, "ip", true) == 0)
                .Value;

            IPAddress ipAddress;

            if (ip == null || !IPAddress.TryParse(ip, out ipAddress))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            log.Info("IP update request received: " + ip);

            // Build the service credentials and DNS management client
            var tenantId = Environment.GetEnvironmentVariable("TenantId");
            var clientId = Environment.GetEnvironmentVariable("AppId");
            var secret = Environment.GetEnvironmentVariable("AppSecret");
            var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
            var resourceGroupName = Environment.GetEnvironmentVariable("ResourceGroupName");
            var zoneName = Environment.GetEnvironmentVariable("ZoneName");
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

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
