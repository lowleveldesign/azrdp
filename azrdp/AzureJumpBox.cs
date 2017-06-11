using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LowLevelDesign.AzureRemoteDesktop
{
    public sealed class AzureJumpBox : IDisposable
    {
        private readonly IAzure azure;
        private readonly string resourceGroupName;
        private readonly IPAddress virtualMachineIPAddress;

        private INetwork virtualMachineVnet;
        private ISubnet virtualMachineSubnet;
        private string jumpBoxName;
        private IVirtualMachine jumpBox;

        public AzureJumpBox(IAzure azure, string resourceGroupName, IPAddress virtualMachineIPAddress)
        {
            this.azure = azure;
            this.resourceGroupName = resourceGroupName;
            this.virtualMachineIPAddress = virtualMachineIPAddress;
        }

        public async Task DeployAndStart(CancellationToken cancellationToken = default(CancellationToken))
        {
            // FIXME: generate public and private key
            await FindVnetByIPAddress();
            FindSubnetNameByIPAddress();

            Debug.Assert(virtualMachineVnet != null);
            Debug.Assert(virtualMachineSubnet != null);

            jumpBoxName = Guid.NewGuid().ToString("n");

            Logger.Log.TraceEvent(TraceEventType.Information, 0, "Creating virtual machine and public IP, both named: '{0}'", jumpBoxName);
            jumpBox = await azure.VirtualMachines.Define(jumpBoxName)
                                 .WithRegion(virtualMachineVnet.RegionName)
                                 .WithExistingResourceGroup(resourceGroupName)
                                 .WithExistingPrimaryNetwork(virtualMachineVnet)
                                 .WithSubnet(virtualMachineSubnet.Name) // FIXME: maybe should be key?
                                 .WithPrimaryPrivateIPAddressDynamic()
                                 .WithNewPrimaryPublicIPAddress(
                                    azure.PublicIPAddresses.Define(jumpBoxName)
                                        .WithRegion("test")
                                        .WithExistingResourceGroup(resourceGroupName)
                                 )
                                 .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer14_04_Lts)
                                 .WithRootUsername("azrdp")
                                 .WithSsh("FIXME:public key")
                                 .WithSize(VirtualMachineSizeTypes.StandardA0)
                                 .CreateAsync(cancellationToken);
        }

        private async Task FindVnetByIPAddress()
        {
            Logger.Log.TraceEvent(TraceEventType.Verbose, 0, "Searching for the subnet containing a given IP address...");
            foreach (var network in await azure.Networks.ListAsync()) {
                if (string.Equals(resourceGroupName, network.ResourceGroupName, StringComparison.OrdinalIgnoreCase)) {
                    foreach (var addressSpace in network.AddressSpaces) {
                        var ipNetwork = IPNetwork.Parse(addressSpace);
                        if (IPNetwork.Contains(ipNetwork, virtualMachineIPAddress)) {
                            Logger.Log.TraceEvent(TraceEventType.Verbose, 0, "Found vnet: {0}", network.Name);
                            virtualMachineVnet = network;
                            return;
                        }
                    }
                }
            }
            throw new ArgumentException($"There is no virtual network that contains an IP address " +
                    "'{virtualMachineIPAddress}' in a resource group '{resourceGroupName}'");
        }

        private void FindSubnetNameByIPAddress()
        {
            Debug.Assert(virtualMachineVnet != null);
            foreach (var subnet in virtualMachineVnet.Subnets.Values) {
                var ipNetwork = IPNetwork.Parse(subnet.AddressPrefix);
                if (IPNetwork.Contains(ipNetwork, virtualMachineIPAddress)) {
                    Logger.Log.TraceEvent(TraceEventType.Verbose, 0, "Found subnet: {0}", subnet.Name);
                    virtualMachineSubnet = subnet;
                    return;
                }
            }
            throw new ArgumentException($"There is no subnet that contains an IP address " +
                    "'{virtualMachineIPAddress}' in a resource group '{resourceGroupName}'");
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            // FIXME: remove Azure resources
        }

        ~AzureJumpBox()
        {
            Dispose(false);
        }
    }
}
