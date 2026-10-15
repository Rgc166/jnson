# Development instructions

Use the existing checkout. Cloud tasks are already isolated; do not create worktrees unless explicitly requested.

Read README.md and docs/architecture.md. This is a Windows application. Linux can cross-compile WPF and run Core tests, but cannot validate Windows services, NTFS ACLs, WPF rendering or the installed Veyon CLI. Never report those as tested without execution on Windows.

Use the SDK pinned in global.json. Restore with `dotnet restore VeyonSync.slnx --locked-mode -p:EnableWindowsTargeting=true`, build with `dotnet build VeyonSync.slnx -c Release --no-restore -warnaserror`, and run `dotnet test tests/VeyonSync.Tests/VeyonSync.Tests.csproj -c Release --no-build`. `pwsh scripts/build.ps1` generates Windows packages. Update lockfiles deliberately when dependencies change.

Keep TLS name/chain/fingerprint checks, local pipe ACLs and atomic data persistence. Do not commit tokens, certificates, databases or logs. Never use `networkobjects clear`, assume CSV import updates UUIDs, silently move agents between labs, or discard pending data to make setup pass.

The Windows smoke script installs services and must run only on a disposable machine without existing VeyonSync data. The repository has no chosen distribution license; do not invent one or publish releases without the owner's instruction.
