#!/usr/bin/env bash
set -Eeuo pipefail

# psql variables quote credentials as SQL literals, including embedded quotes.
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 \
  --set orders_password="$ORDERS_DB_PASSWORD" \
  --set inventory_password="$INVENTORY_DB_PASSWORD" \
  --set payments_password="$PAYMENTS_DB_PASSWORD" \
  --set notifications_password="$NOTIFICATIONS_DB_PASSWORD" <<'SQL'
CREATE ROLE orders LOGIN PASSWORD :'orders_password';
CREATE ROLE inventory LOGIN PASSWORD :'inventory_password';
CREATE ROLE payments LOGIN PASSWORD :'payments_password';
CREATE ROLE notifications LOGIN PASSWORD :'notifications_password';

CREATE DATABASE orders OWNER orders;
CREATE DATABASE inventory OWNER inventory;
CREATE DATABASE payments OWNER payments;
CREATE DATABASE notifications OWNER notifications;

REVOKE ALL ON DATABASE orders FROM PUBLIC;
REVOKE ALL ON DATABASE inventory FROM PUBLIC;
REVOKE ALL ON DATABASE payments FROM PUBLIC;
REVOKE ALL ON DATABASE notifications FROM PUBLIC;
SQL
