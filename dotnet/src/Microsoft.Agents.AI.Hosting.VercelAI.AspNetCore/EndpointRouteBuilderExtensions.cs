// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Converters;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore;

/// <summary>
/// Provides extension methods for mapping Vercel AI SDK–compatible chat endpoints.
/// </summary>
public static class VercelAIEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a POST endpoint at the given <paramref name="pattern"/> that accepts Vercel AI SDK
    /// chat requests and streams responses in the UI Message Stream format.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint (e.g. <c>/api/chat</c>).</param>
    /// <param name="agentBuilder">
    /// The hosted agent builder whose <see cref="IHostedAgentBuilder.Name"/> identifies
    /// the agent and its associated services (e.g. session store) in the DI container.
    /// </param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for the mapped endpoint.</returns>
    /// <remarks>
    /// <para>
    /// The agent is resolved from DI as a keyed service using <see cref="IHostedAgentBuilder.Name"/>.
    /// Register the agent via <c>AddAIAgent</c> on the service collection
    /// and optionally chain <c>.WithInMemorySessionStore()</c> or <c>.WithSessionStore()</c>.
    /// </para>
    /// <para>
    /// See <see href="https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages#sending-only-the-last-message"/>
    /// for the recommended Vercel AI SDK client-side pattern.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapVercelAI(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("route")] string pattern,
        IHostedAgentBuilder agentBuilder)
    {
        ArgumentNullException.ThrowIfNull(agentBuilder);
        return endpoints.MapVercelAI(pattern, agentBuilder.Name);
    }

    /// <summary>
    /// Maps a POST endpoint at the given <paramref name="pattern"/> that accepts Vercel AI SDK
    /// chat requests and streams responses in the UI Message Stream format.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint (e.g. <c>/api/chat</c>).</param>
    /// <param name="agentName">
    /// The name of the agent registered as a keyed service in the DI container.
    /// </param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for the mapped endpoint.</returns>
    public static IEndpointConventionBuilder MapVercelAI(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("route")] string pattern,
        string agentName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var agent = endpoints.ServiceProvider.GetRequiredKeyedService<AIAgent>(agentName);
        return endpoints.MapVercelAI(pattern, agent);
    }

    /// <summary>
    /// Maps a POST endpoint at the given <paramref name="pattern"/> that accepts Vercel AI SDK
    /// chat requests and streams responses in the UI Message Stream format.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The URL pattern for the endpoint (e.g. <c>/api/chat</c>).</param>
    /// <param name="aiAgent">The <see cref="AIAgent"/> instance to handle requests.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for the mapped endpoint.</returns>
    /// <remarks>
    /// <para>
    /// This endpoint supports both full-history and single-message request modes.
    /// In single-message mode the client sends only the latest message and the chat session ID
    /// (via <c>prepareSendMessagesRequest</c>), and the server maintains conversation history
    /// through an <see cref="AgentSession"/> backed by the registered <see cref="AgentSessionStore"/>.
    /// </para>
    /// <para>
    /// See <see href="https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages#sending-only-the-last-message"/>
    /// for the recommended Vercel AI SDK client-side pattern.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapVercelAI(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("route")] string pattern,
        AIAgent aiAgent)
    {
        return endpoints.MapPost(pattern, async ([FromBody] VercelAIChatRequest? request, HttpContext context, CancellationToken cancellationToken) =>
        {
            // Support both full-history mode (Messages array) and single-message mode (Message field).
            // See: https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages#sending-only-the-last-message
            var messages = request?.Messages?.ToChatMessages();
            if (messages is null or { Count: 0 } && request?.Message is not null)
            {
                messages = [request.Message.ToChatMessage()];
            }

            if (messages is null or { Count: 0 })
            {
                return Results.BadRequest("Request must include at least one message.");
            }

            // Resolve the session store using keyed DI (keyed by agent name).
            // Falls back to NoopAgentSessionStore when no store is registered.
            // To enable session persistence, register an AgentSessionStore as a keyed service
            // using the agent's Name as the key (e.g. via builder.WithInMemorySessionStore()).
            var sessionStore = context.RequestServices.GetKeyedService<AgentSessionStore>(aiAgent.Name)
                ?? new NoopAgentSessionStore();
            var hostAgent = new AIHostAgent(aiAgent, sessionStore);

            var conversationId = request!.Id ?? Guid.NewGuid().ToString("N");
            var session = await hostAgent.GetOrCreateSessionAsync(conversationId, cancellationToken).ConfigureAwait(false);

            // Run the agent with the session and convert the streaming response to Vercel AI SDK chunks
            var chunks = hostAgent.RunStreamingAsync(
                messages,
                session: session,
                cancellationToken: cancellationToken)
                .AsVercelAIChunkStreamAsync(cancellationToken);

            var logger = context.RequestServices.GetRequiredService<ILogger<VercelAIStreamResult>>();

            // Save the session after streaming completes so conversation state is persisted.
            return new VercelAIStreamResult(chunks, logger,
                async () => await hostAgent.SaveSessionAsync(conversationId, session, CancellationToken.None).ConfigureAwait(false));
        });
    }
}
