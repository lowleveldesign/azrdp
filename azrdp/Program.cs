using CommandLine;
using Microsoft.Azure.Management.Fluent;
using System;
using System.Diagnostics;
using System.Net;
using Utilities;

namespace LowLevelDesign.AzureRemoteDesktop
{
    class Program
    {

        [STAThread()]
        public static void Main(string[] args)
        {
            Unpack();

            DoMain(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void DoMain(string[] args)
        {
            var argParseResult = Parser.Default.ParseArguments<Options>(args);
            if (argParseResult is NotParsed<Options>) {
                return;
            }
            var options = ((Parsed<Options>)argParseResult).Value;
            if (options.Verbose) {
                Logger.Level = SourceLevels.Verbose;
            }

            var azure = Azure.Authenticate(options.CredentialFilePath).WithDefaultSubscription(); // FIXME to change

            if (options.VirtualMachineIPAddress == null || options.ResourceGroupName == null) {
                // we only list available virtual machines
                ListAvailableVirtualMachines(azure);
                return;
            }

            IPAddress virtualMachineIPAddress;
            if (!IPAddress.TryParse(options.VirtualMachineIPAddress, out virtualMachineIPAddress)) {
                Console.Error.WriteLine("ERROR: invalid format of the IP address");
                return;
            }

            var azureJumpBox = new AzureJumpBox(azure, options.ResourceGroupName, virtualMachineIPAddress);
            try {
                azureJumpBox.DeployAndStart().Wait(); // FIXME: we should show a progress with dots here

                // FIXME: start openssh in a hidden window
                // FIXME: start mstsc with a connection to localhost and a port number
            } catch (Exception ex) {
                Console.WriteLine("ERROR: error occurred while deploying the machine. " + ex.Message);
                azureJumpBox.Dispose();
            }
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

        /// <summary>
        /// Unpacks all the support files associated with this program.   
        /// </summary>
        public static bool Unpack()
        {
            return SupportFiles.UnpackResourcesIfNeeded();
        }
    }
}
