# Sheddueller Roadmap

Status: Accepted for implementation guidance  
Last updated: 2026-04-19

## Purpose

This document explains how to read the versioned specifications when implementing Sheddueller from a clean slate.

The version specs are capability slices, not instructions to build throwaway intermediate APIs. The implementation should take the most direct path to the v5-complete platform while preserving the conceptual boundaries of each spec.

## Target Product

Sheddueller is a self-hosted job scheduler for small trusted development teams. It should be cheaper, easier to test, and easier to understand than cloud-hosted job orchestration products.

The target is “Hangfire done right”:

- simple in-process hosting
- durable scheduling
- clear job state
- live logs and progress
- easy search
- safe trusted-team operations
- no enterprise compliance burden

## Aims

Sheddueller should optimize for:

- predictable local development and integration testing
- simple host integration through standard .NET hosting
- transparent job state from enqueue through terminal completion
- dynamic priorities and concurrency limits without preconfigured queues
- durable scheduling that remains understandable under failure
- live job feedback through logs and progress
- fast search by scheduler metadata and explicit domain tags
- practical operations for trusted teams when work is stuck, failed, or blocked
- provider extensibility without turning the product into a generic event bus

## End State

The v5-complete product should let a small team:

- enqueue immediate, delayed, and recurring jobs from DI-backed expression calls
- run any number of application hosts against a shared durable store
- rely on leases, retries, and cooperative cancellation for resilient execution
- use PostgreSQL as the first production backend
- see queued, running, failed, canceled, delayed, retrying, and completed jobs
- search jobs by id, handler, state, schedule, time range, and tags
- inspect live logs, progress, attempts, queue position, and job history
- understand recurring schedules, recent occurrences, and manual trigger history
- see concurrency group occupancy and blocked work
- see worker node liveness and claimed work
- use trusted no-friction actions for retry clone, cancellation, pause/resume, and trigger-now
- view rolling in-app metrics for queue health, throughput, failures, latency, saturation, and worker health

The v5-complete product should not require:

- cloud scheduler infrastructure
- an external dashboard product
- a separate event bus
- a compliance/audit subsystem
- multi-tenant administration
- OpenTelemetry or Prometheus to understand basic scheduler health

Sheddueller is not intended to be:

- an enterprise event bus
- a workflow/orchestration engine
- a multi-tenant control plane
- a compliance/audit system

## Implementation Strategy

Build toward the final v5 shape from the start.

Do not implement an earlier public API that a later spec intentionally changes. The clearest example is job submission: v1 already uses the final cancellation-aware expression shape, so there is no intermediate non-cancelable handler API to build.

Recommended implementation milestones:

1. Final core runtime: cancellation-aware expressions, priorities, concurrency groups, serializer boundary, retries, leases, delayed jobs, recurring schedules, tags, and final job state model.
2. In-memory provider: proof and test provider for scheduler semantics, not the production target.
3. PostgreSQL provider: first production backend, with schema shaped to avoid immediate churn for dashboard/events/actions.
4. Dashboard foundation: jobs-only read UI, job search, logs, progress, queue position, and durable events.
5. Trusted operations console: job actions, schedule views/actions, group views, node health, and rolling in-app metrics.

## Capability Matrix

| Capability | Spec | Implementation guidance |
| --- | --- | --- |
| Immediate job enqueue | v1 | Core runtime baseline. |
| Cancellation-aware handlers | v1 | Implement from the start; do not build the older non-cancelable shape. |
| Priorities | v1 | Core scheduling invariant. |
| Dynamic concurrency groups | v1 | Core scheduling invariant; dashboard editing deferred beyond v5. |
| Backend abstraction | v1 | Required for in-memory and PostgreSQL providers. |
| In-memory provider | v1 | Test/proof provider. |
| Delayed jobs | v2 | Implement before PostgreSQL so SQL schema can include due-time fields once. |
| Retries | v2 | Implement before dashboard actions; retry clone in v5 depends on retry metadata. |
| Lease/heartbeat recovery | v2 | Required before production PostgreSQL. |
| Recurring schedules | v2 | Required before dashboard schedule views/actions. |
| PostgreSQL provider | v3 | First durable production provider. |
| Dashboard read/event contracts | v4 | Provider-agnostic dashboard contracts live outside core scheduler APIs. |
| Job tags/search | v4 | Implement metadata storage before PostgreSQL schema settles. |
| Job logs/progress | v4 | Durable events underpin live dashboard and later operations history. |
| Queue position | v4 | Uses scheduler claim order and blocked/delayed reasons. |
| Trusted job actions | v5 | Retry clone and cooperative cancel. |
| Schedule views/actions | v5 | Pause, resume, trigger-now; no dashboard schedule editing. |
| Concurrency group views | v5 | View-only until source-of-truth semantics for edits are specified. |
| Worker node health | v5 | Based on scheduler worker heartbeat records. |
| Rolling in-app metrics | v5 | Dashboard metrics only; no exporter requirement. |

## Glossary

- Job: The runtime, storage, and user-facing work unit.
- Schedule: A recurring definition identified by a schedule key.
- Occurrence: One job materialized from a recurring schedule fire.
- Provider: A storage/runtime implementation behind the scheduler abstraction, such as in-memory or PostgreSQL.
- Dashboard-compatible provider: A provider that implements the dashboard read/event contracts from v4 and operation contracts from v5.
- Node: One running application host participating in the scheduler cluster.
- Cluster: All nodes sharing one logical scheduler store.
- Concurrency group: A dynamic named capacity limit shared across the cluster.
- Queue position: A dynamic 1-based position among currently claimable jobs in scheduler claim order.
- Trusted operation: A dashboard action intended for small teams where everyone with access is trusted.

## Non-Goal Semantics

Each version's non-goals are scoped to that version unless explicitly marked permanent. A later spec may intentionally add a feature that an earlier spec excluded.

Examples:

- v4 excludes operator actions; v5 adds trusted operator actions.
- v3 excludes public inspection/query APIs; v4 adds dashboard contracts in the dashboard package.
- v1 excludes retries and recurring schedules; v2 adds them.

## Product Boundary

Keep the roadmap focused on the target user.

Prefer:

- practical state visibility
- safe no-friction recovery actions
- understandable dashboard behavior
- easy local/integration testing

Avoid unless a future product decision changes the target:

- audit trails with user attribution
- role/permission systems inside Sheddueller
- multi-tenancy
- handler payload migration frameworks
- workflow/dependency orchestration
- external metrics exporter requirements
