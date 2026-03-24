// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore;

/// <summary>
/// An <see cref="IResult"/> that writes a stream of <see cref="UIMessageChunk"/> objects
/// as Server-Sent Events in the Vercel AI SDK UI Message Stream format.
/// </summary>
/// <remarks>
/// Each chunk is serialized as <c>data: {json}\n\n</c> and the stream ends with <c>data: [DONE]\n\n</c>.
/// </remarks>
internal sealed partial class VercelAIStreamResult : IResult
{
    private static readonly byte[] s_dataPrefix = "data: "u8.ToArray();
    private static readonly byte[] s_eventSuffix = "\n\n"u8.ToArray();
    private static readonly byte[] s_doneMarker = "data: [DONE]\n\n"u8.ToArray();

    private readonly IAsyncEnumerable<UIMessageChunk> _chunks;
    private readonly ILogger<VercelAIStreamResult> _logger;
    private readonly Func<Task>? _onCompleted;

    internal VercelAIStreamResult(IAsyncEnumerable<UIMessageChunk> chunks, ILogger<VercelAIStreamResult> logger, Func<Task>? onCompleted = null)
    {
        this._chunks = chunks;
        this._logger = logger;
        this._onCompleted = onCompleted;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        response.ContentType = UIMessageStreamHeaders.ContentType;
        response.Headers.CacheControl = UIMessageStreamHeaders.CacheControl;
        response.Headers.Connection = UIMessageStreamHeaders.Connection;
        response.Headers[UIMessageStreamHeaders.ProtocolVersionHeader] = UIMessageStreamHeaders.ProtocolVersion;
        response.Headers[UIMessageStreamHeaders.AccelBufferingHeader] = UIMessageStreamHeaders.AccelBufferingValue;

        var body = response.Body;
        var cancellationToken = httpContext.RequestAborted;

        try
        {
            await foreach (var chunk in this._chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await WriteSseEventAsync(body, chunk, cancellationToken).ConfigureAwait(false);
                await body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Write the [DONE] marker
            await body.WriteAsync(s_doneMarker, cancellationToken).ConfigureAwait(false);
            await body.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Save session state after streaming completes successfully
            if (this._onCompleted is not null)
            {
                await this._onCompleted().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to do
        }
        catch (Exception ex)
        {
            LogStreamingError(this._logger, ex);

            // Try to send an error event before closing
            try
            {
                var errorChunk = new ErrorChunk { ErrorText = ex.Message };
                await WriteSseEventAsync(body, errorChunk, CancellationToken.None).ConfigureAwait(false);
                await body.WriteAsync(s_doneMarker, CancellationToken.None).ConfigureAwait(false);
                await body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception sendErrorEx)
            {
                LogSendErrorEventFailed(this._logger, sendErrorEx);
            }
        }
    }

    private static async Task WriteSseEventAsync(Stream body, UIMessageChunk chunk, CancellationToken cancellationToken)
    {
        await body.WriteAsync(s_dataPrefix, cancellationToken).ConfigureAwait(false);

        var json = JsonSerializer.SerializeToUtf8Bytes(chunk, VercelAIJsonSerializerContext.Default.UIMessageChunk);
        await body.WriteAsync(json, cancellationToken).ConfigureAwait(false);

        await body.WriteAsync(s_eventSuffix, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An error occurred while streaming Vercel AI UI Message Stream events")]
    private static partial void LogStreamingError(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to send error event to client after streaming failure")]
    private static partial void LogSendErrorEventFailed(ILogger logger, Exception exception);
}
