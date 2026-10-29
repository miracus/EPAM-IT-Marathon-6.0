<#
.SYNOPSIS
  Finds the test data left by e2e-preorder.ps1 / l3-test.ps1 and (only with -Delete) removes it.

  Test locations are the ones whose address starts with "E2E street" or "Frontend L3 street".
  For each of them the script collects: tables, dishes, waiter list entries, reservations of those
  tables, their orders, feedback, group reservations and guest sessions.
  It also picks up orphans: tables and waiters whose location no longer exists.

  Without -Delete nothing is changed: the script only prints what it found.
  With -Delete it asks you to type DELETE and then removes exactly those items.
  Deletion in DynamoDB cannot be undone. Cognito test users (@example.com) are not touched.

.EXAMPLE
  .\cleanup-test-data.ps1            # dry run: list only
  .\cleanup-test-data.ps1 -Delete    # remove after confirmation
#>
param(
  [switch]$Delete,
  [string]$Region = 'eu-north-1',
  [string[]]$Prefixes = @('E2E street', 'Frontend L3 street')
)

$ErrorActionPreference = 'Stop'
$tmp = Join-Path $env:TEMP 'green-tasty-cleanup'
New-Item -ItemType Directory -Force $tmp | Out-Null

function Write-JsonFile($object) {
  $file = Join-Path $tmp ([Guid]::NewGuid().ToString() + '.json')
  [IO.File]::WriteAllText($file, ($object | ConvertTo-Json -Depth 10 -Compress), (New-Object Text.UTF8Encoding $false))
  return 'file://' + ($file -replace '\\', '/')
}

# Windows PowerShell treats anything aws.exe writes to stderr as an error, so stderr is captured
# separately. The tables have 1 read/write capacity unit: throttled calls are retried with a pause.
function Invoke-Aws([string[]]$Arguments) {
  $errFile = Join-Path $tmp 'aws-stderr.txt'
  for ($attempt = 1; $attempt -le 6; $attempt++) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out = & aws @Arguments --region $Region --output json 2> $errFile
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous
    if ($code -eq 0) {
      $text = ($out | Out-String).Trim()
      if (-not $text) { return $null }   # e.g. delete-item prints nothing
      return ($text | ConvertFrom-Json)
    }

    $message = (Get-Content $errFile -Raw -ErrorAction SilentlyContinue)
    if ($message -match 'Throttl|ProvisionedThroughputExceeded|RequestLimitExceeded' -and $attempt -lt 6) {
      Write-Host "    throttled, waiting $(2 * $attempt) s..." -ForegroundColor DarkGray
      Start-Sleep -Seconds (2 * $attempt)
      continue
    }
    throw "aws $($Arguments[0..1] -join ' ') failed: $message"
  }
}

# Query a GSI (or the table key) by one string attribute.
function Get-ByKey([string]$Table, [string]$Index, [string]$Attribute, [string]$Value) {
  $values = Write-JsonFile @{ ':v' = @{ S = $Value } }
  $names = Write-JsonFile @{ '#k' = $Attribute }
  $arguments = @('dynamodb', 'query', '--table-name', $Table, '--key-condition-expression', '#k = :v',
    '--expression-attribute-values', $values, '--expression-attribute-names', $names)
  if ($Index) { $arguments += @('--index-name', $Index) }
  return @((Invoke-Aws $arguments).Items)
}

function Get-ByScan([string]$Table, [string]$Attribute, [string]$Value) {
  $values = Write-JsonFile @{ ':v' = @{ S = $Value } }
  $names = Write-JsonFile @{ '#k' = $Attribute }
  return @((Invoke-Aws @('dynamodb', 'scan', '--table-name', $Table, '--filter-expression', '#k = :v',
    '--expression-attribute-values', $values, '--expression-attribute-names', $names)).Items)
}

$toDelete = New-Object System.Collections.ArrayList
function Add-Key([string]$Table, $Key) { [void]$toDelete.Add([pscustomobject]@{ Table = $Table; Key = $Key }) }

Write-Host "Looking for locations starting with: $($Prefixes -join ', ')" -ForegroundColor Cyan
$locations = @((Invoke-Aws @('dynamodb', 'scan', '--table-name', 'Locations')).Items) |
  Where-Object { $address = $_.address.S; $Prefixes | Where-Object { $address -like "$_*" } }

# Dishes created through POST /dishes are linked via location.specialtyDishIds (no locationId attribute).
# A dish is removed only if no real (non-test) location uses it.
$allLocations = @((Invoke-Aws @('dynamodb', 'scan', '--table-name', 'Locations')).Items)
$testIds = @($locations | ForEach-Object { $_.id.S })
# The .NET SDK may store the list as a string set (SS) or as a list (L) - read both.
function Get-SpecialtyIds($location) {
  @($location.specialtyDishIds.SS) + @(@($location.specialtyDishIds.L) | ForEach-Object { $_.S }) | Where-Object { $_ }
}
$keepDishIds = @($allLocations | Where-Object { $testIds -notcontains $_.id.S } | ForEach-Object { Get-SpecialtyIds $_ })
$dishIds = New-Object System.Collections.Generic.HashSet[string]
$allDishes = @((Invoke-Aws @('dynamodb', 'scan', '--table-name', 'Dishes')).Items)

# Everything below a table: its reservations, their orders, feedback, group reservations and guest sessions.
function Add-TableTree($table) {
  Add-Key 'Tables' @{ id = $table.id }
  foreach ($reservation in Get-ByKey 'Reservations' 'GSI_ByTableId' 'tableId' $table.id.S) {
    $reservationId = $reservation.id.S
    Add-Key 'Reservations' @{ id = $reservation.id }
    foreach ($order in Get-ByKey 'Orders' 'GSI_ReservationId' 'reservationId' $reservationId) {
      Add-Key 'Orders' @{ orderId = $order.orderId; customerId = $order.customerId }
    }
    foreach ($feedback in Get-ByKey 'Feedback' '' 'reservationId' $reservationId) {
      Add-Key 'Feedback' @{ reservationId = $feedback.reservationId; createdAt = $feedback.createdAt }
    }
    foreach ($group in Get-ByKey 'GroupReservations' 'reservationId' 'reservationId' $reservationId) {
      Add-Key 'GroupReservations' @{ groupReservationId = $group.groupReservationId }
      foreach ($session in Get-ByKey 'GuestSessions' 'groupReservationId' 'groupReservationId' $group.groupReservationId.S) {
        Add-Key 'GuestSessions' @{ guestSessionId = $session.guestSessionId }
      }
    }
  }
}

foreach ($location in $locations) {
  $locationId = $location.id.S
  Write-Host "  $($location.address.S)" -ForegroundColor Gray
  Add-Key 'Locations' @{ id = @{ S = $locationId } }

  foreach ($dish in $allDishes | Where-Object { $_.locationId.S -eq $locationId }) { [void]$dishIds.Add($dish.id.S) }
  foreach ($specialtyId in Get-SpecialtyIds $location) { [void]$dishIds.Add($specialtyId) }
  foreach ($waiter in Get-ByKey 'Waiters' 'locationId-index' 'locationId' $locationId) { Add-Key 'Waiters' @{ userId = $waiter.userId } }

  foreach ($table in Get-ByKey 'Tables' 'locationId-tableNumber-index' 'locationId' $locationId) { Add-TableTree $table }
}

# Orphans: tables and waiters whose location no longer exists (e.g. left after an interrupted cleanup).
$liveIds = @($allLocations | Where-Object { $testIds -notcontains $_.id.S } | ForEach-Object { $_.id.S })
foreach ($table in @((Invoke-Aws @('dynamodb', 'scan', '--table-name', 'Tables')).Items)) {
  if ($table.locationId.S -and $liveIds -notcontains $table.locationId.S -and $testIds -notcontains $table.locationId.S) {
    Write-Host "  orphan table $($table.id.S) (location $($table.locationId.S))" -ForegroundColor Gray
    Add-TableTree $table
  }
}
foreach ($waiter in @((Invoke-Aws @('dynamodb', 'scan', '--table-name', 'Waiters')).Items)) {
  if ($waiter.locationId.S -and $liveIds -notcontains $waiter.locationId.S -and $testIds -notcontains $waiter.locationId.S) {
    Add-Key 'Waiters' @{ userId = $waiter.userId }
  }
}

# Dishes created by e2e-preorder.ps1 / l3-test.ps1 also have "(e2e)" / "(L3)" at the end of the description.
foreach ($dish in $allDishes) {
  if ($dish.description.S -match '\((e2e|L3)\)$') { [void]$dishIds.Add($dish.id.S) }
}

foreach ($dishId in $dishIds) {
  if ($keepDishIds -notcontains $dishId) { Add-Key 'Dishes' @{ id = @{ S = $dishId } } }
}

if ($toDelete.Count -eq 0) { Write-Host '  Nothing to clean up.' -ForegroundColor Green; return }

Write-Host "`nFound:" -ForegroundColor Cyan
if (-not ($toDelete | Where-Object Table -eq 'Dishes')) { Write-Host '  (no test dishes found)' -ForegroundColor DarkGray }
$toDelete | Group-Object Table | Sort-Object Name | ForEach-Object { Write-Host ("  {0,-18} {1}" -f $_.Name, $_.Count) }

if (-not $Delete) {
  Write-Host "`nDry run: nothing was deleted. Run with -Delete to remove these items." -ForegroundColor Yellow
  return
}

$answer = Read-Host "`nDeletion cannot be undone. Type DELETE (capital letters) to remove $($toDelete.Count) items"
if ($answer -cne 'DELETE') { Write-Host 'Cancelled.' -ForegroundColor Yellow; return }

# One delete-item call per item: slower than batch-write-item, but simple and safe with 1 write capacity unit.
# Children first, locations last: if the run stops halfway, the next run still finds the rest.
$order = @('GuestSessions', 'GroupReservations', 'Feedback', 'Orders', 'Reservations', 'Tables', 'Waiters', 'Dishes', 'Locations')
foreach ($group in $toDelete | Group-Object Table | Sort-Object { $order.IndexOf($_.Name) }) {
  $done = 0
  foreach ($item in $group.Group) {
    $null = Invoke-Aws @('dynamodb', 'delete-item', '--table-name', $group.Name, '--key', (Write-JsonFile $item.Key))
    $done++
  }
  Write-Host "  deleted $($group.Name): $done" -ForegroundColor Green
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Done.' -ForegroundColor Green
