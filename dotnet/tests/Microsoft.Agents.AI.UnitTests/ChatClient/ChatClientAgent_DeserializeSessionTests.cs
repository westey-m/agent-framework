// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Moq;

namespace Microsoft.Agents.AI.UnitTests;

/// <summary>
/// Contains unit tests for the ChatClientAgent.DeserializeSession methods.
/// </summary>
public class ChatClientAgent_DeserializeSessionTests
{
    [Fact]
    public async Task DeserializeSession_UsesAIContextProvider_IfProvidedAsync()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var mockContextProvider = new Mock<AIContextProvider>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Test instructions" },
            AIContextProvider = mockContextProvider.Object
        });

        var json = JsonSerializer.Deserialize("""
            {
                "aiContextProviderState": ["CP1"]
            }
            """, TestJsonSerializerContext.Default.JsonElement);

        // Act
        var session = await agent.DeserializeSessionAsync(json);

        // Assert
        Assert.IsType<ChatClientAgentSession>(session);
        var typedSession = (ChatClientAgentSession)session;
        Assert.Same(mockContextProvider.Object, typedSession.AIContextProvider);
    }

    [Fact]
    public async Task DeserializeSession_UsesChatHistoryProvider_IfProvidedAsync()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();
        var mockChatHistoryProvider = new Mock<ChatHistoryProvider>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "Test instructions" },
            ChatHistoryProvider = mockChatHistoryProvider.Object
        });

        var json = JsonSerializer.Deserialize("""
            {
                "chatHistoryProviderState": { }
            }
            """, TestJsonSerializerContext.Default.JsonElement);

        // Act
        var session = await agent.DeserializeSessionAsync(json);

        // Assert
        Assert.IsType<ChatClientAgentSession>(session);
        var typedSession = (ChatClientAgentSession)session;
        Assert.Same(mockChatHistoryProvider.Object, typedSession.ChatHistoryProvider);
    }
}
