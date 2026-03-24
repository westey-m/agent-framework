// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore.UnitTests;

public sealed class VercelAIStreamResultTests
{
    private static readonly ILogger<VercelAIStreamResult> Logger = NullLogger<VercelAIStreamResult>.Instance;

    [Fact]
    public async Task ExecuteAsync_SetsCorrectHeaders()
    {
        // Arrange
        var chunks = new List<UIMessageChunk>().ToAsyncEnumerableAsync();
        var sut = new VercelAIStreamResult(chunks, Logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        httpContext.Response.Headers["Content-Type"].ToString().Should().Contain("text/event-stream");
        httpContext.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache");
        httpContext.Response.Headers["Connection"].ToString().Should().Be("keep-alive");
        httpContext.Response.Headers["x-vercel-ai-ui-message-stream"].ToString().Should().Be("v1");
        httpContext.Response.Headers["x-accel-buffering"].ToString().Should().Be("no");
    }

    [Fact]
    public async Task ExecuteAsync_WritesChunksAsSseEvents()
    {
        // Arrange
        var chunks = new List<UIMessageChunk>
        {
            new StartChunk { MessageId = "msg-1" },
            new FinishChunk { FinishReason = "stop" }
        };
        var sut = new VercelAIStreamResult(chunks.ToAsyncEnumerableAsync(), Logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        var events = ParseSseDataEvents(body);
        events.Should().HaveCount(2);
        events[0].Should().StartWith("data: ");
        events[0].Should().Contain("\"type\":\"start\"");
        events[1].Should().StartWith("data: ");
        events[1].Should().Contain("\"type\":\"finish\"");
    }

    [Fact]
    public async Task ExecuteAsync_WriteDoneMarkerAtEnd()
    {
        // Arrange
        var chunks = new List<UIMessageChunk>
        {
            new StartChunk { MessageId = "msg-1" }
        };
        var sut = new VercelAIStreamResult(chunks.ToAsyncEnumerableAsync(), Logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        body.Should().EndWith("data: [DONE]\n\n");
    }

    [Fact]
    public async Task ExecuteAsync_OnCompleted_InvokedAfterStream()
    {
        // Arrange
        var callbackInvoked = false;
        var chunks = new List<UIMessageChunk>
        {
            new StartChunk { MessageId = "msg-1" }
        };
        var sut = new VercelAIStreamResult(
            chunks.ToAsyncEnumerableAsync(),
            Logger,
            onCompleted: () =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            });
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_OnCompleted_ExceptionDoesNotPropagate()
    {
        // Arrange
        var chunks = new List<UIMessageChunk>
        {
            new StartChunk { MessageId = "msg-1" }
        };
        var sut = new VercelAIStreamResult(
            chunks.ToAsyncEnumerableAsync(),
            Logger,
            onCompleted: () => throw new InvalidOperationException("callback error"));
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        var act = () => sut.ExecuteAsync(httpContext);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_StreamError_WritesErrorChunkAndDone()
    {
        // Arrange
        var sut = new VercelAIStreamResult(ThrowingStreamAsync(), Logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        body.Should().Contain("\"type\":\"error\"");
        body.Should().Contain("\"errorText\":\"stream failure\"");
        body.Should().EndWith("data: [DONE]\n\n");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_NoErrorChunk()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var sut = new VercelAIStreamResult(CancellingStreamAsync(cts), Logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestAborted = cts.Token;

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        body.Should().NotContain("\"type\":\"error\"");
    }

    [Fact]
    public async Task ExecuteAsync_TextDeltaChunk_SerializedCorrectly()
    {
        // Arrange
        var chunks = new List<UIMessageChunk>
        {
            new TextDeltaChunk { Id = "td-1", Delta = "Hello world" }
        };
        var sut = new VercelAIStreamResult(chunks.ToAsyncEnumerableAsync(), Logger);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        // Act
        await sut.ExecuteAsync(httpContext);

        // Assert
        var body = GetResponseBody(httpContext);
        var events = ParseSseDataEvents(body);
        events.Should().ContainSingle();

        var json = events[0]["data: ".Length..];
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("text-delta");
        root.GetProperty("delta").GetString().Should().Be("Hello world");
        root.GetProperty("id").GetString().Should().Be("td-1");
    }

    private static string GetResponseBody(DefaultHttpContext httpContext)
    {
        return Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray());
    }

    private static List<string> ParseSseDataEvents(string body)
    {
        var events = new List<string>();
        foreach (var segment in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.StartsWith("data: ", StringComparison.Ordinal) && segment != "data: [DONE]")
            {
                events.Add(segment);
            }
        }

        return events;
    }

    private static async IAsyncEnumerable<UIMessageChunk> ThrowingStreamAsync()
    {
        yield return new StartChunk { MessageId = "msg-1" };
        await Task.CompletedTask.ConfigureAwait(false);
        throw new InvalidOperationException("stream failure");
    }

    private static async IAsyncEnumerable<UIMessageChunk> CancellingStreamAsync(
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StartChunk { MessageId = "test" };
        await Task.CompletedTask.ConfigureAwait(false);
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        yield return new FinishChunk { FinishReason = "stop" };
    }
}
