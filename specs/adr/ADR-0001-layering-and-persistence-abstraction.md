# ADR-0001 — Layering and the persistence abstraction

- **Status:** Accepted
- **Date:** 2026-08-13
- **Phase:** 0
- **Relates to:** `project.md` §3 (Architecture pattern), §10 (Phase 9)

## Context

`project.md` §3 locks in SQLite for Phases 1–8 and a migration to SQL Server Express at Phase 9, and
requires a "repository / unit-of-work abstraction over all DB access, from day one" so that migration
is not a rewrite. It also locks in a hybrid data-access pattern: EF Core for writes, migrations and
domain work; Dapper for reporting queries.

The failure mode this is guarding against is well known: `DbContext` leaks into services and
viewmodels, provider-specific SQL is written inline in reports, and by the time the provider changes
the abstraction exists on paper only.

## Decision

1. **Four layers with a one-directional dependency rule:**
   `Domain ← Application ← Infrastructure ← App`.
   - `Domain` references nothing — no EF Core, no other project.
   - `Application` references `Domain` only, and must not reference EF Core.
   - Only `Infrastructure` references `Microsoft.EntityFrameworkCore.*` and the provider packages.
   - This is enforced by an **architecture test**, not by convention, because a convention that is
     only documented will be broken.

2. **No `DbContext` type crosses an assembly boundary.** Callers use:
   - `IRepository<TEntity>` for entity access,
   - `IUnitOfWork` for transactions and `SaveChangesAsync`,
   - `ISqlQueryExecutor` for Dapper reporting reads,
   - `IDbConnectionFactory` to hide `SqliteConnection` vs `SqlConnection`.

3. **Reporting SQL is resolved per provider, by name.** `ISqlQueryExecutor` takes a query key, and an
   `ISqlDialect` implementation supplies the provider-specific statement. Calling code never contains
   a dialect-specific string. Without this, Dapper — not EF Core — is what makes the Phase 9 migration
   expensive.

4. **Provider is configuration**, not a compile-time choice: `Database:Provider` = `Sqlite` today,
   `SqlServer` from Phase 9. Both remain buildable, so a single-user install can stay on SQLite.

5. **Entity `Id` is a client-generated GUID**, assigned at construction rather than by the database.
   Phase 8's Tally XML export depends on a stable GUID per record for Tally's "overwrite if GUID
   exists" behaviour; a database-generated key would not be stable across an export/re-import cycle.

## Consequences

**Positive.** The Phase 9 provider swap is a configuration change plus a migration set. Reporting
queries stay fast (Dapper) without becoming a migration blocker. Tally round-tripping works by
construction rather than by a later retrofit.

**Negative.** More indirection than a small app needs, and named-query resolution is more ceremony
than inline SQL. Accepted deliberately: `project.md` §3 makes the multi-user migration a stated
requirement, and retrofitting this abstraction after eight phases of code is the expensive path.

**Watch for.** Client-generated GUID primary keys fragment clustered indexes on SQL Server. If Phase
9 or Phase 10 performance testing shows this hurting, the fix is sequential GUIDs — not abandoning
stable ids. Record that as a follow-up ADR if it happens.
