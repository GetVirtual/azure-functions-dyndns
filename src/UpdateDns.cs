using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns;

namespace AzureFunctions
{
  public class UpdateDns
  {
    private readonly ILogger<UpdateDns> _logger;

    public UpdateDns(ILogger<UpdateDns> logger)
    {
      _logger = logger;
    }

    [Function("UpdateDns")]
    public async Task<IActionResult> Run(
      [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, string ip)
    {
      if (string.IsNullOrEmpty(ip) || !IPAddress.TryParse(ip, out _))
      {
        return new BadRequestObjectResult("Invalid IP address")
        {
          StatusCode = (int)HttpStatusCode.BadRequest,
          ContentTypes = { "text/plain" }
        };
      }
      
      _logger.LogInformation("IP update request received: {ip}", ip);

      var tenantId = Environment.GetEnvironmentVariable("TenantId");
      var clientId = Environment.GetEnvironmentVariable("AppId");
      var secret = Environment.GetEnvironmentVariable("AppSecret");
      var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
      var resourceGroupName = Environment.GetEnvironmentVariable("ResourceGroupName");
      var zoneName = Environment.GetEnvironmentVariable("ZoneName");
      var recordSetName = Environment.GetEnvironmentVariable("RecordSetName");

      try
      {
        ArmClient client;

        if (!string.IsNullOrEmpty(secret))
        {
          var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, secret);
          client = new ArmClient(clientSecretCredential);
        }
        else
        {
          var managedIdentityCredential = new ManagedIdentityCredential(
            new ManagedIdentityCredentialOptions(
              clientId == null ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(clientId)));

          client = new ArmClient(managedIdentityCredential);
        }

        var dnsRecord = client.GetDnsARecordResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Network/dnsZones/{zoneName}/A/{recordSetName}"));
        dnsRecord = await dnsRecord.GetAsync();

        if (dnsRecord.Data != null)
        {
          var dnsRecordData = dnsRecord.Data;
          var dnsARecord = dnsRecordData.DnsARecords[0];

          if (dnsARecord.IPv4Address.ToString() != ip)
          {
            dnsARecord.IPv4Address = IPAddress.Parse(ip);
            // Update the record set in Azure DNS
            // Note: ETAG check specified, update will be rejected if the record set has changed in the meantime
            dnsRecord = await dnsRecord.UpdateAsync(dnsRecordData, dnsRecordData.ETag);

            _logger.LogInformation("IP changed from {currentIp} to {ip}", dnsARecord.IPv4Address.ToString(), ip);
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error updating DNS record: {message}", ex.Message);
        return new StatusCodeResult((int)HttpStatusCode.InternalServerError);
      }

      return new OkObjectResult("success")
      {
        StatusCode = (int)HttpStatusCode.OK,
        ContentTypes = { "text/plain" }
      };
    }
  }
}
