#!/bin/sh
# Runs once, when the PostgreSQL data volume is first created (docker-entrypoint-initdb.d).
# Creates the application's own login and database so the API never connects as the superuser.
# The app role owns the StudentSync database (it runs EF Core migrations and creates the Hangfire schema)
# but cannot create roles or databases or read other databases.
set -eu
: "${APP_DB_PASSWORD:?set APP_DB_PASSWORD in postgres.env}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -v app_password="$APP_DB_PASSWORD" <<'SQL'
CREATE ROLE studentsync LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE "StudentSync" OWNER studentsync ENCODING 'UTF8';
REVOKE ALL ON DATABASE "StudentSync" FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE "StudentSync" TO studentsync;
SQL
