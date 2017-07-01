using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
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
        private readonly Stack<AzureResource> createdResources = new Stack<AzureResource>(10);

        private string networkSecurityGroupId;
        private string publicIPId;
        private string networkInterfaceCardId;
        private string virtualMachineId;
        private string virtualMachineOsDiskId;

        public AzureJumpHost(AzureResourceManager resourceManager, AzureVMLocalizer targetVM)
        {
            this.resourceManager = resourceManager;
            this.targetVM = targetVM;
            uniqueResourceIdentifier = "a" + Guid.NewGuid().ToString("n");
        }

        public async Task DeployAndStartAsync(string rootUsername, string sshPublicKey,
            string vmSize = "Standard_F1S", CancellationToken cancellationToken = default(CancellationToken))
        {
            await CreatePublicIPAsync(cancellationToken);

            await CreateNetworkSecurityGroupAsync(cancellationToken);

            await CreateNetworkInterfaceAsync(cancellationToken);

            await CreateVirtualMachineAsync(rootUsername, sshPublicKey, vmSize, cancellationToken);
        }

        private async Task CreatePublicIPAsync(CancellationToken cancellationToken)
        {
            Console.Write("Creating public IP address...");

            var properties = new JObject();
            properties.Add("publicIPAllocationMethod", "Dynamic");
            properties.Add("publicIPAddressVersion", "IPv4");

            var ip = new JObject();
            ip.Add("location", targetVM.ResourceGroupLocation);
            ip.Add("properties", properties);

            var id = $"{uniqueResourceIdentifier}-pip";
            publicIPId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/publicIPAddresses/{id}";

            await resourceManager.PutAsync(publicIPId, ip.ToString(), cancellationToken);

            createdResources.Push(new AzureResource { ResourceId = publicIPId });
            Console.WriteLine("done ({0})", id);
        }

        private async Task<string> GetMyPublicIPAsync(CancellationToken cancellationToken)
        {
            var httpClient = new HttpClient();
            try {
                using (var resp = await httpClient.GetAsync("https://api.ipify.org/", cancellationToken)) {
                    if (resp.StatusCode != HttpStatusCode.OK) {
                        throw new Exception($"The bot.whatismyipaddress.com returned: {(int)resp.StatusCode}");
                    }
                    var ipaddr = await resp.Content.ReadAsStringAsync();
                    IPAddress ip;
                    if (!IPAddress.TryParse(ipaddr, out ip)) {
                        throw new Exception($"Invalid IP address returned: {ipaddr}");
                    }
                    return ip.ToString();
                }
            } catch (Exception ex) {
                Console.WriteLine("WARNING: Problem while trying to get your outgoing IP address " +
                    "- the firewall for the jump host will accept connections from all the hosts. " +
                    "Error details: {0}", ex.Message);
                return "Internet";
            } finally {
                httpClient.Dispose();
            }
        }

        private async Task CreateNetworkSecurityGroupAsync(CancellationToken cancellationToken)
        {
            Console.Write("Creating Network Security Group...");

            var securityRules = new JArray();
            var rule = new JObject();
            rule.Add("name", "ssh");
            rule.Add("properties", new JObject() {
                { "protocol", "Tcp" },
                { "sourcePortRange", "*" },
                { "destinationPortRange", "22" },
                { "sourceAddressPrefix", await GetMyPublicIPAsync(cancellationToken) },
                { "destinationAddressPrefix", "*" },
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
            networkSecurityGroupId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/networkSecurityGroups/{id}";

            if (!cancellationToken.IsCancellationRequested) {
                await resourceManager.PutAsync(networkSecurityGroupId, nsg.ToString(), cancellationToken);

                createdResources.Push(new AzureResource { ResourceId = networkSecurityGroupId });
                Console.WriteLine("done ({0})", id);
            }
        }

        public async Task CreateNetworkInterfaceAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(publicIPId != null);
            Debug.Assert(networkSecurityGroupId != null);

            Console.Write("Creating Network Interface Card...");

            var ipConfigurations = new JArray();
            ipConfigurations.Add(new JObject() {
                { "name", "vmip" },
                { "properties", new JObject() {
                    { "subnet", new JObject() { {  "id", targetVM.SubnetId } } },
                    { "privateIPAllocationMethod", "Dynamic" },
                    { "publicIPAddress", new JObject() { { "id", publicIPId } } }
                } }
            });

            var nic = new JObject() {
                { "location", targetVM.ResourceGroupLocation },
                { "properties", new JObject() {
                        { "networkSecurityGroup", new JObject() { { "id", networkSecurityGroupId } } },
                        { "ipConfigurations", ipConfigurations }
                    }
                }
            };

            var id = $"{uniqueResourceIdentifier}-nic";
            networkInterfaceCardId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Network/networkInterfaces/{id}";

            await resourceManager.PutAsync(networkInterfaceCardId, nic.ToString(), cancellationToken);

            createdResources.Push(new AzureResource { ResourceId = networkInterfaceCardId });
            Console.WriteLine("done ({0})", id);
        }

        private async Task CreateVirtualMachineAsync(string rootUsername, string sshPublicKey,
            string vmSize, CancellationToken cancellationToken)
        {
            Debug.Assert(networkInterfaceCardId != null);

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
                        { "id", networkInterfaceCardId },
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
                    { "hardwareProfile", new JObject() { { "vmSize", vmSize } } },
                    { "storageProfile", storageProfile },
                    { "osProfile", osProfile },
                    { "networkProfile", networkProfile },
                    { "diagnosticsProfile", diagnosticsProfile }
                } }
            };

            virtualMachineId = $"/subscriptions/{targetVM.SubscriptionId}/resourceGroups/{targetVM.ResourceGroupName}" +
                $"/providers/Microsoft.Compute/virtualMachines/{id}?api-version=2017-03-30";

            await resourceManager.PutAsync($"{virtualMachineId}", vm.ToString(), cancellationToken);

            do {
                Console.Write(".");
                await Task.Delay(TimeSpan.FromSeconds(5));

                vm = await resourceManager.GetAsync(virtualMachineId, cancellationToken);

                UpdateVirtualMachineAssets(vm);
            } while (!IsVmRunning(vm));
            Console.WriteLine("done ({0})", id);
        }

        private void UpdateVirtualMachineAssets(JToken vm)
        {
            if (virtualMachineOsDiskId == null) {
                var id = vm["properties"]["storageProfile"]["osDisk"]["managedDisk"].Value<string>("id");
                if (id != null) {
                    virtualMachineOsDiskId = $"{id}?api-version=2017-03-30";
                    createdResources.Push(new AzureResource { ResourceId = virtualMachineOsDiskId });
                    createdResources.Push(new AzureResource { ResourceId = virtualMachineId, ShouldWaitForRemoval = true });
                }
            }
        }

        private bool IsVmRunning(JToken vm)
        {
            return string.Equals(vm["properties"].Value<string>("provisioningState"), "succeeded", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> GetPublicIPAddressAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(publicIPId != null);
            if (publicIPId == null) {
                throw new InvalidOperationException();
            }

            var publicIP = await resourceManager.GetAsync(publicIPId, cancellationToken);
            var publicIPAddress = publicIP["properties"].Value<string>("ipAddress");
            IPAddress ip;
            if (!IPAddress.TryParse(publicIPAddress, out ip)) {
                throw new InvalidOperationException("Public IP address was not assigned to the VM.");
            }

            return publicIPAddress;
        }

        public void Dispose()
        {
            Console.WriteLine();
            while (createdResources.Count != 0) {
                var resource = createdResources.Pop();
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
