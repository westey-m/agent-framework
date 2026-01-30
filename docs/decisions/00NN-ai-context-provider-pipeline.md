# Modelling ChatHistoryProvider and AIContextProviders as a pipeline

There have been various suggestions to change the design of ChatHistoryProvider and AIContextProviders, including:

1. Modelling both as a single pipeline
1. Modelling both as separate pipelines
1. Modelling ChatHistoryProvider and AIContextProviders as separate concepts, with AIContextProviders implemented as decorators over the agent or chat client.

This ADR explores these options and their trade-offs.

## Decision Drivers

- ChatHistoryProviders and AIContextProviders produce state that must be serializable with the AgentSession
- State must not clash where multiple providers are used, including multiple instances of the same provider type
- The ChatHistoryProvider for an AgentSession must be replaceable per run
- We must be able to distinguish between messages coming from AIContextProviders and those coming from the user
- We must be able to distinguish between messages coming from ChatHistoryProviders and those coming from AIContextProviders
- It should be possible to provide a default ChatHistoryProvider if none is supplied by the user
- The AIContext merging logic for AIContextProviders should be reusable across different provider types

## Considered Options

- **Option 1**: Combine ChatHistoryProvider and AIContextProviders into a single pipeline
  - Pro: Simplifies the design by having a single pipeline for both chat history and context providers.
  - Con: If we don't distinguish between ChatHistoryProvider and AIContextProviders, we cannot identify and replace the ChatHistoryProvider per run.
  - Con: If we don't distinguish between ChatHistoryProvider and AIContextProviders, we cannot identify whether a ChatHistoryProvider has been supplied in order to provide a default one.
  - Con: Each AIContextProvider must merge AIContext with its predecessors, so if we want to share merging logic, we need a way to separate it.
  - Con: We need to design a way to merge state from the providers in such a way that state from all providers do not clash.

```csharp
InMemoryChatHistoryAIContextProvider: AIContextProvider, IChatHistoryProvider
{
    public abstract Task<AIContext> Invoking(AIContextProvider.InvokingContext context);
    public abstract Task Invoked(AIContextProvider.InvokedContext context);

    public abstract Task<IEnumerable<ChatMessage>> Invoking(ChatHistoryProvider.InvokingContext context);
    public abstract Task Invoked(ChatHistoryProvider.InvokedContext context);
}

InMemoryChatHistoryAIContextProvider: ChatHistoryProvider
{
    public abstract Task<IEnumerable<ChatMessage>> Invoking(ChatHistoryProvider.InvokingContext context);
    public abstract Task Invoked(ChatHistoryProvider.InvokedContext context);
}

Mem0AIContextProvider: AIContextProvider
{
    public abstract Task<AIContext> Invoking(AIContextProvider.InvokingContext context);
    public abstract Task Invoked(AIContextProvider.InvokedContext context);
}


public override ValueTask<ResponseContext> InvokeAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<ResponseContext>> nextProvider, CancellationToken cancellationToken = default)
{
}
```

- **Option 2**: Keep ChatHistoryProvider and AIContextProviders separate, but model each as a separate pipeline
  - Pro: Easy to distinguish between ChatHistoryProvider and AIContextProviders, so that we can easily provide a default ChatHistoryProvider or replace the ChatHistoryProvider.
  - Con: Each AIContextProvider must merge AIContext with its predecessors, so if we want to share merging logic, we need a way to separate it.
  - Con: We need to design a way to merge state from the providers in such a way that state from all providers do not clash.

- **Option 3**: Keep ChatHistoryProvider and AIContextProviders separate, but model AIContextProviders as decorators over the agent or chat client.
  - Pro: Easy to distinguish between ChatHistoryProvider and AIContextProviders, so that we can easily provide a default ChatHistoryProvider or replace the ChatHistoryProvider.
  - Pro: One less concept to consider.
  - Pro: Depending on the type of decorator or location of it (Agent vs ChatClient), the output is automatically availble to be added to the ChatHistoryProvider, or not.
  - Con: Each AIContextProvider must merge AIContext with its predecessors, so if we want to share merging logic, we need a way to separate it.
  - Con: We need to design a way to merge state from the providers in such a way that state from all providers do not clash.
  - Con: We need a way to pass state to a ChatClient decorator (maybe using AgentRunContext?).
  - Con: If we add a decorator to ChatClient, the decorator doesn't know which messages came from the user vs other sources (ChatHistoryProvider, other AIContextProvider decorators on the agent). There is no option for adding a decorator that only consumes user messages and also doesn't store its output in chat history.
  - Con: For streaming scenarios, each decorator needs to assemble the full response from the updates to process response messages. Today, the Agent assembles the full response once for all attached providers. (maybe multiple AIContextProviders can be assembled into a single decorator?)
  - Con: Having a pipeline, assumes that we are able to modify responses as they are returned, but we have streaming and non-streaming cases. Modifying the stream of response messages either requires separate code for streaming vs non-streaming scenarios, or we need to wait until the entire stream is received, convert it to a regular AgentResponse modify it, and then convert it back to a stream. This adds complexity and/or latency.
  - Reminder: We need to be able to initialize the state of an AIContextProvider, like we do today in the AIContextBuilderFactory. Therefore, AIContextProviders must expose an initialization method.
  - Reminder: Decorators for the agent are different to decorators for the chat client, so we need to build some facades to unify the interface in order to avoid building two implementations for the sampe functionality.
  
## Option 3: Keep ChatHistoryProvider and AIContextProviders separate, but model AIContextProviders as decorators over the agent or chat client

With option 3, the AIContextProvider is implemented as a decorator over either the Agent or the ChatClient. This means that any messages added by the AIContextProvider decorator over the Agent will be visible to the ChatHistoryProvider, while messages added by a ChatClient decorator will not.

```csharp
// Add Memory Middleware to Chat Client via Decorator
// Since memories are only added below the agent in the stack, they are not visible to any ChatHistoryProvider, but visible to service stored chat history.
var originalAgent = azureOpenAIClient.AsIChatClient()
    .AsBuilder()
    .Use(
        getResponseFunc: async (IEnumerable<ChatMessage> messages, ChatOptions? options, IChatClient innerChatClient, CancellationToken cancellationToken) =>
        {
            // Check if we have any state in the state bag containing the id of the memories.
            var memoryState = AIAgent.CurrentContext.Session?.StateBag.GetValue<MemoryState>("MemoryState", MemoryStateJsonSerializerContext.Default.Options);
            memoryState ??= new MemoryState(sessionId: Guid.NewGuid().ToString());

            // Try and find some memories and generate messages for them.
            var memories = await memoryClient.FindMemories(messages, memoryState.SessionId);
            var memoryMessages = memories.Select(m => new ChatMessage(ChatRole.User, $"Memory: {m.Content}")).ToList();

            // Invoke the inner chat client with the memory messages added.
            var response = await innerChatClient.GetResponseAsync(messages.Concat(memoryMessages), options, cancellationToken);

            // Extract memories from the input and output messages.
            await memoryClient.ExtractMemories(messages.Append(response.Messages), memoryState.SessionId);

            // Store the updated memory state back in the session state bag.
            AIAgent.CurrentContext.Session?.StateBag.SetValue("MemoryState", memoryState, MemoryStateJsonSerializerContext.Default.Options);

            return response;
        },
        getStreamingResponseFunc: null)
    .BuildAIAgent(
        instructions: "You are an AI assistant that helps people find information.");
```

```csharp
// Add Memory Middleware to Agent via Decorator
// Since memories are added above the agent in the stack, they are visible to any ChatHistoryProvider or service stored chat history.
var middlewareEnabledAgent = originalAgent
    .AsBuilder()
    .Use(async (IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, AIAgent innerAgent, CancellationToken cancellationToken) =>
    {
        // Check if we have any state in the state bag containing the id of the memories.
        var memoryState = session?.StateBag.GetValue<MemoryState>("MemoryState", MemoryStateJsonSerializerContext.Default.Options);
        memoryState ??= new MemoryState(sessionId: Guid.NewGuid().ToString());

        // Try and find some memories and generate messages for them.
        var memories = await memoryClient.FindMemories(messages, memoryState.SessionId);
        var memoryMessages = memories.Select(m => new ChatMessage(ChatRole.User, $"Memory: {m.Content}")).ToList();

        // Invoke the inner chat client with the memory messages added.
        var response = await innerAgent.RunAsync(messages.Concat(memoryMessages), session, options, cancellationToken);

        // Extract memories from the input and output messages.
        await memoryClient.ExtractMemories(messages.Append(response.Messages), memoryState.SessionId);

        // Store the updated memory state back in the session state bag.
        session?.StateBag.SetValue("MemoryState", memoryState, MemoryStateJsonSerializerContext.Default.Options);

        return response;
    }, null)
    .Build();
```

## Distinguishing between messages coming from AIContextProviders and those coming from other sources

Distinguishing between messages coming from AIContextProviders and those coming from other sources (user, ChatHistoryProvider) is important for scenarios like

1. memory generation, where we may want to avoid generating memories for messages that originated from AIContextProviders.
1. chat history storage, where we may want to avoid storing messages from AIContextProviders in the chat history.

Depdending on the chosen option, we may be able to avoid needing to differentiate by simply placing the AIContextProvider at a location where its output is not stored in chat history or used for memory generation.

Option 3 sample chain: User -> Agent Decorator (AIContextProvider 1) -> Agent -> ChatHistoryProvider -> ChatClient Decorator (AIContextProvider 2) -> ChatClient.

In this case, messages from AIContextProvider 1 are stored in chat history, because they are fed into the agent like regular user messages, but messages from AIContextProvider 2 are not.

## State serialization and merging

If we support a pipeline of AIContextProviders, we need to support multiple AIContextProviders per agent.
Today we only support one, and supporting multiple, mean needing to merge state during serialization.

Depending on the chosen option, this could also be achieved by having a state bag that each provider can read and write to. This increases the risk of state clashing, so we need to design a way to merge state from multiple providers in such a way that they don't clash.

## JsonSerializerOptions

Do we need to pass JsonSerializerOptions at the serialization and deserializtion points, or just on constructors? Would users need to customize serialization and types per operation, e.g. would there be types that are only known at runtime that need to be registered? This seems unlikely.

## AI Context Merging Abstraction

To avoid having each AIContextProvider implement its own merging logic, we can introduce an abstraction for AI Context Merging. This abstraction would allow merging logic to be injected into AIContextProviders, enabling code reuse and consistency across different providers.

```csharp
// Can be provided via a class.
public abstract class AIContextMerger
{
    public abstract AIContext Merge(AIContext existingContext, AIContext newContext);
}
public class MergeNewAtEndMerger : AIContextMerger
{
    public override AIContext Merge(AIContext existingContext, AIContext newContext)
    {
        var mergedContext = new AIContext();
        mergedContext.Instructions = existingContext.Instructions.Concat(newContext.Instructions);
        mergedContext.Tools = existingContext.Tools.Concat(newContext.Tools);
        mergedContext.Messages = existingContext.Messages.Concat(newContext.Messages);
        return mergedContext;
    }
}

// Or via a delegate.
Func<AIContext, AIContext, AIContext> mergeNewAtEnd = (existingContext, newContext) =>
{
    var mergedContext = new AIContext();
    mergedContext.Instructions = existingContext.Instructions.Concat(newContext.Instructions);
    mergedContext.Tools = existingContext.Tools.Concat(newContext.Tools);
    mergedContext.Messages = existingContext.Messages.Concat(newContext.Messages);
    return mergedContext;
};

// or both, where one class implementation accepts a delegate in its constructor.
```
