using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop.Azure
{
    internal sealed class AzureJumpHost : IDisposable
    {
        private readonly AzureResourceManager resourceManager;
        private readonly AzureVMLocalizer targetVM;
        private readonly string uniqueResourceIdentifier;
        private readonly LinkedList<string> createdResourceIds = new LinkedList<string>();

        public AzureJumpHost(AzureResourceManager resourceManager, AzureVMLocalizer targetVM)
        {
            this.resourceManager = resourceManager;
            this.targetVM = targetVM;
            this.uniqueResourceIdentifier = Guid.NewGuid().ToString("n");
        }

        public async Task CreatePublicIP(CancellationToken cancellationToken)
        {
            Console.Write("Creating public IP address...");

            var properties = new JObject();
            properties.Add("publicIPAllocationMethod", "Dynamic");
            properties.Add("publicIPAddressVersion", "IPv4");

            var ip = new JObject();
            ip.Add("location", targetVM.ResourceGroupLocation);
            ip.Add("properties", properties);

            var resourceId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/publicIPAddresses/ip{uniqueResourceIdentifier}";

            await resourceManager.PutAsync(resourceId, ip.ToString(), cancellationToken);

            createdResourceIds.AddFirst(resourceId);
            Console.WriteLine("done (ip{0})", uniqueResourceIdentifier);
        }

        public async Task DeployAndStartAsync(string rootUsername, string sshPublicKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            await CreatePublicIP(cancellationToken);
        }

        public void Dispose()
        {
            // FIXME: remove Azure resources
            foreach (var resourceId in createdResourceIds) {
                resourceManager.DeleteAsync(resourceId, CancellationToken.None).Wait();
            }
        }
    }
}
