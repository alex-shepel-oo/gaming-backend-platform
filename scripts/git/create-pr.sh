#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# Usage: scripts/git/create-pr.sh [base-branch]
#
# Verifies the current branch locally (scripts/all/verify.sh: backend +
# frontend build and test, nothing deployed -- the same checks CI runs),
# then opens an editor for the PR title/body, then pushes and opens the PR
# via gh. Nothing reaches GitHub until verify.sh is green, so a red build
# never shows up as a PR someone else has to notice and point out.

HEAD_BRANCH="$(git rev-parse --abbrev-ref HEAD)"

if [[ "$HEAD_BRANCH" == "HEAD" ]]; then
  echo "Error: detached HEAD, not on a branch. Check out a branch first." >&2
  exit 1
fi

BASE_BRANCH="${1:-}"

if [[ -z "$BASE_BRANCH" ]]; then
  echo "No base branch given. Pick one to open the PR against:"

  mapfile -t CANDIDATES < <(
    git for-each-ref --format='%(refname:short)' refs/heads refs/remotes/origin \
      | sed 's#^origin/##' \
      | grep -vFx -e "$HEAD_BRANCH" -e "origin" \
      | sort -u
  )
  CANDIDATES+=("(type a branch name)")

  select CHOICE in "${CANDIDATES[@]}"; do
    if [[ "$CHOICE" == "(type a branch name)" ]]; then
      read -rp "Base branch: " BASE_BRANCH
    else
      BASE_BRANCH="$CHOICE"
    fi
    [[ -n "$BASE_BRANCH" ]] && break
    echo "Pick one of the numbers above, or enter a branch name."
  done
fi

echo "PR: $HEAD_BRANCH -> $BASE_BRANCH"
echo

if [[ -n "$(git status --porcelain)" ]]; then
  echo "Warning: working tree has uncommitted changes -- they will not be part of this PR."
  read -rp "Continue anyway? [y/N] " CONFIRM
  [[ "$CONFIRM" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
fi

echo "Running local verification (scripts/all/verify.sh) before touching GitHub..."
if ! "$REPO_ROOT/scripts/all/verify.sh"; then
  echo
  echo "verify.sh failed -- not pushing, not opening a PR. Fix the failure above and re-run this script." >&2
  exit 1
fi
echo "Verification green."
echo

MESSAGE_FILE="$(mktemp)"
trap 'rm -f "$MESSAGE_FILE"' EXIT

cat > "$MESSAGE_FILE" <<EOF

# Line 1 above is the PR title. Leave a blank line, then the PR body below it.
# Lines starting with # are stripped and ignored, same as git commit.
#
# $HEAD_BRANCH -> $BASE_BRANCH
#
# An empty title aborts, same as an empty commit message aborts a commit.
EOF

"${EDITOR:-vi}" "$MESSAGE_FILE"

# Strip comment lines, then split the first non-empty line (title) from
# everything after it (body) -- same shape as a git commit message.
FILTERED="$(grep -v '^#' "$MESSAGE_FILE")"
TITLE="$(echo "$FILTERED" | awk 'NF{print; exit}')"
BODY="$(echo "$FILTERED" | awk 'f{print} !f && NF{f=1}' | sed '1{/^$/d}')"

if [[ -z "$TITLE" ]]; then
  echo "Empty title, aborting -- not pushing, not opening a PR." >&2
  exit 1
fi

echo "Pushing $HEAD_BRANCH..."
git push -u origin "$HEAD_BRANCH"

echo "Opening PR..."
gh pr create --base "$BASE_BRANCH" --head "$HEAD_BRANCH" --title "$TITLE" --body "$BODY"
