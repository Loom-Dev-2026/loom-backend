using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loom.Models;

namespace Loom.Models.Nodes;

/// <summary>
/// Fetches current weather conditions from the free Open-Meteo API.
/// No API key required. Uses WMO weather interpretation codes for descriptions.
/// Docs: https://open-meteo.com/en/docs
/// </summary>
public class WeatherNode : Node
{
    // ── WMO Weather Interpretation Code map ──────────────────────────────────
    private static readonly IReadOnlyDictionary<int, string> WmoDescriptions =
        new Dictionary<int, string>
        {
            { 0,  "Clear sky" },
            { 1,  "Mainly clear" },
            { 2,  "Partly cloudy" },
            { 3,  "Overcast" },
            { 45, "Fog" },
            { 48, "Icy fog" },
            { 51, "Light drizzle" },
            { 53, "Moderate drizzle" },
            { 55, "Dense drizzle" },
            { 61, "Slight rain" },
            { 63, "Moderate rain" },
            { 65, "Heavy rain" },
            { 71, "Slight snow" },
            { 73, "Moderate snow" },
            { 75, "Heavy snow" },
            { 77, "Snow grains" },
            { 80, "Slight showers" },
            { 81, "Moderate showers" },
            { 82, "Heavy showers" },
            { 85, "Slight snow showers" },
            { 86, "Heavy snow showers" },
            { 95, "Thunderstorm" },
            { 96, "Thunderstorm with slight hail" },
            { 99, "Thunderstorm with heavy hail" },
        };

    // ── Node config ──────────────────────────────────────────────────────────

    /// <summary>
    /// Override the default timeout (seconds). Open-Meteo is fast; 10 s is plenty.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Preset city key when lat/lon inputs are not wired (see <see cref="CityPresets"/>).</summary>
    public string LocationPreset { get; set; } = "London";

    public double DefaultLatitude { get; set; } = 51.5074;
    public double DefaultLongitude { get; set; } = -0.1278;

    private readonly IHttpClientFactory _httpClientFactory;
    private object? _lastOutput;

    // ── Constructor ──────────────────────────────────────────────────────────

    public WeatherNode(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? NullHttpClientFactory.Instance;

        Type = NodeType.Weather;
        Label = "ApiWeather";

        AddInputPort("Latitude", "double");
        AddInputPort("Longitude", "double");

        AddOutputPort("Result", "string");
    }

    // ── Execution ────────────────────────────────────────────────────────────

    public override async Task<object?> Execute(
        WorkflowExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        var lat = GetInputPort("Latitude")?.GetValue() is not null
            ? GetInputValue<double>("Latitude")
            : DefaultLatitude;
        var lon = GetInputPort("Longitude")?.GetValue() is not null
            ? GetInputValue<double>("Longitude")
            : DefaultLongitude;

        var city = LocationPreset;
        if (CityPresets.TryResolve(LocationPreset, out var presetLat, out var presetLon, out var presetLabel))
        {
            city = presetLabel;
            if (GetInputPort("Latitude")?.GetValue() is null)
            {
                lat = presetLat;
                lon = presetLon;
            }
        }

        if (lat < -90 || lat > 90)
            throw new ArgumentOutOfRangeException(nameof(lat),
                $"Latitude must be between -90 and 90, got {lat}.");

        if (lon < -180 || lon > 180)
            throw new ArgumentOutOfRangeException(nameof(lon),
                $"Longitude must be between -180 and 180, got {lon}.");

        // Build URL — request current_weather + hourly humidity
        var url = $"https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={lat:F6}" +
                  $"&longitude={lon:F6}" +
                  $"&current_weather=true" +
                  $"&hourly=relativehumidity_2m,apparent_temperature" +
                  $"&forecast_days=1" +
                  $"&timezone=auto";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var httpClient = _httpClientFactory.CreateClient(ApiHttp.ClientName);

        OpenMeteoResponse response;
        try
        {
            response = await httpClient.GetFromJsonAsync<OpenMeteoResponse>(
                url, cts.Token) ?? throw new InvalidOperationException(
                "Received empty response from Open-Meteo.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Open-Meteo did not respond within {TimeoutSeconds} seconds.");
        }

        var cw = response.CurrentWeather
            ?? throw new InvalidOperationException(
                "Open-Meteo response did not contain current_weather data.");

        // Resolve apparent temperature from the hourly array (index 0 = current hour)
        var feelsLike = response.Hourly?.ApparentTemperature?.FirstOrDefault() ?? cw.Temperature;

        // Resolve humidity from the hourly array
        var humidity = (double)(response.Hourly?.RelativeHumidity2m?.FirstOrDefault() ?? 0);

        var description = WmoDescriptions.TryGetValue(cw.Weathercode, out var desc)
            ? desc
            : $"Unknown (code {cw.Weathercode})";

        var place = string.IsNullOrWhiteSpace(city) ? $"({lat:F2}, {lon:F2})" : city;
        var summary =
            $"{place}: {description}, {cw.Temperature:0.#}°C (feels {feelsLike:0.#}°C), humidity {humidity:0}%, wind {cw.Windspeed:0.#} km/h";

        SetOutputValue("Result", summary);

        _lastOutput = summary;
        _detail = new WeatherResult
        {
            City = place,
            Latitude = lat,
            Longitude = lon,
            Temperature = cw.Temperature,
            FeelsLike = feelsLike,
            Humidity = humidity,
            WindSpeed = cw.Windspeed,
            WeatherDescription = description,
            WeatherCode = cw.Weathercode,
            IsDay = cw.IsDay == 1,
        };

        return _lastOutput;
    }

    private WeatherResult? _detail;

    public override bool Validate() =>
        DefaultLatitude >= -90 && DefaultLatitude <= 90
        && DefaultLongitude >= -180 && DefaultLongitude <= 180;

    public override object? GetOutput() => _lastOutput;

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current_weather")]
        public CurrentWeatherDto? CurrentWeather { get; init; }

        [JsonPropertyName("hourly")]
        public HourlyDto? Hourly { get; init; }
    }

    private sealed class CurrentWeatherDto
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("windspeed")]
        public double Windspeed { get; init; }

        [JsonPropertyName("weathercode")]
        public int Weathercode { get; init; }

        [JsonPropertyName("is_day")]
        public int IsDay { get; init; }

        [JsonPropertyName("time")]
        public string? Time { get; init; }
    }

    private sealed class HourlyDto
    {
        [JsonPropertyName("apparent_temperature")]
        public List<double>? ApparentTemperature { get; init; }

        [JsonPropertyName("relativehumidity_2m")]
        public List<int>? RelativeHumidity2m { get; init; }
    }
}

/// <summary>Typed output model surfaced on the RawResponse port.</summary>
public sealed class WeatherResult
{
    public string City { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Temperature { get; init; }
    public double FeelsLike { get; init; }
    public double Humidity { get; init; }
    public double WindSpeed { get; init; }
    public string WeatherDescription { get; init; } = string.Empty;
    public int WeatherCode { get; init; }
    public bool IsDay { get; init; }
}