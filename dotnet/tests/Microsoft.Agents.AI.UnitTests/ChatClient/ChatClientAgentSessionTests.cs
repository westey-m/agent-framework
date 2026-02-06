// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Moq;

#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Microsoft.Agents.AI.UnitTests;

public class ChatClientAgentSessionTests
{
    #region Constructor and Property Tests

    [Fact]
    public void ConstructorSetsDefaults()
    {
        // Arrange & Act
        var session = new ChatClientAgentSession();

        // Assert
        Assert.Null(session.ConversationId);
        Assert.Null(session.ChatHistoryProvider);
    }

    [Fact]
    public void SetConversationIdRoundtrips()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        const string ConversationId = "test-session-id";

        // Act
        session.ConversationId = ConversationId;

        // Assert
        Assert.Equal(ConversationId, session.ConversationId);
        Assert.Null(session.ChatHistoryProvider);
    }

    [Fact]
    public void SetChatHistoryProviderRoundtrips()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        var chatHistoryProvider = new InMemoryChatHistoryProvider();

        // Act
        session.ChatHistoryProvider = chatHistoryProvider;

        // Assert
        Assert.Same(chatHistoryProvider, session.ChatHistoryProvider);
        Assert.Null(session.ConversationId);
    }

    [Fact]
    public void SetConversationIdThrowsWhenChatHistoryProviderIsSet()
    {
        // Arrange
        var session = new ChatClientAgentSession
        {
            ChatHistoryProvider = new InMemoryChatHistoryProvider()
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => session.ConversationId = "new-session-id");
        Assert.Equal("Only the ConversationId or ChatHistoryProvider may be set, but not both and switching from one to another is not supported.", exception.Message);
        Assert.NotNull(session.ChatHistoryProvider);
    }

    [Fact]
    public void SetChatHistoryProviderThrowsWhenConversationIdIsSet()
    {
        // Arrange
        var session = new ChatClientAgentSession
        {
            ConversationId = "existing-session-id"
        };
        var provider = new InMemoryChatHistoryProvider();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => session.ChatHistoryProvider = provider);
        Assert.Equal("Only the ConversationId or ChatHistoryProvider may be set, but not both and switching from one to another is not supported.", exception.Message);
        Assert.NotNull(session.ConversationId);
    }

    #endregion Constructor and Property Tests

    #region Deserialize Tests

    [Fact]
    public void VerifyDeserializeWithMessages()
    {
        // Arrange
        var json = JsonSerializer.Deserialize("""
            {
                "stateBag": {
                    "InMemoryChatHistoryProvider.State": {
                        "jsonValue": { "messages": [{"authorName": "testAuthor"}] }
                    }
                }
            }
            """, TestJsonSerializerContext.Default.JsonElement);

        // Act.
        var session = ChatClientAgentSession.Deserialize(json);

        // Assert
        Assert.Null(session.ConversationId);

        var chatHistoryProvider = session.ChatHistoryProvider as InMemoryChatHistoryProvider;
        Assert.NotNull(chatHistoryProvider);
        var messages = chatHistoryProvider.GetMessages(session);
        Assert.Single(messages);
        Assert.Equal("testAuthor", messages[0].AuthorName);
    }

    [Fact]
    public void VerifyDeserializeWithId()
    {
        // Arrange
        var json = JsonSerializer.Deserialize("""
            {
                "conversationId": "TestConvId"
            }
            """, TestJsonSerializerContext.Default.JsonElement);

        // Act
        var session = ChatClientAgentSession.Deserialize(json);

        // Assert
        Assert.Equal("TestConvId", session.ConversationId);
        Assert.Null(session.ChatHistoryProvider);
    }

    [Fact]
    public void VerifyDeserializeWithAIContextProvider()
    {
        // Arrange
        var json = JsonSerializer.Deserialize("""
            {
                "conversationId": "TestConvId",
                "aiContextProviderState": ["CP1"]
            }
            """, TestJsonSerializerContext.Default.JsonElement);
        Mock<AIContextProvider> mockProvider = new();

        // Act
        var session = ChatClientAgentSession.Deserialize(json, aiContextProvider: mockProvider.Object);

        // Assert
        Assert.Null(session.ChatHistoryProvider);
        Assert.Same(session.AIContextProvider, mockProvider.Object);
    }

    [Fact]
    public void VerifyDeserializeWithStateBag()
    {
        // Arrange
        var json = JsonSerializer.Deserialize("""
            {
                "conversationId": "TestConvId",
                "stateBag": {
                    "dog": {
                        "jsonValue": {
                            "name": "Fido"
                        }
                    }
                }
            }
            """, TestJsonSerializerContext.Default.JsonElement);
        Mock<AIContextProvider> mockProvider = new();

        // Act
        var session = ChatClientAgentSession.Deserialize(json, aiContextProvider: mockProvider.Object);

        // Assert
        var dog = session.StateBag.GetValue<Animal>("dog", TestJsonSerializerContext.Default.Options);
        Assert.NotNull(dog);
        Assert.Equal("Fido", dog.Name);
    }

    [Fact]
    public void DeserializeWithInvalidJsonThrows()
    {
        // Arrange
        var invalidJson = JsonSerializer.Deserialize("[42]", TestJsonSerializerContext.Default.JsonElement);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ChatClientAgentSession.Deserialize(invalidJson));
    }

    #endregion Deserialize Tests

    #region Serialize Tests

    /// <summary>
    /// Verify session serialization to JSON when the session has an id.
    /// </summary>
    [Fact]
    public void VerifySessionSerializationWithId()
    {
        // Arrange
        var session = new ChatClientAgentSession { ConversationId = "TestConvId" };

        // Act
        var json = session.Serialize();

        // Assert
        Assert.Equal(JsonValueKind.Object, json.ValueKind);

        Assert.True(json.TryGetProperty("conversationId", out var idProperty));
        Assert.Equal("TestConvId", idProperty.GetString());

        Assert.False(json.TryGetProperty("chatHistoryProviderState", out _));
    }

    /// <summary>
    /// Verify session serialization to JSON when the session has messages.
    /// </summary>
    [Fact]
    public void VerifySessionSerializationWithMessages()
    {
        // Arrange
        var provider = new InMemoryChatHistoryProvider();
        var session = new ChatClientAgentSession { ChatHistoryProvider = provider };
        provider.SetMessages(session, [new(ChatRole.User, "TestContent") { AuthorName = "TestAuthor" }]);

        // Act
        var json = session.Serialize();

        // Assert
        Assert.Equal(JsonValueKind.Object, json.ValueKind);

        Assert.False(json.TryGetProperty("conversationId", out _));

        // Messages should be stored in the stateBag
        Assert.True(json.TryGetProperty("stateBag", out var stateBagProperty));
        Assert.Equal(JsonValueKind.Object, stateBagProperty.ValueKind);
        Assert.True(stateBagProperty.TryGetProperty("InMemoryChatHistoryProvider.State", out var providerStateProperty));
        Assert.Equal(JsonValueKind.Object, providerStateProperty.ValueKind);
        Assert.True(providerStateProperty.TryGetProperty("jsonValue", out var jsonValueProperty));
        Assert.Equal(JsonValueKind.Object, jsonValueProperty.ValueKind);
        Assert.True(jsonValueProperty.TryGetProperty("messages", out var messagesProperty));
        Assert.Equal(JsonValueKind.Array, messagesProperty.ValueKind);
        Assert.Single(messagesProperty.EnumerateArray());

        var message = messagesProperty.EnumerateArray().First();
        Assert.Equal("TestAuthor", message.GetProperty("authorName").GetString());
        Assert.True(message.TryGetProperty("contents", out var contentsProperty));
        Assert.Equal(JsonValueKind.Array, contentsProperty.ValueKind);
        Assert.Single(contentsProperty.EnumerateArray());

        var textContent = contentsProperty.EnumerateArray().First();
        Assert.Equal("TestContent", textContent.GetProperty("text").GetString());
    }

    [Fact]
    public void VerifySessionSerializationWithWithStateBag()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        session.StateBag.SetValue("dog", new Animal { Name = "Fido" }, TestJsonSerializerContext.Default.Options);

        // Act
        var json = session.Serialize();

        // Assert
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.True(json.TryGetProperty("stateBag", out var stateBagProperty));
        Assert.Equal(JsonValueKind.Object, stateBagProperty.ValueKind);
        Assert.True(stateBagProperty.TryGetProperty("dog", out var dogProperty));
        Assert.Equal(JsonValueKind.Object, dogProperty.ValueKind);
        Assert.True(dogProperty.TryGetProperty("jsonValue", out var dogJsonValueProperty));
        Assert.True(dogJsonValueProperty.TryGetProperty("name", out var nameProperty));
        Assert.Equal("Fido", nameProperty.GetString());
    }

    /// <summary>
    /// Verify session serialization to JSON with custom options.
    /// </summary>
    [Fact]
    public void VerifySessionSerializationWithCustomOptions()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        options.TypeInfoResolverChain.Add(AgentAbstractionsJsonUtilities.DefaultOptions.TypeInfoResolver!);

        var chatHistoryProviderMock = new Mock<ChatHistoryProvider>();
        session.ChatHistoryProvider = chatHistoryProviderMock.Object;

        // Act
        var json = session.Serialize(options);

        // Assert
        Assert.Equal(JsonValueKind.Object, json.ValueKind);

        Assert.False(json.TryGetProperty("conversationId", out var _));
    }

    #endregion Serialize Tests

    #region StateBag Roundtrip Tests

    [Fact]
    public void VerifyStateBagRoundtrips()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        session.StateBag.SetValue("dog", new Animal { Name = "Fido" }, TestJsonSerializerContext.Default.Options);

        // Act
        var serializedSession = session.Serialize();
        var deserializedSession = ChatClientAgentSession.Deserialize(serializedSession);

        // Assert
        var dog = deserializedSession.StateBag.GetValue<Animal>("dog", TestJsonSerializerContext.Default.Options);
        Assert.NotNull(dog);
        Assert.Equal("Fido", dog.Name);
    }

    #endregion

    #region GetService Tests

    [Fact]
    public void GetService_RequestingAIContextProvider_ReturnsAIContextProvider()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        var mockProvider = new Mock<AIContextProvider>();
        mockProvider
            .Setup(m => m.GetService(It.Is<Type>(x => x == typeof(AIContextProvider)), null))
            .Returns(mockProvider.Object);
        session.AIContextProvider = mockProvider.Object;

        // Act
        var result = session.GetService(typeof(AIContextProvider));

        // Assert
        Assert.NotNull(result);
        Assert.Same(mockProvider.Object, result);
    }

    [Fact]
    public void GetService_RequestingChatHistoryProvider_ReturnsChatHistoryProvider()
    {
        // Arrange
        var session = new ChatClientAgentSession();
        var chatHistoryProvider = new InMemoryChatHistoryProvider();
        session.ChatHistoryProvider = chatHistoryProvider;

        // Act
        var result = session.GetService(typeof(ChatHistoryProvider));

        // Assert
        Assert.NotNull(result);
        Assert.Same(chatHistoryProvider, result);
    }

    #endregion

    internal sealed class Animal
    {
        public string Name { get; set; } = string.Empty;
    }
}
