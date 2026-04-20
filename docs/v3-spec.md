# Sheddueller v3 Specification

Status: Accepted for implementation  
Last updated: 2026-04-19

## Relationship To V1 And V2

This document extends [v1](v1-spec.md) and [v2](v2-spec.md). V3 does not change the core task or recurring-schedule programming model. It standardizes the first durable production backend: PostgreSQL.

V3 is provider-specific by design. It adds a separate PostgreSQL assembly with concrete schema, claiming, migration, and notification behavior.

## Summary

Sheddueller v3 is about a first-class PostgreSQL backend.

V3 is defined by six capabilities:

- A dedicated PostgreSQL provider package built on `Npgsql`.
- A provider-owned PostgreSQL schema and migration flow.
- Transactional task claiming via `FOR UPDATE SKIP LOCKED`.
- Database-authoritative time for due work, leases, retries, and recurring schedules.
- Low-latency wakeups with `LISTEN/NOTIFY` plus polling fallback.
- Testable PostgreSQL schema and operational behavior without adding public inspection APIs.

V3 keeps the backend abstraction in place, but PostgreSQL becomes the first durable provider with a fully specified runtime model.

V3 supports PostgreSQL 14 and later. PostgreSQL 14 is the minimum because it is the oldest community-supported PostgreSQL major version at the time of implementation and includes all provider primitives required by v3, including `FOR UPDATE SKIP LOCKED`, advisory locks, `LISTEN/NOTIFY`, arrays, `bytea`, `timestamptz`, and `transaction_timestamp()`.

## Goals

- Deliver a production-grade PostgreSQL provider as a separate assembly.
- Preserve the v1/v2 app-facing enqueueing and scheduling APIs.
- Define the PostgreSQL schema and runtime behavior precisely enough that different implementations would behave the same way.
- Keep the provider registration surface small and centered on `NpgsqlDataSource`.
- Support explicit schema creation and upgrades without silently mutating production databases during normal scheduler startup.

## Non-Goals

Non-goals are scoped to v3 unless explicitly marked permanent.

- New scheduler semantics beyond what v2 already defined.
- MySQL, SQLite, or other relational providers.
- A backend-agnostic dashboard or generic query API.
- Public task, schedule, or schema inspection APIs.
- Querying or decoding opaque serialized payloads in PostgreSQL.
- Automatic schema application during ordinary hosted-service startup.

## Public API Shape

The PostgreSQL provider lives in a separate assembly/package, assumed to be `Sheddueller.Postgres`.

### Provider Registration

```csharp
public static class ShedduellerPostgresBuilderExtensions
{
    public static ShedduellerBuilder UsePostgres(
        this ShedduellerBuilder builder,
        Action<ShedduellerPostgresOptions> configure);
}

public sealed class ShedduellerPostgresOptions
{
    public required NpgsqlDataSource DataSource { get; init; }
    public string SchemaName { get; set; } = "sheddueller";
}
```

Requirements:

- `UsePostgres` registers the PostgreSQL implementation of the existing scheduler storage/runtime hooks.
- `DataSource` is required and is the primary integration point for PostgreSQL connectivity.
- `SchemaName` is configurable per cluster and defaults to `sheddueller`.
- One logical Sheddueller cluster maps to one configured PostgreSQL schema.
- V3 does not make raw connection strings the primary configuration contract.

### Schema Migration

```csharp
public interface IPostgresMigrator
{
    ValueTask ApplyAsync(
        CancellationToken cancellationToken = default);
}
```

Requirements:

- `ApplyAsync` is the only public schema-management operation in v3.
- `ApplyAsync` creates the provider schema if missing and applies all pending provider-owned migrations to the latest supported schema version.
- `ApplyAsync` must serialize schema changes so only one migrator runs at a time for a given configured schema.
- `ApplyAsync` stamps the schema version only after a successful migration run.
- Normal scheduler startup never calls `ApplyAsync` automatically.

## PostgreSQL Schema

All provider tables live under the configured PostgreSQL schema name.

### `schema_info`

Purpose:

- Store the current provider-managed schema version for the configured schema.

Required columns:

| Column | Type | Notes |
| --- | --- | --- |
| `singleton_id` | `smallint` | Fixed value `1`; primary key. |
| `schema_version` | `integer` | Current applied schema version. |
| `applied_at_utc` | `timestamptz` | When the current version was applied. |

### `tasks`

Purpose:

- Persist queued, claimed, terminal, delayed, retried, and recurring-materialized tasks.

Required columns:

| Column | Type | Notes |
| --- | --- | --- |
| `task_id` | `uuid` | Primary key. |
| `state` | `text` | `Queued`, `Claimed`, `Completed`, `Failed`, `Canceled`. |
| `priority` | `integer` | Higher numbers run first. |
| `enqueue_sequence` | `bigint` | Monotonic identity used for FIFO within equal priority. |
| `enqueued_at_utc` | `timestamptz` | Database time at insertion. |
| `not_before_utc` | `timestamptz` | Null or due time for delayed/retried work. |
| `service_type` | `text` | Unambiguous service type identity. |
| `method_name` | `text` | Target method name. |
| `method_parameter_types` | `text[]` | Parameter type identities. |
| `serialized_arguments` | `bytea` | Opaque serializer-owned payload. |
| `attempt_count` | `integer` | Attempts already consumed. |
| `max_attempts` | `integer` | Effective attempt limit. |
| `retry_backoff_kind` | `text` | `Fixed`, `Exponential`, or null. |
| `retry_base_delay_ms` | `bigint` | Null when retries are disabled. |
| `retry_max_delay_ms` | `bigint` | Null when no max cap exists or retries are disabled. |
| `claimed_by_node_id` | `text` | Owning node id for active claims. |
| `lease_token` | `uuid` | Ownership token for the active claim. |
| `lease_expires_at_utc` | `timestamptz` | Lease expiry for the active claim. |
| `last_heartbeat_at_utc` | `timestamptz` | Last successful heartbeat. |
| `completed_at_utc` | `timestamptz` | Terminal success timestamp. |
| `failed_at_utc` | `timestamptz` | Terminal failure timestamp. |
| `canceled_at_utc` | `timestamptz` | Pending-cancel timestamp. |
| `failure_type_name` | `text` | Best-effort failure type. |
| `failure_message` | `text` | Best-effort failure message. |
| `failure_stack_trace` | `text` | Best-effort failure stack trace. |
| `source_schedule_key` | `text` | Recurring schedule key when materialized from a schedule. |
| `scheduled_fire_at_utc` | `timestamptz` | Cron fire time that produced the task. |
| `retry_clone_source_task_id` | `uuid` | Original failed task when created by a later retry-clone operation; nullable in v3 behavior. |
| `cancellation_requested_at_utc` | `timestamptz` | Later dashboard-driven cancellation request timestamp; nullable in v3 behavior. |
| `cancellation_observed_at_utc` | `timestamptz` | Later cooperative cancellation observation timestamp; nullable in v3 behavior. |

### `task_concurrency_groups`

Purpose:

- Normalize task-to-group membership for claim checks and occupancy accounting.

Required columns:

| Column | Type | Notes |
| --- | --- | --- |
| `task_id` | `uuid` | Foreign key to `tasks`. |
| `group_key` | `text` | Concurrency group key. |

Primary key:

- (`task_id`, `group_key`)

### `concurrency_groups`

Purpose:

- Store configured limits and the current in-use claim count per group.

Required columns:

| Column | Type | Notes |
| --- | --- | --- |
| `group_key` | `text` | Primary key. |
| `configured_limit` | `integer` | Null means the v1/v2 default effective limit of `1`. |
| `in_use_count` | `integer` | Number of active claimed tasks in the group. |
| `updated_at_utc` | `timestamptz` | Database time of last mutation. |

### `recurring_schedules`

Purpose:

- Persist recurring schedule definitions and the next due fire time.

Required columns:

| Column | Type | Notes |
| --- | --- | --- |
| `schedule_key` | `text` | Primary key. |
| `cron_expression` | `text` | UTC minute-precision cron expression. |
| `is_paused` | `boolean` | Pause state. |
| `overlap_mode` | `text` | `Skip` or `Allow`. |
| `priority` | `integer` | Inherited by materialized tasks. |
| `service_type` | `text` | Unambiguous service type identity. |
| `method_name` | `text` | Target method name. |
| `method_parameter_types` | `text[]` | Parameter type identities. |
| `serialized_arguments` | `bytea` | Opaque serializer-owned payload. |
| `max_attempts` | `integer` | Effective attempt limit for materialized tasks. |
| `retry_backoff_kind` | `text` | `Fixed`, `Exponential`, or null. |
| `retry_base_delay_ms` | `bigint` | Null when retries are disabled. |
| `retry_max_delay_ms` | `bigint` | Null when no max cap exists or retries are disabled. |
| `next_fire_at_utc` | `timestamptz` | Next due occurrence; active schedules must have a non-null value. |
| `created_at_utc` | `timestamptz` | Database time of first insert. |
| `updated_at_utc` | `timestamptz` | Database time of last upsert. |

### `schedule_concurrency_groups`

Purpose:

- Normalize schedule-to-group membership so materialized tasks can inherit group keys deterministically.

Required columns:

| Column | Type | Notes |
| --- | --- | --- |
| `schedule_key` | `text` | Foreign key to `recurring_schedules`. |
| `group_key` | `text` | Concurrency group key. |

Primary key:

- (`schedule_key`, `group_key`)

### Forward-Compatible Dashboard Tables

Purpose:

- Avoid immediate schema churn when implementing the planned v4/v5 dashboard, telemetry, and trusted operations features.

Provider implementations may create these tables during the v3 schema even though strict v3 runtime behavior does not yet depend on them:

- `task_tags`: normalized task tag key/value pairs.
- `schedule_tags`: normalized schedule tag key/value pairs.
- `dashboard_events`: durable job, schedule, node, and system events.
- `worker_nodes`: worker node heartbeat and concurrency state.
- `schedule_occurrences`: materialized schedule occurrence history.

Requirements:

- These tables must not change v3 scheduler behavior.
- Opaque serialized payload bodies must not be copied into dashboard/event tables.
- Implementations targeting only the strict v3 feature set may leave these tables unused until v4/v5 features are implemented.
- The v3 implementation creates the core v3 runtime schema only. Dashboard/event tables are deferred until their v4/v5 contracts are implemented.

### Required Indexes

The PostgreSQL provider must create indexes sufficient for these hot paths:

- Claim scan over due queued tasks ordered by `priority DESC, enqueue_sequence ASC`.
- Due-time scan over queued delayed/retried tasks by `not_before_utc`.
- Lease-expiry scan over claimed tasks by `lease_expires_at_utc`.
- Due recurring-schedule scan over active schedules by `next_fire_at_utc`.
- Overlap checks over non-terminal tasks by `source_schedule_key`.
- Group membership lookups by `group_key` for both task and schedule group tables.

## Runtime Semantics

### Authoritative Time

- PostgreSQL server time is authoritative for all provider decisions about due work, lease expiry, retry backoff, terminal timestamps, and recurring schedule advancement.
- The provider must use PostgreSQL time inside SQL operations rather than application-host clocks.
- `transaction_timestamp()` is the normative timestamp source for transactional claim, completion, recovery, and schedule-materialization operations.

### Startup Compatibility Check

- Normal scheduler startup must read `schema_info.schema_version`.
- If the configured schema does not exist, the version row is missing, or the version is not exactly the provider's expected version, provider startup must fail before workers begin claiming or materializing any work.
- This compatibility check trusts the recorded version number. V3 does not require deep schema introspection.

### Task Claiming

- Claiming runs in a PostgreSQL transaction using `READ COMMITTED`.
- Candidate tasks are selected from `tasks` where:
  - `state = 'Queued'`
  - `not_before_utc` is null or less than or equal to `transaction_timestamp()`
- Candidate selection order is `priority DESC, enqueue_sequence ASC`.
- Candidate selection uses `FOR UPDATE SKIP LOCKED`.
- For each candidate in order:
  - read the candidate's group keys from `task_concurrency_groups`
  - ensure a `concurrency_groups` row exists for each group key
  - lock the corresponding `concurrency_groups` rows in ascending `group_key` order
  - verify `in_use_count < COALESCE(configured_limit, 1)` for every group
  - if all checks pass, update the task to `Claimed`, increment `attempt_count`, assign `lease_token`, set `lease_expires_at_utc`, and increment each group's `in_use_count`
  - if any group is saturated, leave the task queued and continue scanning later candidates in the same global order
- A successful claim commits exactly one claimed task.

### Completion, Failure, Cancellation, And Recovery

- Terminal transitions run in a transaction and validate the current `lease_token`.
- Completing, failing, canceling, or recovering a task must decrement `in_use_count` for every referenced group exactly once.
- Group rows must be locked in ascending `group_key` order before decrementing to avoid deadlocks.
- Lease-expiry recovery uses PostgreSQL time and processes only tasks where:
  - `state = 'Claimed'`
  - `lease_expires_at_utc <= transaction_timestamp()`
- Recovery follows the v2 rules:
  - expired claims consume the already-issued attempt
  - tasks requeue with a new `not_before_utc` when attempts remain
  - tasks fail terminally when retries are exhausted

### Concurrency Group Limits

- Missing `concurrency_groups` rows imply the v1/v2 default effective limit of `1`.
- Claiming must create missing `concurrency_groups` rows on demand with:
  - `configured_limit = null`
  - `in_use_count = 0`
- Increasing or decreasing configured limits mutates `configured_limit` only. Running tasks are never preempted.

### Recurring Schedule Materialization

- Due schedules are selected from `recurring_schedules` where:
  - `is_paused = false`
  - `next_fire_at_utc <= transaction_timestamp()`
- Due schedule selection order is `next_fire_at_utc ASC, schedule_key ASC`.
- Due schedule selection uses `FOR UPDATE SKIP LOCKED`.
- Under the schedule row lock:
  - verify the schedule is still due and active
  - apply overlap behavior
  - when allowed, insert exactly one materialized task row plus matching `task_concurrency_groups`
  - advance `next_fire_at_utc` to the next future cron occurrence
- `RecurringOverlapMode.Skip` checks for any task with:
  - matching `source_schedule_key`
  - `state IN ('Queued', 'Claimed')`
- Even when overlap suppresses a fire, `next_fire_at_utc` still advances and the missed fire is not replayed later.

### Notifications And Polling

- PostgreSQL wakeups use `LISTEN/NOTIFY` plus polling fallback.
- The provider listens on one fixed channel named `sheddueller_wakeup`.
- The `NOTIFY` payload is the configured schema name.
- A provider instance must ignore notifications whose payload does not match its configured schema.
- The provider must send a wakeup notification after commit when it performs an operation that can make work newly claimable now, including:
  - immediate enqueue
  - terminal release of claimed work
  - requeue to an immediately due retry
  - increasing a concurrency-group limit
  - creating, resuming, or updating a recurring schedule to an immediately due state
- Polling remains the correctness mechanism for:
  - missed notifications
  - future delayed tasks becoming due
  - future retries becoming due
  - future recurring schedules becoming due
- Polling cadence is an internal provider concern and is not exposed as a public v3 tuning knob.

## Migration And Operational Model

- `IPostgresMigrator.ApplyAsync` is the intentional path for creating or upgrading the provider schema.
- Migration runs must take a provider-scoped PostgreSQL advisory lock so only one migrator mutates the schema at a time.
- Ordinary worker startup is fail-only with respect to schema compatibility.
- The provider assumes one logical Sheddueller cluster per configured schema, even when multiple schemas share the same PostgreSQL database.
- Payload storage remains serializer-owned opaque `bytea`; PostgreSQL is not the canonical payload contract.
- The provider assembly does not expose a public inspection API. Integration tests may use test-assembly-only SQL helpers or fixtures to assert persisted task, schedule, concurrency, and schema-version state.

## Testing Model

- PostgreSQL provider tests are black-box integration tests against real PostgreSQL infrastructure.
- Local and CI tests use Testcontainers only.
- The test PostgreSQL image is selected by `SHEDDUELLER_POSTGRES_IMAGE`.
- When `SHEDDUELLER_POSTGRES_IMAGE` is not set, tests default to `postgres:14`.
- CI must run PostgreSQL provider tests against `postgres:14`, `postgres:16`, and `postgres:18`.

## Acceptance Scenarios

The v3 implementation is complete only when the following scenarios pass against a real PostgreSQL instance:

1. `UsePostgres` registers the PostgreSQL provider through `NpgsqlDataSource` and a configurable schema name.
2. `IPostgresMigrator.ApplyAsync` creates a fresh schema and stamps the expected schema version.
3. Two concurrent migrators targeting the same schema do not both mutate the schema at once.
4. Ordinary scheduler startup fails before doing work when the configured schema is missing, behind, or ahead of the expected provider version.
5. Two concurrent nodes using the same PostgreSQL schema cannot both claim the same task.
6. PostgreSQL claiming preserves the v1/v2 priority and FIFO ordering among eligible tasks.
7. Group saturation under PostgreSQL blocks only the affected task and still allows later eligible tasks to be claimed.
8. PostgreSQL lease expiry, retry requeue, and terminal failure behavior matches the v2 semantics.
9. PostgreSQL task timestamps and lease decisions remain correct even when application node clocks differ, because database time is authoritative.
10. Recurring schedule materialization under PostgreSQL creates exactly one task per due fire across concurrent nodes.
11. `CreateOrUpdateAsync` recurring schedule definitions preserve pause state and interact correctly with PostgreSQL due-time advancement.
12. `LISTEN/NOTIFY` wakes sleeping workers promptly after an operation that makes work immediately claimable.
13. Polling still discovers delayed, retried, or scheduled work when notifications are missed.
14. PostgreSQL integration tests verify persisted task, schedule, concurrency, and schema-version state through test-assembly-only SQL helpers rather than public provider inspection APIs.
