using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop.Azure
{
    sealed class AzureVMLocalizer
    {
        private readonly AzureResourceManager resourceManager;

        private string subscriptionId;
        private string resourceGroupName;
        private string virtualNetworkId;
        private string subnetId;

        public AzureVMLocalizer(AzureResourceManager resourceManager)
        {
            this.resourceManager = resourceManager;
        }

        public string SubscriptionId { get { return subscriptionId; } }

        public string ResourceGroupName { get { return resourceGroupName; } }

        private async Task FindSubscriptionAsync(string subscriptionFromArguments, CancellationToken cancellationToken)
        {
            if (subscriptionFromArguments != null) {
                Guid g;
                if (Guid.TryParse(subscriptionFromArguments, out g)) {
                    subscriptionId = g.ToString();
                    return;
                }
            }

            // we need to figure out which subscription to use
            var subscriptions = (await resourceManager.GetAsync("/subscriptions", cancellationToken))["value"].ToArray();
            if (subscriptions.Length == 0) {
                throw new ArgumentException("No subscription found for your account.");
            }

            if (!string.IsNullOrEmpty(subscriptionFromArguments)) {
                // we will try to find the subscription automatically
                foreach (var subscription in subscriptions) {
                    if (string.Equals(subscription.Value<string>("displayName"), subscriptionFromArguments,
                        StringComparison.InvariantCultureIgnoreCase)) {
                        subscriptionId = subscription.Value<string>("subscriptionId");
                        return;
                    }
                }
                throw new ArgumentException("No subscription with the specified name was found.");
            }

            if (subscriptions.Length == 1) {
                // not much choice to make
                subscriptionId = subscriptions[0].Value<string>("subscriptionId");
                return;
            }

            Console.WriteLine("---------------------------------------");
            Console.WriteLine("Subscriptions assigned to your account:");
            Console.WriteLine("---------------------------------------");
            for (int i = 0; i < subscriptions.Length; i++) {
                Console.WriteLine("[{0}] {1} ({2})", i + 1, subscriptions[i].Value<string>("displayName"),
                    subscriptions[i].Value<string>("subscriptionId"));
            }
            Console.WriteLine();
            Console.Write("Please choose the subscription number to use: ");
            string response = Console.ReadLine();
            int num;
            if (!int.TryParse(response, out num) || num < 1 || num > subscriptions.Length) {
                throw new ArgumentException("Invalid subscription number");

            }

            Debug.Assert(num > 0 && num <= subscriptions.Length);
            subscriptionId = subscriptions[num - 1].Value<string>("subscriptionId");
        }

        private async Task FindResourceGroupAsync(string resourceGroup, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(resourceGroupName)) {
                resourceGroupName = resourceGroup;
                // we do not validate the name of the resource group
                return;
            }
            Debug.Assert(subscriptionId != null);
            var resourceGroups = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/resourceGroups",
                cancellationToken))["value"].ToArray();

            if (resourceGroups.Length == 0) {
                throw new ArgumentException($"No Resource Groups found in the subscription {subscriptionId}");
            }

            if (resourceGroups.Length == 1) {
                resourceGroupName = resourceGroups[0].Value<string>("name");
                return;
            }

            Console.WriteLine("------------------------------------------");
            Console.WriteLine("Resource Groups found in the subscription:");
            Console.WriteLine("------------------------------------------");
            for (int i = 0; i < resourceGroups.Length; i++) {
                Console.WriteLine("[{0}] {1}", i + 1, resourceGroups[i].Value<string>("name"));
            }
            Console.WriteLine();
            Console.Write("Please choose the Resource Group number to use: ");
            string response = Console.ReadLine();
            int num;
            if (!int.TryParse(response, out num) || num < 1 || num > resourceGroups.Length) {
                throw new ArgumentException("Invalid Resource Group number");
            }

            Debug.Assert(num > 0 && num <= resourceGroups.Length);
            resourceGroupName = resourceGroups[num - 1].Value<string>("name");
        }
        private async Task FindVnetAndSubnetByIPAddress(string virtualMachineIPAddressTxt, CancellationToken cancellationToken)
        {
            Debug.Assert(subscriptionId != null);
            Debug.Assert(resourceGroupName != null);

            IPAddress virtualMachineIPAddress;
            if (!IPAddress.TryParse(virtualMachineIPAddressTxt, out virtualMachineIPAddress)) {
                throw new ArgumentException("Invalid format of the IP address");
            }

            var virtualNetworks = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/" +
                "resourceGroups/{resourceGroupName}/providers/Microsoft.Network/virtualnetworks",
                cancellationToken))["value"].ToArray();

            foreach (var virtualNetwork in virtualNetworks) {
                var subnets = virtualNetwork["properties"]["subnets"].ToArray();
                foreach (var subnet in subnets) {
                    var addressSpace = subnet["properties"].Value<string>("addressPrefix");
                    var ipNetwork = IPNetwork.Parse(addressSpace);
                    if (IPNetwork.Contains(ipNetwork, virtualMachineIPAddress)) {
                        virtualNetworkId = virtualNetwork.Value<string>("id");
                        subnetId = subnet.Value<string>("id");
                        return;
                    }
                }
            }
            throw new ArgumentException("There is no subnet that contains an IP address " +
                    $"'{virtualMachineIPAddress}' in a resource group '{resourceGroupName}'");
        }

        private async Task FindVirtualMachineByIPAddress(string virtualMachineIPAddressTxt, CancellationToken cancellationToken)
        {
            Debug.Assert(subscriptionId != null);
            Debug.Assert(resourceGroupName != null);

            if (!string.IsNullOrEmpty(virtualMachineIPAddressTxt)) {
                await FindVnetAndSubnetByIPAddress(virtualMachineIPAddressTxt, cancellationToken);
            } else {
                Console.WriteLine("---------------------------------------------------");
                Console.WriteLine("VM IP addresses found in the chosen Resource Group:");
                Console.WriteLine("---------------------------------------------------");

                var networkInterfaces = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/" +
                    $"resourceGroups/{resourceGroupName}/providers/Microsoft.Network/networkInterfaces",
                    cancellationToken))["value"].ToArray();

                // FIXME: I need to save the VMs in a list for later pick
                foreach (var networkInterface in networkInterfaces) {
                    if (networkInterface["properties"]["virtualMachine"].HasValues) {
                        var virtualMachineId = networkInterface["properties"]["virtualMachine"].Value<string>("id");
                        if (networkInterface["properties"]["ipConfigurations"].HasValues) {
                            var ipConfigurations = networkInterface["properties"]["ipConfigurations"].ToArray();
                            foreach (var ipConfiguration in ipConfigurations) {
                                if (string.Equals(ipConfiguration["properties"].Value<string>("provisioningState"), 
                                    "Succeeded", StringComparison.Ordinal)) {
                                    var privateIpAddress = ipConfiguration["properties"].Value<string>("privateIPAddress");
                                    Console.WriteLine($"FIXME: {virtualMachineId} : {privateIpAddress}");
                                }
                            }
                        }
                    }

                }
            }
        }

        public async Task LocalizeVMAsync(Options options, CancellationToken cancellationToken)
        {
            await FindSubscriptionAsync(options.Subscription, cancellationToken);
            await FindResourceGroupAsync(options.ResourceGroupName, cancellationToken);
            await FindVirtualMachineByIPAddress(options.VirtualMachineIPAddress, cancellationToken);
        }
    }
}
