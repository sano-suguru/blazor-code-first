#!/usr/bin/env bash
# Prints the version the package will be built with, as MSBuild resolves it.
#
# Both ci.yml and release.yml need it -- to name the nupkg they verify, to purge the isolated dev
# cache, and to check a release tag against it -- and neither should repeat the literal that
# eng/Versions.props holds (#228).
#
# This asks MSBuild rather than reading eng/Versions.props directly, so it answers what the package
# will actually be called and not what the file says in isolation. -getProperty evaluates the project
# without building or restoring it, so it is safe to call at any point in a workflow.
set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
runtime_project="$repository_root/src/BlazorCodeFirst.Runtime/BlazorCodeFirst.Runtime.csproj"

version=$(dotnet msbuild "$runtime_project" -getProperty:Version -nologo)
version=${version//[$'\t\r\n ']/}

if [ -z "$version" ]; then
  echo "MSBuild reported an empty Version for $runtime_project." >&2
  exit 1
fi

printf '%s\n' "$version"
