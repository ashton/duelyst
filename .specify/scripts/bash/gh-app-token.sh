#!/usr/bin/env bash

# Mints a short-lived (1 hour) GitHub App installation access token and prints it to stdout.
#
# Usage: .specify/scripts/bash/gh-app-token.sh
#
# Requires these environment variables to be set (never hardcoded/committed):
#   CLAUDE_GH_APP_ID                 - the GitHub App's numeric App ID
#   CLAUDE_GH_APP_INSTALLATION_ID    - the numeric installation ID for this repo
#   CLAUDE_GH_APP_PRIVATE_KEY_PATH   - path to the App's downloaded .pem private key
#
# Standard GitHub App server-to-server auth flow: sign a short-lived JWT with the App's
# private key, exchange it for an installation access token. The printed token is what
# callers should export as GH_TOKEN before running `gh`/`git` commands that should act as
# the App's bot identity rather than the locally logged-in `gh auth` user.

set -euo pipefail

for var in CLAUDE_GH_APP_ID CLAUDE_GH_APP_INSTALLATION_ID CLAUDE_GH_APP_PRIVATE_KEY_PATH; do
    if [[ -z "${!var:-}" ]]; then
        echo "ERROR: $var is not set. Required: CLAUDE_GH_APP_ID, CLAUDE_GH_APP_INSTALLATION_ID, CLAUDE_GH_APP_PRIVATE_KEY_PATH." >&2
        exit 1
    fi
done

if [[ ! -f "$CLAUDE_GH_APP_PRIVATE_KEY_PATH" ]]; then
    echo "ERROR: CLAUDE_GH_APP_PRIVATE_KEY_PATH ($CLAUDE_GH_APP_PRIVATE_KEY_PATH) does not exist." >&2
    exit 1
fi

for tool in openssl curl jq; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "ERROR: required tool '$tool' not found on PATH." >&2
        exit 1
    fi
done

base64url() {
    openssl base64 -A | tr '+/' '-_' | tr -d '='
}

now=$(date +%s)
iat=$((now - 60))
exp=$((now + 540)) # 9 minutes; GitHub caps App JWTs at 10 minutes

header='{"alg":"RS256","typ":"JWT"}'
payload=$(printf '{"iat":%d,"exp":%d,"iss":"%s"}' "$iat" "$exp" "$CLAUDE_GH_APP_ID")

signing_input="$(printf '%s' "$header" | base64url).$(printf '%s' "$payload" | base64url)"
signature=$(printf '%s' "$signing_input" | openssl dgst -sha256 -sign "$CLAUDE_GH_APP_PRIVATE_KEY_PATH" | base64url)
jwt="${signing_input}.${signature}"

response=$(curl -s -X POST \
    -H "Authorization: Bearer $jwt" \
    -H "Accept: application/vnd.github+json" \
    "https://api.github.com/app/installations/$CLAUDE_GH_APP_INSTALLATION_ID/access_tokens")

token=$(echo "$response" | jq -r '.token // empty')

if [[ -z "$token" ]]; then
    echo "ERROR: failed to mint installation token. GitHub API response:" >&2
    echo "$response" >&2
    exit 1
fi

echo "$token"
