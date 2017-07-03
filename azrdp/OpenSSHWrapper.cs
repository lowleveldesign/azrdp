using System;
using System.Diagnostics;
using System.IO;

namespace LowLevelDesign.AzureRemoteDesktop
{
    sealed class OpenSSHWrapper : IDisposable
    {
        internal sealed class SSHSessionInfo
        {
            public ushort TargetPort { get; set; }

            public ushort LocalPort { get; set; }

            public string TargetVMIPAddress { get; set; }

            public string JumpHostPublicIPAddress { get; set; }
        }

        static readonly string sshKeysFolderPath = Environment.ExpandEnvironmentVariables("%USERPROFILE%\\.ssh");

        private readonly string sshKeyGenPath;
        private readonly string sshPath;
        private readonly string rootUsername;

        private string keyPath;
        private string publicKeyPath;
        private Process sshSessionProcess;
        private bool verboseLogging;

        public OpenSSHWrapper(string pathToBinaries, string rootUsername)
        {
            this.rootUsername = rootUsername;
            sshKeyGenPath = Path.Combine(pathToBinaries, "ssh-keygen.exe");
            sshPath = Path.Combine(pathToBinaries, "ssh.exe");

            if (Directory.Exists(sshKeysFolderPath)) {
                var path = Path.Combine(sshKeysFolderPath, "azrdp_rsa");
                if (File.Exists(path)) {
                    keyPath = path;
                }
                path = Path.Combine(sshKeysFolderPath, "azrdp_rsa.pub");
                if (File.Exists(path)) {
                    publicKeyPath = path;
                }
            }
        }

        public bool IsKeyFileLoaded
        {
            get { return keyPath != null && publicKeyPath != null; }
        }

        public bool IsSSHSessionActive
        {
            get { return !sshSessionProcess.HasExited;  }
        }

        public bool VerboseLoggingEnabled
        {
            get { return verboseLogging; }
            set { verboseLogging = value; }
        }

        public string GetPublicKey()
        {
            if (!IsKeyFileLoaded) {
                throw new InvalidOperationException("No RSA key file was found. Please call GenerateRsaKeyInUserProfile first.");
            }
            Debug.Assert(File.Exists(publicKeyPath));
            return File.ReadAllText(publicKeyPath);
        }

        public void GenerateKeyFileInUserProfile()
        {
            if (!Directory.Exists(sshKeysFolderPath)) {
                Directory.CreateDirectory(sshKeysFolderPath);
            }

            Console.WriteLine("Generating new RSA keys for SSH connection.");
            var path = Path.Combine(sshKeysFolderPath, "azrdp_rsa");
            var args = $"-t rsa -b 2048 -q -f \"{path}\" -N \"\"";
            var psi = new ProcessStartInfo(sshKeyGenPath, args) {
                CreateNoWindow = !verboseLogging
            };
            var process = Process.Start(psi);
            process.WaitForExit();

            keyPath = path;
            publicKeyPath = path + ".pub";

            Debug.Assert(File.Exists(keyPath));
            Debug.Assert(File.Exists(publicKeyPath));
        }

        public void StartOpenSSHSession(SSHSessionInfo ssh)
        {
            if (!IsKeyFileLoaded) {
                throw new InvalidOperationException("No RSA key file was found. Please call GenerateKeyFileInUserProfile first.");
            }
            var knownHostsFilePath = Path.Combine(sshKeysFolderPath, "azrdp_known_hosts");

            Console.Write("Opening tunnel to the virtual machine...");

            var args = $"-i \"{keyPath}\" -L {ssh.LocalPort}:{ssh.TargetVMIPAddress}:{ssh.TargetPort} " +
                $"-o \"UserKnownHostsFile '{knownHostsFilePath}'\" " +
                $"-o \"StrictHostKeyChecking no\" " +
                $"{rootUsername}@{ssh.JumpHostPublicIPAddress}";
            var psi = new ProcessStartInfo(sshPath, args) {
                CreateNoWindow = !verboseLogging,
                RedirectStandardInput = true,
                UseShellExecute = false
            };

            Trace.WriteLine($"{sshPath} {args}");
            sshSessionProcess = Process.Start(psi);

            Console.WriteLine("done");
        }

        public void Dispose()
        {
            if (sshSessionProcess != null) {
                // try exit SSH gracefully
                sshSessionProcess.StandardInput.Write("exit\n");
                sshSessionProcess.StandardInput.Flush();

                if (!sshSessionProcess.WaitForExit(5000)) { // number of ms to wait for the process to exit
                    Console.WriteLine("WARNING: SSH sessions did not end gracefully - killing the process.");
                    sshSessionProcess.Kill();
                }
            }
        }
    }
}
