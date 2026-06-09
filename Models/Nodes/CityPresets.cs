namespace Loom.Models.Nodes;

/// <summary>Built-in city coordinates for API node dropdowns (no geocoder call needed).</summary>
public static class CityPresets
{
    public static readonly IReadOnlyDictionary<string, (double Lat, double Lon, string Label)> Cities =
        new Dictionary<string, (double, double, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["London"] = (51.5074, -0.1278, "London, UK"),
            ["NewYork"] = (40.7128, -74.0060, "New York, US"),
            ["Paris"] = (48.8566, 2.3522, "Paris, France"),
            ["Tokyo"] = (35.6762, 139.6503, "Tokyo, Japan"),
            ["Sydney"] = (-33.8688, 151.2093, "Sydney, Australia"),
            ["Dubai"] = (25.2048, 55.2708, "Dubai, UAE"),
            ["Cairo"] = (30.0444, 31.2357, "Cairo, Egypt"),
            ["SãoPaulo"] = (-23.5505, -46.6333, "São Paulo, Brazil"),
        };

    public static bool TryResolve(string key, out double lat, out double lon, out string label)
    {
        if (Cities.TryGetValue(key.Trim(), out var c))
        {
            lat = c.Lat;
            lon = c.Lon;
            label = c.Label;
            return true;
        }

        lat = lon = 0;
        label = key;
        return false;
    }
}
