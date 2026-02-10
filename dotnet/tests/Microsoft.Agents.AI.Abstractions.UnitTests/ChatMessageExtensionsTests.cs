// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Abstractions.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChatMessageExtensions"/> class.
/// </summary>
public sealed class ChatMessageExtensionsTests
{
    #region GetAgentRequestMessageSourceType Tests

    [Fact]
    public void GetAgentRequestMessageSourceType_WithNoAdditionalProperties_ReturnsExternal()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello");

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.External, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithNullAdditionalProperties_ReturnsExternal()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = null
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.External, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithEmptyAdditionalProperties_ReturnsExternal()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary()
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.External, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithExternalSourceType_ReturnsExternal()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.External, "TestSourceId") }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.External, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithAIContextProviderSourceType_ReturnsAIContextProvider()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.AIContextProvider, "TestSourceId") }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.AIContextProvider, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithChatHistorySourceType_ReturnsChatHistory()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.ChatHistory, "TestSourceId") }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.ChatHistory, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithCustomSourceType_ReturnsCustomSourceType()
    {
        // Arrange
        AgentRequestMessageSourceType customSourceType = new("CustomSourceType");
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(customSourceType, "TestSourceId") }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(customSourceType, result);
        Assert.Equal("CustomSourceType", result.Value);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithWrongAttributionType_ReturnsExternal()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, "NotAnAgentRequestMessageSourceAttribution" }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.External, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithNullAttributionValue_ReturnsExternal()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, null! }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.External, result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceType_WithMultipleProperties_ReturnsCorrectSourceType()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "OtherProperty", "SomeValue" },
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.ChatHistory, "TestSourceId") },
                { "AnotherProperty", 123 }
            }
        };

        // Act
        AgentRequestMessageSourceType result = message.GetAgentRequestMessageSourceType();

        // Assert
        Assert.Equal(AgentRequestMessageSourceType.ChatHistory, result);
    }

    #endregion

    #region GetAgentRequestMessageSourceId Tests

    [Fact]
    public void GetAgentRequestMessageSourceId_WithNoAdditionalProperties_ReturnsNull()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello");

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithNullAdditionalProperties_ReturnsNull()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = null
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithEmptyAdditionalProperties_ReturnsNull()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary()
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithAttribution_ReturnsSourceId()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.AIContextProvider, "MyProvider.FullName") }
            }
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Equal("MyProvider.FullName", result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithDifferentSourceIds_ReturnsCorrectSourceId()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.ChatHistory, "CustomHistorySourceId") }
            }
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Equal("CustomHistorySourceId", result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithWrongAttributionType_ReturnsNull()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, "NotAnAgentRequestMessageSourceAttribution" }
            }
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithNullAttributionValue_ReturnsNull()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, null! }
            }
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAgentRequestMessageSourceId_WithMultipleProperties_ReturnsCorrectSourceId()
    {
        // Arrange
        ChatMessage message = new(ChatRole.User, "Hello")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                { "OtherProperty", "SomeValue" },
                { AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.External, "ExpectedSourceId") },
                { "AnotherProperty", 123 }
            }
        };

        // Act
        string? result = message.GetAgentRequestMessageSourceId();

        // Assert
        Assert.Equal("ExpectedSourceId", result);
    }

    #endregion
}
