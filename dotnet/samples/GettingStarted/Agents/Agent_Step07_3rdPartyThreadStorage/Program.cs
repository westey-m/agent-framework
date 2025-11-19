// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances

// This sample shows how to create and use a simple AI agent with a conversation that can be persisted to disk.

using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using OpenAI;
using SampleApp;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

// Create a vector store to store the chat messages in.
// Replace this with a vector store implementation of your choice if you want to persist the chat history to disk.
VectorStore vectorStore = new InMemoryVectorStore();

// Create the agent
AIAgent agent = new AzureOpenAIClient(
    new Uri(endpoint),
    new AzureCliCredential())
    // Use a service that doesn't require storage of chat history in the service itself.
    .GetChatClient(deploymentName)
    .CreateAIAgent(new ChatClientAgentOptions
    {
        Instructions = "You are good at telling jokes.",
        Name = "Joker",
        ChatMessageStoreFactory = ctx =>
        {
            // Create a new chat message store for this agent that stores the messages in a vector store.
            // Each thread must get its own copy of the VectorChatMessageStore, since the store
            // also contains the id that the thread is stored under.
            return new VectorChatMessageStore(vectorStore, ctx.SerializedState, ctx.JsonSerializerOptions);
        }
    });

// Start a new thread for the agent conversation.
AgentThread thread = agent.GetNewThread();

// Run the agent with the thread that stores conversation history in the vector store.
Console.WriteLine(await agent.RunAsync("Tell me a joke about a pirate.", thread));

// Serialize the thread state, so it can be stored for later use.
// Since the chat history is stored in the vector store, the serialized thread
// only contains the guid that the messages are stored under in the vector store.
JsonElement serializedThread = thread.Serialize();

Console.WriteLine("\n--- Serialized thread ---\n");
Console.WriteLine(JsonSerializer.Serialize(serializedThread, new JsonSerializerOptions { WriteIndented = true }));

// The serialized thread can now be saved to a database, file, or any other storage mechanism
// and loaded again later.

// Deserialize the thread state after loading from storage.
AgentThread resumedThread = agent.DeserializeThread(serializedThread);

// Run the agent with the thread that stores conversation history in the vector store a second time.
Console.WriteLine(await agent.RunAsync("Now tell the same joke in the voice of a pirate, and add some emojis to the joke.", resumedThread));

// We can access the VectorChatMessageStore via the thread's GetService method if we need to read the key under which threads are stored.
var messageStoreFromFactory = resumedThread.GetService<VectorChatMessageStore>()!;
Console.WriteLine($"\nThread is stored in vector store under key: {messageStoreFromFactory.ThreadDbKey}");

// Let's store the threadDbKey for later use.
var threadDbKey = messageStoreFromFactory.ThreadDbKey!;

// We can also create an agent without a factory that provides a ChatMessageStore.
AIAgent agentWithDefaultMessageStore = new AzureOpenAIClient(
    new Uri(endpoint),
    new AzureCliCredential())
    // Use a service that doesn't require storage of chat history in the service itself.
    .GetChatClient(deploymentName)
    .CreateAIAgent(new ChatClientAgentOptions
    {
        Instructions = "You are good at telling jokes.",
        Name = "Joker"
    });

// Start a new thread for the agent conversation.
thread = agent.GetNewThread();

// Instead of using a factory on the agent to create the ChatMessageStore, we can
// create a VectorChatMessageStore ourselves and register it in a service provider.
// We can also pass it the same id as before, so that it continues the same conversation.
var perRunMessageStore = new VectorChatMessageStore(vectorStore, threadDbKey);
ServiceCollection collection = new();
collection.AddSingleton<ChatMessageStore>(perRunMessageStore);
ServiceProvider sp = collection.BuildServiceProvider();

// We can then pass our custom message store to the agent when running it by using the OverrideServiceProvider option.
// The message store would only be used for the run that it's passed to.
Console.WriteLine(await agent.RunAsync("Tell the joke again, but this time in the voice of a robot.", thread, options: new AgentRunOptions() { OverrideServiceProvider = sp }));

// We can then pass our custom message store to the agent when running it by using the Features option.
// The message store would only be used for the run that it's passed to.
AgentRunFeatureCollection features = new();
features.Set<ChatMessageStore>(perRunMessageStore);
Console.WriteLine(await agent.RunAsync("Tell the joke again, but this time in the voice of a cat.", thread, options: new AgentRunOptions() { Features = features }));

// When serializing this thread, we'll see that it has no message store state, since the message store was not attached to the thread,
// but just provided for the single run. Note that, depending on the circumstances, the thread may still contain other state, e.g. Memories,
// if an AIContextProvider is attached which adds memory to an agent.
serializedThread = thread.Serialize();

Console.WriteLine("\n--- Serialized thread ---\n");
Console.WriteLine(JsonSerializer.Serialize(serializedThread, new JsonSerializerOptions { WriteIndented = true }));

namespace SampleApp
{
    /// <summary>
    /// A sample implementation of <see cref="ChatMessageStore"/> that stores chat messages in a vector store.
    /// </summary>
    internal sealed class VectorChatMessageStore : ChatMessageStore
    {
        private readonly VectorStore _vectorStore;

        public VectorChatMessageStore(VectorStore vectorStore, string threadDbKey)
        {
            this._vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            this.ThreadDbKey = threadDbKey ?? throw new ArgumentNullException(nameof(threadDbKey));
        }

        public VectorChatMessageStore(VectorStore vectorStore, JsonElement serializedStoreState, JsonSerializerOptions? jsonSerializerOptions = null)
        {
            this._vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));

            if (serializedStoreState.ValueKind is JsonValueKind.String)
            {
                // Here we can deserialize the thread id so that we can access the same messages as before the suspension.
                this.ThreadDbKey = serializedStoreState.Deserialize<string>();
            }
        }

        public string? ThreadDbKey { get; private set; }

        public override async Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            this.ThreadDbKey ??= Guid.NewGuid().ToString("N");

            var collection = this._vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            await collection.UpsertAsync(messages.Select(x => new ChatHistoryItem()
            {
                Key = this.ThreadDbKey + (string.IsNullOrWhiteSpace(x.MessageId) ? Guid.NewGuid().ToString("N") : x.MessageId),
                Timestamp = DateTimeOffset.UtcNow,
                ThreadId = this.ThreadDbKey,
                SerializedMessage = JsonSerializer.Serialize(x),
                MessageText = x.Text
            }), cancellationToken);
        }

        public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
        {
            var collection = this._vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            var records = await collection
                .GetAsync(
                    x => x.ThreadId == this.ThreadDbKey, 10,
                    new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                    cancellationToken)
                .ToListAsync(cancellationToken);

            var messages = records.ConvertAll(x => JsonSerializer.Deserialize<ChatMessage>(x.SerializedMessage!)!)
;
            messages.Reverse();
            return messages;
        }

        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null) =>
            // We have to serialize the thread id, so that on deserialization we can retrieve the messages using the same thread id.
            JsonSerializer.SerializeToElement(this.ThreadDbKey);

        /// <summary>
        /// The data structure used to store chat history items in the vector store.
        /// </summary>
        private sealed class ChatHistoryItem
        {
            [VectorStoreKey]
            public string? Key { get; set; }

            [VectorStoreData]
            public string? ThreadId { get; set; }

            [VectorStoreData]
            public DateTimeOffset? Timestamp { get; set; }

            [VectorStoreData]
            public string? SerializedMessage { get; set; }

            [VectorStoreData]
            public string? MessageText { get; set; }
        }
    }
}
