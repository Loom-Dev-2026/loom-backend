
using Loom.Models;
using Loom.Models.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Loom.Services;

/// <summary>
/// Creates node instances with full dependency injection support.
/// Uses <see cref="ActivatorUtilities.CreateInstance"/> so that nodes
/// requiring services (IHttpClientFactory, IConfiguration, etc.) are
/// resolved correctly from the DI container.
///
/// Register as a singleton in Program.cs:
///   builder.Services.AddSingleton&lt;NodeFactory&gt;();
/// </summary>
/// 
public class NodeFactory
{
    private readonly IServiceProvider _services;

    public NodeFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>
    /// Creates a node of the requested type with all constructor dependencies
    /// resolved from the DI container.
    /// </summary>
    /// <param name="nodeType">The <see cref="NodeType"/> to instantiate.</param>
    /// <param name="additionalArgs">
    ///   Optional extra constructor arguments that are NOT in DI
    ///   (e.g. a URL string passed to ApiNode at design time).
    /// </param>
    public Node Create(NodeType nodeType, params object[] additionalArgs)
    {
        var clrType = nodeType switch
        {
            NodeType.Api => typeof(ApiNode),
            NodeType.Weather => typeof(WeatherNode),
            NodeType.Stripe => typeof(StripeNode),
            NodeType.Arithmetic => typeof(ArithmeticNode),
            NodeType.Logic => typeof(LogicNode),
            NodeType.Input => typeof(InputNode),
            NodeType.Output => typeof(OutputNode),
            NodeType.UserDefined => typeof(UserDefinedNode),
            _ => throw new ArgumentOutOfRangeException(
                     nameof(nodeType), nodeType,
                     $"No CLR type is registered for NodeType.{nodeType}.")
        };

        return (Node)ActivatorUtilities.CreateInstance(_services, clrType, additionalArgs);
    }

    /// <summary>Strongly-typed convenience overloads.</summary>
    public ApiNode CreateApiNode() => (ApiNode)Create(NodeType.Api);
    public WeatherNode CreateWeatherNode() => (WeatherNode)Create(NodeType.Weather);
    public StripeNode CreateStripeNode() => (StripeNode)Create(NodeType.Stripe);
}
