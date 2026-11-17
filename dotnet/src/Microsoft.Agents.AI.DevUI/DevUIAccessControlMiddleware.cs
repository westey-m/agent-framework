// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Net.Http.Headers;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Applies DevUI access validation to the standard OpenAI Responses and Conversations routes,
/// and anti-framing headers to those routes and to DevUI endpoints.
/// </summary>
internal sealed class DevUIAccessControlMiddleware
{
    private const string FrameAncestorsNonePolicy = "frame-ancestors 'none'";

    private static readonly PathString s_responsesPath = new("/v1/responses");
    private static readonly PathString s_conversationsPath = new("/v1/conversations");

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevUIAccessControlMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the request pipeline.</param>
    public DevUIAccessControlMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        this._next = next;
    }

    /// <summary>
    /// Validates protected DevUI backend routes and continues allowed requests.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="validator">The shared DevUI request validator.</param>
    /// <returns>A task that represents processing the request.</returns>
    public async Task InvokeAsync(HttpContext context, DevUIRequestValidator validator)
    {
        var requestPath = context.Request.PathBase.Add(context.Request.Path);
        var isProtectedPath = IsProtectedPath(requestPath);

        // Registered before any validation so rejected responses are covered too.
        // The endpoint is only known after routing, so the check runs when the response starts.
        context.Response.OnStarting(static state =>
        {
            var (httpContext, isProtectedPath) = ((HttpContext, bool))state;
            if (isProtectedPath || httpContext.GetEndpoint()?.Metadata.GetMetadata<DevUIEndpointMetadata>() is not null)
            {
                AddAntiFramingHeaders(httpContext.Response.Headers);
            }

            return Task.CompletedTask;
        }, (context, isProtectedPath));

        if (isProtectedPath)
        {
            var failure = validator.ValidateLocalRequest(context);
            if (failure is not null)
            {
                await failure.ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await this._next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Prevents other sites from embedding DevUI in a frame (clickjacking).
    /// </summary>
    /// <remarks>
    /// The policy is appended rather than replacing any host policy: browsers enforce every
    /// Content-Security-Policy header, so an existing host policy keeps applying.
    /// </remarks>
    private static void AddAntiFramingHeaders(IHeaderDictionary headers)
    {
        if (!headers.ContentSecurityPolicy.Contains(FrameAncestorsNonePolicy))
        {
            headers.Append(HeaderNames.ContentSecurityPolicy, FrameAncestorsNonePolicy);
        }

        headers.XFrameOptions = "DENY";
    }

    private static bool IsProtectedPath(PathString path)
        => ContainsPathSegment(path, s_responsesPath) || ContainsPathSegment(path, s_conversationsPath);

    private static bool ContainsPathSegment(PathString path, PathString routePath)
    {
        var pathValue = path.Value;
        var routeValue = routePath.Value;
        if (pathValue is null || routeValue is null)
        {
            return false;
        }

        var matchIndex = pathValue.IndexOf(routeValue, StringComparison.OrdinalIgnoreCase);
        while (matchIndex >= 0)
        {
            var matchEnd = matchIndex + routeValue.Length;
            if (matchEnd == pathValue.Length || pathValue[matchEnd] == '/')
            {
                return true;
            }

            matchIndex = pathValue.IndexOf(routeValue, matchEnd, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
