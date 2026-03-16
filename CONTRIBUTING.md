# CONTRIBUTING.md - Contribution Guidelines

This repository values maintainability, architectural clarity, and steady evolution over short-term convenience.

This file is meant to guide contributors and to give reviewers a shared standard for evaluating changes.

Read `PROJECT.md` first. It defines the architectural direction of the repo. This document focuses on how to contribute within that direction.

## Contribution Mindset

Good contributions usually do a few things well:

- they solve a concrete problem without expanding scope unnecessarily
- they fit naturally into the current architecture
- they keep responsibilities clear
- they make future changes easier rather than harder
- they leave the codebase more understandable than before

The bar is not just whether the code works. The bar is whether the code remains easy to extend and reason about after the change lands.

## General Expectations

### Preserve architectural clarity

Before adding code, identify the correct layer for it.

- CLI concerns should stay in the CLI layer.
- transport and lifecycle concerns should stay centralized
- Unity editor execution should stay in the bridge package
- docs and packaging should reflect the product rather than reshape it

If a change crosses multiple layers, the boundaries should still remain obvious.

### Prefer evolution over reinvention

Contributors should generally extend existing domains, flows, and structures before introducing new ones.

That means:

- build on current command families where possible
- extend established bridge domains before creating new categories
- avoid parallel abstractions that solve the same problem in a different style

New structure is justified when it makes the system clearer at scale, not just when it is locally convenient.

### Keep changes legible

The codebase should remain easy to navigate by someone who did not author the change.

Prefer:

- focused diffs
- clear naming
- explicit data flow
- local reasoning over hidden behavior
- straightforward control flow over clever compression

### Respect the runtime reality

UCP spans a CLI, a networked bridge, and Unity Editor internals. Small changes can have operational consequences.

Contributions should keep that in mind by preserving:

- predictable lifecycle behavior
- stable protocol expectations
- clear failure behavior
- compatibility with ordinary Unity editor workflows

## Tech-Stack Guidance

### Rust CLI

Contributions to the Rust CLI should aim for:

- focused modules
- explicit context and input handling
- clean separation between orchestration, transport, and presentation
- output that remains usable for both humans and automation

The CLI should stay readable as an orchestration layer even as the command surface grows.

### Unity Bridge

Contributions to the Unity bridge should aim for:

- clear editor-only ownership
- pragmatic controller organization
- simple RPC behavior
- safe use of Unity editor APIs and serialization paths
- behavior that matches Unity expectations instead of fighting them

The bridge should remain robust and understandable, not over-abstracted.

### Docs And Metadata

Documentation and release metadata are part of the maintenance surface.

If a change affects the public surface, architecture, workflow, or packaging model, update the relevant docs and metadata in the same workstream.

## Pull Request Guidance

Strong PRs usually have these qualities:

- one coherent purpose
- clear architectural fit
- a diff that is easy to review
- enough validation for the level of risk introduced
- documentation updates when they are materially needed

Weak PRs usually have these qualities:

- mixed concerns
- unnecessary abstraction
- duplicate structure
- undocumented behavioral change
- changes that make future ownership less obvious

## Issue Guidance

Good issues usually describe:

- the real workflow or product problem
- the affected subsystem or command area
- the expected outcome
- the current limitation or failure
- enough context to place the work in the existing architecture

The most useful issues are grounded in real product behavior, not vague redesign requests.

## Review Standard

When reviewing contributions, the main questions are:

- does this fit the existing architecture cleanly?
- does it improve or weaken maintainability?
- does it preserve clear ownership and boundaries?
- does it make future work easier in the same area?
- does it keep the product understandable for both humans and automation?

## Final Rule

Contributions should move the repository toward greater clarity and better long-term extensibility.

If a change works today but makes the next change harder, it is probably not the right shape yet.
