using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;
using Pulumi.Azure.AppService;
using Pulumi.Azure.KeyVault;
using Pulumi.Azure.KeyVault.Inputs;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.AppInsights;
using AzureAD = Pulumi.AzureAD;
using System.Security.Cryptography;
using System;

class MyStack : Stack
{
    public MyStack()
    {
        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup("pulumiTestRG");

        //create service bus
        var serviceBus = new Pulumi.Azure.ServiceBus.Namespace("webappServiceBus", new Pulumi.Azure.ServiceBus.NamespaceArgs
        {
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Sku = "Standard",
        });

        //create ServiceBus Queue
        var exampleQueue = new Pulumi.Azure.ServiceBus.Queue("exampleQueue", new Pulumi.Azure.ServiceBus.QueueArgs
        {
            ResourceGroupName = resourceGroup.Name,
            NamespaceName = serviceBus.Name,
        });


        //create sql 
        var primary = new Pulumi.Azure.Sql.SqlServer("primary", new Pulumi.Azure.Sql.SqlServerArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            Version = "12.0",
            AdministratorLogin = "sqladmin",
            AdministratorLoginPassword = Password.Generate(32, 12),
        });
        var secondary = new Pulumi.Azure.Sql.SqlServer("secondary", new Pulumi.Azure.Sql.SqlServerArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = "northeurope",
            Version = "12.0",
            AdministratorLogin = "sqladmin",
            AdministratorLoginPassword = Password.Generate(32, 12),
        });
        
        var sqlLogs = new Pulumi.Azure.Storage.Account("sqllogs", new Pulumi.Azure.Storage.AccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            AccountTier = "Standard",
            AccountReplicationType = "LRS",
        });
        var db1 = new Pulumi.Azure.Sql.Database("exampleDatabase", new Pulumi.Azure.Sql.DatabaseArgs
        {
            ResourceGroupName = primary.ResourceGroupName,
            Location = primary.Location,
            ServerName = primary.Name,
            ExtendedAuditingPolicy = new Pulumi.Azure.Sql.Inputs.DatabaseExtendedAuditingPolicyArgs
            {
                StorageEndpoint = sqlLogs.PrimaryBlobEndpoint,
                StorageAccountAccessKey = sqlLogs.PrimaryAccessKey,
                StorageAccountAccessKeyIsSecondary = true,
                RetentionInDays = 6,
            },
            RequestedServiceObjectiveName = "S0",
            Tags = 
            {
                { "environment", "production" },
            },
        });
        
        

        //create sql failover
        var exampleFailoverGroup = new Pulumi.Azure.Sql.FailoverGroup("failover", new Pulumi.Azure.Sql.FailoverGroupArgs
        {
            ResourceGroupName = primary.ResourceGroupName,
            ServerName = primary.Name,
            Databases = 
            {
                db1.Id,
            },
            PartnerServers = 
            {
                new Pulumi.Azure.Sql.Inputs.FailoverGroupPartnerServerArgs
                {
                    Id = secondary.Id,
                },
            },
            ReadWriteEndpointFailoverPolicy = new Pulumi.Azure.Sql.Inputs.FailoverGroupReadWriteEndpointFailoverPolicyArgs
            {
                Mode = "Automatic",
                GraceMinutes = 60,
            },
        });


        // Create an Azure Storage Account
        // var storageAccount = new Account("pulumistoragetest", new AccountArgs
        // {
        //     ResourceGroupName = resourceGroup.Name,
        //     AccountReplicationType = "LRS",
        //     AccountTier = "Standard"
        // });

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
        //get CurentUser
        var current = Output.Create(Pulumi.Azure.Core.GetClientConfig.InvokeAsync());
        
        //create aad group
        var appADGroup = new AzureAD.Group("mynewGroup", new AzureAD.GroupArgs
        {
            Owners = { current.Apply(current => current.ObjectId) },
            Members = { webapp.Identity.Apply(x => x.PrincipalId) },
        });

        //add aad admin to server
        var primarySQLAADAdmin = new Pulumi.Azure.Sql.ActiveDirectoryAdministrator("exampleActiveDirectoryAdministrator", 
            new Pulumi.Azure.Sql.ActiveDirectoryAdministratorArgs
        {
            ServerName = primary.Name,
            Login = "sqladmin",
            ResourceGroupName = primary.ResourceGroupName,
            TenantId = current.Apply(current => current.TenantId),
            ObjectId = appADGroup.ObjectId,
        });
        var secondarySQLAADAdmin = new Pulumi.Azure.Sql.ActiveDirectoryAdministrator("secondarysqlAdmin", 
            new Pulumi.Azure.Sql.ActiveDirectoryAdministratorArgs
        {
            ServerName = secondary.Name,
            Login = "sqladmin",
            ResourceGroupName = secondary.ResourceGroupName,
            TenantId = current.Apply(current => current.TenantId),
            ObjectId = appADGroup.ObjectId,
        });

        //ref https://www.pulumi.com/docs/reference/pkg/azure/keyvault/keyvault/
        var webappAKV = new KeyVault("webappAKV", new Pulumi.Azure.KeyVault.KeyVaultArgs
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
                        "set", "get", "list", "delete"
                    },
                    CertificatePermissions =
                    {
                        "get",
                    },
                },
                new KeyVaultAccessPolicyArgs
                {
                    TenantId = current.Apply(current => current.TenantId),
                    ObjectId =  appADGroup.ObjectId,
                    KeyPermissions =
                    {
                        "create",
                    },
                    SecretPermissions =
                    {
                        "set", "get", "list"
                    },
                    CertificatePermissions =
                    {
                        "get",
                    },
                },
            },
            Tags =
            {
                { "environment", "Testing" },
            },
        });

        var secret = new Secret("paymentApiKey", new SecretArgs
        {
            KeyVaultId = webappAKV.Id,
            Value = serviceBus.DefaultPrimaryConnectionString,
        });

        // Export the connection string for the storage account
        this.Outbound = webapp.OutboundIpAddresses;
        this.akvurl = webappAKV.VaultUri;
        this.secretURL = secret.Id;
    }

    [Output]
    public Output<string> Outbound { get; set; }
    [Output]
    public Output<string> akvurl { get; set; }
    [Output]
    public Output<string> secretURL { get; set; }
}

public static class Password
{
    private static readonly char[] Punctuations = "!@#$%^&*()_-+=[{]};:>|./?".ToCharArray();

    public static string Generate(int length, int numberOfNonAlphanumericCharacters)
    {
        if (length < 1 || length > 128)
        {
            throw new ArgumentException(nameof(length));
        }

        if (numberOfNonAlphanumericCharacters > length || numberOfNonAlphanumericCharacters < 0)
        {
            throw new ArgumentException(nameof(numberOfNonAlphanumericCharacters));
        }

        using (var rng = RandomNumberGenerator.Create())
        {
            var byteBuffer = new byte[length];

            rng.GetBytes(byteBuffer);

            var count = 0;
            var characterBuffer = new char[length];

            for (var iter = 0; iter < length; iter++)
            {
                var i = byteBuffer[iter] % 87;

                if (i < 10)
                {
                    characterBuffer[iter] = (char)('0' + i);
                }
                else if (i < 36)
                {
                    characterBuffer[iter] = (char)('A' + i - 10);
                }
                else if (i < 62)
                {
                    characterBuffer[iter] = (char)('a' + i - 36);
                }
                else
                {
                    characterBuffer[iter] = Punctuations[i - 62];
                    count++;
                }
            }

            if (count >= numberOfNonAlphanumericCharacters)
            {
                return new string(characterBuffer);
            }

            int j;
            var rand = new Random();

            for (j = 0; j < numberOfNonAlphanumericCharacters - count; j++)
            {
                int k;
                do
                {
                    k = rand.Next(0, length);
                }
                while (!char.IsLetterOrDigit(characterBuffer[k]));

                characterBuffer[k] = Punctuations[rand.Next(0, Punctuations.Length)];
            }

            return new string(characterBuffer);
        }
    }
}
