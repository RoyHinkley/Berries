# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Project

## Problem statement

Ordinary duplicate-file tools generally expose DuplicateSets and require the user to decide what to do with individual instances. That works for small isolated duplication, but poorly for accumulated backups, reorganized trees, partial moves, migrations, archives, staging areas, generated output, repositories, and other real filesystem histories.

Berries instead identifies structural relationships among duplicated Content and presents coherent Cases that can resolve many duplicate-content decisions with relatively few user assessments.

## Objective

Provide a safe, efficient way to eliminate unwanted file duplication across large filesystem trees while preserving the Content and organization the user wants.

The system should minimize required user attention without making irreversible semantic decisions on the user's behalf.

## Governing principles

1. Structural relationships among directories and branches are first-class evidence, not presentation wrapped around DuplicateSets.

2. Cases are objective and program-discovered. Situations are semantic context identified by the user, optionally assisted by objective evidence.

3. A Case need not resolve all duplication within its bounds; it should resolve the duplication pattern that caused that Case to exist.

4. Resolutions are Situation-aware descriptions of useful outcomes. A Resolution must address the defining duplication pattern of the Case.

5. Leverage remains an objective payoff measure: the number of distinct duplicated Content relationships directly addressed by the Case's defining structure. It is not the number of duplicates incidentally enclosed by Case bounds.

6. Leverage alone is not presentation priority. Berries should prefer the smallest comprehensible question with the greatest downstream simplifying effect, using objective characteristics such as coverage, concentration, specificity, and structural position rather than prematurely hiding them inside an arbitrary scalar score.

7. User decisions are first applied to a virtual Current Portrait. The user can inspect, revise, undo, and redo the projected result before any real filesystem change occurs.

8. The UI primarily portrays the desired surviving arrangement. Removal follows from the selected Disposition rather than being treated as the fundamental user-facing operation.

9. Berries does not perform filesystem changes solely from programmatic semantic inference. User approval is required.

10. Core is independent of both UI and platform-specific filesystem behavior.

## Design documents

The original monolithic PROJECT file has been deliberately split so each governing document remains focused and small enough to review and maintain reliably.

### [MODEL.md](MODEL.md)

Defines architectural vocabulary and invariants:

    Corpus / Portrait / Content
    DuplicateSet / DirectoryPair / ScopePair
    Case / Situation / Resolution / Disposition
    ActionPlan / ExecutionPlan
    Leverage
    the semantic decision chain

This is the authoritative source for terminology.

### [ANALYSIS.md](ANALYSIS.md)

Defines objective discovery and structural analysis:

    Corpus normalization and Initial Portrait construction
    DuplicateSet discovery
    Directory records
    DirectoryPair graph
    ScopePair construction and effective-side cuts
    Case construction
    leverage and structural evidence
    multi-objective Case presentation research
    empirical development method
    derived-analysis refresh

### [SITUATIONS.md](SITUATIONS.md)

Defines Situation semantics and the working Situation catalogue, including applicable Case types, semantic roles, natural Resolutions, and retained/rejected Situation candidates.

### [WORKFLOW.md](WORKFLOW.md)

Defines the user-decision and execution path:

    Case presentation
    Disposition construction and validation
    directory mappings
    Accept / Apply
    ActionPlan compilation
    virtual Portrait updates
    Execute and ExecutionPlan safety
    filesystem abstraction requirements
    deferred implementation refinements

### [DEVELOPMENT.md](DEVELOPMENT.md)

Records the current implementation state, vertical slices, tests, performance instrumentation, and short-term empirical-development work. It is descriptive of the current code, not the governing design.

## Platform and architecture

Implementation platform:

    C#
    .NET
    Avalonia

The analysis engine is entirely independent of the UI.

Filesystem access is isolated behind platform-neutral interfaces. Filesystem-specific behavior belongs only in adapter implementations.

Architectural test:

    If Core cannot be exercised by a simple test harness against a synthetic
    Portrait, a platform or UI concern has leaked across a boundary.

Current solution decomposition:

    Berries.Core
        domain model
        Portrait
        duplicate analysis
        directory / pair / scope analysis
        Cases
        Situations and Resolutions
        Dispositions and validation
        ActionPlan compilation
        execution-planning contracts

    Berries.FileSystem.Abstractions
        platform-neutral filesystem model and operations

    Berries.FileSystem.Windows
        Windows filesystem implementation

    Berries.FileSystem.<other>
        future platform implementations

    Berries.Gui
        Avalonia desktop UI

    Berries.Core.Tests
        platform-independent Core tests using synthetic filesystem/Portrait data

## Current conceptual chain

    objective duplication pattern
        -> Case
        -> Situation
        -> Resolution
        -> Disposition
        -> ActionPlan

ExecutionPlan safely realizes accumulated ActionPlans against the real filesystem and is outside the semantic decision chain.

Cases and Situations deliberately remain separate:

    Case
        What objective duplication relationship is under consideration?

    Situation
        What is going on that explains that relationship?

    Resolution
        Given that Situation, what is a natural useful outcome?

    Disposition
        What exact surviving arrangement implements that outcome?

    ActionPlan
        What logical filesystem operations produce that arrangement?

## Current research direction

Real-corpus testing has validated the basic structural model and exposed substantial information in inexpensive graph and hierarchy metrics.

In particular:

- high leverage often occurs at broad ScopePairs, while clearer and nearly as powerful questions occur lower in the structural hierarchy;
- directional coverage and edge concentration distinguish strong pairwise correspondence from diffuse standardized/generated duplication;
- nested ScopePairs are best understood as cuts through a containing tree, and moving the cut can legitimately increase or decrease leverage because duplicate relationships cross or cease crossing the boundary;
- related ScopePairs therefore need not form a monotonic refinement chain;
- the next ranking work should remain multi-objective and empirical rather than introducing an arbitrary weighted priority score prematurely.

These are design context, not yet fully solved ranking rules. `ANALYSIS.md` records the current formulation.
