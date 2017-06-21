using CommandLine;
using LowLevelDesign.AzureRemoteDesktop.Azure;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Utilities;

namespace LowLevelDesign.AzureRemoteDesktop
{
    class Program
    {
        static readonly CancellationToken appCancellationToken = new CancellationToken();

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

            try {
                var resourceManager = new AzureResourceManager();
                resourceManager.AuthenticateWithPrompt().Wait();

                var vmLocalizer = new AzureVMLocalizer(resourceManager);
                vmLocalizer.LocalizeVMAsync(options, appCancellationToken).Wait();

                //var targetVm
                //if (options.VirtualMachineIPAddress == null || options.ResourceGroupName == null) {
                //    // we only list available virtual machines
                //    if (!LetUserChooseTheVirtualMachine(azure, options)) {
                //        return;
                //    }
                //}

                IPAddress virtualMachineIPAddress;
                if (!IPAddress.TryParse(options.VirtualMachineIPAddress, out virtualMachineIPAddress)) {
                    Console.Error.WriteLine("ERROR: invalid format of the IP address");
                    return;
                }

                // FIXME var azureJumpBox = new AzureJumpBox(azure, options.ResourceGroupName, virtualMachineIPAddress);
                var openSSHWrapper = new OpenSSHWrapper(SupportFiles.SupportFileDir);
                if (!openSSHWrapper.IsKeyFileLoaded) {
                    openSSHWrapper.GenerateKeyFileInUserProfile();
                }
                Console.WriteLine("Provisioning VM with Public IP in Azure ...");
                // FIXME provision

                // FIXME: start openssh in a hidden window - I should add a port parameter
                openSSHWrapper.StartOpenSSHSession();

                // FIXME: start mstsc with a connection to localhost and a port number
            } catch (Exception ex) {
                // FIXME catch AzureException 
                Console.WriteLine("ERROR: error occurred. Full details:");
                Console.WriteLine(ex);
            } finally {
                //FIXME: azureJumpBox.Dispose(); - should I use the destructor
            }
        }


        //static bool LetUserChooseTheVirtualMachine(IAzure azure, Options options)
        //{
        //    Console.WriteLine("Please choose the VM you would like to connect to:");
        //    Console.WriteLine("-------------------------------------------------");
        //    var vms = azure.VirtualMachines.List().ToArray();
        //    for (int i = 0; i < vms.Length; i++) {
        //        var vm = vms[i];
        //        Console.WriteLine($" [{i + 1}] '{vm.Name}' ({vm.PowerState}), resource group: '{vm.ResourceGroupName}', " +
        //            "ip: {vm.GetPrimaryNetworkInterface().PrimaryPrivateIP}");
        //    }
        //    Console.Write("VM (choose number): ");
        //    string response = Console.ReadLine();
        //    int vmind;
        //    if (!int.TryParse(response, out vmind) || (vmind - 1) >= vms.Length || vmind < 1) {
        //        Console.Error.WriteLine("ERROR: number out of range");
        //        return false;
        //    }
        //    options.ResourceGroupName = vms[vmind - 1].ResourceGroupName;
        //    options.VirtualMachineIPAddress = vms[vmind - 1].GetPrimaryNetworkInterface().PrimaryPrivateIP;
        //    return true;
        //}

        /// <summary>
        /// Unpacks all the support files associated with this program.   
        /// </summary>
        public static bool Unpack()
        {
            return SupportFiles.UnpackResourcesIfNeeded();
        }
    }
}
