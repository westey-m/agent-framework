// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Serialization;
using Microsoft.AspNetCore.Http.Json;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to configure Vercel AI SDK support.
/// </summary>
public static class MicrosoftAgentAIHostingVercelAIServiceCollectionExtensions
{
    /// <summary>
    /// Adds support for exposing <see cref="AIAgent"/> instances via the Vercel AI SDK protocol.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    /// <remarks>
    /// To enable server-side session persistence, register an <see cref="AgentSessionStore"/>
    /// as a keyed service using the agent's <see cref="AIAgent.Name"/> as the key.
    /// Use the <c>WithInMemorySessionStore</c> or <c>WithSessionStore</c> methods on the
    /// <see cref="IHostedAgentBuilder"/> for convenient registration.
    /// </remarks>
    public static IServiceCollection AddVercelAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(VercelAIJsonSerializerContext.Default));

        return services;
    }
}
