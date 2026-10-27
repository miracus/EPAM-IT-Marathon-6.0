<#
.SYNOPSIS
  Builds the backend solution and runs the unit tests. Writes everything to test-log-<time>.txt next to this script.
.EXAMPLE
  .\test-backend.ps1                          # all tests
  .\test-backend.ps1 -Filter GroupPreOrder    # only tests whose name contains "GroupPreOrder"
#>
param([string]$Filter = '')

$ErrorActionPreference = 'Continue'
$Sln = (Resolve-Path (Join-Path $PSScriptRoot '..\project\backend\restaurant-backend-app\restaurant-backend-app.sln')).Path
$Log = Join-Path $PSScriptRoot ("test-log-{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))

function Section($t) { "`n=== $t ===" | Tee-Object -FilePath $Log -Append | Write-Host -ForegroundColor Cyan }

Section "dotnet --version"
dotnet --version 2>&1 | Tee-Object -FilePath $Log -Append

Section "dotnet build"
dotnet build $Sln -c Debug -nologo -clp:"ErrorsOnly;Summary" 2>&1 | Tee-Object -FilePath $Log -Append
$build = $LASTEXITCODE

if ($build -ne 0) {
  Section "BUILD FAILED (exit $build)"
} else {
  Section "dotnet test"
  $testArgs = @('test', $Sln, '--no-build', '-nologo', '--logger', 'console;verbosity=normal')
  if ($Filter) { $testArgs += @('--filter', "FullyQualifiedName~$Filter") }
  & dotnet @testArgs 2>&1 | Tee-Object -FilePath $Log -Append
  $test = $LASTEXITCODE
  if ($test -eq 0) { Section "ALL TESTS PASSED" } else { Section "TESTS FAILED (exit $test)" }
}

Write-Host "`nLog: $Log" -ForegroundColor Yellow
