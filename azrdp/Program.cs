using LowLevelDesign.AzureRemoteDesktop.Azure;
using NDesk.Options;
using System;
using System.Diagnostics;
using System.Reflection;
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
            bool showHelp = false, verbose = false;
            string subscription = null, resourceGroupName = null, vmIPAddress = null;

            var p = new OptionSet
            {
                { "s|subscription", "Subscription name or id, where the VM is located.", v => { subscription = v; } },
                { "r|resgroup", "Resource Group name or id, where the VM is located.", v => { resourceGroupName = v; } },
                { "i|Virtual Machine IP address", "Resource Group name or id, where the VM is located.", v => { vmIPAddress = v; } },
                { "v|verbose", "Verbose output.", v => verbose = v != null },
                { "h|help", "Show this message and exit", v => showHelp = v != null },
                { "?", "Show this message and exit", v => showHelp = v != null }
            };

            try {
                p.Parse(args);
            } catch (OptionException ex) {
                Console.Error.Write("ERROR: invalid argument");
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine();
                showHelp = true;
            } catch (FormatException) {
                Console.Error.WriteLine("ERROR: invalid number in one of the constraints");
                Console.Error.WriteLine();
                showHelp = true;
            }

            if (!(resourceGroupName == null && subscription == null && vmIPAddress == null ||
                resourceGroupName != null && subscription != null && vmIPAddress != null)) {
                Console.Error.WriteLine("ERROR: invalid parameters. Please either provide subscription, resource group, and IP address, or nothing at all.");
                Console.Error.WriteLine();
                showHelp = true;
            }

            if (showHelp) {
                ShowHelp(p);
                return;
            }

            if (verbose) {
                Logger.Level = SourceLevels.Verbose;
            }

            try {
                var resourceManager = new AzureResourceManager();
                resourceManager.AuthenticateWithPrompt().Wait();

                var targetVM = new AzureVMLocalizer(resourceManager);
                targetVM.LocalizeVMAsync(subscription, resourceGroupName, vmIPAddress, appCancellationToken).Wait();

                Logger.Log.TraceEvent(TraceEventType.Verbose, 0, "The target VM IP: {0}, VnetId: {1}, SubnetId: {2}, RG: {3}",
                    targetVM.TargetIPAddress, targetVM.VirtualNetworkId, targetVM.SubnetId, targetVM.ResourceGroupName);

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

        /// <summary>
        /// Unpacks all the support files associated with this program.   
        /// </summary>
        public static bool Unpack()
        {
            return SupportFiles.UnpackResourcesIfNeeded();
        }

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("azrdp v{0} - create a temporary jump host to a VM",
                Assembly.GetExecutingAssembly().GetName().Version.ToString());
            Console.WriteLine("Copyright (C) 2017 Sebastian Solnica (@lowleveldesign)");
            Console.WriteLine();
            Console.WriteLine("Usage: azrdp [OPTIONS]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
            Console.WriteLine();
        }
    }
}
