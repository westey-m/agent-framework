// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Contains unit tests that verify the <see cref="ChatClientAgentRunOptions.StoreFinalFunctionResultContent"/>
/// filtering behavior of the <see cref="ChatClientAgent"/> class.
/// </summary>
public class ChatClientAgent_StoreFinalFunctionResultContentTests
{
    /// <summary>
    /// Verify that when <see cref="ChatClientAgentRunOptions.StoreFinalFunctionResultContent"/> is false (default),
    /// trailing <see cref="FunctionResultContent"/> messages are filtered out before being stored in chat history.
    /// </summary>
    [Fact]
    public async Task RunAsync_FiltersFinalFunctionResultContent_WhenSettingIsFalseAsync()
    {
        // Arrange
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Sunny")])
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act
        await agent.RunAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = false });

        // Assert — chat history should have: user message + assistant FunctionCallContent (tool message filtered out)
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(2, stored.Count);
        Assert.Equal(ChatRole.User, stored[0].Role);
        Assert.Equal(ChatRole.Assistant, stored[1].Role);
        Assert.True(stored[1].Contents.OfType<FunctionCallContent>().Any());
    }

    /// <summary>
    /// Verify that when <see cref="ChatClientAgentRunOptions.StoreFinalFunctionResultContent"/> is null (defaults to false),
    /// trailing <see cref="FunctionResultContent"/> messages are filtered out before being stored in chat history.
    /// </summary>
    [Fact]
    public async Task RunAsync_FiltersFinalFunctionResultContent_WhenSettingIsNullAsync()
    {
        // Arrange
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Sunny")])
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act — no explicit StoreFinalFunctionResultContent set (null → defaults to false behavior)
        await agent.RunAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions());

        // Assert — tool message should be filtered out
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(2, stored.Count);
        Assert.Equal(ChatRole.Assistant, stored[1].Role);
        Assert.True(stored[1].Contents.OfType<FunctionCallContent>().Any());
    }

    /// <summary>
    /// Verify that when <see cref="ChatClientAgentRunOptions.StoreFinalFunctionResultContent"/> is true,
    /// trailing <see cref="FunctionResultContent"/> messages are kept and stored in chat history.
    /// </summary>
    [Fact]
    public async Task RunAsync_KeepsFinalFunctionResultContent_WhenSettingIsTrueAsync()
    {
        // Arrange
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Sunny")])
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act
        await agent.RunAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = true });

        // Assert — chat history should have all 3 messages (user + assistant + tool)
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(3, stored.Count);
        Assert.Equal(ChatRole.Tool, stored[2].Role);
        Assert.True(stored[2].Contents.OfType<FunctionResultContent>().Any());
    }

    /// <summary>
    /// Verify that no filtering occurs when the last content in the response is not <see cref="FunctionResultContent"/>.
    /// </summary>
    [Fact]
    public async Task RunAsync_NoFiltering_WhenLastContentIsNotFunctionResultAsync()
    {
        // Arrange
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "The weather is sunny.")
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act
        await agent.RunAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = false });

        // Assert — chat history should have user + assistant text (no filtering applied)
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(2, stored.Count);
        Assert.Equal("The weather is sunny.", stored[1].Text);
    }

    /// <summary>
    /// Verify that multiple trailing messages containing only <see cref="FunctionResultContent"/> are all removed.
    /// </summary>
    [Fact]
    public async Task RunAsync_FiltersMultipleTrailingFunctionResultMessagesAsync()
    {
        // Arrange — two trailing tool messages with FunctionResultContent
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather"), new FunctionCallContent("c2", "get_news")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Sunny")]),
            new(ChatRole.Tool, [new FunctionResultContent("c2", "Headlines")])
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act
        await agent.RunAsync([new(ChatRole.User, "Weather and news?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = false });

        // Assert — both trailing tool messages should be filtered, leaving user + assistant
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(2, stored.Count);
        Assert.Equal(ChatRole.Assistant, stored[1].Role);
        Assert.Equal(2, stored[1].Contents.OfType<FunctionCallContent>().Count());
    }

    /// <summary>
    /// Verify that in the streaming path, trailing <see cref="FunctionResultContent"/> is also filtered
    /// before being stored in chat history.
    /// </summary>
    [Fact]
    public async Task RunStreamingAsync_FiltersFinalFunctionResultContent_WhenSettingIsFalseAsync()
    {
        // Arrange
        var streamingUpdates = new[]
        {
            new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new FunctionCallContent("c1", "get_weather")] },
            new ChatResponseUpdate { Role = ChatRole.Tool, Contents = [new FunctionResultContent("c1", "Sunny")] }
        };

        Mock<IChatClient> mockService = new();
        mockService.Setup(
            s => s.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(streamingUpdates.ToAsyncEnumerable());

        var (agent, session) = CreateAgentWithChatClient(mockService: mockService);

        // Act
        await foreach (var update in agent.RunStreamingAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = false }))
        {
            // consume all updates
        }

        // Assert — tool message should be filtered from chat history
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(2, stored.Count);
        Assert.Equal(ChatRole.User, stored[0].Role);
        Assert.Equal(ChatRole.Assistant, stored[1].Role);
        Assert.True(stored[1].Contents.OfType<FunctionCallContent>().Any());
    }

    /// <summary>
    /// Verify that <see cref="AgentResponse"/> returned to the caller still contains the unfiltered response.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReturnsUnfilteredResponseToCallerAsync()
    {
        // Arrange
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "get_weather")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "Sunny")])
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act
        var response = await agent.RunAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = false });

        // Assert — the returned AgentResponse should contain the full unfiltered response
        Assert.Equal(2, response.Messages.Count);
        Assert.True(response.Messages[^1].Contents.OfType<FunctionResultContent>().Any());
    }

    /// <summary>
    /// Verify that when a trailing message has mixed content (FunctionResultContent and other content),
    /// the message is left unchanged.
    /// </summary>
    [Fact]
    public async Task RunAsync_KeepsMixedContentMessage_UnchangedAsync()
    {
        // Arrange — last message has both TextContent and FunctionResultContent
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new TextContent("Some note"), new FunctionResultContent("c1", "Sunny")])
        };

        var (agent, session) = CreateAgentWithChatClient(responseMessages);

        // Act
        await agent.RunAsync([new(ChatRole.User, "What's the weather?")], session,
            options: new ChatClientAgentRunOptions { StoreFinalFunctionResultContent = false });

        // Assert — chat history should have user + the original mixed-content tool message (kept as-is)
        var stored = GetStoredMessages(agent, session);
        Assert.Equal(2, stored.Count);
        Assert.Equal(2, stored[1].Contents.Count);
        Assert.IsType<TextContent>(stored[1].Contents[0]);
        Assert.IsType<FunctionResultContent>(stored[1].Contents[1]);
    }

    #region Helpers

    private static (ChatClientAgent Agent, ChatClientAgentSession Session) CreateAgentWithChatClient(
        List<ChatMessage>? responseMessages = null,
        Mock<IChatClient>? mockService = null)
    {
        mockService ??= new Mock<IChatClient>();

        if (responseMessages is not null)
        {
            mockService.Setup(
                s => s.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(responseMessages));
        }

        ChatClientAgent agent = new(mockService.Object, options: new()
        {
            ChatOptions = new() { Instructions = "test" },
            UseProvidedChatClientAsIs = true,
        });

        ChatClientAgentSession session = new();

        return (agent, session);
    }

    private static List<ChatMessage> GetStoredMessages(ChatClientAgent agent, ChatClientAgentSession session)
    {
        var provider = agent.ChatHistoryProvider as InMemoryChatHistoryProvider;
        Assert.NotNull(provider);
        return provider.GetMessages(session);
    }

    #endregion
}
