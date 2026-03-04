// Copyright (c) Microsoft. All rights reserved.

using System.Security.Claims;

namespace AspNetAgentAuthorization.Service;

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

    /// <summary>OAuth scopes granted in the current access token.</summary>
    IReadOnlySet<string> Scopes { get; }
}

/// <summary>
/// Resolves the current user's identity from Keycloak-specific JWT claims.
/// Keycloak uses <c>sub</c> for the user ID, <c>preferred_username</c>
/// for the login name, <c>given_name</c>/<c>family_name</c> for the
/// display name, and <c>scope</c> (space-delimited) for granted scopes.
/// Registered as a singleton — properties are read from the current
/// <see cref="HttpContext"/> on every access.
/// </summary>
public sealed class KeycloakUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public KeycloakUserContext(IHttpContextAccessor httpContextAccessor)
    {
        this._httpContextAccessor = httpContextAccessor;
    }

    public string UserId
    {
        get
        {
            ClaimsPrincipal? user = this.CurrentUser;
            return user?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user?.FindFirstValue("sub")
                ?? "anonymous";
        }
    }

    public string UserName
    {
        get
        {
            ClaimsPrincipal? user = this.CurrentUser;
            return user?.FindFirstValue("preferred_username")
                ?? user?.FindFirstValue(ClaimTypes.Name)
                ?? "unknown";
        }
    }

    public string DisplayName
    {
        get
        {
            ClaimsPrincipal? user = this.CurrentUser;
            string? givenName = user?.FindFirstValue("given_name") ?? user?.FindFirstValue(ClaimTypes.GivenName);
            string? familyName = user?.FindFirstValue("family_name") ?? user?.FindFirstValue(ClaimTypes.Surname);
            return (givenName, familyName) switch
            {
                (not null, not null) => $"{givenName} {familyName}",
                (not null, null) => givenName,
                (null, not null) => familyName,
                _ => this.UserName,
            };
        }
    }

    public IReadOnlySet<string> Scopes
    {
        get
        {
            string? scopeClaim = this.CurrentUser?.FindFirstValue("scope");
            return scopeClaim is not null
                ? new HashSet<string>(scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private ClaimsPrincipal? CurrentUser => this._httpContextAccessor.HttpContext?.User;
}
