using Loom.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;

namespace Loom.Models.Nodes;

/// <summary>
/// Creates a Stripe PaymentIntent using the Stripe.net SDK.
///
/// ⚠️  TEST MODE ONLY ⚠️
/// This node will refuse to execute if a live-mode key is detected.
/// Configure in appsettings.json:
///   "Stripe": { "SecretKey": "sk_test_..." }
///
/// Install SDK:  dotnet add package Stripe.net
/// </summary>
public class StripeNode : Node
{
    // ── Safety constants ─────────────────────────────────────────────────────
    private const string TestKeyPrefix = "sk_test_";
    private const string TestModeWarning =
        "⚠️  STRIPE TEST MODE — no real charges are made. " +
        "Use Stripe test card 4242 4242 4242 4242 for testing.";

    // ── Config ───────────────────────────────────────────────────────────────
    private readonly IConfiguration _config;
    private readonly ILogger<StripeNode> _logger;
    private object? _lastOutput;

    // ── Constructor ──────────────────────────────────────────────────────────

    public StripeNode(IConfiguration config, ILogger<StripeNode> logger)
    {
        _config = config;
        _logger = logger;

        Type = NodeType.Stripe;
        Label = "Stripe Payment";

        // Input ports
        AddInputPort("Amount", "long");    // in cents, e.g. 2000 = $20.00
        AddInputPort("Currency", "string");  // ISO 4217, e.g. "usd"
        AddInputPort("PaymentMethodId", "string");  // pm_... test token
        AddInputPort("Description", "string");  // optional

        // Output ports
        AddOutputPort("PaymentIntentId", "string");
        AddOutputPort("Status", "string");
        AddOutputPort("ClientSecret", "string");
        AddOutputPort("TestModeWarning", "string");
    }

    // ── Execution ────────────────────────────────────────────────────────────

    public override async Task<object?> Execute(
        WorkflowExecutionContext ctx,
        CancellationToken cancellationToken = default)
    {
        // ── Key safety check ─────────────────────────────────────────────────
        var secretKey = _config["Stripe:SecretKey"]?.Trim();

        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException(
                "StripeNode: 'Stripe:SecretKey' is not configured in appsettings.json.");

        if (!secretKey.StartsWith(TestKeyPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "StripeNode: Live Stripe keys are FORBIDDEN. " +
                "Only test keys (sk_test_...) may be used in this node. " +
                "Attempted key prefix: " + secretKey[..Math.Min(10, secretKey.Length)] + "...");

        // ── Read inputs ───────────────────────────────────────────────────────
        var amount = GetInputValue<long>("Amount");
        var currency = GetInputValue<string>("Currency")?.Trim().ToLowerInvariant() ?? "usd";
        var paymentMethodId = GetInputValue<string>("PaymentMethodId")?.Trim();
        var description = GetInputValue<string>("Description") ?? string.Empty;

        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount),
                "StripeNode: Amount must be a positive integer (in cents).");

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException(
                $"StripeNode: '{currency}' is not a valid ISO 4217 currency code.");

        // ── Emit test-mode warning ────────────────────────────────────────────
        _logger.LogWarning(TestModeWarning);
        SetOutputValue("TestModeWarning", TestModeWarning);

        // ── Configure Stripe SDK ─────────────────────────────────────────────
        StripeConfiguration.ApiKey = secretKey;

        var service = new PaymentIntentService();

        var options = new PaymentIntentCreateOptions
        {
            Amount = amount,
            Currency = currency,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
            },
            Metadata = new Dictionary<string, string>
            {
                ["loom_execution_id"] = ctx.ExecutionId.ToString(),
                ["loom_workflow_id"] = ctx.WorkflowId.ToString(),
            },
        };

        // Attach an explicit payment method if provided
        if (!string.IsNullOrWhiteSpace(paymentMethodId))
        {
            options.PaymentMethod = paymentMethodId;
            options.ConfirmationMethod = "manual";
            // Don't auto-confirm — let the caller decide when to confirm
        }

        PaymentIntent intent;
        try
        {
            var requestOptions = new RequestOptions();
            // Stripe.net doesn't natively accept a CancellationToken in older versions,
            // but the overload is available in Stripe.net >= 43.x
            intent = await service.CreateAsync(options, requestOptions, cancellationToken);
        }
        catch (StripeException ex)
        {
            throw new InvalidOperationException(
                $"StripeNode: Stripe API error [{ex.StripeError?.Code}]: {ex.Message}", ex);
        }

        // ── Write outputs ─────────────────────────────────────────────────────
        SetOutputValue("PaymentIntentId", intent.Id);
        SetOutputValue("Status", intent.Status);
        SetOutputValue("ClientSecret", intent.ClientSecret);

        _lastOutput = new StripePaymentResult
        {
            PaymentIntentId = intent.Id,
            Status = intent.Status,
            ClientSecret = intent.ClientSecret,
            Amount = intent.Amount,
            Currency = intent.Currency,
            IsTestMode = true,
            Warning = TestModeWarning,
        };

        return _lastOutput;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    public override bool Validate()
    {
        var key = _config["Stripe:SecretKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!key.StartsWith(TestKeyPrefix, StringComparison.Ordinal)) return false;

        var amount = GetInputValue<long>("Amount");
        var currency = GetInputValue<string>("Currency");
        return amount > 0 && !string.IsNullOrWhiteSpace(currency);
    }

    public override object? GetOutput() => _lastOutput;
}

/// <summary>Typed output model returned from StripeNode execution.</summary>
public sealed class StripePaymentResult
{
    public string PaymentIntentId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public long Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public bool IsTestMode { get; init; } = true;
    public string Warning { get; init; } = string.Empty;
}