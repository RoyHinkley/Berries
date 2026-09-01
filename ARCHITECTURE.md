# Berries Architecture

This document defines implementation boundaries and runtime invariants. They are design constraints, not optional optimization guidance.

## Dependency direction

    Berries.Core
        domain/session model, scanning and Group discovery
        factual queries and structural analysis
        portrait operations, analysis lifecycle, planning contracts

    Berries.Projection
        UI-independent presentation-shaped transformations
        ProjectionState

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem implementation

    Berries.Gui
        Avalonia interaction and GUI-specific presentation state
        ExplorerNode construction, binding, selection, status/progress

Core must not depend on Projection or Gui. Projection may depend on Core. Gui may depend on both, but initiating work does not make the GUI its architectural owner.

## Placement rule

> Put work at the lowest reusable layer that naturally owns it.

Placement is determined by meaning, not cost or async implementation:

- **Core** owns domain facts, analysis, model queries, and reusable domain computation.
- **Projection** owns UI-independent transformations whose result exists specifically for presentation.
- **Gui** owns genuinely GUI-specific construction and control state.

Examples: hashing and Branch relationship scoring are Core; presentation ordering and a UI-independent Branch hierarchy are Projection; `ExplorerNode` creation and `TreeView` binding are Gui.

Expensive GUI-specific work remains GUI work. It must be made responsive there rather than moved downward merely because it is expensive.

## Responsiveness contract

Any work whose runtime can grow materially with user data must be reviewed for responsiveness at its natural owner. Potentially appreciable work must:

1. avoid monopolizing the UI thread;
2. participate in a `CancellationToken` lifetime;
3. check cancellation inside the scaling loop at useful granularity;
4. report a meaningful phase;
5. report completed/total progress when the total is cheaply knowable.

Indeterminate progress is appropriate when determining the total would duplicate substantial work, notably open-ended filesystem enumeration.

The layer doing the work reports progress as data. The GUI owns status-bar presentation.

## Explorer realization

Logical presentation size and realized Avalonia control count are different concerns.

Large Explorer trees must use virtualized item panels so off-screen roots do not acquire visual containers merely because they exist in the projection. This is a functional scaling requirement: a nonvirtualized `TreeView` can turn inexpensive collection publication into repeated realization/layout work proportional to the entire tree.

GUI construction may publish known presentation nodes incrementally in bounded batches when useful for cancellation and early display. Batching does not replace virtualization; both control different costs.

## Projection caching and prewarming

Projection results that depend only on a particular `WorkingPortrait` may be cached for that portrait. A portrait change invalidates such caches.

Completed ordinary Groups and Corpus Roots projections are cached. Construction that can be requested concurrently must be serialized or otherwise shared so two callers do not duplicate the same expensive work.

The initial Groups view has priority after primary discovery. Once it is published, Corpus Roots may be prewarmed in the background so a later Pivot normally uses the cache without delaying first useful display.

Selection-dependent projections are different: selection is durable and can change without a new `WorkingPortrait`, so they must not be cached solely by portrait identity.

## Navigation correctness

Only the most recently requested navigation may publish a view.

- each navigation receives an operation generation and cancellation token;
- a newer request cancels the previous one for efficiency;
- only the current generation has publication authority;
- a stale completion cannot overwrite a newer requested view.

Navigation ownership provides correctness; cancellation provides responsiveness.

Navigation lifetime is GUI-owned because it governs presentation. It is distinct from the Core analysis lifecycle.

## Analysis correctness

Derived analysis is valid only for the `WorkingPortrait` generation from which it was computed. When the portrait changes:

- the previous generation becomes stale immediately;
- obsolete work is asked to cancel;
- stale results cannot publish;
- current-generation analysis is scheduled.

Prompt cancellation avoids wasted work; generation validation is the correctness mechanism.

`BerriesApplication` owns session replacement, serialized portrait mutation, portrait generation, and dependency-driven analysis scheduling. The GUI must not maintain a competing analysis scheduler or cancellation lifecycle.

## Review rule

For work that scales with user data, explicitly review:

    correct architectural owner
    asynchronous/non-blocking execution where appreciable
    cancellation granularity
    meaningful progress
    determinate progress where practical
    bounded GUI publication
    virtualized realization for large item populations
    reusable projection caching where appropriate
    stale-result publication protection

A responsiveness regression is an architectural regression even when the result is functionally correct.