<#
.SYNOPSIS
  Loads the reference data (3 restaurant locations and their dishes) into DynamoDB.

  The files in .\seed are the marathon seed data, corrected for this project:
  - "Fats" -> "fats" (the backend reads the lower-case attribute);
  - "Grilled Salmon 2" -> "Grilled Salmon" (test leftover);
  - appetizers get dishType 0 (Green Salad, Fresh Avocado Toast), main courses 1;
  - one weight format: "350 g", "200 g", "220 g"; calories "180 kcal";
  - location images point to the frontend assets (the original URLs pointed to an S3 bucket
    of another AWS account and region), Green Salad uses assets/img/dishes/spring-salad.jpg.

  PutRequest overwrites only the items with these ids; other data (E2E test data, users,
  reservations) is not touched. Re-running is safe. A few DynamoDB writes - within the Free plan.

.EXAMPLE
  .\seed-data.ps1
#>
param([string]$Region = 'eu-north-1')

$ErrorActionPreference = 'Stop'

$account = aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0 -or -not $account) { throw "AWS CLI is not authenticated. Run 'aws configure' first." }
Write-Host "AWS account $account, region $Region" -ForegroundColor Cyan

foreach ($file in 'locations.json', 'dishes.json') {
  $path = (Join-Path $PSScriptRoot "seed\$file") -replace '\\', '/'
  $result = aws dynamodb batch-write-item --region $Region --request-items "file://$path" --output json
  if ($LASTEXITCODE -ne 0) { throw "batch-write-item failed for $file" }
  $unprocessed = ($result | ConvertFrom-Json).UnprocessedItems.PSObject.Properties.Count
  if ($unprocessed -gt 0) { throw "${file}: some items were not written, run the script again." }
  Write-Host "  OK  $file" -ForegroundColor Green
}

$dishes = aws dynamodb scan --region $Region --table-name Dishes --select COUNT --query Count --output text
$locations = aws dynamodb scan --region $Region --table-name Locations --select COUNT --query Count --output text
Write-Host "  Dishes in the table: $dishes, locations: $locations" -ForegroundColor Green
Write-Host "  Note: tables (seats) for these locations are not in the seed - add them in the admin UI or via POST /tables." -ForegroundColor Yellow
