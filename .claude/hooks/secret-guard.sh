#!/usr/bin/env bash
set -euo pipefail

# PreToolUse guard: Claude Code passes the tool input as JSON on stdin. Block obvious credential material.
# exit 2 = deny the write. Patterns cover generic keys/passwords (bare, quoted and JSON-escaped `\"`
# forms, since the payload is JSON), Azure storage/Service Bus connection strings, PEM private keys,
# Square access tokens (production EAAA…, sandbox EAAAE…/sandbox-sq0…), Square application secrets
# (sq0csp-…), WooCommerce keys, GitHub tokens and literal bearer tokens. Best-effort: it only sees
# Write/Edit/MultiEdit content, not Bash-tool writes.
payload="$(cat)"

if printf '%s' "$payload" | grep -Eiq '(BEGIN ((RSA|EC|OPENSSH|ENCRYPTED|DSA) )?PRIVATE KEY|AccountKey=|SharedAccessKey=|SharedAccessSignature=|(password|pwd)=[^;"'"'"' ]{4,}|(password|pwd|client[_-]?secret|api[_-]?key|signature[_-]?key)[\\]?"?[[:space:]]*[:=][[:space:]]*[\\]?["'"'"'][^"'"'"'\\ ]{4,}|EAAA[A-Za-z0-9_-]{20,}|sandbox-sq0[a-z]{3}-[A-Za-z0-9_-]{10,}|sq0csp-[A-Za-z0-9_-]{10,}|(ck|cs)_[0-9a-f]{40}|ghp_[A-Za-z0-9]{20,}|Bearer [A-Za-z0-9._-]{20,})'; then
  echo "Blocked: possible secret in file content. Use Aspire secret parameters (user-secrets locally, Key Vault in Azure)." >&2
  exit 2
fi

exit 0
