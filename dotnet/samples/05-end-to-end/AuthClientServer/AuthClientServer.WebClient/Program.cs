// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates an OIDC-authenticated Razor Pages web client
// that calls a JWT-secured AI agent REST API.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Persist data protection keys so antiforgery tokens survive container rebuilds
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"));

// ---------------------------------------------------------------------------
// Authentication: Cookie + OpenID Connect (Keycloak)
// ---------------------------------------------------------------------------
string authority = builder.Configuration["Auth:Authority"]
    ?? throw new InvalidOperationException("Auth:Authority is not configured.");

// Auto-detect Codespaces: derive the public Keycloak URL for browser redirects.
// Authority stays as localhost (reachable on host networking) for backchannel
// discovery; only browser-facing redirects use the public URL.
string? codespaceName = Environment.GetEnvironmentVariable("CODESPACE_NAME");
string? codespaceDomain = Environment.GetEnvironmentVariable("GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN");
string? publicKeycloakBase = (!string.IsNullOrEmpty(codespaceName) && !string.IsNullOrEmpty(codespaceDomain))
    ? $"https://{codespaceName}-5002.{codespaceDomain}"
    : null;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.Authority = authority;
        options.ClientId = builder.Configuration["Auth:ClientId"]
            ?? throw new InvalidOperationException("Auth:ClientId is not configured.");

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        // Request the agent.chat scope so the access token includes it
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("agent.chat");

        // For local development with HTTP-only Keycloak
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        // In Codespaces, the tunnel delivers requests to localhost but the
        // browser must redirect to the public Codespaces URLs. Rewrite the
        // authorization endpoint and redirect URIs so the browser reaches
        // Keycloak and the web-client via the public Codespaces URLs.
        // Issuer validation is disabled because the token is issued via the
        // public URL (issuer = public hostname) but the discovery doc is
        // fetched from localhost (issuer = localhost).
        if (publicKeycloakBase is not null)
        {
#pragma warning disable CA5404 // Disabling token validation checks in development environment to allow Codespaces tunnel URL for browser redirects, do not do this in production.
            options.TokenValidationParameters.ValidateIssuer = false;
#pragma warning restore CA5404

            // The UserInfo endpoint is on localhost but the token issuer is
            // the public URL — Keycloak rejects the token. The ID token
            // already contains the claims we need, so skip the UserInfo call.
            options.GetClaimsFromUserInfoEndpoint = false;

            string publicBase = $"https://{codespaceName}-8080.{codespaceDomain}";
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.IssuerAddress = context.ProtocolMessage.IssuerAddress
                        .Replace("http://localhost:5002", publicKeycloakBase);
                    context.ProtocolMessage.RedirectUri = $"{publicBase}/signin-oidc";
                    return Task.CompletedTask;
                },
                OnRedirectToIdentityProviderForSignOut = context =>
                {
                    context.ProtocolMessage.IssuerAddress = context.ProtocolMessage.IssuerAddress
                        .Replace("http://localhost:5002", publicKeycloakBase);
                    context.ProtocolMessage.PostLogoutRedirectUri = $"{publicBase}/signout-callback-oidc";
                    return Task.CompletedTask;
                },
            };
        }
    });

// ---------------------------------------------------------------------------
// HttpClient for calling the AgentService — attaches Bearer token
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient("AgentService", client =>
{
    string baseUrl = builder.Configuration["AgentService:BaseUrl"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
});

WebApplication app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

await app.RunAsync();
