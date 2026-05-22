using Loom.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Loom;

/// <summary>
/// Extension method that registers all LOOM backend services.
/// 
/// Usage in Program.cs:
///   builder.Services.AddLoomServices();
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddLoomServices(
        this IServiceCollection services,
        string? storageDirectory = null)
    {
        // Infrastructure
        services.AddSingleton<DataStorage>(_ =>
            new DataStorage(storageDirectory));

        // Node factory (stateless — singleton is fine)
        services.AddSingleton<INodeFactory, NodeFactory>();

        // Scoped to the Blazor circuit — one per connected user session
        services.AddScoped<NodeManager>();
        services.AddScoped<ExecutionEngine>();
        services.AddScoped<HistoryManager>();
        services.AddScoped<WorkflowService>();

        return services;
    }
}