using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;
using Pulumi.Azure.AppService;
using Pulumi.Azure.KeyVault;
using Pulumi.Azure.KeyVault.Inputs;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.AppInsights;
class MyStack : Stack
{
    public MyStack()
    {
        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup("pulumiTestRG");

        // Create an Azure Storage Account
        var storageAccount = new Account("pulumistoragetest", new AccountArgs
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

        var appInsights = new Insights("appInsights", new InsightsArgs
        {
            ApplicationType = "web",
            ResourceGroupName = resourceGroup.Name
        });

        var webapp = new AppService("webapp", new AppServiceArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AppServicePlanId = appServicePlan.Id,
            AppSettings =
            {
                // {"WEBSITE_RUN_FROM_PACKAGE", codeBlobUrl},
                {"APPINSIGHTS_INSTRUMENTATIONKEY", appInsights.InstrumentationKey},
                // {"APPLICATIONINSIGHTS_CONNECTION_STRING", appInsights.InstrumentationKey.Apply(key => $"InstrumentationKey={key}")},
                {"ApplicationInsightsAgent_EXTENSION_VERSION", "~2"},
            },
            HttpsOnly = true,
            Identity = new AppServiceIdentityArgs
            {
                Type = "SystemAssigned"
            },
        });
        //ref https://www.pulumi.com/docs/reference/pkg/azure/keyvault/keyvault/
        var current = Output.Create(Pulumi.Azure.Core.GetClientConfig.InvokeAsync());
        var exampleKeyVault = new KeyVault("exampleKeyVault", new Pulumi.Azure.KeyVault.KeyVaultArgs
        {
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            EnabledForDiskEncryption = true,
            TenantId = current.Apply(current => current.TenantId),
            SoftDeleteEnabled = true,
            SoftDeleteRetentionDays = 7,
            PurgeProtectionEnabled = false,
            SkuName = "standard",
            AccessPolicies = 
            {
                new KeyVaultAccessPolicyArgs
                {
                    TenantId = current.Apply(current => current.TenantId),
                    ObjectId = current.Apply(current => current.ObjectId),
                    KeyPermissions = 
                    {
                        "get",
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
            NetworkAcls = new KeyVaultNetworkAclsArgs
            {
                DefaultAction = "Deny",
                Bypass = "AzureServices",
            },
            Tags = 
            {
                { "environment", "Testing" },
            },
        });    
        // Export the connection string for the storage account
        this.ConnectionString = storageAccount.PrimaryConnectionString;
        this.appID = webapp.Id;
        this.Outbound = webapp.OutboundIpAddresses;
    }

    [Output]
    public Output<string> ConnectionString { get; set; }
    public Output<string> appID { get; set; }
    public Output<string> Outbound { get; set; }

}
