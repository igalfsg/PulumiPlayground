using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;

class MyStack : Stack
{
    public MyStack()
    {
        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup("resourceGroup");

        // Create an Azure Storage Account
        var storageAccount = new Account("storage", new AccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountReplicationType = "LRS",
            AccountTier = "Standard"
        });

        var appServicePlan = new Plan("asp", new PlanArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Kind = "App",
                Sku = new PlanSkuArgs
                {
                    Tier = "Basic",
                    Size = "B1",
                },
            });

        //ref https://www.pulumi.com/docs/reference/pkg/azure/keyvault/keyvault/
        var exampleKeyVault = new Azure.KeyVault.KeyVault("exampleKeyVault", new Azure.KeyVault.KeyVaultArgs
        {
            Location = exampleResourceGroup.Location,
            ResourceGroupName = exampleResourceGroup.Name,
            EnabledForDiskEncryption = true,
            TenantId = current.Apply(current => current.TenantId),
            SoftDeleteEnabled = true,
            SoftDeleteRetentionDays = 7,
            PurgeProtectionEnabled = false,
            SkuName = "standard",
            AccessPolicies = 
            {
                new Azure.KeyVault.Inputs.KeyVaultAccessPolicyArgs
                {
                    TenantId = current.Apply(current => current.TenantId),
                    ObjectId = current.Apply(current => current.ObjectId),
                    KeyPermissions = 
                    {
                        "get",
                        "ManageContacts",
                    },
                    SecretPermissions = 
                    {
                        "get",
                    },
                    StoragePermissions = 
                    {
                        "get",
                    },
                },
            },
            NetworkAcls = new Azure.KeyVault.Inputs.KeyVaultNetworkAclsArgs
            {
                DefaultAction = "Deny",
                Bypass = "AzureServices",
            },
            Contacts = 
            {
                new Azure.KeyVault.Inputs.KeyVaultContactArgs
                {
                    Email = "example@example.com",
                    Name = "example",
                    Phone = "0123456789",
                },
            },
            Tags = 
            {
                { "environment", "Testing" },
            },
        });    
        // Export the connection string for the storage account
        this.ConnectionString = storageAccount.PrimaryConnectionString;
    }

    [Output]
    public Output<string> ConnectionString { get; set; }
}
