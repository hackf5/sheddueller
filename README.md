# Sheddueller

A task scheduling library for .NET.

![Sheddueller hero](docs/assets/hero.png)

## Layout

- `src/Sheddueller`: core library package
- `samples/Sheddueller.SampleHost`: local sample host for dashboard and job-launcher development
- `docs/roadmap.md`: implementation roadmap and shared glossary
- `docs/v1-spec.md`: v1 implementation specification
- `docs/v2-spec.md`: v2 implementation specification
- `docs/v3-spec.md`: v3 PostgreSQL backend specification
- `docs/v4-spec.md`: v4 dashboard specification
- `docs/v5-spec.md`: v5 trusted operations console specification
- `test/Sheddueller.Tests`: unit tests

## Commands

```bash
dotnet restore
dotnet build Sheddueller.slnx -c Debug
dotnet test --solution Sheddueller.slnx -c Debug
dotnet pack src/Sheddueller/Sheddueller.csproj -c Release
```
