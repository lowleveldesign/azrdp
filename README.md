
# AzRdp (or Azure Remote Desktop)

This application creates a temporary SSH tunnel to a virtual machine in Azure, so you could access it from your computer. The target VM is identified by the subscription id, the resource group name, and the IP address. Among these parameters only the subscription id is required on start; you will be presented with a choice for the other values. The full command line looks as follows:

```
AzRdp v1.0.0.0 - creates a temporary SSH tunnel to a VM in Azure
Copyright (C) 2017 Sebastian Solnica (@lowleveldesign)

Usage: azrdp [OPTIONS]

Options:
  -s, --subscriptionId=VALUE Subscription id, in which the VM is located.
  -r, --resgroup=VALUE       Resource Group name or id, where the VM is
                               located.
  -i, --vmip=VALUE           Virtual Machine IP address.
  -l, --localport=VALUE      Port number of the local machine used by the SSH
                               tunnel (default 50000).
  -p, --remoteport=VALUE     Port number of the remote machine (default 3389 -
                                RDP).
      --vmsize=VALUE         The size of the Virtual Machine to be created
                               (default Standard_F1S)
  -v, --verbose              Outputs all requests to Azure.
  -h, --help                 Show this message and exit
  -?                         Show this message and exit
```

The application name might be a bit misleading, as my initial idea was to have a tool only for the Windows Remote Desktop connections, but when I switched to the SSH tunnel other protocols started to work too. However, I got used to azrdp - I hope you will too :)

**CAVEAT**: In the current version, the newly created virtual machine is deployed in the same subnet as the target VM. And although I tried to make it as secure as possible (NSG rule on the incoming traffic and SSH certificate-based authentication), there will be a public IP connected to your virtual network while the application is running. Please keep that in mind.

## How does it work

The tunneling is performed by the Linux virtual machine, configured to use SSH certificate-based authentication. The azrdp certificate is automatically generated on the first run ([OpenSSH](https://github.com/PowerShell/Win32-OpenSSH) client is embedded in the application resources). The following resources are provisioned in your Azure environment when you start the application:

- Public IP with dynamic allocation
- Network Interface Card
- Network Security Group bound to the newly created NIC, allowing access from your IP address (read from <https://api.ipify.org/>) to port 22 on the jump host
- Virtual Machine with Ubuntu 16.04 (default size: Standard\_F1S)

If there is a Network Security Group associated to the target subnet, a new rule will be added allowing connections from your IP to the newly created virtual machine. When the provisioning is done (usually within 2-3 minutes), an ssh.exe process is started with an SSH tunnel enabled (by default it uses port 50000 on the localhost).

A sample session might look as follows:

```
PS bin> .\azrdp.exe -s {subscription-id} -r azrdp
---------------------------------------------
  Gathering information about the target VM
---------------------------------------------

-------------------------------------------
  Provisioning VM with Public IP in Azure
-------------------------------------------

Creating public IP address...done (a09ac0a30c70d4efcb59b04eefc6b693a-pip)
Creating Network Security Group...done (a09ac0a30c70d4efcb59b04eefc6b693a-nsg)
Creating Network Interface Card...done (a09ac0a30c70d4efcb59b04eefc6b693a-nic)
Creating Virtual Machine..............................done (a09ac0a30c70d4efcb59b04eefc6b693a-vm)
Opening tunnel to the virtual machine...done

---------------------------------------
  SSH tunnel to the target VM is open
---------------------------------------

       Local endpoint : localhost:50000
   Target VM endpoint : 10.0.0.4:3389
SSH jump host address : 194.194.194.194

Press Ctrl+C to end the session and remove all the resources.
```

Now, you may access your Azure VM by using the address localhost:50000. If you sign in to the Azure portal you would see a list of resources created by azrdp (the unique identifier will be different):

![azrdp resources in Azure porta](https://raw.githubusercontent.com/lowleveldesign/azrdp/master/docs/components-in-azure.png)

When you are done with your work, you may press Ctrl+C and the clean-up process should start:

```
Please wait. It may take up to few minutes to remove all the resources.

Removing /subscriptions/{subscription-id}/resourceGroups/azrdp/providers/Microsoft.Compute/virtualMachines/a09ac0a30c70d4efcb59b04eefc6b693a-vm?api-version=2017-03-30.......................done
Removing /subscriptions/{subscription-id}/resourceGroups/azrdp/providers/Microsoft.Compute/disks/a09ac0a30c70d4efcb59b04eefc6b693a-vm_OsDisk_1_53f7d119fbbf44e3b6ffab7ad0e74b54?api-version=2017-03-30...done
Removing /subscriptions/{subscription-id}/resourceGroups/azrdp/providers/Microsoft.Network/networkInterfaces/a09ac0a30c70d4efcb59b04eefc6b693a-nic...done
Removing /subscriptions/{subscription-id}/resourceGroups/azrdp/providers/Microsoft.Network/networkSecurityGroups/a09ac0a30c70d4efcb59b04eefc6b693a-nsg...done
Removing /subscriptions/{subscription-id}/resourceGroups/azrdp/providers/Microsoft.Network/publicIPAddresses/a09ac0a30c70d4efcb59b04eefc6b693a-pip...done
```

This tool uses Azure REST API (a lot of code taken from the [ARMClient](https://github.com/projectkudu/ARMClient) project). You may use the **-v** switch to see all the HTTP requests and responses sent and received by the application.

## Projects on which this tool is based

This tool would not exist if not these excellent projects:

- [ARMClient](https://github.com/projectkudu/ARMClient) - code for authentication and HTTP access to Azure (license: Apache License 2.0)
- [IPNetwork](https://github.com/lduchosal/ipnetwork) - logic for parsing and validating CIDR notation (license: BSD 2-clause)
- [PerfView](https://github.com/microsoft/perfview) - code for embedding binary resources into the .exe file (license: MIT)
- [Win32-OpenSSH](https://github.com/PowerShell/Win32-OpenSSH) - ssh client (license: OpenSSH)

## Contributions and error reports

If you found an error or have an idea for improvement, please create an issue on the [Issues](https://github.com/lowleveldesign/azrdp/issues) page. If an issue is already created for your case, please upvote it (using Thump Up emoticon) so I will start working on it sooner.
