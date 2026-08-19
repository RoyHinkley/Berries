# Berries Domain Model

This document defines the architectural vocabulary and semantic invariants used throughout Berries. `PROJECT.md` is the governing overview; `ANALYSIS.md`, `SITUATIONS.md`, and `WORKFLOW.md` contain the corresponding detailed designs.

## Core terminology

### Corpus

The set of disjoint filesystem trees selected for analysis.

### Corpus root

A filesystem directory selected to contribute its tree to the Corpus. Selected roots are normalized so no retained root is a descendant of another retained root.

### File

One filesystem instance of Content at a particular path. A File is an instance, not the byte content itself.

### Content

The byte sequence contained by a File. Content identity is established sufficiently for the program's purposes by duplicate detection.

### Initial Portrait

The modeled state of the Corpus when it was scanned. It records the real filesystem state from which the user begins constructing projected Portraits and provides the known source state used by execution validation.

### Portrait

A modeled filesystem state. The Initial Portrait is obtained from disk. Subsequent Portraits are derived by applying virtual Actions to the preceding Portrait.

### Current Portrait

The Portrait produced by the currently accepted sequence of virtual Actions. It represents the filesystem state the user is presently designing.

### Duplicate

A File whose Content is identical to at least one other File in the same Portrait.

### DuplicateSet

All File instances in a Portrait having one identical Content identity, when at least two such instances exist. One DuplicateSet therefore represents exactly one distinct duplicated Content.

### Directory

For duplicate-analysis statistics, a Directory record describes Files directly contained by that directory only. Descendants are not folded into local counts. The filesystem directory/tree itself may of course contain descendants.

### DirectoryPair

An unordered pair of distinct directories having one or more distinct duplicated Contents directly represented in both directories. DirectoryPair describes local/direct shared Content only.

### Scope

A directory and all of its descendants.

### ScopePair

An unordered pair of distinct directory-rooted Scopes for which descendant DirectoryPairs provide shared-content leverage. A ScopePair can exist even when its two root directories have no directly shared Files.

The two effective sides of a ScopePair are always disjoint. If one root is a descendant of the other, the descendant subtree is omitted from the ancestor side and becomes the other effective side. Canonical pair ordering has no semantic relationship to ancestry. Identical roots are invalid.

A nested ScopePair is best understood as a **cut through a containing tree**. Moving the descendant root moves the cut: material formerly on the descendant side can move to the ancestor side. Consequently, moving a root downward can increase or decrease leverage even though no duplicate relationship in the Portrait has changed.

### Bounded

Having membership determined unambiguously by specified criteria.

### Case

A bounded set of Files in the Current Portrait, containing at least one duplicate, considered together for one coherent Disposition.

A Case is objective and program-discovered. A Situation is not required to discover a Case.

A structural Case may include unique Files because those Files can matter to the eventual Disposition. The Case boundary limits Disposition authority: evidence outside the Case may provide context, but external Files remain unchanged unless independently brought under Disposition authority.

Most importantly, **a Case need not resolve all duplication within its bounds; it should resolve the duplication that caused that Case to exist.** Internal or otherwise unrelated duplication can remain for other Cases.

The defining duplication pattern is therefore part of the Case's meaning, even when the Case bounds contain additional Files.

Contextual question:

    Case
        What Files are under consideration, and what objective duplication
        relationship caused them to be considered together?

### Situation

The asserted semantic context of a Case: what is going on that explains the Case's defining duplication pattern.

A Situation is semantic input, not an objective filesystem fact and not a prerequisite for Case discovery. Berries may constrain or rank the Situations it offers using objective Case evidence, but once the user identifies a Situation, that Situation is the operative context for proposing Resolutions unless objective contradictions emerge.

The possibility that a Situation assertion could be mistaken does not create a competing programmatic interpretation. Berries does not offer unrelated Resolutions merely because some other semantic explanation is conceivable.

Contextual question:

    Situation
        What is going on here?

A useful Situation evokes one or more potentially appropriate Resolutions that can be mapped to fully parameterized Dispositions.

### Resolution

A Situation-aware, user-facing description of a proposed outcome.

A Resolution exists to express an operational outcome naturally in the language of the selected Situation. Different Resolutions, including Resolutions belonging to different Situations, may produce the same Disposition.

A Resolution is applicable only when its resulting Disposition addresses the duplication pattern that defines the Case. It may address additional relevant Files within Case authority, but it cannot leave the defining duplication relationship untouched while claiming to resolve the Case.

Contextual question:

    Resolution
        Given this Situation, what is a natural useful outcome for the
        duplication that produced this Case?

### Disposition

The precise desired placement and retention of Files within a Case.

A Disposition is the operational meaning of an accepted Resolution. It defines the desired surviving arrangement, including approved directory mappings and destinations where applicable. It is a desired state, not a sequence of filesystem operations.

Case membership defines the maximum authority of the Disposition; it does not imply that every File within the Case must be changed. Files and internal duplication unrelated to the selected Resolution can remain untouched.

Contextual question:

    Disposition
        What outcome does the user want?
        Which Content instances/locations should remain or appear, and where?

### Directory mapping

A user-approved relationship between a source directory and a destination directory within a structural Disposition.

Example:

    OldPhotos\Family -> Photos\Family
    OldPhotos\Trips  -> Photos\Travel

Mappings can be suggested from observed DirectoryPair relationships and edited piecemeal by the user.

### Action

A primitive filesystem operation used to implement an ActionPlan. Tentative primitive operations are delete, copy, and move/rename. Directory creation/removal and metadata handling may be explicit Actions or well-defined helpers.

### ActionPlan

A deterministic logical transformation, expressed in primitive filesystem Actions, that implements a fully specified Disposition. Its ordering is sufficient to define and apply the virtual transformation, but need not be the literal order used for real filesystem execution.

### Disposition validation

Validation of a proposed Disposition before it is applied to the virtual Portrait. Validation detects collisions, ambiguities, missing parameters, and other conditions preventing the desired state from being realized unambiguously. It reports structured issues; it does not invent a solution.

### ExecutionPlan

A safe physical realization of accumulated ActionPlans against the real filesystem. It may reorder or expand logical Actions when the final result is equivalent and safety improves.

### Execution cache

Temporary verified storage used by the ExecutionPlan to secure required Content before an operation could destroy or invalidate a source needed later. The cache is execution machinery, not part of the Portrait or Disposition.

### Leverage

The number of distinct duplicated Contents whose duplicate relationship is directly addressed by the Case's defining structure.

Leverage measures reduction in duplicate-content decisions, not bytes saved and not every duplicate incidentally located inside Case bounds.

By Case type:

    DuplicateSet Case
        leverage = 1

    Single-directory Case
        leverage = distinct Contents represented more than once directly
                   in that directory

    DirectoryPair Case
        leverage = distinct duplicated Contents represented directly in
                   both directories

    ScopePair Case
        leverage = distinct duplicated Contents represented across the two
                   effective sides of the ScopePair cut

For ScopePairs, leverage therefore measures duplicated-content connectivity crossing the cut. Duplicates internal to either effective side do not contribute to that ScopePair's leverage and need not be resolved by a Resolution of that Case.

### Hidden

A Case presentation state indicating that the user has dismissed the Case without requiring an ActionPlan. Hidden Cases are omitted from the working list unless requested. Hidden does not alter the Portrait or affect inclusion of the same Files in other Cases.

## Semantic chain

The central decision chain is:

    objective duplication pattern
        -> Case
        -> Situation
        -> Resolution
        -> Disposition
        -> ActionPlan

ExecutionPlan is the safe physical realization of accumulated ActionPlans and sits outside the semantic decision chain.

The chain is intentionally compositional. Resolving one Case need not eliminate every duplicate inside its bounds. The resulting Current Portrait is analyzed again, and unresolved duplication remains available through other Cases.
