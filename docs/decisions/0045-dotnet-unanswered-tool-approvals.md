---
status: proposed
contact: westey-m
date: 2026-10-02
deciders: westey-m
---

# Reject unanswered tool approvals on the next .NET agent run

## Context and Problem Statement

An agent can save a tool-approval request and return it to an app that never sends
an answer. The user might close the page or the app might fail before submitting
the decision. Later messages then fail because the saved conversation contains
an unanswered request. Retrying a previously supplied answer is supported, but
does not help an app that no longer holds that answer.

See [microsoft/agent-framework#8849](https://github.com/microsoft/agent-framework/issues/8849)
and the earlier [approval design](0006-userapproval.md).

## Decision Drivers

- A missing answer must never authorize tool execution.
- A conversation should remain usable after the app loses an approval request.
- Supplied decisions and retry behavior should keep working.
- Recovery should work with a saved and restored session.

## Considered Options

- Continue throwing until the app supplies every answer.
- Return the unanswered approval requests to the caller again.
- Automatically reject unanswered requests on the next run.

## Decision Outcome

Chosen option: **automatically reject unanswered requests on the next run**.
This allows the conversation to continue without executing tools the user did
not approve. Returning requests again complicates the response and history
handling, while continuing to throw leaves the reported problem unresolved.

The existing `ApprovalResponseBindingChatClient` first checks supplied answers,
then supplies a rejection for each remaining saved request whose call does not
already have a result. This applies to partially answered batches and empty
input, not just new text messages. Waiting or closing the app does not itself
reject anything; the app must start another run.

Use the saved request's tool call, not a caller-supplied replacement. Keep the
existing protection against fabricated and duplicate approvals, and the
existing removal of pending state only after the inner call succeeds. A failed
attempt therefore leaves requests available for retry.

This is the default where approval-response binding is enabled. Disabling that
component also disables automatic rejection; no separate setting is added.
Tools that do not require approval keep their existing automatic handling.
The helper that presents requests one at a time keeps its queue behavior: this
rule applies when it next calls the underlying agent.

Apps must supply all desired approval decisions before continuing the agent.
An answer arriving after a successful automatic rejection cannot authorize the
old call. The model can still request a new call and ask for approval again.
This decision does not make tool execution and history storage a single atomic
operation or remove the existing possibility of repeated execution after a
failed run.
