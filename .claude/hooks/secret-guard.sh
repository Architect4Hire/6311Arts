#!/usr/bin/env bash
set -euo pipefail

# PreToolUse guard: Claude Code passes the tool input as JSON on stdin. Block obvious credential material.
# exit 2 = deny the write. Patterns cover generic keys/passwords, Azure storage/Service Bus connection
# strings, private keys, Square access tokens (production EAAA…, sandbox EAAAE…/sandbox-sq0…) and Square
# application secrets (sq0csp-…).
payload="$(cat)"

if printf '%s' "$payload" | grep -Eiq '(BEGIN (RSA|EC|OPENSSH|PRIVATE) KEY|AccountKey=|SharedAccessKey=|SharedAccessSignature=|Password=[^;"'"'"' ]{4,}|client[_-]?secret[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"']+|api[_-]?key[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"']+|password[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"']+|signature[_-]?key[[:space:]]*[:=][[:space:]]*["'"'"'][^"'"'"']+|EAAA[A-Za-z0-9_-]{20,}|sandbox-sq0[a-z]{3}-[A-Za-z0-9_-]{10,}|sq0csp-[A-Za-z0-9_-]{10,}|(ck|cs)_[0-9a-f]{40})'; then
  echo "Blocked: possible secret in file content. Use Aspire secret parameters (user-secrets locally, Key Vault in Azure)." >&2
  exit 2
fi

exit 0
