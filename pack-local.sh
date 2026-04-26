#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
feed="${SHEDDUELLER_LOCAL_FEED:-"$HOME/.nuget/local-sheddueller"}"
version="${1:-${SHEDDUELLER_LOCAL_VERSION:-"0.1.0-local.$(date -u +%Y%m%d).$(date -u +%s)"}}"
configuration="${CONFIGURATION:-Release}"

projects=(
  "src/Sheddueller/Sheddueller.csproj"
  "src/Sheddueller.Worker/Sheddueller.Worker.csproj"
  "src/Sheddueller.Postgres/Sheddueller.Postgres.csproj"
  "src/Sheddueller.Dashboard/Sheddueller.Dashboard.csproj"
  "src/Sheddueller.Testing/Sheddueller.Testing.csproj"
)

mkdir -p "$feed"

{
  printf 'Packing Sheddueller local packages\n'
  printf '  Version: %s\n' "$version"
  printf '  Feed: %s\n' "$feed"
  printf '  Configuration: %s\n' "$configuration"
} >&2

cd "$repo_root"

for project in "${projects[@]}"; do
  printf 'Packing %s\n' "$project" >&2
  dotnet pack "$project" \
    --configuration "$configuration" \
    --output "$feed" \
    -p:Version="$version" \
    -p:PackageVersion="$version" >&2
done

printf '%s\n' "$version"
