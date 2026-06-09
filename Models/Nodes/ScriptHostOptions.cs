using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.Scripting;

namespace Loom.Models.Nodes;

/// <summary>
/// Shared Roslyn scripting references/imports for <see cref="UserDefinedNode"/> snippets.
/// </summary>
internal static class ScriptHostOptions
{
    private static readonly Lazy<ScriptOptions> Options = new(Create);

    public static ScriptOptions Default => Options.Value;

    private static ScriptOptions Create()
    {
        var refs = new HashSet<Assembly>(ReferenceEqualityComparer.Instance)
        {
            typeof(object).Assembly,
            typeof(Uri).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly,
            typeof(Dictionary<,>).Assembly,
            typeof(Convert).Assembly,
            typeof(Math).Assembly,
            typeof(ScriptGlobals).Assembly,
        };

        // Pull in runtime facades shipped with the app (Json, tasks, etc.)
        foreach (var name in new[]
        {
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "netstandard",
        })
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
                refs.Add(loaded);
        }

        return ScriptOptions.Default
            .AddReferences(refs)
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.Globalization",
                "System.Math",
                "System.Text",
                "Loom.Models")
            .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);
    }
}
