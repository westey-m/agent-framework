// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.VectorDataMemory;

/// <summary>
/// A context provider that stores all chat history in a vector store and is able to
/// retrieve related chat history later to augment the current convesation.
/// </summary>
/// <remarks>
/// <para>
/// This provider stores chat messages in a vector store and retrieves relevant previous messages
/// to provide as context during agent invocations. It uses the VectorStore and VectorStoreCollection
/// abstractions to work with any compatible vector store implementation.
/// </para>
/// <para>
/// Messages are stored during the <see cref="InvokedAsync"/> method and retrieved during the
/// <see cref="InvokingAsync"/> method using semantic similarity search.
/// </para>
/// <para>
/// Behavior is configurable through <see cref="ChatHistoryMemoryProviderOptions"/>. When
/// <see cref="ChatHistoryMemoryProviderOptions.SearchBehavior.OnDemandFunctionCalling"/> is selected the provider
/// exposes a function tool that the model can invoke to retrieve relevant memories on demand instead of
/// injecting them automatically on each invocation.
/// </para>
/// </remarks>
[RequiresDynamicCode("This API is not compatible with NativeAOT. For dynamic mapping via Dictionary<string, object?>, use GetCollectionDynamic() instead.")]
[RequiresUnreferencedCode("This API is not compatible with trimming. For dynamic mapping via Dictionary<string, object?>, use GetCollectionDynamic() instead.")]
public sealed class ChatHistoryMemoryProvider : AIContextProvider, IDisposable
{
    private const string DefaultContextPrompt = "## Memories\nConsider the following memories when answering user questions:";
    private const int DefaultMaxResults = 3;
    private const string DefaultFunctionToolName = "Search";
    private const string DefaultFunctionToolDescription = "Allows searching for related previous chat history to help answer the user question.";

    private readonly VectorStore _vectorStore;
    private readonly VectorStoreCollection<Guid, ChatHistoryItem> _collection;
    private readonly int _maxResults;
    private readonly string _contextPrompt;
    private readonly ChatHistoryMemoryProviderOptions.SearchBehavior _searchTime;
    private readonly AITool[] _tools;
    private readonly ILogger<ChatHistoryMemoryProvider>? _logger;

    private readonly ChatHistoryMemoryProviderScope? _storageScope;
    private readonly ChatHistoryMemoryProviderScope? _searchScope;

    private bool _collectionInitialized;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHistoryMemoryProvider"/> class.
    /// </summary>
    /// <param name="vectorStore">The vector store to use for storing and retrieving chat history.</param>
    /// <param name="collectionName">The name of the collection for storing chat history in the vector store.</param>
    /// <param name="vectorDimensions">The number of dimensions to use for the chat history vector store embeddings.</param>
    /// <param name="storageScope">Optional values to scope the chat history storage with.</param>
    /// <param name="searchScope">Optional values to scope the chat history search with. Where values are null, no filtering is done using those values. Defaults to <paramref name="storageScope"/> if not provided.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="vectorStore"/> is <see langword="null"/>.</exception>
    public ChatHistoryMemoryProvider(
        VectorStore vectorStore,
        string collectionName,
        int vectorDimensions,
        ChatHistoryMemoryProviderScope? storageScope = null,
        ChatHistoryMemoryProviderScope? searchScope = null,
        ChatHistoryMemoryProviderOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        this._vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        options ??= new ChatHistoryMemoryProviderOptions();
        this._maxResults = options.MaxResults.HasValue ? Throw.IfLessThanOrEqual(options.MaxResults.Value, 0) : DefaultMaxResults;
        this._contextPrompt = options.ContextPrompt ?? DefaultContextPrompt;
        this._searchTime = options.SearchTime;
        this._logger = loggerFactory?.CreateLogger<ChatHistoryMemoryProvider>();

        this._storageScope = storageScope is null ? null : new ChatHistoryMemoryProviderScope(storageScope);
        this._searchScope = searchScope is null ? this._storageScope : new ChatHistoryMemoryProviderScope(searchScope);

        // Create on-demand search tool (only used when behavior is OnDemandFunctionCalling)
        this._tools =
        [
            AIFunctionFactory.Create(
                (Func<string, CancellationToken, Task<string>>)this.SearchTextAsync,
                name: options.FunctionToolName ?? DefaultFunctionToolName,
                description: options.FunctionToolDescription ?? DefaultFunctionToolDescription)
        ];

        // Create a definition so that we can use the dimensions provided at runtime.
        var definition = new VectorStoreCollectionDefinition
        {
            Properties = new List<VectorStoreProperty>
                {
                    new VectorStoreKeyProperty("Key", typeof(Guid)),
                    new VectorStoreDataProperty("Role", typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty("MessageId", typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty("AuthorName", typeof(string)),
                    new VectorStoreDataProperty("ApplicationId", typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty("AgentId", typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty("UserId", typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty("ThreadId", typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
                    new VectorStoreDataProperty("CreatedAt", typeof(string)) { IsIndexed = true },
                    new VectorStoreVectorProperty("ContentEmbedding", typeof(string), Throw.IfLessThan(vectorDimensions, 1))
                }
        };

        this._collection = this._vectorStore.GetCollection<Guid, ChatHistoryItem>(
            Throw.IfNullOrWhitespace(collectionName),
            definition);
    }

    /// <inheritdoc />
    public override async ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(context);

        if (this._searchTime == ChatHistoryMemoryProviderOptions.SearchBehavior.OnDemandFunctionCalling)
        {
            // Expose search tool for on-demand invocation by the model
            return new AIContext { Tools = this._tools };
        }

        try
        {
            // Ensure the collection is initialized
            var collection = await this.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

            // Get the text from the current request messages
            var requestText = string.Join("\n", context.RequestMessages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => m.Text));

            if (string.IsNullOrWhiteSpace(requestText))
            {
                return new AIContext();
            }

            // Search for relevant chat history
            var contextText = await this.SearchTextAsync(requestText, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(contextText))
            {
                return new AIContext();
            }

            return new AIContext
            {
                Messages = [new ChatMessage(ChatRole.User, contextText)]
            };
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "ChatHistoryMemoryProvider: Failed to search for chat history due to error");
            return new AIContext();
        }
    }

    /// <inheritdoc />
    public override async ValueTask InvokedAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(context);

        // Only store if invocation was successful
        if (context.InvokeException != null)
        {
            return;
        }

        try
        {
            // Ensure the collection is initialized
            var collection = await this.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

            List<ChatHistoryItem> itemsToStore = context.RequestMessages
                .Concat(context.ResponseMessages ?? [])
                .Select(message => new ChatHistoryItem
                {
                    Key = Guid.NewGuid(),
                    Role = message.Role.ToString(),
                    MessageId = message.MessageId,
                    AuthorName = message.AuthorName,
                    ApplicationId = this._storageScope?.ApplicationId,
                    AgentId = this._storageScope?.AgentId,
                    UserId = this._storageScope?.UserId,
                    ThreadId = this._storageScope?.ThreadId,
                    Content = message.Text,
                    CreatedAt = message.CreatedAt?.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"),
                })
                .ToList();

            if (itemsToStore.Count > 0)
            {
                await collection.UpsertAsync(itemsToStore, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            this._logger?.LogError(ex, "ChatHistoryMemoryProvider: Failed to add messages to chat history vector store due to error");
        }
    }

    /// <summary>
    /// Function callable by the AI model (when enabled) to perform an ad-hoc chat history search.
    /// </summary>
    /// <param name="userQuestion">The query text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted search results (may be empty).</returns>
    internal async Task<string> SearchTextAsync(string userQuestion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return string.Empty;
        }

        var results = await this.SearchChatHistoryAsync(userQuestion, this._maxResults, cancellationToken).ConfigureAwait(false);
        if (!results.Any())
        {
            return string.Empty;
        }

        // Format the results as a single context message
        var outputResultsText = string.Join("\n", results.Select(x => x.Content).Where(c => !string.IsNullOrWhiteSpace(c)));
        if (string.IsNullOrWhiteSpace(outputResultsText))
        {
            return string.Empty;
        }

        var formatted = $"{this._contextPrompt}\n{outputResultsText}";

        this._logger?.LogTrace("ChatHistoryMemoryProvider: Search Results\nInput:{Input}\nOutput:{MessageText}", userQuestion, formatted);
        return formatted;
    }

    /// <summary>
    /// Searches for relevant chat history items based on the provided query text.
    /// </summary>
    /// <param name="queryText">The text to search for.</param>
    /// <param name="top">The maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of relevant chat history items.</returns>
    private async Task<IEnumerable<ChatHistoryItem>> SearchChatHistoryAsync(
        string queryText,
        int top,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Enumerable.Empty<ChatHistoryItem>();
        }

        var collection = await this.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        string? applicationId = this._searchScope?.ApplicationId;
        string? agentId = this._searchScope?.AgentId;
        string? userId = this._searchScope?.UserId;
        string? threadId = this._searchScope?.ThreadId;

        Expression<Func<ChatHistoryItem, bool>> applicationIdFilter = x => x.ApplicationId == applicationId;
        Expression<Func<ChatHistoryItem, bool>> agentIdFilter = x => x.AgentId == agentId;
        Expression<Func<ChatHistoryItem, bool>> userIdFilter = x => x.UserId == userId;
        Expression<Func<ChatHistoryItem, bool>> threadIdFilter = x => x.ThreadId == threadId;

        Expression<Func<ChatHistoryItem, bool>>? filter = null;
        if (applicationId != null)
        {
            filter = applicationIdFilter;
        }

        if (agentId != null)
        {
            filter = filter == null ? agentIdFilter : Expression.Lambda<Func<ChatHistoryItem, bool>>(
                Expression.AndAlso(filter.Body, agentIdFilter.Body),
                filter.Parameters);
        }

        if (userId != null)
        {
            filter = filter == null ? userIdFilter : Expression.Lambda<Func<ChatHistoryItem, bool>>(
                Expression.AndAlso(filter.Body, userIdFilter.Body),
                filter.Parameters);
        }

        if (threadId != null)
        {
            filter = filter == null ? threadIdFilter : Expression.Lambda<Func<ChatHistoryItem, bool>>(
                Expression.AndAlso(filter.Body, threadIdFilter.Body),
                filter.Parameters);
        }

        // Use search to find relevant messages
        var searchResults = collection.SearchAsync(
            queryText,
            top,
            options: new()
            {
                Filter = filter
            },
            cancellationToken: cancellationToken);

        var results = new List<ChatHistoryItem>();
        await foreach (var result in searchResults.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            results.Add(result.Record);
        }

        this._logger?.LogInformation("ChatHistoryMemoryProvider: Retrieved {Count} search results.", results.Count);

        return results;
    }

    /// <summary>
    /// Ensures the collection exists in the vector store, creating it if necessary.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The vector store collection.</returns>
    private async Task<VectorStoreCollection<Guid, ChatHistoryItem>> EnsureCollectionExistsAsync(
        CancellationToken cancellationToken = default)
    {
        if (this._collectionInitialized)
        {
            return this._collection;
        }

        await this._initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._collectionInitialized)
            {
                return this._collection;
            }

            await this._collection.EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
            this._collectionInitialized = true;

            return this._collection;
        }
        finally
        {
            this._initializationLock.Release();
        }
    }

    /// <inheritdoc/>
    private void Dispose(bool disposing)
    {
        if (!this._disposedValue)
        {
            if (disposing)
            {
                this._initializationLock.Dispose();
                this._collection?.Dispose();
            }

            this._disposedValue = true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Represents a chat history item stored in the vector store.
    /// </summary>
    internal sealed class ChatHistoryItem
    {
        /// <summary>
        /// Gets or sets the unique identifier for the chat history item.
        /// </summary>
        [VectorStoreKey]
        public Guid Key { get; set; }

        /// <summary>
        /// Gets or sets the role of the message author (e.g., "user", "assistant", "system").
        /// </summary>
        [VectorStoreData]
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the message ID.
        /// </summary>
        [VectorStoreData]
        public string? MessageId { get; set; }

        /// <summary>
        /// Gets or sets the message author name.
        /// </summary>
        [VectorStoreData]
        public string? AuthorName { get; set; }

        /// <summary>
        /// Gets or sets an optional id for the application to scope memories to.
        /// </summary>
        [VectorStoreData]
        public string? ApplicationId { get; set; }

        /// <summary>
        /// Gets or sets an optional id for the agent to scope memories to.
        /// </summary>
        [VectorStoreData]
        public string? AgentId { get; set; }

        /// <summary>
        /// Gets or sets an optional id for the user to scope memories to.
        /// </summary>
        [VectorStoreData]
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets an optional id for the thread to scope memories to.
        /// </summary>
        [VectorStoreData]
        public string? ThreadId { get; set; }

        /// <summary>
        /// Gets or sets the content of the chat message.
        /// </summary>
        [VectorStoreData]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the timestamp when the message was stored.
        /// </summary>
        [VectorStoreData]
        public string? CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the text embedding for vector search.
        /// </summary>
        public string? ContentEmbedding => this.Content;
    }
}
