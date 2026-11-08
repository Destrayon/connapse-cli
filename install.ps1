<#
.SYNOPSIS
Installs the connapse CLI on Windows from a GitHub release. No .NET needed.

.DESCRIPTION
    irm https://raw.githubusercontent.com/Destrayon/connapse-cli/main/install.ps1 | iex

Pin a version (prereleases too) by setting CONNAPSE_VERSION first, or by running the script with -Version:

    $env:CONNAPSE_VERSION = 'v0.5.1-alpha.1'; irm .../install.ps1 | iex
    & ([scriptblock]::Create((irm .../install.ps1))) -Version v0.5.1-alpha.1

The binary goes to %LOCALAPPDATA%\Programs\connapse, which is added to the user PATH.
CONNAPSE_INSTALL_DIR (or -InstallDir) picks another directory; CONNAPSE_NO_MODIFY_PATH=1 (or -NoModifyPath)
leaves PATH alone.
#>

param (
    [string]$Version = $env:CONNAPSE_VERSION,
    [string]$InstallDir = $(if ($env:CONNAPSE_INSTALL_DIR) { $env:CONNAPSE_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\connapse' }),
    [switch]$NoModifyPath = [bool]$env:CONNAPSE_NO_MODIFY_PATH
)

$ErrorActionPreference = 'Stop'
# Invoke-WebRequest's progress bar slows large downloads in Windows PowerShell 5.1 by an order of magnitude.
$ProgressPreference = 'SilentlyContinue'

function Install-Connapse {
    $repo = 'Destrayon/connapse-cli'
    $asset = 'connapse-cli-win-x64.exe'

    # Windows PowerShell 5.1 may default to TLS 1.0, which GitHub refuses.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $arch = $env:PROCESSOR_ARCHITEW6432
    if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
    if ($arch -eq 'ARM64') {
        Write-Host 'connapse-install: no ARM64 build yet; installing the x64 build, which runs under Windows emulation'
    } elseif ($arch -ne 'AMD64') {
        throw "connapse-install: unsupported architecture $arch; only x64 is published"
    }

    if (-not $Version -or $Version -eq 'latest') {
        $base = "https://github.com/$repo/releases/latest/download"
        $label = 'latest release'
    } else {
        if (-not $Version.StartsWith('v')) { $Version = "v$Version" }
        $base = "https://github.com/$repo/releases/download/$Version"
        $label = $Version
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("connapse-" + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        $download = Join-Path $tmp 'connapse.exe'
        Write-Host "connapse-install: downloading $asset ($label)"
        try {
            Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset" -OutFile $download
        } catch {
            throw "connapse-install: download failed: $base/$asset (does that release exist?) $($_.Exception.Message)"
        }

        # Releases from v0.5.1 on publish checksums.txt; older ones have none, so skip the check there.
        $sums = Join-Path $tmp 'checksums.txt'
        $haveSums = $true
        try { Invoke-WebRequest -UseBasicParsing -Uri "$base/checksums.txt" -OutFile $sums } catch { $haveSums = $false }
        if ($haveSums) {
            $line = Get-Content $sums | Where-Object { ($_ -split '\s+')[1] -in @($asset, "*$asset") } | Select-Object -First 1
            if (-not $line) { throw "connapse-install: checksums.txt has no entry for $asset" }
            $expected = ($line -split '\s+')[0].ToLowerInvariant()
            $actual = (Get-FileHash -Algorithm SHA256 $download).Hash.ToLowerInvariant()
            if ($expected -ne $actual) { throw "connapse-install: checksum mismatch for ${asset}: expected $expected, got $actual" }
            Write-Host 'connapse-install: checksum verified'
        } else {
            Write-Host 'connapse-install: this release has no checksums.txt; skipping checksum check'
        }

        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        $target = Join-Path $InstallDir 'connapse.exe'
        try {
            Move-Item -Force -Path $download -Destination $target
        } catch {
            throw "connapse-install: could not write $target; close any running connapse and retry. $($_.Exception.Message)"
        }
    } finally {
        Remove-Item -Recurse -Force -Path $tmp -ErrorAction SilentlyContinue
    }

    Write-Host "connapse-install: installed to $target"
    & $target --version
    if ($LASTEXITCODE -ne 0) { throw "connapse-install: '$target --version' exited with $LASTEXITCODE" }

    if ($NoModifyPath) { return }

    # Edit the registry value directly so entries like %USERPROFILE%\bin stay unexpanded (REG_EXPAND_SZ);
    # [Environment]::SetEnvironmentVariable would flatten them.
    $key = 'registry::HKEY_CURRENT_USER\Environment'
    $current = (Get-Item -LiteralPath $key).GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    $entries = $current -split ';' | Where-Object { $_ }
    if ($entries -notcontains $InstallDir) {
        $newPath = (@($InstallDir) + $entries) -join ';'
        Set-ItemProperty -Type ExpandString -LiteralPath $key -Name Path -Value $newPath
        # Setting any user variable through .NET broadcasts WM_SETTINGCHANGE, so new terminals see the PATH change.
        $dummy = 'connapse-install-' + [Guid]::NewGuid()
        [Environment]::SetEnvironmentVariable($dummy, '1', 'User')
        [Environment]::SetEnvironmentVariable($dummy, [NullString]::Value, 'User')
        Write-Host "connapse-install: added $InstallDir to your user PATH; open a new terminal to use 'connapse'"
    }
    if (($env:Path -split ';') -notcontains $InstallDir) { $env:Path = "$InstallDir;$env:Path" }
}

Install-Connapse
