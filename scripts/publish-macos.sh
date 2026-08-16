#!/usr/bin/env bash
set -euo pipefail

version="${1:-1.1.1}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "$(uname -m)" in
  arm64)
    runtime="osx-arm64"
    ;;
  x86_64)
    runtime="osx-x64"
    ;;
  *)
    echo "Unsupported macOS architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

package_name="shiori-v${version}-${runtime}"
artifacts_root="${repo_root}/artifacts"
publish_directory="${artifacts_root}/${package_name}"
archive_path="${artifacts_root}/${package_name}.tar.gz"
native_directory="${repo_root}/native/shiori-engine"
native_library="${native_directory}/target/release/libshiori_engine.dylib"

rm -rf "${publish_directory}"
mkdir -p "${artifacts_root}"

cargo build --release --manifest-path "${native_directory}/Cargo.toml"
dotnet publish "${repo_root}/src/Shiori.Cli/Shiori.Cli.csproj" \
  --configuration Release \
  --runtime "${runtime}" \
  --output "${publish_directory}" \
  --self-contained false

cp "${native_library}" "${publish_directory}/"
cp "${repo_root}/README.md" "${publish_directory}/"
cp "${repo_root}/LICENSE" "${publish_directory}/"
cp "${repo_root}/CHANGELOG.md" "${publish_directory}/"
cp "${repo_root}/docs/release-notes-v${version}.md" \
  "${publish_directory}/RELEASE_NOTES.md"

rm -f "${archive_path}" "${archive_path}.sha256"
tar -C "${artifacts_root}" -czf "${archive_path}" "${package_name}"
(
  cd "${artifacts_root}"
  shasum -a 256 "${package_name}.tar.gz" > "${package_name}.tar.gz.sha256"
)

printf '%s\n' "${archive_path}" "${archive_path}.sha256"
