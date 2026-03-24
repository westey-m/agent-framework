// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;

/// <summary>
/// Defines the HTTP response headers required by the Vercel AI SDK UI Message Stream protocol.
/// </summary>
internal static class UIMessageStreamHeaders
{
    /// <summary>The content type for SSE streams.</summary>
    internal const string ContentType = "text/event-stream";

    /// <summary>Cache control header value to prevent caching.</summary>
    internal const string CacheControl = "no-cache";

    /// <summary>Connection header value for keep-alive.</summary>
    internal const string Connection = "keep-alive";

    /// <summary>Vercel AI SDK protocol version header name.</summary>
    internal const string ProtocolVersionHeader = "x-vercel-ai-ui-message-stream";

    /// <summary>Vercel AI SDK protocol version value.</summary>
    internal const string ProtocolVersion = "v1";

    /// <summary>Disables nginx buffering for streaming responses.</summary>
    internal const string AccelBufferingHeader = "x-accel-buffering";

    /// <summary>Value to disable nginx buffering.</summary>
    internal const string AccelBufferingValue = "no";
}
