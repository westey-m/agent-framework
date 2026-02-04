// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Abstractions.UnitTests;

/// <summary>
/// Contains tests for the <see cref="ChatHistoryProvider"/> class.
/// </summary>
public class ChatHistoryProviderTests
{
    #region InvokingAsync Message Stamping Tests

    [Fact]
    public async Task InvokingAsync_StampsMessagesWithSourceTypeAndSourceAsync()
    {
        // Arrange
        var provider = new TestChatHistoryProvider();
        var context = new ChatHistoryProvider.InvokingContext([new ChatMessage(ChatRole.User, "Request")]);

        // Act
        IEnumerable<ChatMessage> messages = await provider.InvokingAsync(context);

        // Assert
        ChatMessage message = messages.Single();
        Assert.NotNull(message.AdditionalProperties);
        Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSourceType.AdditionalPropertiesKey, out object? sourceType));
        Assert.Equal(AgentRequestMessageSourceType.ChatHistory, sourceType);
        Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSource.AdditionalPropertiesKey, out object? source));
        Assert.Equal(typeof(TestChatHistoryProvider).FullName, source);
    }

    [Fact]
    public async Task InvokingAsync_WithCustomSourceName_StampsMessagesWithCustomSourceAsync()
    {
        // Arrange
        const string CustomSourceName = "CustomHistorySource";
        var provider = new TestChatHistoryProviderWithCustomSource(CustomSourceName);
        var context = new ChatHistoryProvider.InvokingContext([new ChatMessage(ChatRole.User, "Request")]);

        // Act
        IEnumerable<ChatMessage> messages = await provider.InvokingAsync(context);

        // Assert
        ChatMessage message = messages.Single();
        Assert.NotNull(message.AdditionalProperties);
        Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSourceType.AdditionalPropertiesKey, out object? sourceType));
        Assert.Equal(AgentRequestMessageSourceType.ChatHistory, sourceType);
        Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSource.AdditionalPropertiesKey, out object? source));
        Assert.Equal(CustomSourceName, source);
    }

    [Fact]
    public async Task InvokingAsync_DoesNotReStampAlreadyStampedMessagesAsync()
    {
        // Arrange
        var provider = new TestChatHistoryProviderWithPreStampedMessages();
        var context = new ChatHistoryProvider.InvokingContext([new ChatMessage(ChatRole.User, "Request")]);

        // Act
        IEnumerable<ChatMessage> messages = await provider.InvokingAsync(context);

        // Assert
        ChatMessage message = messages.Single();
        Assert.NotNull(message.AdditionalProperties);
        Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSourceType.AdditionalPropertiesKey, out object? sourceType));
        Assert.Equal(AgentRequestMessageSourceType.ChatHistory, sourceType);
        Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSource.AdditionalPropertiesKey, out object? source));
        Assert.Equal(typeof(TestChatHistoryProviderWithPreStampedMessages).FullName, source);
    }

    [Fact]
    public async Task InvokingAsync_StampsMultipleMessagesAsync()
    {
        // Arrange
        var provider = new TestChatHistoryProviderWithMultipleMessages();
        var context = new ChatHistoryProvider.InvokingContext([new ChatMessage(ChatRole.User, "Request")]);

        // Act
        IEnumerable<ChatMessage> messages = await provider.InvokingAsync(context);

        // Assert
        List<ChatMessage> messageList = messages.ToList();
        Assert.Equal(3, messageList.Count);

        foreach (ChatMessage message in messageList)
        {
            Assert.NotNull(message.AdditionalProperties);
            Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSourceType.AdditionalPropertiesKey, out object? sourceType));
            Assert.Equal(AgentRequestMessageSourceType.ChatHistory, sourceType);
            Assert.True(message.AdditionalProperties.TryGetValue(AgentRequestMessageSource.AdditionalPropertiesKey, out object? source));
            Assert.Equal(typeof(TestChatHistoryProviderWithMultipleMessages).FullName, source);
        }
    }

    #endregion

    #region GetService Method Tests

    [Fact]
    public void GetService_RequestingExactProviderType_ReturnsProvider()
    {
        var provider = new TestChatHistoryProvider();
        var result = provider.GetService(typeof(TestChatHistoryProvider));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_RequestingBaseProviderType_ReturnsProvider()
    {
        var provider = new TestChatHistoryProvider();
        var result = provider.GetService(typeof(ChatHistoryProvider));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_RequestingUnrelatedType_ReturnsNull()
    {
        var provider = new TestChatHistoryProvider();
        var result = provider.GetService(typeof(string));
        Assert.Null(result);
    }

    [Fact]
    public void GetService_WithServiceKey_ReturnsNull()
    {
        var provider = new TestChatHistoryProvider();
        var result = provider.GetService(typeof(TestChatHistoryProvider), "some-key");
        Assert.Null(result);
    }

    [Fact]
    public void GetService_WithNullServiceType_ThrowsArgumentNullException()
    {
        var provider = new TestChatHistoryProvider();
        Assert.Throws<ArgumentNullException>(() => provider.GetService(null!));
    }

    [Fact]
    public void GetService_Generic_ReturnsCorrectType()
    {
        var provider = new TestChatHistoryProvider();
        var result = provider.GetService<TestChatHistoryProvider>();
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_Generic_ReturnsNullForUnrelatedType()
    {
        var provider = new TestChatHistoryProvider();
        var result = provider.GetService<string>();
        Assert.Null(result);
    }

    #endregion

    private sealed class TestChatHistoryProvider : ChatHistoryProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new([new ChatMessage(ChatRole.User, "Test Message")]);

        protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
            => default;

        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
            => default;
    }

    private sealed class TestChatHistoryProviderWithCustomSource : ChatHistoryProvider
    {
        public TestChatHistoryProviderWithCustomSource(string sourceName) : base(sourceName)
        {
        }

        protected override ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new([new ChatMessage(ChatRole.User, "Test Message")]);

        protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
            => default;

        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
            => default;
    }

    private sealed class TestChatHistoryProviderWithPreStampedMessages : ChatHistoryProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
        {
            var message = new ChatMessage(ChatRole.User, "Pre-stamped Message");
            message.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AgentRequestMessageSourceType.AdditionalPropertiesKey] = AgentRequestMessageSourceType.ChatHistory,
                [AgentRequestMessageSource.AdditionalPropertiesKey] = this.GetType().FullName!
            };
            return new([message]);
        }

        protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
            => default;

        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
            => default;
    }

    private sealed class TestChatHistoryProviderWithMultipleMessages : ChatHistoryProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new([
                new ChatMessage(ChatRole.User, "Message 1"),
                new ChatMessage(ChatRole.Assistant, "Message 2"),
                new ChatMessage(ChatRole.User, "Message 3")
            ]);

        protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
            => default;

        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
            => default;
    }
}
