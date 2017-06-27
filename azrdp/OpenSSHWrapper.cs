using System;
using System.Diagnostics;
using System.IO;

namespace LowLevelDesign.AzureRemoteDesktop
{
    sealed class OpenSSHWrapper
    {
        static readonly string sshKeysFolderPath = Environment.ExpandEnvironmentVariables("%USERPROFILE%\\.ssh");

        private readonly string sshKeyGenPath;
        private readonly string sshPath;
        private string keyPath;
        private string publicKeyPath;

        public OpenSSHWrapper(string pathToBinaries)
        {
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
            Console.WriteLine("Generating new RSA keys for SSH connection.");
            var path = Path.Combine(sshKeysFolderPath, "azrdp_rsa");
            var args = $"-t rsa -b 2048 -q -f \"{path}\" -N \"azrdp\"";
            var psi = new ProcessStartInfo(sshKeyGenPath, args) {
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var process = Process.Start(psi);
            process.WaitForExit();

            keyPath = path;
            publicKeyPath = path + ".pub";

            Debug.Assert(File.Exists(keyPath));
            Debug.Assert(File.Exists(publicKeyPath));
        }

        public void StartOpenSSHSession(ushort localPort, string targetVMIPAddress, ushort remotePort)
        {
            // FIXME: open SSH session
        }
    }
}
