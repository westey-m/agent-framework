// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates how to secure an AI agent REST API with
// JWT Bearer authentication and policy-based scope authorization.

using System.Security.Claims;
using System.Text.Json.Serialization;
using AuthClientServer.AgentService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Authentication: JWT Bearer tokens validated against the OIDC provider
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is not configured.");
        options.Audience = builder.Configuration["Auth:Audience"]
            ?? throw new InvalidOperationException("Auth:Audience is not configured.");

        // For local development with HTTP-only Keycloak
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;

        // In Codespaces, tokens are issued with the public tunnel URL as
        // issuer (Keycloak sees X-Forwarded-Host from the tunnel) but the
        // agent-service discovers Keycloak via the internal Docker hostname.
        // Disable issuer validation in development to handle this mismatch.
        options.TokenValidationParameters.ValidateIssuer = !builder.Environment.IsDevelopment();
    });

// ---------------------------------------------------------------------------
// Authorization: policy requiring the "agent.chat" scope
// ---------------------------------------------------------------------------
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AgentChat", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(context =>
              {
                  // Keycloak puts scopes in the "scope" claim (space-delimited)
                  var scopeClaim = context.User.FindFirstValue("scope");
                  if (scopeClaim is not null)
                  {
                      var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                      if (scopes.Contains("agent.chat", StringComparer.OrdinalIgnoreCase))
                      {
                          return true;
                      }
                  }

                  return false;
              }));

// ---------------------------------------------------------------------------
// Configure JSON serialization
// ---------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(SampleServiceSerializerContext.Default));

// ---------------------------------------------------------------------------
// CORS: allow the WebClient origin
// ---------------------------------------------------------------------------
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:8080")
              .AllowAnyHeader()
              .AllowAnyMethod()));

// ---------------------------------------------------------------------------
// Create the AI agent with TODO tools, registered in DI
// ---------------------------------------------------------------------------
string apiKey = builder.Configuration["OPENAI_API_KEY"]
    ?? throw new InvalidOperationException("Set the OPENAI_API_KEY environment variable.");
string model = builder.Configuration["OPENAI_MODEL"] ?? "gpt-4.1-mini";

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, KeycloakUserContext>();
builder.Services.AddScoped<TodoService>();
builder.Services.AddScoped<AIAgent>(sp =>
{
    var todoService = sp.GetRequiredService<TodoService>();

    return new OpenAIClient(apiKey)
        .GetChatClient(model)
        .AsIChatClient()
        .AsAIAgent(
            name: "AuthDemoAgent",
            instructions: "You are a helpful assistant that can manage the user's TODO list. "
                        + "Use the available tools to list and add TODO items when asked. "
                        + "Keep responses concise.",
            tools:
            [
                AIFunctionFactory.Create(todoService.ListTodos),
                AIFunctionFactory.Create(todoService.AddTodo),
            ]);
});

WebApplication app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// POST /chat — requires the "agent.chat" scope
// ---------------------------------------------------------------------------
app.MapPost("/chat", [Authorize(Policy = "AgentChat")] async (ChatRequest request, IUserContext userContext, AIAgent agent) =>
{
    var response = await agent.RunAsync(request.Message);

    return Results.Ok(new ChatResponse(response.Text, userContext.DisplayName));
});

// ---------------------------------------------------------------------------
// GET /me — returns the caller's identity (any authenticated user)
// ---------------------------------------------------------------------------
app.MapGet("/me", [Authorize] (ClaimsPrincipal user) =>
{
    var claims = user.Claims.Select(c => new ClaimInfo(c.Type, c.Value));
    return Results.Ok(claims);
});

await app.RunAsync();

// ---------------------------------------------------------------------------
// Request / Response models
// ---------------------------------------------------------------------------
internal sealed record ChatRequest(string Message);
internal sealed record ChatResponse(string Reply, string User);
internal sealed record ClaimInfo(string Type, string Value);

[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(IEnumerable<ClaimInfo>))]
internal sealed partial class SampleServiceSerializerContext : JsonSerializerContext;
