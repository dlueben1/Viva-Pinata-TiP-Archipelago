---
name: ghidra-name-sync
description: Reconcile verified Viva Piñata research with Ghidra function names using evidence-based confidence prefixes.
argument-hint: "[scope: subsystem, address/range, or recent research] [max=25]"
agent: agent
---

# TiP / ReTiP — Evidence-based Ghidra function naming pass

Maintain understandable, honest function names in the existing **GhidraTiP / default.xex** analysis database. This is a repeatable, _incremental_ annotation pass, not a request to decompile or rename every function.

Interpret any text appended to `/ghidra-name-sync` as the requested focus and optional batch limit. Examples: `shop unlocks max=25`, `regions`, `0x825B2508`, `recent research`. If no focus is given, use the newest well-supported `.research/` discoveries that have not already been reconciled. Default maximum: **25 functions changed per run**. Do not do an unbounded scan.

## 1. Preconditions and sources

1. Read `.research/index.md`, relevant topic reports, and `.research/memory-map.md`. Read `.research/ghidra/function-name-ledger.jsonl` if present. If the ledger is absent, create it when the first change is committed.
2. Connect to Ghidra via the configured Ghidra MCP. Confirm the **actual active program** is `default.xex` in `GhidraTiP`, with Xbox **guest** addresses and the expected PowerPC big-endian analysis. Confirm that the tools support reading the current function/symbol and a supported way to rename it. If not, **stop with a proposed rename list**; do not pretend Ghidra was updated.
3. Treat the existing research as a shortlist of candidates, **not** an instruction to rename without checking. Verify original instructions, calling relationships, data accesses, relevant tables, current Ghidra function boundaries, and existing symbols as necessary. Distinguish original Xbox guest addresses from host RVAs, pack-file offsets, and transient heap pointers.
4. Check that the Ghidra project has a recoverable pre-change state (existing reliable backup/checkpoint, or a supported newly created checkpoint). Before writes, record current names and addresses to an external, persistent change plan that can restore them. If neither recovery nor durable journaling can be assured, perform a read-only naming proposal and stop.
5. Never use a wiki page, guessed decompiler parameter name, one suggestive string, or another AI's tentative label as sufficient proof by itself. Static-confirmed behavior is valid evidence even when not runtime-tested; describe that limitation accurately.

## 2. Naming convention — exact confidence hierarchy

Use lowerCamelCase for the action portion where practical, and a stable subsystem prefix, e.g. `shopSource_hasAvailableTag` or `trap_calculateEscape`. Names must be valid, unique Ghidra symbols.

| Evidence level                             | Form                                                    | Meaning                                                                                                                                    |
| ------------------------------------------ | ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| **Confirmed**                              | `subsystem_actionObject` (**no confidence prefix**)     | Exact relevant function responsibility is established by strong original-code/data evidence, with important caveats understood.            |
| **Likely**                                 | `likely_subsystem_actionObject`                         | Multiple independent clues converge on a particular purpose, but a material edge remains unverified.                                       |
| **Maybe**                                  | `maybe_subsystem_actionObject`                          | General subsystem/role appears supported, but the exact contract, ownership, or direction remains uncertain.                               |
| **Unknown with a distinctive observation** | `unknown_observedBehavior`                              | The overall role is unclear, but a concrete, specific operation is directly evidenced. Describe _what it does_, not what we hope it means. |
| **Insufficient evidence**                  | Keep `FUN_...`, `sub_...`, or the existing neutral name | No genuinely useful, supported name yet.                                                                                                   |

`identifiedName` in the project convention means a **confirmed name without any `identified_` prefix**. Only use `likely_`, `maybe_`, and `unknown_` when appropriate. Do not use `confirmed_` or `identified_` prefixes.

Name the **whole observed function**, not one interesting instruction inside a much broader routine. Prefer `unknown_dispatchesMessage298` to falsely naming a broad dispatcher `shop_grantItem`. Avoid labels like `unknown_shopStuff`, unsupported `unlock`/`grant` claims, or purely address-derived names that add no meaning.

Do **not** treat a runtime-unverified function as automatically `likely_`: a complete, independently verified static contract may be confirmed. Conversely, a function called `grant` in a prior note may deserve `likely_` if the full caller/side-effect contract is still uncertain.

## 3. Reconciliation and promotion/demotion

For each candidate, inspect its **current** name and the previously recorded evidence before choosing an action:

- Neutral/generated -> best supported confidence-qualified name.
- `unknown_` -> `maybe_` -> `likely_` -> unprefixed confirmed when **new evidence actually justifies** the change.
- Correct an obsolete descriptive portion if evidence clarifies function purpose; do not simply strip a prefix.
- **Demote or replace** a name when new contradictory evidence warrants it. Record the contradiction and superseded evidence.
- Leave correctly named functions unchanged; make the workflow idempotent on reruns.
- Respect existing meaningful user-defined names and namespaces. Do not overwrite a prior human name without inspecting its basis; if uncertain, log a proposal for review rather than clobbering it.
- Do not rename imported symbols, trampolines, thunks, SDK generated wrappers, branch targets that are not true function entries, or linked aliases as though they are ordinary original-game functions. Preserve known original/exported names.
- Check for collisions in the function's namespace; never force a rename that would replace another symbol. If necessary, propose a clearer domain-qualified name or stop for that candidate.

Use existing research only when it directly supports the _specific guest entry address_. A named callsite inside a function is not automatically an independent function entry.

## 4. Safe execution

1. Produce a **pre-change manifest** with guest entry address, existing name, proposed name, confidence, exact evidence/report locations, and one-sentence rationale. Exclude ambiguous candidates from writes.
2. Apply renames in a **small, bounded batch** through supported Ghidra MCP operations or a properly authorized Ghidra API script using ordinary Ghidra transactions and `SourceType.USER_DEFINED`. If an operation lacks supported semantics or fails, stop or skip rather than guessing an API signature.
3. Only permitted Ghidra changes: **function symbol names**, and optional concise annotation comments **without overwriting existing comments**. No instruction/data bytes, disassembly, function boundaries, signatures, types, variables, references, patches, analysis settings, or program structure changes.
4. Read each symbol back after rename; verify the intended address, name, namespace, and absence of collision. Record errors and partial progress faithfully. If a batch errors, use the supported transaction rollback or revert the affected names from the pre-change manifest when safe; never claim all-or-nothing success without verification.
5. Do not launch or attach to ReTiP. Do not call guest functions, change installed binaries, modify original game assets, or touch saved games. Do not edit the prepared native membership-invocation experiment.
6. Save/commit Ghidra annotations only through its supported project workflow, respecting active transactions and project state. Never claim persistence unless verified.

## 5. Durable ledger and research synchronization

Append one JSON object **per successfully verified changed function** to `.research/ghidra/function-name-ledger.jsonl` with at least:

- `guest_address`, `old_name`, `new_name`, `confidence`, `rationale`
- `evidence` (file paths and section identifiers or instruction addresses)
- `operation` (`initial`, `promote`, `demote`, `correct`)
- `run_timestamp`, `program_id`, and `verified_in_ghidra: true`

Keep old records so name history remains auditable; the latest valid entry for an address is its current naming recommendation. If the current Ghidra name differs from the ledger, do not blindly restore either one: inspect and flag the drift.

Write a compact per-run report under `.research/ghidra/naming-runs/` containing the full proposed/changed/skipped/conflicted table, any rollback detail, and unresolved next candidates. The ledger and report are evidence tracking, not substitutes for checking the live Ghidra symbol.

Update `.research/index.md` only if a genuinely new report merits indexing. Update `.research/memory-map.md` **only for newly verified function behaviors/addresses**, not simply because a function was renamed. Keep naming ledger/reports in the Git-ignored research directory.

## 6. Final response

Report:

1. Actual active Ghidra program and whether writes were committed/verified.
2. Number reviewed, newly named, promoted, demoted/corrected, unchanged, skipped, and failed.
3. A compact address / old name -> new name / confidence / evidence table.
4. Any name conflicts, important uncertainty, or contradicted previous assumptions.
5. The most useful scope for the **next** naming pass.

If Ghidra MCP is unavailable or unsafe to write, provide the read-only proposal instead. **Never state that Ghidra was renamed merely because notes or a script were generated.**
