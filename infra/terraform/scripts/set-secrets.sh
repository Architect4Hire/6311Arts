#!/usr/bin/env bash
# Set the Key Vault secrets Terraform only creates shells for.
#
# Terraform creates each of these with the literal placeholder "REPLACE-ME ..." and then ignores the value
# for ever, so nothing here ever enters Terraform state, a plan or a workflow log (CLAUDE.md "Environments
# and secrets"). This script generates the ones that are generated and prompts for the ones a person holds.
#
# It prints secret *names*, never values — the same rule .claude/rules/aspire.md sets for checking
# user-secrets on a laptop.
#
# Usage:
#   ./set-secrets.sh --vault <key-vault-name>            # fill in anything still a placeholder
#   ./set-secrets.sh --vault <key-vault-name> --rotate internal-token-signing-key,internal-token-public-key
#
# Requires: az (signed in, with Key Vault Secrets Officer on the vault) and openssl.

set -euo pipefail

VAULT=""
ROTATE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --vault)  VAULT="${2:?--vault needs a value}"; shift 2 ;;
    --rotate) ROTATE="${2:?--rotate needs a comma-separated list}"; shift 2 ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$VAULT" ]]; then
  echo "--vault is required. Get it with: terraform output -raw key_vault_name" >&2
  exit 2
fi

for tool in az openssl; do
  command -v "$tool" >/dev/null || { echo "$tool is not installed" >&2; exit 1; }
done

PLACEHOLDER_PREFIX="REPLACE-ME"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
chmod 700 "$TMP"

# Is this secret still unset? Reads the value to compare it, and never echoes it.
needs_value() {
  local name="$1"
  if [[ ",$ROTATE," == *",$name,"* ]]; then
    return 0
  fi
  local current
  current="$(az keyvault secret show --vault-name "$VAULT" --name "$name" --query value -o tsv 2>/dev/null || echo "__missing__")"
  [[ "$current" == "__missing__" || "$current" == "$PLACEHOLDER_PREFIX"* ]]
}

set_from_file() {
  local name="$1" file="$2"
  az keyvault secret set --vault-name "$VAULT" --name "$name" --file "$file" --output none
  echo "  set $name"
}

echo "Vault: $VAULT"

# --- The ES256 pair the internal token is signed with (ADR 0002) ----------------------------------------
# One key, two secrets: the gateway reads the private half and the API reads only the public half. They are
# generated together, because a public key from a different pair fails every request with a 401 and nothing
# else explains why. Rotating one means rotating both.
if needs_value internal-token-signing-key || needs_value internal-token-public-key; then
  openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$TMP/signing.pem" 2>/dev/null
  openssl pkey -in "$TMP/signing.pem" -pubout -out "$TMP/public.pem" 2>/dev/null

  # PKCS#8 "PRIVATE KEY" and SubjectPublicKeyInfo "PUBLIC KEY", which is what ECDsa.ImportFromPem reads and
  # what Arts6311.Api's InternalTokenOptions insists on (it refuses anything containing "PRIVATE KEY").
  set_from_file internal-token-signing-key "$TMP/signing.pem"
  set_from_file internal-token-public-key "$TMP/public.pem"
else
  echo "  internal-token-signing-key, internal-token-public-key already set"
fi

# --- Keys and passwords generated per environment -------------------------------------------------------
if needs_value idempotency-fingerprint-key; then
  # Base64 of 32 bytes, as AppHost's Base64KeyDefault produces for run mode (ADR 0020).
  openssl rand -base64 32 | tr -d '\n' > "$TMP/idem"
  set_from_file idempotency-fingerprint-key "$TMP/idem"
else
  echo "  idempotency-fingerprint-key already set"
fi

if needs_value seed-admin-password; then
  # 32 base64url characters: well past the 14-character staff minimum (IdentityPolicy), and random enough
  # that the breached-password check will never have seen it.
  openssl rand -base64 48 | tr -d '\n=+/' | cut -c1-32 > "$TMP/admin"
  set_from_file seed-admin-password "$TMP/admin"
  echo "  (read it back with: az keyvault secret show --vault-name $VAULT --name seed-admin-password --query value -o tsv)"
else
  echo "  seed-admin-password already set"
fi

if needs_value site-ssr-credential; then
  # The credential the site presents on its server-side reads so the gateway gives them their own
  # rate-limit partition (ADR 0046, prompt 6.1). Hex, so it is safe in a header.
  openssl rand -hex 32 | tr -d '\n' > "$TMP/ssr"
  set_from_file site-ssr-credential "$TMP/ssr"
else
  echo "  site-ssr-credential already set"
fi

# --- Square, which only a person has -------------------------------------------------------------------
# Read from the environment if it is there, prompted for otherwise, and skipped if neither. Sandbox
# credentials outside production; production credentials only ever into prod's vault.
prompt_secret() {
  local name="$1" env_var="$2" value=""

  if ! needs_value "$name"; then
    echo "  $name already set"
    return
  fi

  if [[ -n "${!env_var:-}" ]]; then
    value="${!env_var}"
  elif [[ -t 0 ]]; then
    read -rsp "  $name (leave empty to skip): " value
    echo
  fi

  if [[ -z "$value" ]]; then
    echo "  skipped $name - still a placeholder"
    return
  fi

  printf '%s' "$value" > "$TMP/$name"
  set_from_file "$name" "$TMP/$name"
}

prompt_secret square-access-token SQUARE_ACCESS_TOKEN
prompt_secret square-webhook-signature-key SQUARE_WEBHOOK_SIGNATURE_KEY

# --- What is still unset --------------------------------------------------------------------------------
echo
echo "Still placeholders (a revision using one of these will fail to start):"
remaining=0
while read -r name; do
  if needs_value "$name"; then
    echo "  $name"
    remaining=$((remaining + 1))
  fi
done < <(az keyvault secret list --vault-name "$VAULT" --query "[].name" -o tsv)

if [[ "$remaining" -eq 0 ]]; then
  echo "  none"
fi
