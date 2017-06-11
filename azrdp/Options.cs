
using CommandLine;

namespace AzRdp
{
    class Options
    {
        // FIXME: plan is to use the normal user account information (need to learn how to do that)

        // [Option('u', "username", Required = true, 
        //     HelpText = "A user with contributor rights to the resource group.")]
        // public string Username {get; set;}

        // [Option('s', "subscription", Required = true, 
        //     HelpText = "Subscription name or id, where the VM is located")]
        // public string Subscription {get; set;}


        [Option('c', "credfile", Required = true,
                HelpText = "FIXME: a path to the credential file")]
        public string CredentialFilePath { get; set; }

        [Option('r', "resgroup", Required = false,
            HelpText = "Resource Group name or id, where the VM is located")]
        public string ResourceGroupName { get; set; }

        [Option('i', "vmip", Required = false,
            HelpText = "Virtual Machine IP address")]
        public string VirtualMachineIPAddress {get; set;}

        [Option('v', "verbose", Required = false,
            HelpText = "Turn on verbose logging")]
        public bool Verbose { get; set; }
    }
}