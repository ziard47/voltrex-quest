[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$Offline
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "voltrex-quest.csproj"
$IssPath = Join-Path $PSScriptRoot "VoltrexQuest.iss"
$PublishDir = Join-Path $RepoRoot "bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"

function Find-InnoSetupCompiler {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

if (-not $SkipPublish) {
    Write-Host "Publishing Voltrex Quest ($Configuration | win-x64 | unpackaged self-contained)..." -ForegroundColor Cyan

    if ($Offline) {
        $localNuGet = Join-Path $env:USERPROFILE ".nuget\packages"
        & dotnet restore $ProjectPath --source $localNuGet -p:Platform=x64 -p:RuntimeIdentifier=win-x64
        if ($LASTEXITCODE -ne 0) {
            throw "Offline dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    $publishArgs = @(
        "publish", $ProjectPath,
        "-c", $Configuration,
        "-p:Platform=x64",
        "-p:RuntimeIdentifier=win-x64",
        "-p:SelfContained=true",
        "-p:WindowsPackageType=None",
        "-p:WindowsAppSDKSelfContained=true",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false",
        "-p:PublishSingleFile=false"
    )

    if ($Offline) {
        $publishArgs += "--no-restore"
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path (Join-Path $PublishDir "voltrex-quest.exe"))) {
    throw "Published executable not found at '$PublishDir'. Run without -SkipPublish first."
}

$iscc = Find-InnoSetupCompiler
Write-Host "Compiling Inno Setup script with $iscc..." -ForegroundColor Cyan

& $iscc "/DAppVersion=$Version" "/DSourceDir=$PublishDir" $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$installerExe = Join-Path $PSScriptRoot "Output\Voltrex-Quest-Setup-$Version-win-x64.exe"
if (Test-Path $installerExe) {
    $sizeMb = [Math]::Round((Get-Item $installerExe).Length / 1MB, 2)
    Write-Host "Installer built successfully: $installerExe ($sizeMb MB)" -ForegroundColor Green
}

