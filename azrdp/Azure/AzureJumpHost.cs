using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop.Azure
{
    internal sealed class AzureJumpHost : IDisposable
    {
        private class AzureResource
        {
            public string ResourceId { get; set; }

            public bool ShouldWaitForRemoval { get; set; }
        }

        private readonly AzureResourceManager resourceManager;
        private readonly AzureVMLocalizer targetVM;
        private readonly string uniqueResourceIdentifier;
        private readonly LinkedList<AzureResource> createdResources = new LinkedList<AzureResource>();

        public AzureJumpHost(AzureResourceManager resourceManager, AzureVMLocalizer targetVM)
        {
            this.resourceManager = resourceManager;
            this.targetVM = targetVM;
            this.uniqueResourceIdentifier = Guid.NewGuid().ToString("n");
        }

        public async Task DeployAndStartAsync(string rootUsername, string sshPublicKey, CancellationToken cancellationToken = default(CancellationToken))
        {
            var publicIP = await CreatePublicIP(cancellationToken);

            var nsg = await CreateNetworkSecurityGroup(cancellationToken);

            var nic = await CreateNetworkInterface(publicIP, nsg, cancellationToken);

            await CreateVirtualMachine(rootUsername, sshPublicKey, nic, cancellationToken);
        }

        private async Task<string> CreatePublicIP(CancellationToken cancellationToken)
        {
            Console.Write("Creating public IP address...");

            var properties = new JObject();
            properties.Add("publicIPAllocationMethod", "Dynamic");
            properties.Add("publicIPAddressVersion", "IPv4");

            var ip = new JObject();
            ip.Add("location", targetVM.ResourceGroupLocation);
            ip.Add("properties", properties);

            var id = $"IP{uniqueResourceIdentifier}-pip";
            var resourceId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/publicIPAddresses/{id}";

            await resourceManager.PutAsync(resourceId, ip.ToString(), cancellationToken);

            createdResources.AddFirst(new AzureResource { ResourceId = resourceId });
            Console.WriteLine("done ({0})", id);

            return resourceId;
        }

        private async Task<string> CreateNetworkSecurityGroup(CancellationToken cancellationToken)
        {
            Console.Write("Creating Network Security Group...");

            var securityRules = new JArray();
            var rule = new JObject();
            rule.Add("name", "ssh");
            rule.Add("properties", new JObject() {
                { "protocol", "Tcp" },
                { "sourcePortRange", "*" },
                { "destinationPortRange", "22" },
                { "sourceAddressPrefix", "Internet" }, // FIXME we should only use public IP here
                { "destinationAddressPrefix", targetVM.TargetIPAddress },
                { "access", "Allow" },
                { "direction", "Inbound" },
                { "priority", 100 }
            });
            securityRules.Add(rule);

            var nsg = new JObject();
            nsg.Add("location", targetVM.ResourceGroupLocation);
            nsg.Add("properties", new JObject() {
                { "securityRules", securityRules }
            });

            var id = $"{uniqueResourceIdentifier}-nsg";
            var resourceId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/networkSecurityGroups/{id}";

            await resourceManager.PutAsync(resourceId, nsg.ToString(), cancellationToken);

            createdResources.AddFirst(new AzureResource { ResourceId = resourceId });
            Console.WriteLine("done ({0})", id);

            return resourceId;
        }

        public async Task<string> CreateNetworkInterface(string publicIP, string nsg, CancellationToken cancellationToken)
        {
            Console.Write("Creating Network Interface Card...");

            var ipConfigurations = new JArray();
            ipConfigurations.Add(new JObject() {
                { "name", "vmip" },
                { "properties", new JObject() {
                    { "subnet", new JObject() { {  "id", targetVM.SubnetId } } },
                    { "privateIPAllocationMethod", "Dynamic" },
                    { "publicIPAddress", new JObject() { { "id", publicIP } } }
                } }
            });

            var nic = new JObject() {
                { "location", targetVM.ResourceGroupLocation },
                { "properties", new JObject() {
                        { "networkSecurityGroup", new JObject() { { "id", nsg } } },
                        { "ipConfigurations", ipConfigurations }
                    }
                }
            };

            var id = $"{uniqueResourceIdentifier}-nic";
            var resourceId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/networkInterfaces/{id}";

            await resourceManager.PutAsync(resourceId, nic.ToString(), cancellationToken);

            createdResources.AddFirst(new AzureResource { ResourceId = resourceId });
            Console.WriteLine("done ({0})", id);

            return resourceId;
        }

        private async Task CreateVirtualMachine(string rootUsername, string sshPublicKey, string nic, CancellationToken cancellationToken)
        {
            Console.Write("Creating Virtual Machine...");

            var id = $"{uniqueResourceIdentifier}-vm";

            var storageProfile = new JObject() {
                { "imageReference", new JObject() {
                    { "publisher", "Canonical" },
                    { "offer", "UbuntuServer" },
                    { "sku", "16.04-LTS" },
                    { "version", "latest" }
                } },
                { "osDisk", new JObject() {
                    { "createOption", "fromImage" },
                    { "managedDisk", new JObject() { { "storageAccountType", "Premium_LRS" } } }
                } }
            };

            var osProfile = new JObject() {
                { "computerName", id },
                { "adminUsername", rootUsername },
                { "linuxConfiguration", new JObject() {
                    { "disablePasswordAuthentication", true },
                    { "ssh", new JObject() {
                        { "publicKeys", new JArray() {
                            new JObject() {
                                { "path", $"/home/{rootUsername}/.ssh/authorized_keys" },
                                { "keyData", sshPublicKey }
                            }
                        } }
                    } }
                } }
            };

            var networkProfile = new JObject() {
                { "networkInterfaces", new JArray() {
                    new JObject() {
                        { "id", nic },
                        { "properties", new JObject() { { "primary", true } } }
                    }
                } }
            };

            var diagnosticsProfile = new JObject() {
                { "bootDiagnostics", new JObject() { { "enabled", false } } }
            };

            JToken vm = new JObject() {
                { "name", id },
                { "location", targetVM.ResourceGroupLocation },
                { "properties", new JObject() {
                    { "hardwareProfile", new JObject() { { "vmSize", "Standard_F1S" } } },
                    { "storageProfile", storageProfile },
                    { "osProfile", osProfile },
                    { "networkProfile", networkProfile },
                    { "diagnosticsProfile", diagnosticsProfile }
                } }
            };

            var resourceId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Compute/virtualMachines/{id}?api-version=2017-03-30";
            // FIXME managed disk to save
            //var osDiskResourceId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
            //    $"/providers/Microsoft.Compute/disks/{osDiskId}?api-version=2017-03-30";

            await resourceManager.PutAsync(resourceId, vm.ToString(), cancellationToken);

            createdResources.AddFirst(new AzureResource { ResourceId = resourceId, ShouldWaitForRemoval = true });
            //createdResources.AddFirst(new AzureResource { ResourceId = osDiskResourceId });

            bool provisioningFinished = false;
            while (!provisioningFinished) {
                Console.Write(".");
                await Task.Delay(TimeSpan.FromSeconds(5));

                vm = await resourceManager.GetAsync(resourceId, cancellationToken);
                foreach (var status in vm.Values("statuses")) {
                    if (status.Value<string>("code").Equals("PowerState/running", StringComparison.Ordinal)) {
                        provisioningFinished = true;
                        break;
                    }
                }
            }
            Console.WriteLine("done ({0})", id);
        }

        public void Dispose()
        {
            foreach (var resource in createdResources) {
                Console.Write("Removing {0}...", resource.ResourceId);
                resourceManager.DeleteAsync(resource.ResourceId, CancellationToken.None).Wait();
                if (resource.ShouldWaitForRemoval) {
                    while (resourceManager.HeadAsync(resource.ResourceId, CancellationToken.None).Result) {
                        Thread.Sleep(TimeSpan.FromSeconds(5));
                        Console.Write(".");
                    }
                }
                Console.WriteLine("done");
            }
        }
    }
}
