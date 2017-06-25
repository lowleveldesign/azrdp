using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop.Azure
{
    sealed class AzureVMLocalizer
    {
        sealed class AzureVM
        {
            public string Name { get; set; }

            public bool IsInScaleSet { get; set; }

            public string ScaleSetName { get; set; }

            public string IPAddress { get; set; }

            public string SubnetId { get; set; }
        }

        private readonly AzureResourceManager resourceManager;

        private string subscriptionId;
        private string resourceGroupName;
        private string resourceGroupLocation;
        private string virtualNetworkId;
        private string subnetId;
        private string targetIPAddress;

        public AzureVMLocalizer(AzureResourceManager resourceManager)
        {
            this.resourceManager = resourceManager;
            this.subscriptionId = resourceManager.SubscriptionId;
        }

        public string SubscriptionId { get { return subscriptionId; } }

        public string ResourceGroupName { get { return resourceGroupName; } }

        public string ResourceGroupLocation { get { return resourceGroupLocation; } }

        public string VirtualNetworkId { get { return virtualNetworkId; } }

        public string SubnetId { get { return subnetId; } }

        public string TargetIPAddress { get { return targetIPAddress; } }

        public async Task LocalizeVMAsync(string resourceGroupName, string vmIPAddress, CancellationToken cancellationToken)
        {
            // resource group - we do not validate the name of the resource group
            await FindResourceGroupAsync(resourceGroupName, cancellationToken);

            // virtual machine
            if (!string.IsNullOrEmpty(vmIPAddress)) {
                targetIPAddress = vmIPAddress;
                await FindVnetAndSubnet(cancellationToken);
            } else {
                await PromptForVirtualMachineAsync(cancellationToken);
            }

            Debug.Assert(subscriptionId != null);
            Debug.Assert(this.resourceGroupName != null);
            Debug.Assert(subnetId != null);
            Debug.Assert(virtualNetworkId != null);
            Debug.Assert(targetIPAddress != null);
        }

        private async Task FindResourceGroupAsync(string resourceGroupName, CancellationToken cancellationToken)
        {
            Debug.Assert(subscriptionId != null);

            if (!string.IsNullOrEmpty(resourceGroupName)) {
                var resourceGroup = await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}",
                    cancellationToken);
                this.resourceGroupName = resourceGroupName;
                this.resourceGroupLocation = resourceGroup.Value<string>("location");
                return;
            } 

            var resourceGroups = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/resourceGroups",
                cancellationToken))["value"].ToArray();

            if (resourceGroups.Length == 0) {
                throw new ArgumentException($"No Resource Groups found in the subscription {subscriptionId}");
            }

            int num;
            if (resourceGroups.Length > 1) {
                Console.WriteLine("------------------------------------------");
                Console.WriteLine("Resource Groups found in the subscription:");
                Console.WriteLine("------------------------------------------");
                for (int i = 0; i < resourceGroups.Length; i++) {
                    Console.WriteLine("[{0}] {1}", i + 1, resourceGroups[i].Value<string>("name"));
                }
                Console.WriteLine();
                Console.Write("Please choose the Resource Group number to use: ");
                string response = Console.ReadLine();
                if (!int.TryParse(response, out num) || num < 1 || num > resourceGroups.Length) {
                    throw new ArgumentException("Invalid Resource Group number");
                }
            } else {
                num = 1;
            }

            Debug.Assert(num > 0 && num <= resourceGroups.Length);
            this.resourceGroupName = resourceGroups[num - 1].Value<string>("name");
            resourceGroupLocation = resourceGroups[num - 1].Value<string>("location");
        }

        private string GetVirtualNetworkIdFromSubnetId(string subnetId)
        {
            var tokens = subnetId.Split('/');
            return string.Join("/", tokens, 0, tokens.Length - 2);
        }

        private async Task PromptForVirtualMachineAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(subscriptionId != null);
            Debug.Assert(resourceGroupName != null);

            var foundVirtualMachines = new List<AzureVM>();

            var networkInterfaces = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/" +
                $"resourceGroups/{resourceGroupName}/providers/Microsoft.Network/networkInterfaces",
                cancellationToken))["value"].ToArray();

            foreach (var networkInterface in networkInterfaces) {
                if (networkInterface["properties"]["virtualMachine"].HasValues) {
                    var virtualMachineId = networkInterface["properties"]["virtualMachine"].Value<string>("id");
                    if (networkInterface["properties"]["ipConfigurations"].HasValues) {
                        var ipConfigurations = networkInterface["properties"]["ipConfigurations"].ToArray();
                        foreach (var ipConfiguration in ipConfigurations) {
                            if (string.Equals(ipConfiguration["properties"].Value<string>("provisioningState"),
                                "Succeeded", StringComparison.Ordinal)) {
                                foundVirtualMachines.Add(new AzureVM {
                                    IsInScaleSet = false,
                                    Name = Path.GetFileName(virtualMachineId),
                                    IPAddress = ipConfiguration["properties"].Value<string>("privateIPAddress"),
                                    SubnetId = ipConfiguration["properties"]["subnet"].Value<string>("id")
                                });
                            }
                        }
                    }
                }
            }

            // VM might be in a scale set
            var scaleSets = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/" +
                $"resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/VirtualMachineScaleSets?api-version=2017-03-30",
                cancellationToken))["value"].ToArray();
            foreach (var scaleSet in scaleSets) {
                var r = await resourceManager.GetAsync(scaleSet.Value<string>("id") + "/networkInterfaces?api-version=2017-03-30",
                    cancellationToken);
                networkInterfaces = (r)["value"].ToArray();
                foreach (var networkInterface in networkInterfaces) {
                    if (networkInterface["properties"]["virtualMachine"].HasValues) {
                        var virtualMachineId = networkInterface["properties"]["virtualMachine"].Value<string>("id");
                        if (networkInterface["properties"]["ipConfigurations"].HasValues) {
                            var ipConfigurations = networkInterface["properties"]["ipConfigurations"].ToArray();
                            foreach (var ipConfiguration in ipConfigurations) {
                                if (string.Equals(ipConfiguration["properties"].Value<string>("provisioningState"),
                                    "Succeeded", StringComparison.Ordinal)) {
                                    foundVirtualMachines.Add(new AzureVM {
                                        IsInScaleSet = true,
                                        ScaleSetName = scaleSet.Value<string>("name"),
                                        Name = Path.GetFileName(virtualMachineId),
                                        IPAddress = ipConfiguration["properties"].Value<string>("privateIPAddress"),
                                        SubnetId = ipConfiguration["properties"]["subnet"].Value<string>("id")
                                    });
                                }
                            }
                        }
                    }
                }
            }

            if (foundVirtualMachines.Count == 0) {
                throw new ArgumentException($"No running VMs found in the resource group {resourceGroupName}");
            }

            int num;
            if (foundVirtualMachines.Count == 1) {
                num = 1;
            } else {
                Console.WriteLine("---------------------------------------------------");
                Console.WriteLine("VM IP addresses found in the chosen Resource Group:");
                Console.WriteLine("---------------------------------------------------");
                for (int i = 0; i < foundVirtualMachines.Count; i++) {
                    var vm = foundVirtualMachines[i];
                    Console.WriteLine("[{0}] {1}, ip: {2}", i + 1, 
                        vm.IsInScaleSet ? $"{vm.ScaleSetName}/{vm.Name}" : vm.Name, vm.IPAddress);
                }

                Console.Write("Please choose the Virtual Machine number to connect: ");
                string response = Console.ReadLine();
                if (!int.TryParse(response, out num) || num < 1 || num > foundVirtualMachines.Count) {
                    throw new ArgumentException("Invalid Resource Group number");
                }
            }

            Debug.Assert(num > 0 && num <= foundVirtualMachines.Count);
            subnetId = foundVirtualMachines[num - 1].SubnetId;
            virtualNetworkId = GetVirtualNetworkIdFromSubnetId(subnetId);
            targetIPAddress = foundVirtualMachines[num - 1].IPAddress;
        }

        private async Task FindVnetAndSubnet(CancellationToken cancellationToken)
        {
            Debug.Assert(subscriptionId != null);
            Debug.Assert(resourceGroupName != null);
            Debug.Assert(targetIPAddress != null);

            IPAddress virtualMachineIPAddress;
            if (!IPAddress.TryParse(targetIPAddress, out virtualMachineIPAddress)) {
                throw new ArgumentException("Invalid format of the IP address");
            }

            var virtualNetworks = (await resourceManager.GetAsync($"/subscriptions/{subscriptionId}/" +
                $"resourceGroups/{resourceGroupName}/providers/Microsoft.Network/virtualnetworks",
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
    }
}
