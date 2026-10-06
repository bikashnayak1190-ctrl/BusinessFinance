# Architecture and controls

The C# Finance.Core assembly owns all business writes. Windows Forms is a client of this engine. SQLite is the relational source of truth. No external server or browser storage is used.

`Engine.Write` authenticates, starts a database transaction, performs the domain operation, validates daily running balances for protected advance/loan/payable accounts, then commits. Exceptions dispose/roll back the complete transaction. `Post` enforces balanced signed paise lines, minimum two nonzero lines, voucher sequencing, required dimensions and period locks. The database enforces foreign keys and immutable posting/audit rows.

A positive posting is debit; negative is credit. Bank, party, employee, site, instrument and category dimensions travel on the same lines used by ledgers and dashboard. Receipts/payments allocate to source invoices. Salary allocations refer to finalized payroll rows. Invoice/payment status is derived from allocations; instruments additionally retain operational metadata and closure/lien history.

Reversal creates new opposite postings and links the original and reversing vouchers. Financial history remains. Dependency checks stop reversal of settled invoices, spent advances, paid payroll and encumbered FDs. Payroll reopen creates a new revision after reversing the old finalization. Daily protected-balance validation includes future transactions, so an inserted backdated recovery cannot silently overdraw a later balance.

Invoice lines snapshot rates, tax components and transaction GST state information. Payroll snapshots store attendance, salary, selected statutory setting ID, all contributions and recovery instructions. Company/master changes and critical controls are audited. Financial record editing is intentionally limited; cancellation/re-entry is available for invoices.

Amounts are Int64 paise at rest. Quantity, rates and formula arithmetic use decimal, then a single Money conversion rounds to paise away from zero. Payroll applies configured paise/nearest-rupee/ceiling rules. DateOnly supplies numeric dates; display uses DD-MM-YYYY. Database money checks reject non-integer monetary storage for the principal posting sources.

Backup uses SQLite's backup API. Candidate restore validation checks application/version identity, database integrity, foreign keys and balanced vouchers. Attachment blobs and company logo are contained in the database, making backups self-contained. Restore creates a verified protective copy before replacement and requires a fresh login.

Security: local administrator only, PBKDF2-SHA256 with per-user random salt and 210,000 iterations, fixed-time hash comparison, five-failure timed lockout and a 20-minute idle session. No plaintext password. This is application access control, not database encryption or protection against an OS administrator.

See RELEASE_NOTES.md for explicitly uncompleted requirements and limitations, particularly historical settlement reporting, compliance exceptions and Windows acceptance.
