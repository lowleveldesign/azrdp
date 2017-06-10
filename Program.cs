using System;
using System.Diagnostics;
using System.Net;
using CommandLine;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;

namespace AzRdp
{
    class Program
    {
        static void Main(string[] args)
        {
            var argParseResult = Parser.Default.ParseArguments<Options>(args);
            if (argParseResult is NotParsed<Options>) {
                return;
            }
            var options = ((Parsed<Options>)argParseResult).Value;

            var logger = new TraceSource("default", options.Verbose ? SourceLevels.Verbose : SourceLevels.Information);
            logger.Listeners.Add(new ConsoleTraceListener());

            var azure = Azure.Authenticate(options.CredentialFilePath).WithDefaultSubscription(); // FIXME to change

            if (options.VirtualMachineIPAddress == null || options.ResourceGroupName == null) {
                // we only list available virtual machines
                ListAvailableVirtualMachines(azure);
                return;
            }

            if (!IPAddress.TryParse(options.VirtualMachineIPAddress, out var virtualMachineIPAddress)) {
                Console.Error.WriteLine("ERROR: invalid format of the IP address");
                return;
            }

            azure.VirtualMachines.Define("__azrdp")
                                 .WithRegion("FIXME")
                                 .WithExistingResourceGroup("FIXME")
                                 .WithExistingPrimaryNetwork()
                                 .WithSubnet()
                                 .WithPrimaryPrivateIPAddressDynamic()
                                 .WithNewPrimaryPublicIPAddress("leafdnslabel")
                                 .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.)
                                 .WithRootUsername("azrdp")
                                 .WithSsh("FIXME:public key")
                                 .WithSize(VirtualMachineSizeTypes.StandardA1)

            // FIXME: move to method
            logger.TraceEvent(TraceEventType.Verbose, 0, "Searching for the subnet containing a given IP address...");
            foreach (var network in azure.Networks.List()) {
                if (string.Equals(options.ResourceGroupName, network.ResourceGroupName, StringComparison.OrdinalIgnoreCase)) {
                    foreach (var addressSpace in network.AddressSpaces) {
                        var ipNetwork = IPNetwork.Parse(addressSpace);
                        if (IPNetwork.Contains(ipNetwork, virtualMachineIPAddress)) {
                            foreach (var subnet in network.Subnets.Values) {
                                
                            }
                        }
                    }
                }
            }

            // FIXME: not sure if I really need do this
            // var vm = FindVirtualMachine(azure, options.ResourceGroupName, options.VirtualMachineIPAddress);
            // if (vm == null) {
            //     Console.Error.WriteLine("ERROR: could not find the virtual machine. " +
            //         "Make sure you typed a valid resource group and IP address.");
            //     return;
            // }
        }

        static void ListAvailableVirtualMachines(IAzure azure)
        {
            Console.WriteLine("To start connecting to the VM you need to uniquely identify it. " + 
                "Please provide the resource group name and the virtual machine IP address. The list below should help you.");
            Console.WriteLine();
            Console.WriteLine("Virtual machines found in your subscription:");
            Console.WriteLine("--------------------------------------------");
            foreach (var vm in azure.VirtualMachines.List()) {
                Console.WriteLine($" * '{vm.Name}', resource group: '{vm.ResourceGroupName}', ip: {vm.GetPrimaryNetworkInterface().PrimaryPrivateIP}");
            }
        }

        static IVirtualMachine FindVirtualMachine(IAzure azure, string resourceGroupName, string ipAddress) {
            foreach (var vm in azure.VirtualMachines.List()) {
                if (string.Equals(resourceGroupName, vm.ResourceGroupName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ipAddress, vm.GetPrimaryNetworkInterface().PrimaryPrivateIP, StringComparison.OrdinalIgnoreCase)) {
                    return vm;
                }
            }
            return null;
        }
    }
}
