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

### Implementation Options

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

### Feature Collections extensions points

We need to decide the set of actions that feature collections would be supported for. Here are the suggested list of actions:

MAAI.AIAgent:

1. GetNewThread
    1. E.g. this would allow passing an already existing storage id for the thread to use, or an initialized custom chat message store to use.
1. DeserializeThread
    1. E.g. this would allow passing an already existing storage id for the thread to use, or an initialized custom chat message store to use.
1. Run / RunStreaming
    1. E.g. this would allow passing an override chat message store just for that run, or a desired schema for a structured output middleware component.

MEAI.ChatClient:

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
  - If the hierarchy changes as it progresses down the execution stack, then the caller can only pass in the action level feature collection, and the callee needs to combine it with its own artifact level feature collection and the application level feature collection.
- Does the user need to provide the application level feature collection to each artifact that the user constructs, or is there a static application level feature collection that is used automatically?
  - Static create many issues with testing and isolation.
  - Passing the application level feature collection to each artifact is tedious for the user.

### Feature Collections vs Mixins

An alternative to feature collections is to use mixins to add optional capabilities to agents or chat clients, where the 'feature' to pass to the agent would be part of the mixin interface.
Mixins have the advantage of being strongly typed and discoverable via interface checks.
However, mixins are less flexible, in that user code and all matching agent implementations need to share the same interface.
This creates a push towards more centralized mixing contracts which limit flexibility.

Combining multiple features together is also more difficult with mixins, as a new mixin interface needs to be created for each combination of features.

## Decision Drivers

- {decision driver 1, e.g., a force, facing concern, �}
- {decision driver 2, e.g., a force, facing concern, �}
- � <!-- numbers of drivers can vary -->

## Considered Options

- {title of option 1}
- {title of option 2}
- {title of option 3}
- � <!-- numbers of options can vary -->

## Decision Outcome

Chosen option: "{title of option 1}", because
{justification. e.g., only option, which meets k.o. criterion decision driver | which resolves force {force} | � | comes out best (see below)}.
