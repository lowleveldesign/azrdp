using LowLevelDesign.AzureRemoteDesktop.Azure;
using NDesk.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Utilities;

namespace LowLevelDesign.AzureRemoteDesktop
{
    class Program
    {
        const string RootUsername = "azrdp";
        static readonly CancellationTokenSource appCancellationTokenSource = new CancellationTokenSource();
        static readonly CancellationToken appCancellationToken = appCancellationTokenSource.Token;

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
            string subscriptionId = null, resourceGroupName = null,
                vmIPAddress = null, vmSize = "Standard_F1S";
            ushort localPort = 50000, remotePort = 3389;

            var p = new OptionSet
            {
                { "s|subscriptionId=", "Subscription id, in which the VM is located.", v => { subscriptionId = v; } },
                { "r|resgroup=", "Resource Group name or id, where the VM is located.", v => { resourceGroupName = v; } },
                { "i|vmip=", "Virtual Machine IP address.", v => { vmIPAddress = v; } },
                { "l|localport=", "Port number of the local machine used by the SSH tunnel (default 50000).", v => { localPort = ushort.Parse(v); } },
                { "p|remoteport=", "Port number of the remote machine (default 3389 - RDP).", v => { remotePort = ushort.Parse(v); } },
                { "vmsize=", "The size of the Virtual Machine to be created (default Standard_F1S)", v => { vmSize = v; } },
                { "v|verbose", "Outputs all requests to Azure.", v => verbose = v != null },
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
            } catch (OverflowException) {
                Console.Error.WriteLine("ERROR: invalid port number");
                Console.Error.WriteLine();
                showHelp = true;
            }

            Guid g;
            if (!showHelp && !Guid.TryParseExact(subscriptionId, "d", out g)) {
                Console.Error.WriteLine("ERROR: subscription id was not provided or is invalid");
                Console.Error.WriteLine();
                showHelp = true;
            }

            if (showHelp) {
                ShowHelp(p);
                return;
            }

            SetConsoleCtrlCHook();

            try {
                var resourceManager = new AzureResourceManager(subscriptionId, verbose);
                resourceManager.AuthenticateWithPrompt().Wait();

                var targetVM = new AzureVMLocalizer(resourceManager);
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine("  Gathering information about the target VM");
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine();
                targetVM.LocalizeVMAsync(resourceGroupName, vmIPAddress, appCancellationToken).Wait();

                Trace.TraceInformation($"The target VM IP: {targetVM.TargetIPAddress}, Vnet: {Path.GetFileName(targetVM.VirtualNetworkId)}, " +
                    $"Subnet: {Path.GetFileName(targetVM.SubnetId)}, RG: {targetVM.ResourceGroupName}");

                using (var azureJumpHost = new AzureJumpHost(resourceManager, targetVM)) {
                    using (var openSSHWrapper = new OpenSSHWrapper(SupportFiles.SupportFileDir, RootUsername)) {
                        if (!openSSHWrapper.IsKeyFileLoaded) {
                            openSSHWrapper.GenerateKeyFileInUserProfile();
                        }

                        Console.WriteLine("-------------------------------------------");
                        Console.WriteLine("  Provisioning VM with Public IP in Azure");
                        Console.WriteLine("-------------------------------------------");
                        Console.WriteLine();
                        azureJumpHost.DeployAndStartAsync(RootUsername, openSSHWrapper.GetPublicKey(), vmSize, appCancellationToken).Wait();

                        var jumpHostPublicIPAddress = azureJumpHost.GetPublicIPAddressAsync(appCancellationToken).Result;
                        openSSHWrapper.StartOpenSSHSession(new OpenSSHWrapper.SSHSessionInfo {
                            LocalPort = localPort,
                            TargetPort = remotePort,
                            TargetVMIPAddress = targetVM.TargetIPAddress,
                            JumpHostPublicIPAddress = jumpHostPublicIPAddress
                        });

                        Console.WriteLine();
                        Console.WriteLine("---------------------------------------");
                        Console.WriteLine("  SSH tunnel to the target VM is open");
                        Console.WriteLine("---------------------------------------");
                        Console.WriteLine();
                        Console.WriteLine("       Local endpoint : localhost:{0}", localPort);
                        Console.WriteLine("   Target VM endpoint : {0}:{1}", targetVM.TargetIPAddress, remotePort);
                        Console.WriteLine("SSH jump host address : {0}", jumpHostPublicIPAddress);
                        Console.WriteLine();
                        Console.WriteLine("Press Ctrl+C to end the session and remove all the resources.");

                        if (remotePort == 3389) { // for RDP we will start mstsc
                            Process.Start("mstsc", $"/v:localhost:{localPort}");
                        }

                        while (!appCancellationToken.IsCancellationRequested) {
                            Thread.Sleep(TimeSpan.FromSeconds(5));
                        }
                    }
                }
            } catch (AggregateException ex) {
                Trace.TraceError(ex.ToString());
                Console.Write("ERROR: unrecovable error(s) occured. Details: ");
                ex = ex.Flatten();
                if (ex.InnerExceptions.Count == 1) {
                    Console.WriteLine($"[{ex.InnerException.GetType().Name}] {ex.InnerException.Message}");
                } else {
                    Console.WriteLine();
                    foreach (var inex in ex.InnerExceptions) {
                        Console.WriteLine($"- [{inex.GetType().Name}] {inex.Message}");
                    }
                }
            } catch (AzureException ex) {
                Trace.TraceError(ex.ToString());
                Console.WriteLine($"ERROR: error while performing action in Azure. Details: {ex.Message}.");
                Console.WriteLine("If the error persists, you may turn detailed logging with -v switch to learn more.");
            } catch (Exception ex) {
                Trace.TraceError(ex.ToString());
                Console.WriteLine($"ERROR: unrecovable error occured. Details: [{ex.GetType().Name}] {ex.Message}.");
            }
        }

        /// <summary>
        /// Unpacks all the support files associated with this program.   
        /// </summary>
        public static bool Unpack()
        {
            return SupportFiles.UnpackResourcesIfNeeded();
        }

        static void SetConsoleCtrlCHook()
        {
            // Set up Ctrl-C to stop both user mode and kernel mode sessions
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs cancelArgs) => {
                Console.WriteLine("Please wait. It may take up to few minutes to remove all the resources.");
                cancelArgs.Cancel = true;
                appCancellationTokenSource.Cancel();
            };
        }

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("AzRdp v{0} - creates a temporary SSH tunnel to a VM in Azure",
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
