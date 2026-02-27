// Copyright (c) Microsoft. All rights reserved.

using System.Security.Claims;

namespace AuthClientServer.AgentService;

/// <summary>
/// Provides the authenticated user's identity for the current request.
/// </summary>
public interface IUserContext
{
    /// <summary>Unique identifier for the current user (e.g. the OIDC "sub" claim).</summary>
    string UserId { get; }

    /// <summary>Login name for the current user.</summary>
    string UserName { get; }

    /// <summary>Human-readable display name (e.g. "Test User").</summary>
    string DisplayName { get; }
}

/// <summary>
/// Resolves the current user's identity from Keycloak-specific JWT claims.
/// Keycloak uses <c>sub</c> for the user ID, <c>preferred_username</c>
/// for the login name, and <c>given_name</c>/<c>family_name</c> for the
/// display name. Registered as a scoped service so it is resolved once
/// per request.
/// </summary>
public sealed class KeycloakUserContext : IUserContext
{
    public string UserId { get; }

    public string UserName { get; }

    public string DisplayName { get; }

    public KeycloakUserContext(IHttpContextAccessor httpContextAccessor)
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;

        this.UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user?.FindFirstValue("sub")
                   ?? "anonymous";

        this.UserName = user?.FindFirstValue("preferred_username")
                     ?? user?.FindFirstValue(ClaimTypes.Name)
                     ?? "unknown";

        string? givenName = user?.FindFirstValue("given_name") ?? user?.FindFirstValue(ClaimTypes.GivenName);
        string? familyName = user?.FindFirstValue("family_name") ?? user?.FindFirstValue(ClaimTypes.Surname);
        this.DisplayName = (givenName, familyName) switch
        {
            (not null, not null) => $"{givenName} {familyName}",
            (not null, null) => givenName,
            (null, not null) => familyName,
            _ => this.UserName,
        };
    }
}
