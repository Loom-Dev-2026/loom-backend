// ─────────────────────────────────────────────────────────────────────────────
// Program.cs  —  Loom Blazor Server (.NET 8)
//
// This file shows the COMPLETE minimal Program.cs required to wire up all
// Loom services, including IHttpClientFactory, Stripe, and the NodeFactory.
//
// Polly packages required:
//   dotnet add package Microsoft.Extensions.Http.Polly
//   dotnet add package Polly.Extensions.Http
//
// Stripe package required:
//   dotnet add package Stripe.net
// ─────────────────────────────────────────────────────────────────────────────

using Loom.Api;
using Loom.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ─────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Loom services (HttpClient, Stripe, NodeFactory, ExecutionEngine, …) ───────
builder.Services.AddLoomServices(builder.Configuration);

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:5280", "https://localhost:7280"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("LoomFrontend", policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// ── Optional: OpenAPI / Swagger ───────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("LoomFrontend");
app.UseStaticFiles();
app.UseAntiforgery();

app.MapWorkflowApi();
app.MapRazorComponents<LOOM_VP_Project.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();