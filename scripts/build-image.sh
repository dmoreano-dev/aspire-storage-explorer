#!/usr/bin/env bash
# Builds the explorer image locally, named exactly as the extension expects it (StorageExplorerContainerImageTags.cs),
# so the sample finds it without pulling. Pass a tag to override the one in that file.
set -euo pipefail

cd "$(dirname "$0")/.."

tags_file=src/StorageExplorer.Aspire.Hosting/StorageExplorerContainerImageTags.cs
registry=$(sed -n 's|.*Registry = "\(.*\)";.*|\1|p' "$tags_file")
image=$(sed -n 's|.*const string Image = "\(.*\)";.*|\1|p' "$tags_file")
tag=$(sed -n 's|.*const string Tag = "\(.*\)";.*|\1|p' "$tags_file")

reference="${registry:+$registry/}${image}:${1:-$tag}"

echo "Building $reference"
docker build -f src/StorageExplorer.Web/Dockerfile -t "$reference" .
