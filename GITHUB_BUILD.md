# GitHub Windows build handoff

This source package prepares a Windows build and verification workflow; it is not a completed production application.

Repository root must contain Finance.Core, Finance.Desktop, Finance.Tests and .github/workflows/windows-portable.yml. Do not upload the old compiled binaries, financial databases, personal attachments or credentials.

Workflow: **Build and verify portable Windows EXE**.

It runs the financial integration suite on a Windows runner, publishes a single self-contained BusinessFinance.exe (including native dependencies), then launches that EXE from a folder containing no companion application DLLs. It generates a launch/navigation smoke report and uploads the EXE, test evidence and hash only after the checks succeed. There is no automatic public release publication.

The single-file runtime may extract bundled native libraries to its cache automatically. This is not an installer, and the user does not install a SDK or runtime. Normal application data remains in the user's LocalAppData folder.

The new GitHub workflow and smoke-test source have not been run yet. GitHub connection/repository access is required before remote execution can be verified. The previous 62-test result describes the previously compiled core, not an execution of this new workflow.

Full business functionality remains a separate completion gate: see RELEASE_NOTES.md. Successful compilation, unit/integration checks and smoke checks alone do not establish full production acceptance.

Documentation used:
- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://docs.github.com/en/actions/tutorials/build-and-test-code/net
