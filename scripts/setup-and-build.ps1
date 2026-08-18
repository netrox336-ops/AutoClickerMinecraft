[CmdletBinding()]
param(
    [switch]$InstallDependencies,
    [switch]$BuildInstaller,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDirectory
$projectFile = Join-Path $projectRoot 'src\WinClicker\WinClicker.csproj'
$distDirectory = Join-Path $projectRoot 'dist'
$buildDirectory = Join-Path $projectRoot 'build\publish'
$portableDirectory = Join-Path $distDirectory 'AutoClicker-v3.0.1-Win11-x64'
$portableExecutable = Join-Path $portableDirectory 'AutoClicker.exe'
$portableArchive = Join-Path $distDirectory 'AutoClicker-v3.0.1-Windows-x64-portable.zip'
$installerStage = Join-Path $distDirectory 'installer-staging'
$installerExecutable = Join-Path $installerStage 'AutoClicker.exe'
$installerScript = Join-Path $projectRoot 'installer\AutoClicker.iss'

function Find-DotNet {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $common = @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    return $common | Select-Object -First 1
}

function Install-DotNetSdk {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw '.NET 8 SDK is missing and Windows Package Manager is unavailable. Install the .NET 8 SDK and run build-only.bat.'
    }

    Write-Host '[1/7] Installing .NET 8 SDK...'
    & $winget.Source install --id Microsoft.DotNet.SDK.8 --exact --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install .NET 8 SDK (exit $LASTEXITCODE)."
    }

    $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"
}

function Find-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    return $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

function Install-InnoSetup {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        return $null
    }

    Write-Host '[6/7] Installing Inno Setup...'
    & $winget.Source install --id JRSoftware.InnoSetup --exact --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return Find-InnoCompiler
}

if (-not (Test-Path $projectFile)) {
    throw "Project file was not found: $projectFile"
}

$dotnet = Find-DotNet
if (-not $dotnet -and $InstallDependencies) {
    Install-DotNetSdk
    $dotnet = Find-DotNet
}
if (-not $dotnet) {
    throw '.NET 8 SDK was not found. Run install-and-build.bat or install the SDK manually.'
}

Write-Host '[1/7] .NET SDK ready.'
& $dotnet --version

Write-Host '[2/7] Cleaning release directories...'
foreach ($directory in @($buildDirectory, $portableDirectory, $installerStage)) {
    if (Test-Path $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}
if (Test-Path $portableArchive) {
    Remove-Item -LiteralPath $portableArchive -Force
}
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerStage -Force | Out-Null

Write-Host '[3/7] Restoring and publishing Auto Clicker 3.0.1...'
& $dotnet restore $projectFile
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (exit $LASTEXITCODE)." }

& $dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $buildDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$publishedExecutable = Join-Path $buildDirectory 'AutoClicker.exe'
if (-not (Test-Path $publishedExecutable)) {
    throw 'Published AutoClicker.exe was not created.'
}

Write-Host '[4/7] Running built-in self-test...'
$test = Start-Process -FilePath $publishedExecutable -ArgumentList '--self-test' -Wait -PassThru
if ($test.ExitCode -ne 0) {
    throw "Self-test failed (exit $($test.ExitCode))."
}

Write-Host '[5/7] Creating portable package...'
Copy-Item -LiteralPath $publishedExecutable -Destination $portableExecutable -Force
New-Item -ItemType File -Path (Join-Path $portableDirectory 'portable.flag') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $portableDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $portableDirectory 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $portableDirectory 'THIRD_PARTY_NOTICES.md') -Force
New-Item -ItemType Directory -Path (Join-Path $portableDirectory 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses\Apache-2.0.txt') -Destination (Join-Path $portableDirectory 'licenses\Apache-2.0.txt') -Force
Copy-Item -LiteralPath $publishedExecutable -Destination $installerExecutable -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $installerStage 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $installerStage 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $installerStage 'THIRD_PARTY_NOTICES.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses\Apache-2.0.txt') -Destination (Join-Path $installerStage 'Apache-2.0.txt') -Force
Compress-Archive -Path (Join-Path $portableDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal -Force

$installerBuilt = $false
if ($BuildInstaller) {
    $iscc = Find-InnoCompiler
    if (-not $iscc -and $InstallDependencies) {
        $iscc = Install-InnoSetup
    }

    if ($iscc) {
        Write-Host '[6/7] Building Windows installer...'
        & $iscc "/DSourceExe=$installerExecutable" $installerScript
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed (exit $LASTEXITCODE)."
        }
        $installerBuilt = $true
    }
    else {
        Write-Warning 'Inno Setup was not found. Portable package was built; install Inno Setup 6 to create Setup.exe.'
    }
}

Write-Host '[7/7] Release completed.'
Write-Host "Portable folder: $portableDirectory"
Write-Host "Portable ZIP:    $portableArchive"
if ($installerBuilt) {
    Write-Host "Installer:       $(Join-Path $distDirectory 'AutoClicker-v3.0.1-Setup.exe')"
}

if ($Run) {
    Start-Process -FilePath $portableExecutable
}
