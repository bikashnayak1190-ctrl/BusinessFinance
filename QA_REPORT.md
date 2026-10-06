# Verification report

Build: Business Finance 0.2.0
Date: 06-10-2026

## Completed evidence

- C# core, desktop and test projects compiled with .NET SDK 8.0.425 / Roslyn, targeting .NET 8. The final compiler run reported no warnings or errors.
- **62 automated checks passed, 0 failed.** See Test-Results.txt and Source/Finance.Tests/Program.cs.
- The same tests were also executed using an application-local, self-contained Linux runtime layout without relying on an installed runtime. This verifies the packaging approach and shared engine, **not Windows execution**.
- Windows app host is an x64 PE GUI executable. The test host is an x64 PE console executable. Windows core/desktop runtimes 8.0.31, hostfxr, hostpolicy, SQLite native x64 DLL and managed dependencies are bundled.
- Backup/restore tests operated on actual SQLite files, including attachment bytes and a protective pre-restore copy. They were not mocked.
- Trial-balance and dashboard tests use the same postings as invoices, settlements, payroll and bank movement.
- Money is stored as integer paise and calculated with decimal arithmetic. SQLite has no dedicated date storage class; date fields use DateOnly day numbers, not DD-MM-YYYY display strings. Format conversion occurs at the UI/report boundary.

## Windows acceptance procedure — pending

Use a separate test company and record the outcome of each step.

1. Extract into a path containing spaces on Windows 10/11 x64 without a separately installed .NET runtime. Launch BusinessFinance.exe.
2. Create login, confirm wrong-password handling, change password, restart and verify persistence.
3. Enter company, logo, bank, customer/supplier, employee, site and opening balances.
4. Post a two-line intra-state invoice; test an interstate one; test duplicate/manual invoice numbers; receive a partial amount; reconcile bank, party ledger and current outstanding.
5. Post purchase and part payment. Verify input GST versus non-ITC purchase handling.
6. Advance 10,000 to a site employee; expense 6,000; refund 4,000. Bank must change only for the advance and refund.
7. Run October payroll with salary 30,000 and two absent days. Earned salary must be 28,064.52; basic 14,032.26 under 50% setting. Under the initial 15,000 basic-wage EPF ceiling and nearest-rupee rule, EPF is 1,684. ESIC is zero under a 21,000 salary eligibility threshold unless a valid continuation override applies.
8. Recover advance and employee loan separately; finalize; pay part salary; reverse payment and reopen payroll with a reason. Check all ledgers.
9. Create two FDs and link both to one BG; close BG; confirm neither FD closes and pending-lien state appears. Release and close/renew an FD with interest recorded separately.
10. Draw business loan and make principal/interest repayment; inspect EMI projection and update next recovery date.
11. Match/unmatch a bank entry. Lock a period and verify attempted posting/reversal is rejected. Inspect audit.
12. Add/open/remove attachments, preview company logo on documents, print and export PDF through Microsoft Print to PDF. Open XLSX in Excel and verify dates, rupees, negative balances and numeric totals.
13. Back up, enter another transaction, restore the backup, verify the protective copy and re-login. Test the copy on a separate Windows user profile.
14. Run Run_Automated_Checks.cmd and retain Windows-Test-Results.txt.

Production sign-off also requires completing the functional gaps in RELEASE_NOTES.md, accountant review, security review, representative-volume testing and recovery testing. Passing the automated suite alone is not production approval.
