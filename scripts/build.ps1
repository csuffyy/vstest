# Copyright (c) Microsoft. All rights reserved.
# Build script for Test Platform.

[CmdletBinding()]
Param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [Alias("c")]
    [System.String] $Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

#
# Variables
#
Write-Verbose "Setup environment variables."
$env:TP_ROOT_DIR = (Get-Item (Split-Path $MyInvocation.MyCommand.Path)).Parent.FullName
$env:TP_TOOLS_DIR = Join-Path $env:TP_ROOT_DIR "tools"
$env:TP_PACKAGES_DIR = Join-Path $env:TP_ROOT_DIR "packages"
$env:TP_OUT_DIR = Join-Path $env:TP_ROOT_DIR "artifacts"

#
# Dotnet configuration
#
# Disable first run since we want to control all package sources 
Write-Verbose "Setup dotnet configuration."
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1 
# Dotnet build doesn't support --packages yet. See https://github.com/dotnet/cli/issues/2712
$env:NUGET_PACKAGES = $env:TP_PACKAGES_DIR

#
# Build configuration
#
# Folders to build. TODO move to props
Write-Verbose "Setup build configuration."
$SourceFolders = @("src", "test")
$TargetFramework = "net46"
$TargetRuntime = "win7-x64"

function Write-Log ([string] $message)
{
    $currentColor = $Host.UI.RawUI.ForegroundColor
    $Host.UI.RawUI.ForegroundColor = "Green"
    if ($message)
    {
        Write-Output "... $message"
    }
    $Host.UI.RawUI.ForegroundColor = $currentColor
}

function Write-VerboseLog([string] $message)
{
    Write-Verbose $message
}

function Remove-Tools
{
}

function Install-DotNetCli
{
    $timer = Start-Timer

    Write-Log "Install-DotNetCli: Get dotnet-install.ps1 script..."
    $dotnetInstallRemoteScript = "https://raw.githubusercontent.com/dotnet/cli/rel/1.0.0/scripts/obtain/dotnet-install.ps1"
    $dotnetInstallScript = Join-Path $env:TP_TOOLS_DIR "dotnet-install.ps1"
    if (-not (Test-Path $env:TP_TOOLS_DIR)) {
        New-Item $env:TP_TOOLS_DIR -Type Directory
    }

    (New-Object System.Net.WebClient).DownloadFile($dotnetInstallRemoteScript, $dotnetInstallScript)

    if (-not (Test-Path $dotnetInstallScript)) {
        Write-Error "Failed to download dotnet install script."
    }

    Unblock-File $dotnetInstallScript

    Write-Log "Install-DotNetCli: Get the latest dotnet cli toolset..."
    $dotnetInstallPath = Join-Path $env:TP_TOOLS_DIR "dotnet"
    & $dotnetInstallScript -InstallDir $dotnetInstallPath -NoPath

    Write-Log "Install-DotNetCli: Complete. {$(Get-ElapsedTime($timer))}"
}

function Restore-Package
{
    $timer = Start-Timer
    Write-Log "Restore-Package: Start restoring packages to $env:TP_PACKAGES_DIR."
    $dotnetExe = Get-DotNetPath

    foreach ($src in $SourceFolders) {
        Write-Log "Restore-Package: Restore for source directory: $src"
        & $dotnetExe restore $src --packages $env:TP_PACKAGES_DIR
    }

    Write-Log "Restore-Package: Complete. {$(Get-ElapsedTime($timer))}"
}

function Invoke-Build
{
    $timer = Start-Timer
    Write-Log "Invoke-Build: Start build."
    $dotnetExe = Get-DotNetPath

    foreach ($src in $SourceFolders) {
        # Invoke build for each project.json since we want a custom output
        # path.
        Write-Log ".. Build: Source directory: $src"
        #foreach ($fx in $TargetFramework) {
            #Get-ChildItem -Recurse -Path $src -Include "project.json" | ForEach-Object {
                #Write-Log ".. .. Build: Source: $_"
                #$binPath = Join-Path $env:TP_OUT_DIR "$fx\$src\$($_.Directory.Name)\bin"
                #$objPath = Join-Path $env:TP_OUT_DIR "$fx\$src\$($_.Directory.Name)\obj"
                #Write-Verbose "$dotnetExe build $_ --output $binPath --build-base-path $objPath --framework $fx"
                #& $dotnetExe build $_ --output $binPath --build-base-path $objPath --framework $fx
                #Write-Log ".. .. Build: Complete."
            #}
        #}
        Write-Verbose "$dotnetExe build $src\**\project.json --configuration $Configuration"
        & $dotnetExe build $_ $src\**\project.json --configuration $Configuration
    }

    Write-Log "Invoke-Build: Complete. {$(Get-ElapsedTime($timer))}"
}

function Publish-Package
{
    $timer = Start-Timer
    Write-Log "Publish-Package: Started."
    $dotnetExe = Get-DotNetPath
    $packageDir = Get-PackageDirectory

    Write-Log ".. Package: Publish package\project.json"
    Write-Verbose "$dotnetExe publish src\package\project.json --runtime win7-x64 --framework net46 --no-build --configuration $Configuration --out $packageDir"
    & $dotnetExe publish src\package\project.json --runtime win7-x64 --framework net46 --no-build --configuration $Configuration --output $packageDir

    Write-Log "Publish-Package: Complete. {$(Get-ElapsedTime($timer))}"
}

function Create-VsixPackage
{
    $timer = Start-Timer

    # Copy vsix manifests
    $packageDir = Get-PackageDirectory
    $vsixManifests = @("*Content_Types*.xml",
        "extension.vsixmanifest",
        "testhost.x86.exe.config",
        "testhost.exe.config")
    foreach ($file in $vsixManifests) {
        Copy-Item src\package\$file $packageDir -Force
    }

    # Copy legacy dependencies
    $legacyDir = Join-Path $env:TP_PACKAGES_DIR "Microsoft.Internal.TestPlatform.Extensions\15.0.0\contentFiles\any\any"
    Copy-Item -Recurse $legacyDir\* $packageDir -Force

    # Zip the folder
    # TODO remove vsix creator
    & src\Microsoft.TestPlatform.VSIXCreator\bin\$Configuration\net461\Microsoft.TestPlatform.VSIXCreator.exe $packageDir $env:TP_OUT_DIR\$Configuration

    Write-Log "Publish-Package: Complete. {$(Get-ElapsedTime($timer))}"
}

#
# Helper functions
#
function Get-DotNetPath
{
    $dotnetPath = Join-Path $env:TP_TOOLS_DIR "dotnet\dotnet.exe"
    if (-not (Test-Path $dotnetPath)) {
        Write-Error "Dotnet.exe not found at $dotnetPath. Did the dotnet cli installation succeed?"
    }

    return $dotnetPath
}

function Get-PackageDirectory
{
    return $(Join-Path $env:TP_OUT_DIR "$Configuration\$TargetFramework\$TargetRuntime")
}

function Start-Timer
{
    return [System.Diagnostics.Stopwatch]::StartNew()
}

function Get-ElapsedTime([System.Diagnostics.Stopwatch] $timer)
{
    $timer.Stop()
    return $timer.Elapsed
}

# Execute build
$timer = Start-Timer
Write-Log "Build started: args = '$args'"
Write-Log "Test platform environment variables: "
Get-ChildItem env: | Where-Object -FilterScript { $_.Name.StartsWith("TP_") } | Format-Table

Install-DotNetCli
Restore-Package
Invoke-Build
Publish-Package
Create-VsixPackage

Write-Log "Build complete. {$(Get-ElapsedTime($timer))}"