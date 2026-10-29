<#
.SYNOPSIS
  Loads the reference data into the deployed project: 3 restaurant locations, their dishes,
  tables (seats) and one waiter per location.

  The files in .\seed are the marathon seed data, corrected for this project:
  - "Fats" -> "fats" (the backend reads the lower-case attribute);
  - "Grilled Salmon 2" -> "Grilled Salmon" (test leftover);
  - appetizers get dishType 0 (Green Salad, Fresh Avocado Toast), main courses 1;
  - one weight format: "350 g", "200 g", "220 g"; calories "180 kcal";
  - location images point to the frontend assets (the original URLs pointed to an S3 bucket
    of another AWS account and region), Green Salad uses assets/img/dishes/spring-salad.jpg.
  tables.json adds tables for 2/4/6(/8) guests: a reservation needs a table and a waiter.

  Locations, dishes and tables are written with PutRequest: only the items with these ids are
  overwritten, other data is not touched. Waiters are created through the API (Cognito user +
  POST /waiters); their passwords are saved to seed\waiter-accounts.local.txt (not in git).
  Re-running is safe. A few DynamoDB/Cognito calls - within the Free plan.

.EXAMPLE
  .\seed-data.ps1
#>
param([string]$Region = 'eu-north-1')

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$account = aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or -not $account) { throw "AWS CLI is not authenticated. Run 'aws configure' first." }
Write-Host "AWS account $account, region $Region" -ForegroundColor Cyan

# ------------------------------------------------------------------ 1. DynamoDB: locations, dishes, tables
foreach ($file in 'locations.json', 'dishes.json', 'tables.json') {
  $path = (Join-Path $PSScriptRoot "seed\$file") -replace '\\', '/'
  $result = aws dynamodb batch-write-item --region $Region --request-items "file://$path" --output json
  if ($LASTEXITCODE -ne 0) { throw "batch-write-item failed for $file" }
  $unprocessed = ($result | ConvertFrom-Json).UnprocessedItems.PSObject.Properties.Count
  if ($unprocessed -gt 0) { throw "${file}: some items were not written, run the script again." }
  Write-Host "  OK  $file" -ForegroundColor Green
}

# ------------------------------------------------------------------ 2. Waiters (one per location)
$apiId = aws apigateway get-rest-apis --region $Region --query "items[?name=='restaurant-backend-api'].id | [0]" --output text
if (-not $apiId -or $apiId -eq 'None') { throw 'API Gateway restaurant-backend-api not found - deploy the backend first.' }
$base = "https://$apiId.execute-api.$Region.amazonaws.com/develop"

function Invoke-Api([string]$Method, [string]$Path, $Body, [string]$Token = '') {
  $headers = @{}
  if ($Token) { $headers['Authorization'] = "Bearer $Token" }
  $params = @{ Uri = "$base$Path"; Method = $Method; Headers = $headers; UseBasicParsing = $true; ContentType = 'application/json; charset=utf-8' }
  if ($null -ne $Body) { $params['Body'] = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress)) }
  try {
    $r = Invoke-WebRequest @params
    return [pscustomobject]@{ Status = [int]$r.StatusCode; Json = ($r.Content | ConvertFrom-Json) }
  } catch {
    $resp = $_.Exception.Response
    if ($null -eq $resp) { throw }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Json = $null; Text = "$($_.ErrorDetails.Message)" }
  }
}

$accountsFile = Join-Path $PSScriptRoot 'seed\waiter-accounts.local.txt'
$saved = @{}
if (Test-Path $accountsFile) {
  foreach ($line in Get-Content $accountsFile) {
    $parts = $line -split '\s+'
    if ($parts.Count -ge 2) { $saved[$parts[0]] = $parts[1] }
  }
}

$waiters = @(
  # The backend accepts only top-level domains of 2-3 letters (user@domain.com), so example.com is used.
  @{ email = 'waiter.abashidze@example.com';    firstName = 'Nino';  lastName = 'Abashidze';    locationId = '7d598da3-0887-4a04-a3a1-fe26d8a4a4f2' },
  @{ email = 'waiter.baratashvili@example.com'; firstName = 'Giorgi'; lastName = 'Baratashvili'; locationId = '2de08053-9bec-4662-bf56-2ccb70aeeb78' },
  @{ email = 'waiter.rustaveli@example.com';    firstName = 'Tamar'; lastName = 'Rustaveli';    locationId = '98c4acc4-ca8b-4416-be9d-ef40f1e73e0a' }
)

foreach ($w in $waiters) {
  $password = $saved[$w.email]
  if (-not $password) {
    $password = 'Wt#' + (Get-Random -Minimum 100000 -Maximum 999999) + 'aA'
    $reg = Invoke-Api POST '/register' @{ firstName = $w.firstName; lastName = $w.lastName; email = $w.email; password = $password; confirmPassword = $password }
    if ($reg.Status -notin 200, 201) {
      Write-Host "  SKIP $($w.email): registration failed (HTTP $($reg.Status)) $($reg.Text)" -ForegroundColor Yellow
      continue
    }
    Add-Content -Path $accountsFile -Value "$($w.email) $password"
  }

  $signIn = Invoke-Api POST '/signin' @{ email = $w.email; password = $password }
  if ($signIn.Status -ne 200) { Write-Host "  SKIP $($w.email): sign in failed (HTTP $($signIn.Status))" -ForegroundColor Yellow; continue }

  $add = Invoke-Api POST '/waiters' @{ email = $w.email; locationId = $w.locationId } $signIn.Json.idToken
  if ($add.Status -in 200, 201) { Write-Host "  OK  waiter $($w.email)" -ForegroundColor Green }
  elseif ($add.Text -match 'already') { Write-Host "  OK  waiter $($w.email) (already in the list)" -ForegroundColor Green }
  else { Write-Host "  WARN waiter $($w.email): HTTP $($add.Status) $($add.Text)" -ForegroundColor Yellow }
}

# ------------------------------------------------------------------ 3. Summary
foreach ($table in 'Locations', 'Dishes', 'Tables') {
  $count = aws dynamodb scan --region $Region --table-name $table --select COUNT --query Count --output text
  Write-Host "  $table in the database: $count" -ForegroundColor Green
}
Write-Host "  Waiter accounts (email + password): $accountsFile" -ForegroundColor Yellow
