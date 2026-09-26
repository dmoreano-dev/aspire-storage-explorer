#!/usr/bin/env bash
# Tags the current main commit as a release and pushes the tag, which starts the Release workflow.
# Usage: scripts/release.sh 0.1.0   (a leading "v" is accepted; 0.2.0-rc.1 makes a pre-release)
set -euo pipefail

cd "$(dirname "$0")/.."

fail() { echo "error: $1" >&2; exit 1; }

[ $# -eq 1 ] || fail "usage: scripts/release.sh <version>  (for example 0.1.0 or 0.2.0-rc.1)"

version="${1#v}"
tag="v$version"

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
  || fail "'$version' is not a valid version (expected X.Y.Z or X.Y.Z-suffix)."

[ "$(git branch --show-current)" = "main" ] || fail "releases are tagged from main; you are on '$(git branch --show-current)'."
[ -z "$(git status --porcelain --untracked-files=no)" ] || fail "there are uncommitted changes; commit them first."

git fetch --quiet origin main
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || fail "main is not in sync with origin/main; pull or push first."

git rev-parse -q --verify "refs/tags/$tag" > /dev/null && fail "tag $tag already exists locally."
[ -z "$(git ls-remote --tags origin "refs/tags/$tag")" ] || fail "tag $tag already exists on origin; a published version cannot be reused, bump it."

# The checks the Release workflow makes, read from the commit that will be tagged, so a mistake shows up
# before the tag exists.
git cat-file -e HEAD:.github/workflows/release.yml 2> /dev/null || fail ".github/workflows/release.yml is not committed."

hosting=src/StorageExplorer.Aspire.Hosting
project_version=$(git show "HEAD:$hosting/StorageExplorer.Aspire.Hosting.csproj" | sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p')
image_tag=$(git show "HEAD:$hosting/StorageExplorerContainerImageTags.cs" | sed -n 's|.*const string Tag = "\(.*\)";.*|\1|p')

[ "$project_version" = "$version" ] || fail "<Version> in the csproj is '$project_version', not '$version'."
[ "$image_tag" = "$version" ] || fail "Tag in StorageExplorerContainerImageTags.cs is '$image_tag', not '$version'."
git show HEAD:CHANGELOG.md | awk -v v="$version" 'index($0, "## [" v "]") == 1 { found = 1 } END { exit !found }' \
  || fail "CHANGELOG.md has no '## [$version]' section in the last commit."

echo "This tags $(git rev-parse --short HEAD) as $tag and pushes it, which publishes the image and the package."
read -r -p "Continue? [y/N] " answer
[[ "$answer" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }

git tag -a "$tag" -m "Release $version"
git push origin "$tag" || { git tag -d "$tag" > /dev/null; fail "could not push $tag; the local tag was removed."; }

echo "Pushed $tag. Follow the run in the Actions tab of the repository."
