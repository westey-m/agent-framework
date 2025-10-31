// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Moq;

namespace Microsoft.Agents.AI.VectorDataMemory.UnitTests;

/// <summary>
/// Contains unit tests for the <see cref="ChatHistoryMemoryProvider"/> class.
/// </summary>
public class ChatHistoryMemoryProviderTests
{
    private readonly Mock<ILogger<ChatHistoryMemoryProvider>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;

    private readonly Mock<VectorStore> _vectorStoreMock;
    private readonly Mock<VectorStoreCollection<Guid, ChatHistoryMemoryProvider.ChatHistoryItem>> _vectorStoreCollectionMock;
    private const string TestCollectionName = "testcollection";

    public ChatHistoryMemoryProviderTests()
    {
        this._loggerMock = new();
        this._loggerFactoryMock = new();
        this._loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(this._loggerMock.Object);
        this._loggerFactoryMock
            .Setup(f => f.CreateLogger(typeof(ChatHistoryMemoryProvider).FullName!))
            .Returns(this._loggerMock.Object);

        this._vectorStoreCollectionMock = new Mock<VectorStoreCollection<Guid, ChatHistoryMemoryProvider.ChatHistoryItem>>(MockBehavior.Strict);
        this._vectorStoreMock = new Mock<VectorStore>(MockBehavior.Strict);

        this._vectorStoreCollectionMock
            .Setup(c => c.EnsureCollectionExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        this._vectorStoreMock
            .Setup(vs => vs.GetCollection<Guid, ChatHistoryMemoryProvider.ChatHistoryItem>(
                It.IsAny<string>(),
                It.IsAny<VectorStoreCollectionDefinition>()))
            .Returns(this._vectorStoreCollectionMock.Object);
    }

    [Fact]
    public void Constructor_Throws_ForNullVectorStore()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChatHistoryMemoryProvider(null!, "testcollection", 1));
    }

    [Fact]
    public void Constructor_Throws_ForInvalidVectorDimensions()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, "testcollection", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, "testcollection", -5));
    }

    #region InvokedAsync Tests

    [Fact]
    public async Task InvokedAsync_UpsertsMessages_ToCollectionAsync()
    {
        // Arrange
        var stored = new List<ChatHistoryMemoryProvider.ChatHistoryItem>();

        this._vectorStoreCollectionMock
            .Setup(c => c.UpsertAsync(It.IsAny<IEnumerable<ChatHistoryMemoryProvider.ChatHistoryItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatHistoryMemoryProvider.ChatHistoryItem>, CancellationToken>((items, ct) =>
            {
                if (items != null)
                {
                    stored.AddRange(items);
                }
            })
            .Returns(Task.CompletedTask);

        var storeScope = new ChatHistoryMemoryProviderScope
        {
            ApplicationId = "app1",
            AgentId = "agent1",
            ThreadId = "thread1",
            UserId = "user1"
        };

        var provider = new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, TestCollectionName, 1, storeScope);

        var requestMsgWithValues = new ChatMessage(ChatRole.User, "request text") { MessageId = "req-1", AuthorName = "user1", CreatedAt = new DateTimeOffset(new DateTime(2000, 1, 1), TimeSpan.Zero) };
        var requestMsgWithNulls = new ChatMessage(ChatRole.User, "request text nulls");
        var responseMsg = new ChatMessage(ChatRole.Assistant, "response text") { MessageId = "resp-1", AuthorName = "assistant" };

        var invokedContext = new AIContextProvider.InvokedContext([requestMsgWithValues, requestMsgWithNulls], aiContextProviderMessages: null)
        {
            ResponseMessages = [responseMsg]
        };

        // Act
        await provider.InvokedAsync(invokedContext, CancellationToken.None);

        // Assert
        this._vectorStoreCollectionMock.Verify(
            m => m.EnsureCollectionExistsAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal(3, stored.Count);

        Assert.Equal("req-1", stored[0].MessageId);
        Assert.Equal("request text", stored[0].Content);
        Assert.Equal("user1", stored[0].AuthorName);
        Assert.Equal(ChatRole.User.ToString(), stored[0].Role);
        Assert.Equal("2000-01-01T00:00:00.0000000+00:00", stored[0].CreatedAt);
        Assert.Equal("app1", stored[0].ApplicationId);
        Assert.Equal("agent1", stored[0].AgentId);
        Assert.Equal("thread1", stored[0].ThreadId);
        Assert.Equal("user1", stored[0].UserId);

        Assert.Null(stored[1].MessageId);
        Assert.Equal("request text nulls", stored[1].Content);
        Assert.Null(stored[1].AuthorName);
        Assert.Equal(ChatRole.User.ToString(), stored[1].Role);
        Assert.Equal("app1", stored[1].ApplicationId);
        Assert.Equal("agent1", stored[1].AgentId);
        Assert.Equal("thread1", stored[1].ThreadId);
        Assert.Equal("user1", stored[1].UserId);

        Assert.Equal("resp-1", stored[2].MessageId);
        Assert.Equal("response text", stored[2].Content);
        Assert.Equal("assistant", stored[2].AuthorName);
        Assert.Equal(ChatRole.Assistant.ToString(), stored[2].Role);
        Assert.Equal("app1", stored[2].ApplicationId);
        Assert.Equal("agent1", stored[2].AgentId);
        Assert.Equal("thread1", stored[2].ThreadId);
        Assert.Equal("user1", stored[2].UserId);
    }

    [Fact]
    public async Task InvokedAsync_DoesNotUpsertMessages_WhenInvokeFailedAsync()
    {
        // Arrange
        this._vectorStoreCollectionMock
            .Setup(c => c.UpsertAsync(It.IsAny<IEnumerable<ChatHistoryMemoryProvider.ChatHistoryItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var provider = new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, TestCollectionName, 1);
        var requestMsg = new ChatMessage(ChatRole.User, "request text") { MessageId = "req-1" };
        var invokedContext = new AIContextProvider.InvokedContext([requestMsg], aiContextProviderMessages: null)
        {
            InvokeException = new InvalidOperationException("Invoke failed")
        };

        // Act
        await provider.InvokedAsync(invokedContext, CancellationToken.None);

        // Assert
        this._vectorStoreCollectionMock.Verify(
            c => c.UpsertAsync(It.IsAny<IEnumerable<ChatHistoryMemoryProvider.ChatHistoryItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokedAsync_DoesNotThrow_WhenUpsertThrowsAsync()
    {
        // Arrange
        this._vectorStoreCollectionMock
            .Setup(c => c.UpsertAsync(It.IsAny<IEnumerable<ChatHistoryMemoryProvider.ChatHistoryItem>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Upsert failed"));

        var provider = new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, TestCollectionName, 1, loggerFactory: this._loggerFactoryMock.Object);
        var requestMsg = new ChatMessage(ChatRole.User, "request text") { MessageId = "req-1" };
        var invokedContext = new AIContextProvider.InvokedContext([requestMsg], aiContextProviderMessages: null);

        // Act
        await provider.InvokedAsync(invokedContext, CancellationToken.None);

        // Assert
        this._loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ChatHistoryMemoryProvider: Failed to add messages to chat history vector store due to error")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region InvokingAsync Tests

    [Fact]
    public async Task InvokedAsync_SearchesVectorStoreAsync()
    {
        // Arrange
        var providerOptions = new ChatHistoryMemoryProviderOptions
        {
            SearchTime = ChatHistoryMemoryProviderOptions.SearchBehavior.BeforeAIInvoke,
            MaxResults = 2,
            ContextPrompt = "Here is the relevant chat history:\n"
        };

        var storedItems = new List<VectorSearchResult<ChatHistoryMemoryProvider.ChatHistoryItem>>
        {
            new(
                new ChatHistoryMemoryProvider.ChatHistoryItem
                {
                    MessageId = "msg-1",
                    Content = "First stored message",
                    Role = ChatRole.User.ToString(),
                    CreatedAt = "2023-01-01T00:00:00.0000000+00:00"
                },
                0.9f),
            new(
                new ChatHistoryMemoryProvider.ChatHistoryItem
                {
                    MessageId = "msg-2",
                    Content = "Second stored message",
                    Role = ChatRole.User.ToString(),
                    CreatedAt = "2023-01-02T00:00:00.0000000+00:00"
                },
                0.8f)
        };

        this._vectorStoreCollectionMock
            .Setup(c => c.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<VectorSearchOptions<ChatHistoryMemoryProvider.ChatHistoryItem>>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerableAsync(storedItems));

        var provider = new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, TestCollectionName, 1, options: providerOptions);

        var requestMsg = new ChatMessage(ChatRole.User, "requesting relevant history");
        var invokingContext = new AIContextProvider.InvokingContext([requestMsg]);

        // Act
        await provider.InvokingAsync(invokingContext, CancellationToken.None);

        // Assert
        this._vectorStoreCollectionMock.Verify(
            c => c.SearchAsync(
                It.Is<string>(s => s == "requesting relevant history"),
                2,
                It.Is<VectorSearchOptions<ChatHistoryMemoryProvider.ChatHistoryItem>>(x => x.Filter == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokedAsync_CreatesFilter_WhenSearchScopeProvidedAsync()
    {
        // Arrange
        var providerOptions = new ChatHistoryMemoryProviderOptions
        {
            SearchTime = ChatHistoryMemoryProviderOptions.SearchBehavior.BeforeAIInvoke,
            MaxResults = 2,
            ContextPrompt = "Here is the relevant chat history:\n"
        };

        var searchScope = new ChatHistoryMemoryProviderScope
        {
            ApplicationId = "app1",
            AgentId = "agent1",
            ThreadId = "thread1",
            UserId = "user1"
        };

        this._vectorStoreCollectionMock
            .Setup(c => c.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<VectorSearchOptions<ChatHistoryMemoryProvider.ChatHistoryItem>>(),
                It.IsAny<CancellationToken>()))
            .Callback((string query, int maxResults, VectorSearchOptions<ChatHistoryMemoryProvider.ChatHistoryItem> options, CancellationToken ct) =>
            {
                // Verify that the filter was created correctly
                const string ExpectedFilter = "x => ((((x.ApplicationId == value(Microsoft.Agents.AI.VectorDataMemory.ChatHistoryMemoryProvider+<>c__DisplayClass20_0).applicationId) AndAlso (x.AgentId == value(Microsoft.Agents.AI.VectorDataMemory.ChatHistoryMemoryProvider+<>c__DisplayClass20_0).agentId)) AndAlso (x.UserId == value(Microsoft.Agents.AI.VectorDataMemory.ChatHistoryMemoryProvider+<>c__DisplayClass20_0).userId)) AndAlso (x.ThreadId == value(Microsoft.Agents.AI.VectorDataMemory.ChatHistoryMemoryProvider+<>c__DisplayClass20_0).threadId))";
                Assert.Equal(ExpectedFilter, options.Filter!.ToString());
            })
            .Returns(ToAsyncEnumerableAsync(new List<VectorSearchResult<ChatHistoryMemoryProvider.ChatHistoryItem>>()));

        var provider = new ChatHistoryMemoryProvider(this._vectorStoreMock.Object, TestCollectionName, 1, options: providerOptions, searchScope: searchScope);

        var requestMsg = new ChatMessage(ChatRole.User, "requesting relevant history");
        var invokingContext = new AIContextProvider.InvokingContext([requestMsg]);

        // Act
        await provider.InvokingAsync(invokingContext, CancellationToken.None);

        // Assert
        this._vectorStoreCollectionMock.Verify(
            c => c.SearchAsync(
                It.Is<string>(s => s == "requesting relevant history"),
                2,
                It.IsAny<VectorSearchOptions<ChatHistoryMemoryProvider.ChatHistoryItem>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    private static async IAsyncEnumerable<T> ToAsyncEnumerableAsync<T>(IEnumerable<T> values)
    {
        await Task.Yield();
        foreach (var update in values)
        {
            yield return update;
        }
    }
}
