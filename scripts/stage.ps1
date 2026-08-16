<#
.SYNOPSIS
    Refresh the committed deploy payload (DEV only).

.DESCRIPTION
    Builds a Release WrathAccess.dll (no dev server / no Mono.CSharp - Release strips all of
    src/Dev) and copies it, plus NAudio.dll, into deploy\Assemblies\. Commit that folder and
    push so alpha testers can `git pull` + `.\deploy.ps1` without a build toolchain.

        .\scripts\stage.ps1
        git add deploy/Assemblies; git commit -m "Stage build"; git push

    Does not touch your installed game - keep using `dotnet build` (Debug) for your own play
    (it auto-deploys with the dev server); this only produces the tester-facing payload.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Host "Building Release..."
dotnet build -c Release "$root\WrathAccess.csproj"
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

$asm = Join-Path $root 'deploy\Assemblies'
New-Item -ItemType Directory -Force -Path $asm | Out-Null

Copy-Item (Join-Path $root 'bin\Release\WrathAccess.dll') $asm -Force

$naudio = Get-ChildItem "$env:USERPROFILE\.nuget\packages\naudio\*\lib\net35\NAudio.dll" -ErrorAction SilentlyContinue |
    Sort-Object FullName | Select-Object -Last 1
if (-not $naudio) { throw "NAudio.dll not found in the NuGet cache. Run a normal build first to restore it." }
Copy-Item $naudio.FullName $asm -Force

# The reloadable module (its OUTPUT name is timestamped per build - see the module csproj; the
# deployed FILE name is stable). Take the newest build from the module's Release output.
$modOut = Get-ChildItem (Join-Path $root 'module\bin\Release\WrathAccess.Module_*.dll') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $modOut) { throw "Module dll not found under module\bin\Release. The Release build should have produced it." }
$modDir = Join-Path $root 'deploy\Module'
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Remove-Item "$modDir\*.dll" -Force -ErrorAction SilentlyContinue
Copy-Item $modOut.FullName (Join-Path $modDir 'WrathAccess.Module.dll') -Force

Write-Host ""
Write-Host "Staged deploy\Assemblies (WrathAccess.dll + NAudio.dll) + deploy\Module (WrathAccess.Module.dll)." -ForegroundColor Green
Write-Host "Commit deploy/Assemblies + deploy/Module and push so testers can pull and run deploy.ps1."
