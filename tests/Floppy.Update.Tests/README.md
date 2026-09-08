# UpdateService regression tests

Run from the repository root:

```powershell
dotnet run --project tests/Floppy.Update.Tests/Floppy.Update.Tests.csproj -c Release --artifacts-path artifacts/update-tests
```

The executable links the production `UpdateService.cs` directly. It uses an injected `HttpClient` with a handler that never opens a socket, version 1.5.0, a unique temporary download directory and counting restart/installer callbacks. `UpdateInstallerStub` excludes the real process-launching installer from this assembly. Download bytes are explicitly synthetic and not executable. The tests never call `OpenReleases` or the public service constructor.

Fourteen groups cover stable versions, older versions, draft/prerelease and malformed JSON, repository and redirect boundaries, bounded metadata, checksum filename/digest binding, missing checksums, corrupt/short/oversized payloads, successful handoff, installer-preparation failures, single-flight checks/downloads, disposal and cancellation. Installer application/rollback and actual GitHub behavior belong to separate tests. Temporary-file cleanup validates its target prefix before recursive deletion.

The first run independently exposed two service defects now guarded by regression cases: `InvalidDataException` did not belong to the handled failure set, and a permitted GitHub hostname alone did not preserve the fixed repository across redirects.
