// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Contains tests for the duplicate tool name validation performed by
/// <see cref="ApprovalNotRequiredFunctionBypassingChatClient"/>, exercised end to end through a
/// <see cref="ChatClientAgent"/> so that every contributor to the run's tool list is covered.
/// </summary>
public class ChatClientAgent_DuplicateToolNameTests
{
    [Fact]
    public async Task RunAsync_DuplicateNameAcrossAgentAndRunTools_ThrowsAsync()
    {
        // Arrange — the agent gates 'deploy', the run supplies an ungated tool with the same name.
        var gated = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "gated", "deploy"));
        var free = AIFunctionFactory.Create(() => "free", "deploy");

        var agent = CreateAgent(out _, agentTools: [gated]);
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Tools = [free] });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "test")], options: runOptions));
        Assert.Equal("Duplicate tool name 'deploy'. Tool names must be unique.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_DuplicateNameWithinAgentTools_ThrowsAsync()
    {
        // Arrange
        var first = AIFunctionFactory.Create(() => "first", "deploy");
        var second = AIFunctionFactory.Create(() => "second", "deploy");

        var agent = CreateAgent(out _, agentTools: [first, second]);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "test")]));
        Assert.Equal("Duplicate tool name 'deploy'. Tool names must be unique.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_DuplicateNameWithinRunTools_ThrowsAsync()
    {
        // Arrange
        var first = AIFunctionFactory.Create(() => "first", "deploy");
        var second = AIFunctionFactory.Create(() => "second", "deploy");

        var agent = CreateAgent(out _);
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Tools = [first, second] });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "test")], options: runOptions));
        Assert.Equal("Duplicate tool name 'deploy'. Tool names must be unique.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_DuplicateNameAddedByContextProvider_ThrowsAsync()
    {
        // Arrange — a provider contributes a tool whose name collides with the agent's gated tool.
        var gated = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "gated", "deploy"));
        var free = AIFunctionFactory.Create(() => "free", "deploy");

        var agent = CreateAgent(out _, agentTools: [gated], contextProvider: new ToolAddingAIContextProvider(free));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync([new ChatMessage(ChatRole.User, "test")]));
        Assert.Equal("Duplicate tool name 'deploy'. Tool names must be unique.", exception.Message);
    }

    [Fact]
    public async Task RunStreamingAsync_DuplicateNameAcrossAgentAndRunTools_ThrowsAsync()
    {
        // Arrange
        var gated = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "gated", "deploy"));
        var free = AIFunctionFactory.Create(() => "free", "deploy");

        var agent = CreateAgent(out _, agentTools: [gated]);
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Tools = [free] });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync([new ChatMessage(ChatRole.User, "test")], options: runOptions))
            {
            }
        });
        Assert.Equal("Duplicate tool name 'deploy'. Tool names must be unique.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_UniqueToolNames_DoesNotThrowAsync()
    {
        // Arrange
        var gated = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "gated", "deploy"));
        var free = AIFunctionFactory.Create(() => "free", "lookup");

        var agent = CreateAgent(out var capturedOptions, agentTools: [gated]);
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Tools = [free] });

        // Act
        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")], options: runOptions);

        // Assert
        Assert.NotNull(capturedOptions.Value);
        Assert.Equal(2, capturedOptions.Value!.Tools!.Count);
    }

    [Fact]
    public async Task RunAsync_SameToolInstanceSuppliedTwice_DoesNotThrowAsync()
    {
        // Arrange — listing one instance twice is not an ambiguous call, so it is allowed.
        var tool = AIFunctionFactory.Create(() => "result", "deploy");

        var agent = CreateAgent(out _, agentTools: [tool]);
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Tools = [tool] });

        // Act & Assert — no exception.
        await agent.RunAsync([new ChatMessage(ChatRole.User, "test")], options: runOptions);
    }

    #region Helpers

    private static ChatClientAgent CreateAgent(
        out StrongBox<ChatOptions?> capturedOptions,
        IList<AITool>? agentTools = null,
        AIContextProvider? contextProvider = null)
    {
        var captured = new StrongBox<ChatOptions?>(null);
        capturedOptions = captured;

        var mockClient = new Mock<IChatClient>();
        mockClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, options, _) => captured.Value = options)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]));

        mockClient.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, ChatOptions? options, CancellationToken _) =>
            {
                captured.Value = options;
                return SingleUpdateAsync();
            });

        return new ChatClientAgent(mockClient.Object, new ChatClientAgentOptions
        {
            ChatOptions = agentTools is null ? null : new ChatOptions { Tools = agentTools },
            AIContextProviders = contextProvider is null ? null : [contextProvider]
        });
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> SingleUpdateAsync()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "response");
        await Task.CompletedTask;
    }

    private sealed class ToolAddingAIContextProvider(AITool tool) : AIContextProvider
    {
        protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
        {
            context.AIContext.Tools = [.. context.AIContext.Tools ?? [], tool];
            return new(context.AIContext);
        }
    }

    private sealed class StrongBox<T>(T value)
    {
        public T Value { get; set; } = value;
    }

    #endregion
}
