# Berries Problem Boundary — Empirical Findings

This document records empirical findings that shaped Berries' current practical boundary. It is research context, not a competing workflow specification. Governing behavior is defined in `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`.

## What the experiments established

Berries reliably discovers real duplicate structure. The difficult question is not whether files are byte-identical but which duplication is useful for the user to act on.

Real corpora contain both user-managed organization and application/generated structure. Exact duplicate identity alone does not explain why copies exist.

Developer/source corpora were intentionally useful stress tests because they contain repositories, generated output, package material, copied source trees, saved web assets, and many repeated support files. They exposed analysis failure modes early.

## File-centric duplication can manufacture container noise

Widely repeated content can induce many Directory Pairs and apparent Branch relationships even when the containing directories do not represent one coherent user question.

Earlier experiments with a special distributed-duplicate checklist demonstrated this dramatically: resolving or removing a relatively small number of repeated contents could collapse a large amount of downstream structural evidence.

The lasting lesson is not that experimental checklist. The current Explorer does not use it. The lasting lessons are:

    file-centric and container-centric duplication are complementary viewpoints

and:

    low-level repeated content can manufacture weak higher-level structure

The current Group projection plus ordinary Exclude gives the user a general way to remove such material from the Working Portrait when it should not participate in analysis.

## Application-managed/generated material

Observed examples included Git hook samples and object storage, build outputs, DLL/PDB groups, UUID-like generated artifacts, saved-web-page support files, package/runtime trees, and other repeated infrastructure.

These can be genuine duplicate files and can even reveal real ancestry/relationships among branches. That does not make them automatically useful deletion targets.

Berries therefore avoids producer-specific semantic rules. The practical boundary is instead controlled by Corpus choice and exclusion:

    selected roots define what is scanned
    Berries.config [exclude] filters known unwanted paths during acquisition
    interactive Exclude removes files from the Working Portrait during a session

No additional "analytically visible but protected" taxonomy is currently implemented.

## User-managed material remains the clearest target

Berries is particularly well matched to accumulated user-managed trees such as:

    documents and photographs
    downloads
    old machine copies
    backups and archives
    migrations
    partial moves
    reorganized directory trees
    staging/import areas

Here the user is the authority on organization, and duplicate relationships can support useful explicit operations such as the current source/destination Move behavior.

## Structural discovery findings

Exhaustive Branch-Pair enumeration is unnecessary to exploit structural evidence. Large-corpus experiments showed combinatorial growth without proportional user benefit.

The current practical alternative is:

    direct Directory evidence
    first-class Branch statistics
    bounded Branch seed ranking
    targeted counterpart search
    on-demand best-counterpart search for a selected Branch

This repeatedly surfaced recognizable high-value structures while remaining tractable on much larger corpora.

Boundary precision also proved less important than initially feared. A mathematically broader counterpart can differ from an intuitively neat child boundary by little duplicated content and still support the same useful user operation. Ranking should not become substantially more complicated merely to perfect such boundaries unless real Explorer use shows a resolution difference.

## Current practical boundary

The current implementation can be summarized as follows:

1. Berries scans the disjoint roots the user chooses.
2. Configuration exclusion can remove known paths before they enter the session.
3. Duplicate-resolution commands target duplicate files represented in current Groups.
4. Unique files remain in the Portrait because they can constrain valid operations such as Move destination paths; they are not general maintenance targets.
5. Interactive Exclude/Delete/Move alter the virtual Working Portrait immediately.
6. Rename and general unique-file reorganization are outside scope.
7. Berries does not require semantic knowledge of Git, build systems, package managers, browsers, or other producers.
8. Structural analysis is deliberately targeted rather than exhaustive.
9. Physical filesystem changes occur only at Execute.

This boundary can widen later if real use demonstrates a coherent need. It should not widen merely because another detectable filesystem phenomenon exists.

## Situation-catalogue implications

The discovery of a new producer-specific artifact does not justify a new Situation.

`SITUATIONS.md` is now explicitly retained research rather than current workflow. The useful historical criterion remains:

    A useful Situation evokes one or more natural resolutions that can be
    mapped to fully specified operations/outcomes.

But the current Explorer does not require Situation -> Resolution -> Disposition classification. When the user's intent is clear, Exclude/Delete/Move expresses it directly.

## Current implementation consequences

Several important product choices came directly from the empirical work:

- the UI says Group rather than exposing internal DuplicateSet terminology;
- Group, Directory, Branch, Directory Pair, Branch Pair, and Corpus Roots are projections over one Working Portrait rather than separate workflow stages;
- Suggest is an attention aid, not a mandatory Case queue;
- comprehensive Branch-Pair generation was dropped in favor of targeted search;
- the Initial/Working Portrait retains unique files for operational constraints even though ordinary Explorer operations remain duplicate-oriented;
- broad derived-analysis invalidation is currently preferred over complicated incremental bookkeeping;
- after portrait operations, expensive derived analysis can refresh in the background without taking over the current Explorer view.

## Questions intentionally left empirical

The following remain appropriate to learn from real use rather than solve speculatively:

- How often semantic Situation labels would add value beyond direct operations.
- Whether recurring interactive exclusions motivate richer persistent configuration/rules.
- Whether same-directory duplication needs specialized presentation beyond Group/Directory views.
- Whether any application-managed material needs a future "visible but protected" state rather than simple exclusion.
- Whether the current Suggest/targeted-counterpart heuristics consistently find useful work across varied non-source corpora.
- Whether saved sessions are valuable enough to justify persistence.
- Whether analysis recomputation should become independently lazy/validity-driven rather than the current coarse invalidation and refresh model.

These are empirical development questions, not contradictions in the current implementation.
