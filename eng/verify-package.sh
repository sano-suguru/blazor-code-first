#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: bash eng/verify-package.sh <path-to-nupkg>" >&2
  exit 1
fi

package_path=$1

if [ ! -f "$package_path" ]; then
  echo "Package not found: $package_path" >&2
  exit 1
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
scratch_root="$repository_root/artifacts/verify-package"

mkdir -p "$scratch_root"

workdir=
attempt=1

while [ "$attempt" -le 100 ]; do
  candidate="$scratch_root/extract-$$-$attempt"

  if mkdir "$candidate" 2>/dev/null; then
    workdir=$candidate
    break
  fi

  attempt=$((attempt + 1))
done

if [ -z "${workdir}" ]; then
  echo "Could not create a verification directory under $scratch_root" >&2
  exit 1
fi

cleanup() {
  if [ -z "${workdir:-}" ] || [ ! -d "$workdir" ]; then
    return
  fi

  case "$workdir" in
    "$scratch_root"/*)
      rm -rf -- "$workdir"
      ;;
    *)
      echo "Refusing to remove unexpected directory: $workdir" >&2
      exit 1
      ;;
  esac
}

trap cleanup EXIT

unzip -q "$package_path" -d "$workdir"

packaged_files=$(
  cd "$workdir"
  find . -type f -print | sed 's#^\./##' | LC_ALL=C sort
)

expected_payload_files=$(printf '%s\n' \
  'analyzers/dotnet/cs/BlazorCompose.Compiler.dll' \
  'lib/net10.0/BlazorCompose.Runtime.dll' \
  'lib/net10.0/BlazorCompose.Runtime.xml')

payload_files=$(
  printf '%s\n' "$packaged_files" |
    awk '/^(analyzers|lib|build|buildTransitive|contentFiles|tools|runtimes)\//'
)

if [ "$payload_files" != "$expected_payload_files" ]; then
  echo "Unexpected files under package payload roots:" >&2
  printf '%s\n' "$payload_files" >&2
  exit 1
fi

roslyn_dlls=$(
  cd "$workdir"
  find . -type f -name 'Microsoft.CodeAnalysis*.dll' -print | sed 's#^\./##' | LC_ALL=C sort
)

if [ -n "$roslyn_dlls" ]; then
  echo "Unexpected Roslyn assemblies found:" >&2
  printf '%s\n' "$roslyn_dlls" >&2
  exit 1
fi

if ! printf '%s\n' "$packaged_files" | grep -Fx 'README.md' >/dev/null; then
  echo "Package README.md is missing." >&2
  exit 1
fi

unexpected_files=()

while IFS= read -r packaged_file; do
  [ -z "$packaged_file" ] && continue

  case "$packaged_file" in
    '[Content_Types].xml' | '_rels/.rels' | 'BlazorCompose.nuspec' | 'README.md' | \
    'analyzers/dotnet/cs/BlazorCompose.Compiler.dll' | 'lib/net10.0/BlazorCompose.Runtime.dll' | \
    'lib/net10.0/BlazorCompose.Runtime.xml' )
      ;;
    package/services/metadata/core-properties/*.psmdcp)
      ;;
    *)
      unexpected_files+=("$packaged_file")
      ;;
  esac
done <<< "$packaged_files"

if [ "${#unexpected_files[@]}" -ne 0 ]; then
  echo "Unexpected package files found:" >&2
  printf '%s\n' "${unexpected_files[@]}" >&2
  exit 1
fi

python3 - "$workdir/BlazorCompose.nuspec" <<'PY'
import sys
import xml.etree.ElementTree as ET

nuspec_path = sys.argv[1]
# Packing a <dependencies> group (added when Runtime moved from an unconditional
# FrameworkReference to a granular PackageReference, see issue #23) makes the SDK
# emit the 2013/05 nuspec schema instead of the dependency-less 2012/06 schema.
namespace = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"}

root = ET.parse(nuspec_path).getroot()
metadata = root.find("n:metadata", namespace)

if metadata is None:
    raise SystemExit("Package metadata element is missing from nuspec.")

expected_values = {
    "id": "BlazorCompose",
    "version": "0.1.0-dev",
    "readme": "README.md",
}

for element_name, expected_value in expected_values.items():
    actual_value = metadata.findtext(f"n:{element_name}", default="", namespaces=namespace)
    if actual_value != expected_value:
        raise SystemExit(
            f"Unexpected nuspec {element_name!r}: expected {expected_value!r}, got {actual_value!r}."
        )

# Runtime must depend on the granular Microsoft.AspNetCore.Components package only,
# never on the ASP.NET Core shared framework (which has no browser-wasm runtime
# pack and broke WASM consumers, see issue #23).
dependency_elements = metadata.findall(".//n:dependency", namespace)
dependency_ids = sorted(element.attrib.get("id", "<missing-id>") for element in dependency_elements)

if dependency_ids != ["Microsoft.AspNetCore.Components"]:
    raise SystemExit(
        "Unexpected package dependencies declared in nuspec: expected exactly "
        f"['Microsoft.AspNetCore.Components'], got {dependency_ids}."
    )
PY

echo "Verified package contents: $package_path"
