#!/usr/bin/env bash
# Bumps the version: sets <Version> in the hosting csproj, Tag in StorageExplorerContainerImageTags.cs, and turns
# "## [Unreleased]" in CHANGELOG.md into "## [<version>] - <date>", with a new empty Unreleased section above it.
# It only edits files: review the diff, commit and push, then run scripts/release.sh <version>, which checks that
# the three agree.
# Usage: scripts/bump-version.sh 0.5.0   (a leading "v" is accepted; 0.5.0-rc.1 works too)
set -euo pipefail

cd "$(dirname "$0")/.."

fail() { echo "error: $1" >&2; exit 1; }

[ $# -eq 1 ] || fail "usage: scripts/bump-version.sh <version>  (for example 0.5.0)"

version="${1#v}"

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
  || fail "'$version' is not a valid version (expected X.Y.Z or X.Y.Z-suffix)."

hosting=src/StorageExplorer.Aspire.Hosting
csproj="$hosting/StorageExplorer.Aspire.Hosting.csproj"
tags_file="$hosting/StorageExplorerContainerImageTags.cs"
changelog=CHANGELOG.md

current_version=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$csproj")
[ -n "$current_version" ] || fail "could not find <Version> in $csproj."
[ "$current_version" != "$version" ] || fail "the csproj is already at $version."

grep -q '^## \[Unreleased\]$' "$changelog" || fail "$changelog has no '## [Unreleased]' heading."
grep -q "^## \[$version\]" "$changelog" && fail "$changelog already has a '## [$version]' section."

# The Unreleased section must have a body (only blank lines between it and the next "## [" heading means nothing
# to release yet, and CHANGELOG.md needs entries there before a bump makes sense).
unreleased_body=$(awk '/^## \[Unreleased\]$/ { found=1; next } found && /^## \[/ { exit } found' "$changelog")
[ -n "$(echo "$unreleased_body" | tr -d '[:space:]')" ] \
  || fail "$changelog's Unreleased section is empty; add changelog entries before bumping."

date=$(date +%Y-%m-%d)

sed -i.bak "s|<Version>$current_version</Version>|<Version>$version</Version>|" "$csproj"
rm -f "$csproj.bak"

sed -i.bak "s|const string Tag = \".*\";|const string Tag = \"$version\";|" "$tags_file"
rm -f "$tags_file.bak"

awk -v ver="$version" -v d="$date" '
  /^## \[Unreleased\]$/ && !done {
    print
    print ""
    print "## [" ver "] - " d
    done = 1
    next
  }
  { print }
' "$changelog" > "$changelog.tmp"
mv "$changelog.tmp" "$changelog"

# Verify the edits landed before declaring success.
new_version=$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$csproj")
new_tag=$(sed -n 's|.*const string Tag = "\(.*\)";.*|\1|p' "$tags_file")
[ "$new_version" = "$version" ] || fail "failed to update <Version> in $csproj."
[ "$new_tag" = "$version" ] || fail "failed to update Tag in $tags_file."
grep -q "^## \[$version\] - $date$" "$changelog" || fail "failed to add the '## [$version]' section to $changelog."

echo "Bumped to $version. Review the diff, commit and push, then run scripts/release.sh $version."
