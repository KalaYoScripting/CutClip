# CutClip installer

Windows installer built with [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Output

- `artifacts/installer/CutClip-Setup-<version>.exe`

## Quick build

From the repo root:

```powershell
dotnet publish src/CutClip/CutClip.csproj -c Release -o ./artifacts/build
.\installer\build-installer.ps1 -Version 1.0.0
```

## GitHub release

Push a version tag; CI publishes the portable exe and installer to a GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```
