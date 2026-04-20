# Sheddueller v4 Dashboard Wireframe Specification

Status: Draft for design handoff  
Last updated: 2026-04-20

## Purpose

This document defines the wireframe brief for the v4 Sheddueller dashboard UI.

The designer should produce desktop-only wireframes for a read-only, jobs-focused operations dashboard. The wireframes should help engineers and operators understand scheduler health, find jobs, inspect queue position, and follow durable job logs/progress while work is running.

This is not a visual design system brief. It should establish layout, information hierarchy, interaction states, and component behavior clearly enough for implementation.

## Product Context

Sheddueller v4 adds an embedded ASP.NET Core/Blazor dashboard backed by provider-agnostic dashboard contracts. V4 is intentionally observation-only.

The dashboard can display:

- scheduler job metadata
- job state and queue-position disposition
- exact job tags
- latest progress
- durable job events
- live job logs and progress updates

The dashboard must not display:

- serialized payload bodies
- method argument bodies
- retry, cancel, requeue, delete, pause, resume, or edit controls
- schedule management views
- concurrency-group management views
- node or cluster health views

Those capabilities are outside v4.

## Audience And Design Direction

Primary audience: engineers and operators responsible for diagnosing job scheduling behavior.

Design density: balanced dashboard. The UI should be calmer and more structured than a raw console, but still optimized for scanning many jobs and events.

Target viewport: desktop only. Responsive mobile layouts are not required for this wireframe pass.

Tone: practical, precise, operational. Prefer direct labels and predictable table/detail patterns over marketing language or explanatory onboarding content.

## Information Model

The wireframes should reflect these core objects and relationships.

### Job State

Persisted job state is one of:

- Queued
- Claimed
- Completed
- Failed
- Canceled

The UI must distinguish persisted job state from dynamic queue disposition.

### Queue Disposition

Queue position describes whether a job currently has a meaningful claim-order position.

Kinds:

- Claimable: queued, due now, not blocked, with a 1-based global queue position
- Delayed: waiting for an initial not-before timestamp
- RetryWaiting: waiting for retry backoff
- BlockedByConcurrency: blocked by concurrency group saturation
- Claimed: already running on a worker
- Terminal: completed or failed
- Canceled: canceled before running
- NotFound: no job exists

The UI should show queue disposition beside, but not as a replacement for, persisted job state.

### Job Summary Fields

List and overview rows can display:

- job id
- state
- service type
- method name
- priority
- enqueue sequence
- enqueued timestamp
- not-before timestamp
- attempt count and max attempts
- tags
- source schedule key
- latest progress percent/message
- queue position or non-position reason
- terminal timestamp when completed, failed, or canceled

### Job Detail Fields

Detail pages can additionally display:

- claimed timestamp
- claimed-by node id
- lease expiration timestamp
- scheduled fire timestamp
- recent durable events

### Durable Events

Events are ordered by ascending event sequence and can be one of:

- Lifecycle
- AttemptStarted
- AttemptCompleted
- AttemptFailed
- Log
- Progress

Event fields:

- event sequence
- occurred timestamp
- kind
- attempt number
- optional log level
- optional message
- optional progress percent
- optional structured string fields

## Global Navigation

The v4 dashboard has two primary destinations:

- Overview
- Jobs

Job detail is reached from overview or jobs result rows. It does not need top-level navigation.

The shell should include:

- product name: Sheddueller
- active page indication
- route-level content region
- optional live connection indicator if useful for the detail page pattern

Avoid adding settings, actions, schedule navigation, worker navigation, metrics navigation, or user/account controls in v4 wireframes.

## Page 1: Overview

Primary purpose: health triage.

The overview should answer:

- Are jobs failing?
- What is running now?
- Is there a queue backlog?
- Is work delayed or retry-waiting?
- Which jobs need immediate inspection?

Required sections:

- state-count summary for all persisted job states
- currently running jobs
- recently failed jobs
- queued jobs
- delayed jobs
- retry-waiting jobs

Recommended layout:

- Top summary band with compact state counts.
- Main content split into prioritized sections, with failed and running work visually prominent.
- Job sections use compact tables or structured lists with enough metadata to triage without opening every job.

Overview cards and section headers should support drilldown into the Jobs page with matching filters where the data model supports it. For example:

- Failed count opens Jobs filtered by `State = Failed`.
- Running jobs section opens Jobs filtered by `State = Claimed`.
- Queued jobs section opens Jobs filtered by `State = Queued`.

Delayed and retry-waiting are queue dispositions rather than persisted states, so wireframes should show these as dashboard overview groups but avoid implying they are stored job states.

Overview empty states:

- No jobs exist.
- No jobs in a section.
- Overview data fails to load.

## Page 2: Jobs Search

Primary purpose: find and compare jobs.

The jobs page should include a visible desktop filter panel and a results table.

Required filters:

- job id
- state
- handler service type
- handler method name
- exact tag name and exact tag value
- source schedule key
- enqueued time range
- terminal time range

Filter behavior:

- Filters are exact or structured filters, not full-text search.
- Tag search requires exact name/value pair semantics.
- The wireframe should include clear Apply/Search and Clear/Reset affordances.
- Invalid job id input should have an inline validation state.
- Time ranges should make start and end fields visually paired.

Results table should show at least:

- job id link
- state
- queue disposition/position
- handler
- priority
- enqueued timestamp
- attempts
- latest progress
- terminal timestamp when present

Pagination:

- Use Next page or Load more.
- Do not show total result count or page count because the API exposes continuation tokens, not total counts.
- Optional previously loaded results behavior may be represented, but the designer should not require arbitrary page jumps.

Results states:

- initial loading
- searching/loading after applying filters
- no matching jobs
- invalid filter input
- query failure
- results with continuation available
- results with no continuation available

## Page 3: Job Detail

Primary purpose: understand one job from enqueue through its current or terminal state.

The detail page should combine:

- a concise job summary header
- metadata sections
- current queue position or non-position reason
- latest progress
- durable event timeline
- live log stream

Recommended layout:

- Header area: job id, state badge, queue disposition, handler, latest progress, and key timestamps.
- Metadata area: priority, enqueue sequence, attempts, source schedule key, schedule fire time, claimed node, lease expiration, and tags.
- Timeline area: lifecycle and attempt/progress milestones in event-sequence order.
- Log stream area: dense table/list of log events with level, time, attempt, message, and expandable structured fields.

Live behavior:

- The detail page should show a live connection/update indicator.
- New durable events should appear while the page is open.
- The UI should include a local pause/resume control for the live stream.
- If paused, indicate that new events may be waiting and provide a way to resume.
- Autoscroll behavior should be represented for the log stream. Pausing live updates should prevent disruptive scrolling.

Latest progress:

- Show progress percent when present.
- Show progress message when present.
- Show reported timestamp.
- Handle message-only progress and percent-only progress.
- Handle no progress reported.

Timeline:

- Use event sequence as the durable ordering source.
- Lifecycle, attempt, and progress events should be visually distinguishable.
- The timeline should not hide failed attempt context.

Log stream:

- Show log level, timestamp, attempt, message, and event sequence.
- Structured fields should be available through expandable rows.
- Empty log state should be explicit.
- Long messages should be readable without breaking table layout.

Detail states:

- loading
- job not found
- live connected
- live disconnected/reconnecting
- live paused
- event read failure
- no events
- no logs
- no progress

## Shared Components

The wireframes should define reusable component patterns for:

- page shell and navigation
- state badge
- queue disposition badge/summary
- timestamp display
- job summary row
- filter panel
- continuation pagination
- empty state
- loading state
- error state
- tag chips or tag list
- progress summary
- timeline event item
- log stream row
- expandable structured-fields panel
- live status indicator

Timestamp display:

- Use relative freshness plus exact UTC, for example `3m ago` with `2026-04-20 14:32:10 UTC`.
- Exact UTC must remain available wherever a timestamp is operationally important.
- Avoid local-time-only wireframes.

Status display:

- Persisted job state and dynamic queue disposition should be separate visual elements.
- Failed, canceled, running/claimed, queued, delayed, retry-waiting, and blocked work should be distinguishable at a glance.
- Do not imply that delayed/retry-waiting/blocked are persisted `JobState` values.

Tags:

- Tags are key/value pairs.
- Tags should be scannable on detail pages.
- In job lists, tags may be summarized to avoid row overload.

## Interaction Principles

Read-only:

- No mutating controls in v4.
- No action menus that imply retry, cancel, requeue, delete, pause, resume, or edit.

Drilldown:

- Job ids link to detail pages.
- Overview counts and sections link to filtered jobs where possible.
- Filter state should be visible after drilldown.

Operator efficiency:

- Keep important identifiers copyable or easy to select.
- Avoid hiding primary diagnostic information behind multiple clicks.
- Use progressive disclosure for bulky data such as structured log fields.

Failure handling:

- Loading, empty, and error states should be explicitly represented.
- Error states should preserve enough context for retrying or adjusting filters.

Security boundary:

- Never show serialized payload bodies or method argument bodies.
- Do not add authentication, authorization, account, or user-management UI.
- The host application owns dashboard protection.

## Designer Deliverables

Produce desktop wireframes for:

- Overview default state
- Overview empty/low-activity state
- Jobs search with populated results
- Jobs search with visible filters and validation/error states
- Jobs search with no results
- Job detail for a running job with live progress/logs
- Job detail for a failed job with failed-attempt context
- Job detail with no progress/logs
- Job detail not found

Include annotations for:

- which fields are populated by the dashboard data model
- which interactions navigate to another page
- which UI elements are live-update aware
- where structured log fields expand
- where exact UTC timestamps appear
- which controls are intentionally absent because v4 is read-only

The handoff should avoid specifying implementation technology beyond the known dashboard context: embedded Blazor, desktop web UI, read-only v4 scope.
