# NetUnitOfWorkManager prerelease package smoke application (.NET Framework 4.7.2)

This console application exists only for P10 prerelease closure verification.

Unlike `NetUnitOfWorkManager.Sample.Net472`, it does **not** use a `ProjectReference`. It consumes the produced prerelease nupkg through `PackageReference`, so the P10 gate verifies the package artifact that would be distributed.

The application requires `NETUOW_SQLSERVER_CONNECTION_STRING` and runs against SQL Server using the .NET Framework `System.Data.SqlClient` provider.

Do not add this project to the solution: normal solution restore must not depend on an unpublished prerelease package. `scripts/verify-prerelease-package.ps1` supplies the local package feed and version explicitly.

Run the full P10 flow instead of invoking this project directly:

```powershell
$env:NETUOW_SQLSERVER_CONNECTION_STRING = 'Server=(localdb)\MSSQLLocalDB;Initial Catalog=tempdb;Integrated Security=true;TrustServerCertificate=true'
pwsh -File .\scripts\verify-prerelease.ps1 -Version 1.0.0-preview.1
```
