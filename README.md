# Business Finance — Windows portable acceptance build

Version 0.2.0 • Build date 06-10-2026

Designed & Developed by Bikash Ranjan Nayak

## Open the application

1. Download the ZIP on your Windows computer.
2. Right-click it and select **Extract All**.
3. Open the extracted **BusinessFinance** folder.
4. Double-click **BusinessFinance.exe**.
5. Create your administrator name and password (at least 12 characters).

No Visual Studio, Python, .NET SDK, separate .NET runtime installation or administrator installation is required. Keep the entire extracted folder together; do not move just the EXE. This package targets **Windows 10/11, 64-bit x64**. It is not an Android application. It does not require internet for ordinary use.

**Release status:** This is a real application with persistent SQLite records and connected accounting workflows, delivered for acceptance testing. It is not approved for production use and does not yet meet every requested acceptance criterion. Read `RELEASE_NOTES.md` for the remaining work. Windows interactive launch and printing have not been tested in the build environment.

## First-time setup

- **Masters → Company → Edit:** enter your company details and two-digit GST state code. Company logo is managed with **Company logo**.
- Add banks/cash, customers, suppliers, employees, sites, items and expense categories.
- **Settings → Financial year:** select the reporting year.
- **Settings → Statutory settings:** review contribution rates, wage bases, ceilings and rounding. Defaults are user-requested configuration, not a statement of current legal requirements.
- **Settings → Opening balance:** enter verified opening ledger balances. Debit assets; credit liabilities/equity. The balancing entry goes to opening equity. Reconcile the complete opening trial balance before use.
- For historical FDs and employee loans, select **Historical opening (no bank movement)** on their creation forms. Do not also enter the same principal again in Opening balance.
- Configure an automatic backup folder and create a manual verified backup.

The application starts without demo financial data or a default password. Default ledger accounts, expense categories and editable statutory settings are initialized.

## Daily workflows

- **Sales/Purchase:** manually enter invoice numbers, add multiple lines and choose Draft or Posted. Invoice posting creates debt and GST entries, not bank movement. Drafts can be posted; use cancellation/re-entry for corrections.
- **Receipt/Payment:** choose a named head. Customer receipts and supplier payments allocate to one or more invoices. Use the opening-balance settlement switch only for unallocated opening debt. A transaction posts once.
- **Customer advances:** receive using Customer Advance, then use **Sales → Apply advance** to settle an invoice without another bank entry. Customer Refund offers AR or CUSTADV as the source.
- **Bank transfer:** select two different company accounts. No income or expense is created.
- **Staff advance:** Payment → Staff Advance. Site expenses paid from this advance reduce the advance, not bank again. Select a bank instead for a directly paid site expense. Unused amounts return through Staff Advance Refund.
- **Employee advance:** separate receivable, recoverable through payroll or Employee Advance Refund.
- **Payroll:** select month, Load employees, enter absent days/adjustments, Calculate/review, then Finalize. Each outstanding employee loan has its own recovery column. Sundays remain paid; joining/leaving dates bound eligible days. Finalization does not change bank. Pay salary with Payment → Salary/Wages, selecting the employee and payroll month. Reverse salary payments before reopening payroll.
- **Employee loan:** creation disburses money unless marked historical opening. Recover separately from advances, through payroll or a direct receipt.
- **FD:** creation invests principal once. Close/renew with separately stated actual interest. Release BG/other liens before closing.
- **BG:** maintain dispatch, delivery, confirmation and amendments. Link multiple FDs. Closing BG retains FDs and flags unreleased liens.
- **Business loan:** create the sanction/account first; use Receipt → Business Loan Received for actual disbursement. Repayment splits total payment into principal, interest and charges. Maintain next EMI/recovery date through Recovery / EMI terms. EMI Schedule is a monthly reducing-balance projection, not the lender's statement.
- **Reports:** search and paginate, drill into details, export XLSX, preview/print or export PDF. PDF export uses the Windows **Microsoft Print to PDF** printer. Excel money columns are numeric; displayed dates are DD-MM-YYYY.
- **Attachments:** supported on invoices, vouchers, employees, loans/FDs and BGs. For site expenses, use their voucher in Transaction Register. Remove/replace through the audited attachment manager.
- **Bank reconciliation:** Reports → Bank Reconciliation → select entry → Match or Unmatch.
- **Corrections:** use Transaction Register → Reverse, invoice cancellation, or controlled payroll reopen. Posted lines and audit rows are not deleted.

## Data and backups

Data is saved outside the extracted program folder, at:

`%LOCALAPPDATA%\BikashRanjanNayak\BusinessFinance\business.db`

Moving/replacing the program folder does not remove that database. Records remain after restart. Backups include logo and attachment bytes. Backups and the local database are not encrypted.

Automatic backups run only while the application is open and the session is active. Restore first validates the candidate, creates a verified pre-restore copy, replaces the database and signs out. Sign in with the account stored in the restored backup.

The application is local and single-user. There is no multi-computer synchronization. Only one instance should use its data folder. It is not compatible with the earlier incomplete package's database; no automatic migration is included.

## Password and support

Keep the administrator password safely. There is no password-recovery bypass in this release. Password changes require the current password. Idle sessions expire after 20 minutes; close and reopen to sign in again.

Unexpected errors are logged to `technical.log` in the data folder. Automatic backup problems go to `backup-errors.log`. Do not send a financial database or attachments when only an error log is needed.

## Included verification and source

- `QA_REPORT.md` and `Test-Results.txt`: completed automated checks and outstanding Windows checks.
- `Run_Automated_Checks.cmd`: optional Windows check runner. It uses isolated temporary test databases, not your application database, and writes `Windows-Test-Results.txt` beside the program.
- `Source`: C# source, relational schema, automated tests and normal .NET build commands for a developer. You do not need these tools to launch the packaged application.
- `Licenses`: runtime redistribution notices and dependency inventory.
