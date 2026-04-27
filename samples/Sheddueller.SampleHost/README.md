# Sheddueller Sample Host

This sample hosts the embedded Sheddueller dashboard, a worker node, and a small launcher UI for creating representative jobs and schedules on demand.

## Prerequisites

- Docker with Compose support
- .NET 10 SDK

## Start PostgreSQL

From the repository root:

```bash
docker compose up -d postgres
```

If you previously started the sample with an older Postgres volume layout, remove the old volume once before restarting:

```bash
docker compose down -v
```

The sample defaults are aligned with `docker-compose.yml`:

- host: `localhost`
- port: `5432`
- database: `sheddueller_sample`
- username: `sheddueller`
- password: `sheddueller`

## Run The Sample

From the repository root:

```bash
dotnet run --project samples/Sheddueller.SampleHost
```

Open:

- launcher: `http://localhost:5000/`
- dashboard: `http://localhost:5000/sheddueller`

The sample applies PostgreSQL schema migrations automatically on startup and registers `AddShedduellerWorker(...)` so demo jobs execute in the same process. Use the launcher to create demo data, then use the dashboard to inspect jobs, view schedules, and request cancellation.

## Launcher Scenarios

- `Quick success`: immediate completion
- `Progress + logs`: emits `ILogger<T>` messages captured as durable job logs, plus `IProgress<decimal>` progress updates
- `Retry then succeed`: fails twice, then succeeds
- `Permanent failure`: terminal failure without retries
- `Delayed job`: waits 30 seconds before becoming claimable
- `Concurrency batch`: sets a shared limit of 1 and queues several long-running jobs
- `Idempotent reprice`: queues a 10-second reprice job with generated idempotency behind a group limit of 1; click twice quickly to see the same queued job reused
- `Recurring demo`: creates or updates a recurring schedule that fires each minute
- `Cancelable delayed job`: creates a queued delayed job that can be canceled from the dashboard job detail page

## Configuration

The sample reads:

- `ConnectionStrings__Sheddueller`
- `Sheddueller__Postgres__SchemaName`

Example override:

```bash
ConnectionStrings__Sheddueller="Host=localhost;Port=5432;Database=other_db;Username=postgres;Password=postgres" dotnet run --project samples/Sheddueller.SampleHost
```
