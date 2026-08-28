# Berries Problem Boundary — Empirical Findings

This document records empirical findings about Berries' practical problem boundary. It is research context, not a competing workflow specification. Governing behavior is defined in `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`.

## What the experiments established

Berries reliably discovers real duplicate structure. The difficult question is not whether Contents are identical but which duplication is useful for the user to act on.

Real corpora contain both user-managed organization and application/generated structure. Exact duplicate identity alone does not explain why copies exist.

Developer/source corpora were intentionally useful stress tests because they contain repositories, generated output, package material, copied source trees, saved web assets, and many repeated support files. They exposed failure modes early.

## File-centric duplication can manufacture container noise

Widely repeated Contents can induce many DirectoryPairs and apparent Branch relationships even when the containing directories do not represent one coherent user question.

The experimental distributed-DuplicateSet checklist demonstrated this dramatically: resolving a relatively small number of repeated Contents removed a large amount of downstream structural evidence.

The lasting lesson is not the checklist itself. The checklist is obsolete in the intended UI. The lasting lesson is:

    file-centric and container-centric duplication are different useful viewpoints

and:

    low-level repeated Content can manufacture weak higher-level structure

The Content projection and ordinary Exclude operation now provide the general mechanism for removing such material from the working Corpus when the user does not want Berries to consider it.

## Application-managed/generated material

Examples observed in experiments include Git hook samples and object storage, build outputs, DLL/PDB groups, UUID-like generated artifacts, saved-web-page support files, package/runtime trees, and other repeated infrastructure.

These can be genuine duplicate Content and can even reveal meaningful ancestry/relationship among branches. But that does not make them automatically useful deletion targets.

Rather than encode application-specific knowledge into Berries, the practical design now gives the user direct control over the logical Corpus:

    selected roots add material
    Exclude subtracts material

`Berries.config [exclude]` provides persistent/automatic subtraction for known unwanted paths/patterns. Interactive Exclude provides the same semantic result during a session.

This is intentionally simpler than the earlier proposed distinction between "analytically visible" and "destructively protected" regions. Such a protection taxonomy is not currently required by the governing design.

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

Here the user is the authority on organization, and duplicate relationships can support meaningful structural operations such as the explicit source/destination Move semantics now defined in `WORKFLOW.md`.

## Structural discovery findings

Exhaustive BranchPair enumeration is not necessary to exploit structural evidence. Large-corpus experiments showed combinatorial growth without proportional user benefit.

Cheap first-class Branch statistics plus targeted counterpart search repeatedly surfaced recognizable high-value structures, including known copied/moved trees. This materially expands the practical corpus size Berries can address without requiring comprehensive pair generation.

Boundary precision also proved less important than initially feared. A mathematically broader Branch counterpart can differ from the intuitively neat child boundary by only one duplicated Content and still support the same useful user operation. Do not complicate ranking solely to perfect such boundaries unless Explorer use demonstrates a real resolution difference.

## Current practical boundary

The present working boundary is deliberately simple:

1. Berries scans the roots the user chooses.
2. The user/configuration can Exclude material that should not participate.
3. Duplicate resolution operations target duplicate FileInstances only.
4. Unique files remain known because they can constrain valid operations, but general unique-file maintenance is out of scope.
5. Rename and general filesystem reorganization are out of scope.
6. Berries does not need semantic knowledge of Git, build systems, package managers, browsers, or other producers.

This boundary can be widened later if real use demonstrates a coherent need. It should not be widened merely because another special filesystem phenomenon can be detected.

## Situation catalogue implications

The discovery of a new producer-specific artifact does not justify a new Situation. Situations remain optional semantic vocabulary, and the Explorer no longer requires Situation -> Resolution -> Disposition classification before the user can act.

The useful criterion from the earlier research remains:

    A useful Situation evokes one or more natural resolutions that can be
    mapped to fully specified operations/outcomes.

But ordinary Exclude/Delete/Move can be performed directly when the user's intention is already clear.

## Questions intentionally left empirical

The following are worth observing during real Explorer use rather than solving in advance:

- How often users need explicit Situation labels once direct operations and Pivot exist.
- Whether recurring Exclude patterns motivate richer configuration/rules.
- Whether same-directory duplication needs specialized presentation beyond ordinary projections.
- Whether any application-managed material needs a future "visible but protected" state rather than simple Exclude.
- Whether the current Suggested-Case heuristics consistently find useful work across non-source corpora.
- Whether saved sessions are valuable enough to justify persistence.

These are empirical product questions, not blockers for the next implementation revision.
