// Copyright (c) Microsoft. All rights reserved.

using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Contains unit tests for the ChatClientAgent.CreateSessionAsync methods.
/// </summary>
public class ChatClientAgent_CreateSessionTests
{
    [Fact]
    public async Task CreateSession_UsesAIContextProvider_IfProvidedAsync()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var mockContextProvider = new Mock<AIContextProvider>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Test instructions" },
            AIContextProvider = mockContextProvider.Object
        });

        // Act
        var session = await agent.CreateSessionAsync();

        // Assert
        Assert.IsType<ChatClientAgentSession>(session);
        var typedSession = (ChatClientAgentSession)session;
        Assert.Same(mockContextProvider.Object, typedSession.AIContextProvider);
    }

    [Fact]
    public async Task CreateSession_UsesChatHistoryProvider_IfProvidedAsync()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var mockChatHistoryProvider = new Mock<ChatHistoryProvider>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Test instructions" },
            ChatHistoryProvider = mockChatHistoryProvider.Object
        });

        // Act
        var session = await agent.CreateSessionAsync();

        // Assert
        Assert.IsType<ChatClientAgentSession>(session);
        var typedSession = (ChatClientAgentSession)session;
        Assert.Same(mockChatHistoryProvider.Object, typedSession.ChatHistoryProvider);
    }

    [Fact]
    public async Task CreateSession_UsesChatHistoryProvider_FromTypedOverloadAsync()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var mockChatHistoryProvider = new Mock<ChatHistoryProvider>();
        var agent = new ChatClientAgent(mockChatClient.Object);

        // Act
        var session = await agent.CreateSessionAsync(mockChatHistoryProvider.Object);

        // Assert
        Assert.IsType<ChatClientAgentSession>(session);
        var typedSession = (ChatClientAgentSession)session;
        Assert.Same(mockChatHistoryProvider.Object, typedSession.ChatHistoryProvider);
    }

    [Fact]
    public async Task CreateSession_UsesConversationId_FromTypedOverloadAsync()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        const string TestConversationId = "test_conversation_id";
        var agent = new ChatClientAgent(mockChatClient.Object);

        // Act
        var session = await agent.CreateSessionAsync(TestConversationId);

        // Assert
        Assert.IsType<ChatClientAgentSession>(session);
        var typedSession = (ChatClientAgentSession)session;
        Assert.Equal(TestConversationId, typedSession.ConversationId);
    }
}
