#!/bin/bash
set -e

BUMP_TYPE="$1"

if [ "$BUMP_TYPE" != "major" ] && [ "$BUMP_TYPE" != "minor" ] && [ "$BUMP_TYPE" != "patch" ]; then
    echo "Usage: ./release.sh <major|minor|patch>"
    exit 1
fi

# Check for uncommitted changes
if [ -n "$(git status --porcelain)" ]; then
    echo "Error: uncommitted changes. Commit or stash first."
    exit 1
fi

PROJECT_SETTINGS="ProjectSettings/ProjectSettings.asset"

if [ ! -f "$PROJECT_SETTINGS" ]; then
    echo "Error: $PROJECT_SETTINGS not found. Run this from the Unity project root."
    exit 1
fi

# Read current Unity bundle version
CURRENT=$(node -e "
const fs = require('fs');
const text = fs.readFileSync('$PROJECT_SETTINGS', 'utf8');
const match = text.match(/^  bundleVersion: (.+)$/m);
if (!match) process.exit(1);
console.log(match[1].trim());
")

if ! [[ "$CURRENT" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: bundleVersion must be semantic version major.minor.patch, got: $CURRENT"
    exit 1
fi

IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT"

case "$BUMP_TYPE" in
    major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
    minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
    patch) PATCH=$((PATCH + 1)) ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"
TAG="v$NEW_VERSION"
DATE=$(date +%Y-%m-%d)

if git rev-parse "$TAG" >/dev/null 2>&1; then
    echo "Error: tag $TAG already exists locally."
    exit 1
fi

if git ls-remote --exit-code --tags origin "refs/tags/$TAG" >/dev/null 2>&1; then
    echo "Error: tag $TAG already exists on origin."
    exit 1
fi

echo "Bumping Unity bundleVersion: $CURRENT -> $NEW_VERSION"

# Update Unity ProjectSettings bundleVersion and increment AndroidBundleVersionCode if present
node -e "
const fs = require('fs');
const file = '$PROJECT_SETTINGS';
let text = fs.readFileSync(file, 'utf8');
text = text.replace(/^  bundleVersion: .+$/m, '  bundleVersion: $NEW_VERSION');
text = text.replace(/^  AndroidBundleVersionCode: (\d+)$/m, (_, n) => '  AndroidBundleVersionCode: ' + (Number(n) + 1));
fs.writeFileSync(file, text);
"

# Update CHANGELOG if it exists
if [ -f CHANGELOG.md ]; then
    node -e "
const fs = require('fs');
let changelog = fs.readFileSync('CHANGELOG.md', 'utf8');
if (changelog.includes('## [Unreleased]')) {
  changelog = changelog.replace('## [Unreleased]', '## [Unreleased]\n\n## [$NEW_VERSION] - $DATE');
} else {
  changelog = '## [$NEW_VERSION] - $DATE\n\n' + changelog;
}
fs.writeFileSync('CHANGELOG.md', changelog);
"
fi

# Commit, tag, push. The GitHub release workflow runs from the pushed tag.
git add "$PROJECT_SETTINGS"
if [ -f CHANGELOG.md ]; then
    git add CHANGELOG.md
fi

git commit -m "Release $TAG"
git tag "$TAG"
git push origin HEAD
git push origin "$TAG"

echo ""
echo "Released $TAG"
echo "GitHub Actions will build and create the release from the tag."
