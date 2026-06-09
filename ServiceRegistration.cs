using Loom.Api;
using Loom.Services;
using Loom.Models.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddLoomServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;

        services.AddHttpClient(ApiHttp.ClientName, client => ApiHttp.ApplyDefaultHeaders(client));

        services.AddSingleton<NodeFactory>();
        services.AddSingleton<NodeManager>();
        services.AddSingleton<DataStorage>();
        services.AddTransient<ExecutionEngine>();
        services.AddSingleton<WorkflowSessionStore>();
        services.AddSingleton<WorkflowApiService>();
        services.AddScoped<WorkflowGraphService>();

        services.AddTransient<InputNode>();
        services.AddTransient<ArithmeticNode>();
        services.AddTransient<MultiArithmeticNode>();
        services.AddTransient<OutputNode>();
        services.AddTransient<LogicNode>();
        services.AddTransient<StringOpNode>();
        services.AddTransient<StringTransformNode>();
        services.AddTransient<UnaryMathNode>();
        services.AddTransient<UserDefinedNode>();
        services.AddTransient<WeatherNode>();
        services.AddTransient<GeocodeNode>();
        services.AddTransient<IpLocationNode>();

        return services;
    }
}
