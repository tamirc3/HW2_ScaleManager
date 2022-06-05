using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using ScaleManager.Services;

var builder = WebApplication.CreateBuilder(args);

var tenantId =  builder.Configuration["tenantID"];
var clientId =  builder.Configuration["clientID"];
var clientSecret = builder.Configuration["clientSecret"];
var subscriptionId = builder.Configuration["subscriptionId"];
var queueHost = builder.Configuration["QueueHost"];

AzureCredentials azureCredentials =  SdkContext.AzureCredentialsFactory.FromServicePrincipal(
    clientId,
    clientSecret,
    tenantId,
    AzureEnvironment.AzureGlobalCloud);
azureCredentials.WithDefaultSubscription(subscriptionId);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var appServiceManager = new AppServiceManager(azureCredentials, queueHost);


//builder.Services.AddSingleton<IAppServiceManager>(_ => appServiceManager);

var autoScaleService = new AutoScaleService(appServiceManager, queueHost);
autoScaleService.Start();

builder.Services.AddSingleton<IAppServiceManager>(_ => appServiceManager);


var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
