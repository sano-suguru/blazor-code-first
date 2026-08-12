#!/usr/bin/env bash
# One assertion over tests/BlazorCodeFirst.WasmPackageApp's `dotnet publish` output.
#
# That project's BUILD is the #23 check; see the comment on its ItemGroup for what it measures and
# why. This script covers the case a build cannot see: a publish that resolved every reference and
# still shipped no BlazorCodeFirst would exit 0.
#
# A script rather than an inline block in .github/workflows/ci.yml, so it can be RUN. A check that
# lives inside a YAML string cannot be watched failing without pushing, and CONTRIBUTING.md
# §Engineering standard does not accept a check nobody has watched fail. eng/verify-site-prerender.sh
# is the precedent for the shape.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: bash eng/verify-wasm-package.sh <path-to-published-wwwroot>" >&2
  exit 1
fi

P=$1

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# Sourcing eng/ci-assert.sh rather than hand-rolling grep: its header documents the shell hazards
# that make a grep-based check silently pass. A missing directory needs no guard of its own here --
# assert_grep reports the absent file, and reports it as a `::error::` annotation, which a
# hand-rolled `[ ! -d ]` above would not.
# shellcheck source=eng/ci-assert.sh
. "$script_dir/ci-assert.sh"

# .NET 10 publishes no blazor.boot.json. The boot manifest is embedded in _framework/dotnet.js, and
# it is what the browser reads to decide which assemblies to download, so asserting against it says
# the thing that matters -- the app boots with the runtime in hand. It also sidesteps the
# fingerprint in the asset's own file name, which changes with every build.
assert_grep 'BlazorCodeFirst\.Runtime\.wasm' "$P/_framework/dotnet.js" \
  "the published boot manifest does not name BlazorCodeFirst.Runtime, so the package never reached the browser"

echo "The published boot manifest names BlazorCodeFirst.Runtime."
