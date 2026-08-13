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
  'analyzers/dotnet/cs/BlazorCodeFirst.Compiler.dll' \
  'lib/net10.0/BlazorCodeFirst.Runtime.dll' \
  'lib/net10.0/BlazorCodeFirst.Runtime.xml')

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
    '[Content_Types].xml' | '_rels/.rels' | 'BlazorCodeFirst.nuspec' | 'README.md' | \
    'analyzers/dotnet/cs/BlazorCodeFirst.Compiler.dll' | 'lib/net10.0/BlazorCodeFirst.Runtime.dll' | \
    'lib/net10.0/BlazorCodeFirst.Runtime.xml' )
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

python3 - "$workdir/BlazorCodeFirst.nuspec" "$repository_root/eng/Versions.props" <<'PY'
import re
import sys
import xml.etree.ElementTree as ET

nuspec_path, versions_props_path = sys.argv[1], sys.argv[2]
# Packing a <dependencies> group (added when Runtime moved from an unconditional
# FrameworkReference to a granular PackageReference, see issue #23) makes the SDK
# emit the 2013/05 nuspec schema instead of the dependency-less 2012/06 schema.
namespace = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"}

# The expected version is read from the repository's one declaration of it rather than
# repeated here (#228). That makes this assertion the check that the package the build
# produced carries the version the repository declares -- which is the invariant worth
# holding, and which a literal copy in this script could only restate.
versions_root = ET.parse(versions_props_path).getroot()
declared_version = versions_root.findtext(".//BlazorCodeFirstPackageVersion", default="").strip()

if not declared_version:
    raise SystemExit(
        f"Could not read BlazorCodeFirstPackageVersion from {versions_props_path}."
    )

root = ET.parse(nuspec_path).getroot()

# Never the ASP.NET Core shared framework, which has no browser-wasm runtime pack and broke WASM
# consumers (#23). That arrives as its own <frameworkReferences> element rather than as a dependency,
# so the dependency assertion below cannot see it and the payload check does not read the nuspec's
# contents at all (#301).
#
# First, and matched on the local name, because everything below is bound to one namespace and to a
# dependency being declared -- and an added framework reference takes both away. It prunes the
# granular PackageReference, which empties the dependency group, and an empty one drops the nuspec
# back to the 2012/06 schema. The checks below then fail on a missing metadata element and a missing
# dependency, neither of which names what is actually wrong.
framework_reference_names = sorted(
    element.attrib.get("name", "<missing-name>")
    for element in root.iter()
    if element.tag.rpartition("}")[2] == "frameworkReference"
)

if framework_reference_names:
    raise SystemExit(
        "Package nuspec declares framework references, which a browser-wasm consumer cannot "
        f"resolve: {framework_reference_names}."
    )

metadata = root.find("n:metadata", namespace)

if metadata is None:
    raise SystemExit("Package metadata element is missing from nuspec.")

expected_values = {
    "id": "BlazorCodeFirst",
    "version": declared_version,
    "readme": "README.md",
    # An SPDX expression, so nuget.org links the license instead of embedding a copy, and no
    # license file lands in the payload (#226). NuGet emits a licenseUrl beside it for older
    # clients; that one is derived and not asserted.
    "license": "MIT",
    "projectUrl": "https://blazor-code-first-site.snsgr.workers.dev/",
    "copyright": "Copyright (c) 2026 Sano Suguru",
    "tags": "blazor razor source-generator html dsl aot trimming components",
}

for element_name, expected_value in expected_values.items():
    actual_value = metadata.findtext(f"n:{element_name}", default="", namespaces=namespace)
    if actual_value != expected_value:
        raise SystemExit(
            f"Unexpected nuspec {element_name!r}: expected {expected_value!r}, got {actual_value!r}."
        )

license_element = metadata.find("n:license", namespace)
if license_element is None or license_element.attrib.get("type") != "expression":
    raise SystemExit(
        "Package license must be declared as an SPDX expression "
        f"(type=\"expression\"), got {license_element.attrib if license_element is not None else None}."
    )

# The repository element is what SourceLink produces (#227). Asserting the resolved commit is
# present and is a full SHA is the only check that SourceLink actually ran: RepositoryUrl and
# RepositoryType are set by hand in the csproj and would appear whether it did or not.
repository = metadata.find("n:repository", namespace)
if repository is None:
    raise SystemExit("Package repository element is missing from nuspec.")

expected_repository = {
    "type": "git",
    "url": "https://github.com/sano-suguru/blazor-code-first",
}

for attribute_name, expected_value in expected_repository.items():
    actual_value = repository.attrib.get(attribute_name, "")
    if actual_value != expected_value:
        raise SystemExit(
            f"Unexpected nuspec repository {attribute_name!r}: "
            f"expected {expected_value!r}, got {actual_value!r}."
        )

commit = repository.attrib.get("commit", "")
if not re.fullmatch(r"[0-9a-f]{40}", commit):
    raise SystemExit(
        f"Package repository commit is not a full git SHA: {commit!r}. "
        "SourceLink did not resolve it."
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

# The symbol package (#227). SymbolPackageFormat=snupkg keeps the .pdb out of the .nupkg entirely,
# which is why the exact payload match above still holds; the symbols are verified here instead.
# Only the runtime has symbols: the generator half is staged into analyzers/dotnet/cs by the
# PackCompilerAnalyzer target as a raw file, and giving it symbols is a separate question (#227).
symbol_package_path="${package_path%.nupkg}.snupkg"

if [ ! -f "$symbol_package_path" ]; then
  echo "Symbol package not found beside the package: $symbol_package_path" >&2
  exit 1
fi

symbol_workdir="$workdir/symbols"
mkdir "$symbol_workdir"
unzip -q "$symbol_package_path" -d "$symbol_workdir"

symbol_payload_files=$(
  cd "$symbol_workdir"
  find . -type f -print | sed 's#^\./##' |
    awk '/^(analyzers|lib|build|buildTransitive|contentFiles|tools|runtimes)\//' |
    LC_ALL=C sort
)

if [ "$symbol_payload_files" != 'lib/net10.0/BlazorCodeFirst.Runtime.pdb' ]; then
  echo "Unexpected files under symbol package payload roots:" >&2
  printf '%s\n' "$symbol_payload_files" >&2
  exit 1
fi

echo "Verified package contents: $package_path"
echo "Verified symbol package contents: $symbol_package_path"
