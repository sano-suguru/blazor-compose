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

packaged_dlls=$(
  cd "$workdir"
  find . -type f -name '*.dll' -print | sed 's#^\./##' | LC_ALL=C sort
)

expected_dlls=$(printf '%s\n' \
  'analyzers/dotnet/cs/BlazorCompose.Compiler.dll' \
  'lib/net10.0/BlazorCompose.Runtime.dll')

if [ "$packaged_dlls" != "$expected_dlls" ]; then
  echo "Unexpected packaged DLLs:" >&2
  printf '%s\n' "$packaged_dlls" >&2
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

echo "Verified package contents: $package_path"
