namespace ScaleManager.Services;

public interface IAppServiceManager
{
    void CreateInfraResources();
    Task CreateAppServiceAsync();
    Task DeleteAppService();
}