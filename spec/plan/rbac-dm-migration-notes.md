# RBAC DM migration notes

## Confirmed locally

- `E:\DM\bin\disql.exe` exists.
- `disql SYSDBA/"""1q2w3e4R"""@192.168.124.2:5236` can connect.
- DM supports `CREATE TABLE IF NOT EXISTS`.
- DM supports `CREATE INDEX IF NOT EXISTS`.
- DM supports `UUID()` and returns a 36-character string with hyphens.
- DM supports `TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP`.
- DM trigger syntax with `:new."column" := CURRENT_TIMESTAMP;` works in `disql`.

## Implemented in code

- API and Worker use `Database:Provider` to select EF provider.
- `Database:Provider=DM` uses `UseDm(connectionString)`.
- `Outbox.payload` maps to `CLOB` when the EF provider name contains `DM`.
- Entity `Guid` primary keys are explicitly converted to 36-character strings.
- Entity `DateTimeOffset` columns are explicitly converted through UTC `DateTime`, because DM provider returns `System.DateTime` for timestamp columns during materialization.
- Development connection strings were switched from MySQL to DM.

## Scripts

- DM schema script: `sql/rbac-init-dm.sql`.
- DM bootstrap script: `sql/rbac-bootstrap-dm.sql`.
- The DM bootstrap script seeds only the `auth` permission-management rules and the `dashboard` rules. Chinese menu titles are written as normal string literals because the script is intended to be executed from a database management tool.

Suggested schema execution:

```powershell
Get-Content sql\rbac-init-dm.sql | & E:\DM\bin\disql.exe 'SYSDBA/"""1q2w3e4R"""@192.168.124.2:5236'
```

Before running bootstrap, edit the values at the top of `sql/rbac-bootstrap-dm.sql` if needed:

```sql
INSERT INTO "rbac_bootstrap_config" ("userid", "project")
VALUES ('196045', 'oversia');
```

The current local bootstrap values are:

- userid: `196045`
- project: `oversia`

Then run:

```powershell
Get-Content sql\rbac-bootstrap-dm.sql | & E:\DM\bin\disql.exe 'SYSDBA/"""1q2w3e4R"""@192.168.124.2:5236'
```

## Questions to confirm before intranet repeat

- Confirm whether intranet bootstrap should also use userid `196045` and project `oversia`.
- Should the project and namespaces be renamed away from `Rbac.Infrastructure.MySql`? This is architectural cleanup, not required for DM runtime.
- Should MySQL provider packages be removed completely, or kept temporarily for rollback? Runtime config now points to DM, but packages remain referenced.
- Should `DMStorage.Hangfire` be wired? Current Worker uses custom Outbox polling and does not start Hangfire.
