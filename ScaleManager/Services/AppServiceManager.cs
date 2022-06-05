using System.Collections.Concurrent;
using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using IAppServiceManager = ScaleManager.Services.IAppServiceManager;

namespace ScaleManager.Services;

public class AppServiceManager : IAppServiceManager
{
    private readonly AzureCredentials _azureCredentials;
    private readonly string _queueHost;
    private IAzure _azureConnection;
    private readonly Region _region = Region.USEast;
    private readonly Dictionary<string, string> _tags = new() { { "environment", "development" } };
    private IAppServicePlan? _appServicePlan;
    private readonly ConcurrentQueue<IWebApp> _appServicePool = new ConcurrentQueue<IWebApp>();
    private string _RG_name = "workers" + Guid.NewGuid();
    private readonly ILogger<AppServiceManager> _logger;
    public AppServiceManager(AzureCredentials azureCredentials, string queueHost)
    {
        _azureCredentials = azureCredentials;
        _queueHost = queueHost;
        CreateInfraResources();
    }


    //from https://github.com/Azure-Samples/app-service-dotnet-configure-deployment-sources-for-web-apps/blob/master/Program.cs
    public async void CreateInfraResources()
    {
        await CreateAzureConnection();
        CreateResourceGroup();
    }

    private async Task CreateAzureConnection()
    {
        _azureConnection = await Azure.Configure()
            .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
            .Authenticate(_azureCredentials).WithDefaultSubscriptionAsync();
    }

    private void CreateResourceGroup()
    {
        _azureConnection.ResourceGroups
            .Define(_RG_name)
            .WithRegion(_region)
            .WithTags(_tags)
            .Create();
    }

    private async Task StartMachine(IWebApp machine)
    {
        string startworking = "https://" + machine.DefaultHostName + "/startWorking";
        using var request = new HttpRequestMessage { Method = HttpMethod.Get, RequestUri = new Uri(startworking), };
        using HttpClient httpClient = new HttpClient();
        var responseMessage = await httpClient.SendAsync(request);
        Console.WriteLine($"scale manager called {startworking} , response{responseMessage.StatusCode}");

    }
    public async Task CreateAppServiceAsync()
    {
        try
        {
            Console.WriteLine("starting to create worker");
            var appService = CreateAppServiceAndPlan();
            await UpdateWorkerConfig(appService.Name);
            await StartMachine(appService);
            _appServicePool.Enqueue(appService);
            Console.WriteLine($"create worker done,new worker is: {appService.Name}");
        }
        catch (Exception e)
        {
            Console.WriteLine("failed to create worker");
            Console.WriteLine(e);

        }

    }

    private IWebApp CreateAppServiceAndPlan()
    {
        string appName = "worker" + _region.Name + Guid.NewGuid();
        var webapp = _azureConnection.WebApps
            .Define(appName)
            .WithRegion(_region)
            .WithExistingResourceGroup(_RG_name)
            .WithNewWindowsPlan(PricingTier.FreeF1)
            .DefineSourceControl()
            .WithPublicGitRepository("https://github.com/evyatarweiss/HW2_WorkerNode")
            .WithBranch("main")
            .Attach()
            .Create();
        return webapp;
    }

    private async Task UpdateWorkerConfig(string appName)
    {
        try
        {
            RestClient restClient = RestClient
                .Configure()
                .WithEnvironment(AzureEnvironment.AzureGlobalCloud)
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .WithCredentials(_azureCredentials)
                .Build();

            var _websiteClient = new WebSiteManagementClient(restClient);
            _websiteClient.SubscriptionId = _azureCredentials.DefaultSubscriptionId;
            // get
            var configs = await _websiteClient.
                WebApps.ListApplicationSettingsAsync(_RG_name, appName);

            // add config
            configs.Properties.Add("QueueHost", _queueHost);

            // update
            var result = await _websiteClient.WebApps.UpdateApplicationSettingsAsync(_RG_name, appName, configs);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

    }

    public async Task DeleteAppService()
    {
        _appServicePool.TryDequeue(out var webApp);

        if (webApp != null)
        {
            await StopWorker(webApp);

            int numberOfSeconds = 0;

            while (numberOfSeconds < 60 * 5)
            {
                if (await WorkerStoppedWorking(webApp)) break;
                numberOfSeconds++;
                await Task.Delay(1000);

            }
            await _azureConnection.WebApps.DeleteByIdAsync(webApp.Id);
            await _azureConnection.AppServices.AppServicePlans.DeleteByIdAsync(webApp.AppServicePlanId);
        }
    }

    private static async Task<bool> WorkerStoppedWorking(IWebApp webApp)
    {
        string stopWorkingUrl = "https://" + webApp.DefaultHostName + "/workerIsBusy";
        Console.WriteLine($"ScaleManager workerIsBusy: {stopWorkingUrl}");
        using var request = new HttpRequestMessage { Method = HttpMethod.Get, RequestUri = new Uri(stopWorkingUrl), };
        using HttpClient httpClient = new HttpClient();
        var responseMessage = await httpClient.SendAsync(request);
        if (responseMessage.IsSuccessStatusCode)
        {
            var res = await responseMessage.Content.ReadAsStringAsync();
            if (Boolean.Parse(res)) //worker stoped
            {
                return true;
            }
        }

        return false;
    }

    private static async Task StopWorker(IWebApp webApp)
    {
        string stopWorkingUrl = "https://" + webApp.DefaultHostName + "/stopWorking";
        Console.WriteLine($"ScaleManager stopWorkingUrl: {stopWorkingUrl}");
        using var request = new HttpRequestMessage { Method = HttpMethod.Get, RequestUri = new Uri(stopWorkingUrl), };
        using HttpClient httpClient = new HttpClient();
        var responseMessage = await httpClient.SendAsync(request);
        Console.WriteLine($"stopping worker {stopWorkingUrl} got status code {responseMessage.StatusCode}");
        if (responseMessage.IsSuccessStatusCode)
        {
            //completedMessage = await responseMessage.Content.ReadAsStringAsync();
        }
    }

    public int GetNumberOFWorkers()
    {
        return _appServicePool.Count;
    }
}