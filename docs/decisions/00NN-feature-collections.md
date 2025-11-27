---
status: proposed
contact: westey-m
date: 2025-11-26
deciders: {list everyone involved in the decision}
consulted: {list everyone whose opinions are sought (typically subject-matter experts); and with whom there is a two-way communication}
informed: {list everyone who is kept up-to-date on progress; and with whom there is a one-way communication}
---

# Feature Collections

## Context and Problem Statement

When using agents, we often have cases where we want to pass some arbitrary services or data to an agent or some component in the agent execution stack.
These services or data are not necessarily known at compile time and can vary by the agent stack that the user has built.
E.g., there may be an agent decorator or chat client decorator that was added to the stack by the user, and an arbitrary payload needs to be passed to that decorator.

Since these payloads are related to components that are not integral parts of the agent framework, they cannot be added as strongly typed settings to the agent run options.
However, the payloads could be added to the agent run options as loosely typed 'features', that can be retrieved as needed.

In some cases certain classes of agents may support the same capability, but not all agents do.
Having the configuration for such a capability on the main abstraction would advertise the functionality to all users, even if their chosen agent does not support it.
The user may type test for certain agent types, and call overloads on the appropriate agent types, with the strongly typed configuration.
Having a feature collection though, would be an alternative way of passing such configuration, without needing to type check the agent type.
All agents that support the functionality would be able to check for the configuration and use it, simplifying the user code.
If the agent does not support the capability, that configuration would be ignored.

## Implementation Options

Three options were considered for implementing feature collections:

- **Option 1**: FeatureCollections similar to ASP.NET Core
- **Option 2**: AdditionalProperties Dictionary
- **Option 3**: IServiceProvider

Here are some comparisons about their suitability for our use case:

| Criteria         | Feature Collection | Additional Properties | IServiceProvider |
|------------------|--------------------|-----------------------|------------------|
|Ease of use       |✅ Good             |❌ Bad                |✅ Good           |
|User familiarity  |❌ Bad              |✅ Good               |✅ Good           |
|Type safety       |✅ Good             |❌ Bad                |✅ Good           |
|Ability to modify registered options when progressing down the stack|✅ Supported|✅ Supported|❌ Not-Supported (IServiceProvider is read-only)|
|Already available in MEAI stack|❌ No|✅ Yes|❌ No|
|Ability to layer features by scope (e.g., per-agent, per-request)|✅ Supported|❌ Not-Supported|❌ Not-Supported|

## Feature Collection

If we choose the feature collection option, we need to decide on the design of the feature collection itself.

### Feature Collections extension points

We need to decide the set of actions that feature collections would be supported for. Here is the suggested list of actions:

**MAAI.AIAgent:**

1. GetNewThread
    1. E.g. this would allow passing an already existing storage id for the thread to use, or an initialized custom chat message store to use.
1. DeserializeThread
    1. E.g. this would allow passing an already existing storage id for the thread to use, or an initialized custom chat message store to use.
1. Run / RunStreaming
    1. E.g. this would allow passing an override chat message store just for that run, or a desired schema for a structured output middleware component.

**MEAI.ChatClient:**

1. GetResponse / GetStreamingResponse

### Feature Layering

One possible feature when adding support for feature collections is to allow layering of features by scope.

The following levels of scope could be supported:

1. Application - Application wide features that apply to all agents / chat clients
2. Artifact (Agent / ChatClient) - Features that apply to all runs of a specific agent or chat client instance
3. Action (GetNewThread / Run / GetResponse) - Feature that apply to a single action only

When retrieving a feature from the collection, the search would start from the most specific scope (Action) and progress to the least specific scope (Application), returning the first matching feature found.

Introducing layering adds some challenges:

- There may be multiple feature collections at the same scope level, e.g. an Agent that uses a ChatClient where both have their own feature collections.
  - Do we layer the agent feature collection over the chat client feature collection (Application -> ChatClient -> Agent -> Run), or only use the agent feature collection in the agent (Application -> Agent -> Run), and the chat client feature collection in the chat client (Application -> ChatClient -> Run)?
- The appropriate base feature collection may change when progressing down the stack, e.g. when an Agent calls a ChatClient, the action feature collection stays the same, but the artifact feature collection changes.
- Who creates the feature collection hierarchy?
  - If the hierarchy changes as it progresses down the execution stack, then the caller can only pass in the action level feature collection, and the callee needs to combine it with its own artifact level feature collection and the application level feature collection.  This will require changes to the feature collection type compared to asp.net, so that it can change its base collections as needed.

#### Layering Options

1. No layering - only a single feature collection is supported per action (the caller can still create a layered collection if desired, but the callee does not do any layering automatically).
1. Simple layering - only support layering at the artifact level (Artifact -> Action).
    1. Only apply applicable artifact level features when calling into that artifact.
    1. Apply upstream artfact features when calling into downstream artifacts, e.g. Feature hierarchy in ChatClientAgent would be `Agent -> Run` and in ChatClient would be `ChatClient -> Agent -> Run` or `Agent -> ChatClient -> Run`
1. Full layering - support layering at all levels (Application -> Artifact -> Action).
    1. Only apply applicable artifact level features when calling into that artifact.
    1. Apply upstream artfact features when calling into downstream artifacts, e.g. Feature hierarchy in ChatClientAgent would be `Application -> Agent -> Run` and in ChatClient would be `Application -> ChatClient -> Agent -> Run` or `Application -> Agent -> ChatClient -> Run`

#### Accessing application level features Options

1. The user provides the application level feature collection to each artifact that the user constructs
    1. Passing the application level feature collection to each artifact is tedious for the user.
1. There is a static application level feature collection that can be accessed globally.
    1. Statics create issues with testing and isolation.

### Reconciling with existing AdditionalProperties

If we decide to add feature collections, separately from the existing AdditionalProperties dictionaries, we need to consider how to explain to users when to use each one.
One possible approach though is to have the one use the other under the hood.
AdditionalProperties could be stored as a feature in the feature collection.

Users would be able to retrieve additional properties from the feature collection, in addition to retrieving it via a dedicated AdditionalProperties property.
E.g. `features.Get<AdditionalPropertiesDictionary>()`

One challenge with this approach is that when setting a value in the AdditionalProperties dictionary, the feature collection would need to be created first if it does not already exist.

```csharp
public class AgentRunOptions
{
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
    public IAgentFeatureCollection? Features { get; set; }
}

var options = new AgentRunOptions();
// This would need to create the feature collection first, if it does not already exist.
options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
```

Since IAgentFeatureCollection is an interface, AgentRunOptions would need to have a concrete implementation of the interface to create, meaning that the user cannot decide.
It also means that if the user doesn't realise that AdditionalProperties is implemented using feature collections, they may set a value on AdditionalProperties, and then later overwrite the entire feature collection, losing the AdditionalProperties feature.

Options to avoid these issues:

1. Make `Features` readonly.
    1. This would prevent the user from overwriting the feature collection after setting AdditionalProperties.
    1. Since the user cannot set their own implementation of IAgentFeatureCollection, having an interface for it may not be necessary.

### Feature Collections vs Mixins

An alternative to feature collections is to use mixins to add optional capabilities to agents or chat clients, where the 'feature' to pass to the agent would be part of the mixin interface.
Mixins have the advantage of being strongly typed and discoverable via interface checks.
However, mixins are less flexible, in that user code and all matching agent implementations need to share the same interface.
This creates a push towards more centralized mixing contracts which limit flexibility.

Combining multiple features together is also more difficult with mixins, as a new mixin interface needs to be created for each combination of features.
