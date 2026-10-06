# Release assessment — 06-10-2026

**Business Finance 0.2.0 is an acceptance build, not a completed production release.**

The earlier package was audited and found to contain primarily read-only registers. This build replaces that implementation with a new shared accounting engine and working transaction forms. It does not upgrade or import the earlier schema.

## Implemented and exercised in automated tests

- Persistent relational SQLite database, foreign keys, immutable posting/audit rows, integer-paise money, decimal calculations and numeric calendar dates.
- Password hashing, failed-login lockout, password change and session authorization.
- Manual sales numbers; purchase duplicate scope is supplier + financial year + number.
- Draft/post/cancel invoices, multi-line domestic GST calculation, manual-rate verification, ITC/non-ITC purchase posting.
- Partial/multiple invoice settlement, opening-balance settlements, customer advances and advance allocation without duplicate bank movement.
- Transfers, staff/site advance settlement/refund, separate employee advances.
- Calendar payroll, paid Sundays, joining/leaving, basic/allowance, configured employee EPF/ESIC deductions, ESIC continuation override, multiple employee-loan recovery columns, finalization, partial salary payment and controlled reopening.
- FD investment/closure/renewal, multiple FD-BG links, other-lien release, BG closure with pending-lien status, loan principal/interest split and EMI projection.
- Period control, reversal dependencies, prevention of backdated over-recovery, attachments, manual reconciliation, audit, reports, dashboard reconciliation and XLSX export.
- Verified backup, restore with protective copy, corrupt-backup rejection and restart persistence.

## Remaining production gates and functional gaps

1. **Windows acceptance is outstanding.** The x64 executable was cross-compiled and packaged; it has not been launched on Windows here. Screen layout, keyboard flow, high-DPI scaling, all visible actions, logos, native file association, PDF/print drivers, Excel desktop opening and Windows restore/file-lock behavior require real Windows checks.
2. **The complete specification is not yet fulfilled.** Draft invoices use cancellation/re-entry rather than an edit-in-place draft workflow. Master registers show at most 500 records and do not yet have server-side master paging/search. Several report families share an underlying report instead of dedicated tailored statement layouts. Salary details are exposed through a saved calculation snapshot rather than a fully expanded exportable payroll register. Bank reconciliation matches ledger entries manually; it has no separate statement-entry register/import. Arbitrary correcting journals and FD-closure reversal are not offered.
3. **Historical operational reporting needs further work.** Ledger, trial balance and invoice allocation reports use effective dates. Tests cover historical invoice outstanding and GST reversals across periods. Historical instrument metadata/status and a dedicated FY carry-forward presentation still need refinement; BG amendments currently retain an audit trail rather than an event-dated operational register.
4. **Compliance is configurable but not certified.** Regular domestic GST logic is implemented; exceptions such as exports/SEZ, reverse charge, credit notes, filing JSON and government portal submission are outside this build. Payroll is the requested employee-deduction workflow, not a complete statutory payroll product with employer contributions, professional tax, TDS, ECR filing or a verified contribution-period eligibility engine. ESIC continuation is an explicit reviewed input. Verify current applicable rules and the chosen wage bases before live use.
5. **Document polish remains.** Print/PDF uses a branded, paginated document layout, but invoices and financial statements still need commercial document-format acceptance. Full filter controls for every dimension, batch salary payment, file-type content validation beyond the current controls and a complete data migration workflow are not finished.
6. **Deployment/security sign-off remains.** Executables are unsigned. There is no installer/update service, at-rest encryption, password recovery, external audit anchoring, load/soak testing or independent security review. The application is intentionally local and single-admin. SQLitePCLRaw 2.1.6 and Microsoft.Data.Sqlite 8.0.10 are reproducibly pinned; dependency update/security review is a release gate. Local machine administrators can access the database files.
7. **Automatic backup scope:** application-open scheduling only; no Windows service or unattended closed-application scheduler. Test recovery using a second Windows folder/computer before relying on the backup operationally.

These are concrete remaining requirements, not claims that all 40 final acceptance criteria passed. Do not describe this package as the final production system.
