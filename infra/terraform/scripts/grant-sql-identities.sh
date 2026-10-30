#!/usr/bin/env bash
# Create a database user for each application's managed identity.
#
# Terraform creates the SQL server, the database and the identities, but it cannot create a database *user*:
# that is T-SQL, run inside the database, by the Entra administrator. Until this has run, every connection
# fails with "Login failed for user '<token-identified principal>'" — which is the single most common way a
# first deploy to a new environment fails.
#
# The server is Entra-authentication-only (there is no SQL login and no password), so whoever runs this has
# to be a member of the sql_admin_group from the tfvars.
#
# Usage, from infra/terraform with dev's state initialised:
#   ./scripts/grant-sql-identities.sh
#   ./scripts/grant-sql-identities.sh --dry-run        # print the T-SQL and stop
#
# Requires: az (signed in as a member of the admin group), terraform, and sqlcmd (go-sqlcmd:
# `brew install sqlcmd`). With --dry-run neither sqlcmd nor a route to the server is needed.

set -euo pipefail

DRY_RUN=0
[[ "${1:-}" == "--dry-run" ]] && DRY_RUN=1

command -v terraform >/dev/null || { echo "terraform is not installed" >&2; exit 1; }

SERVER="$(terraform output -raw sql_server_fqdn)"
DATABASE="$(terraform output -raw sql_database_name)"
IDENTITY_NAMES_JSON="$(terraform output -json app_identity_names)"

# Roles per application, following what each one actually does:
#   migrations  applies the schema and seeds, so it owns the database
#   api, worker read and write rows; they never change the schema
#
# The gateway, site and admin get no database user at all: nothing below the API talks to SQL
# (CLAUDE.md "Mandatory seams").
declare -A ROLES=(
  [migrations]="db_owner"
  [api]="db_datareader db_datawriter"
  [worker]="db_datareader db_datawriter"
)

sql=""
for app in migrations api worker; do
  name="$(printf '%s' "$IDENTITY_NAMES_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['$app'])")"

  sql+=$'\n'"-- ${app}"$'\n'
  sql+="IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'${name}')"$'\n'
  sql+="    CREATE USER [${name}] FROM EXTERNAL PROVIDER;"$'\n'

  for role in ${ROLES[$app]}; do
    # Guarded rather than bare: both statements are then safe to re-run, which matters because this script
    # is run again every time an application is added to the model.
    sql+="IF IS_ROLEMEMBER(N'${role}', N'${name}') = 0"$'\n'
    sql+="    ALTER ROLE ${role} ADD MEMBER [${name}];"$'\n'
  done
done

echo "Server:   $SERVER"
echo "Database: $DATABASE"
echo "$sql"

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "-- dry run: nothing was executed"
  exit 0
fi

command -v sqlcmd >/dev/null || { echo "sqlcmd is not installed (brew install sqlcmd)" >&2; exit 1; }

# -G with no username: sqlcmd takes the Entra token from the az CLI session. Every statement above is
# guarded, so this is safe to re-run after a new application is added to the model.
printf '%s' "$sql" | sqlcmd -S "$SERVER" -d "$DATABASE" -G --exit-on-error
echo "done"
