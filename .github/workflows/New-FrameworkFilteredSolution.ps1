#!/usr/bin/env pwsh
# Copyright (c) Microsoft. All rights reserved.

<#
.SYNOPSIS
    Generates a filtered .slnx solution file that only includes test projects supporting a given target framework.

.DESCRIPTION
    Parses a .slnx solution file and queries each test project's TargetFrameworks using MSBuild.
    Removes test projects that don't support the specified target framework, writes the result
    to a temporary or specified output path, and prints the output path.

    This is useful for running `dotnet test --solution` with MTP (Microsoft Testing Platform),
    which requires all test projects in the solution to support the requested target framework.

.PARAMETER Solution
    Path to the source .slnx solution file.

.PARAMETER TargetFramework
    The target framework to filter by (e.g., net10.0, net472).

.PARAMETER Configuration
    Optional MSBuild configuration used when querying TargetFrameworks. Defaults to Debug.

.PARAMETER OutputPath
    Optional output path for the filtered .slnx file. If not specified, a temp file is created.

.EXAMPLE
    # Generate a filtered solution and run tests
    $filtered = ./eng/New-FilteredSolution.ps1 -Solution ./agent-framework-dotnet.slnx -TargetFramework net472
    dotnet test --solution $filtered --no-build -f net472

.EXAMPLE
    # Inline usage with dotnet test (PowerShell)
    dotnet test --solution (./eng/New-FilteredSolution.ps1 -Solution ./agent-framework-dotnet.slnx -TargetFramework net472) --no-build -f net472
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Solution,

    [Parameter(Mandatory)]
    [string]$TargetFramework,

    [string]$Configuration = "Debug",

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

# Resolve the solution path
$solutionPath = Resolve-Path $Solution
$solutionDir = Split-Path $solutionPath -Parent

if (-not $OutputPath) {
    $OutputPath = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "filtered-$(Split-Path $solutionPath -Leaf)")
}

# Parse the .slnx XML
[xml]$slnx = Get-Content $solutionPath -Raw

# Find all Project elements with paths containing "tests/"
$testProjects = $slnx.SelectNodes("//Project[contains(@Path, 'tests/')]")

$removed = @()
$kept = @()

foreach ($proj in $testProjects) {
    $projRelPath = $proj.GetAttribute("Path")
    $projFullPath = Join-Path $solutionDir $projRelPath

    if (-not (Test-Path $projFullPath)) {
        Write-Verbose "Project not found, keeping in solution: $projRelPath"
        $kept += $projRelPath
        continue
    }

    # Query the project's target frameworks using MSBuild
    $targetFrameworks = & dotnet msbuild $projFullPath -getProperty:TargetFrameworks -p:Configuration=$Configuration -nologo 2>$null
    $targetFrameworks = $targetFrameworks.Trim()

    if ($targetFrameworks -like "*$TargetFramework*") {
        Write-Verbose "Keeping: $projRelPath (targets: $targetFrameworks)"
        $kept += $projRelPath
    }
    else {
        Write-Verbose "Removing: $projRelPath (targets: $targetFrameworks, missing: $TargetFramework)"
        $removed += $projRelPath
        $proj.ParentNode.RemoveChild($proj) | Out-Null
    }
}

# Write the filtered solution
$slnx.Save($OutputPath)

# Report results to stderr so stdout is clean for piping
Write-Host "Filtered solution written to: $OutputPath" -ForegroundColor Green
if ($removed.Count -gt 0) {
    Write-Host "Removed $($removed.Count) test project(s) not targeting ${TargetFramework}:" -ForegroundColor Yellow
    foreach ($r in $removed) {
        Write-Host "  - $r" -ForegroundColor Yellow
    }
}
Write-Host "Kept $($kept.Count) test project(s)." -ForegroundColor Green

# Output the path for piping
Write-Output $OutputPath
