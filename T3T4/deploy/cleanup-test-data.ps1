<#
.SYNOPSIS
  Finds the test data left by e2e-preorder.ps1 / l3-test.ps1 and (only with -Delete) removes it.

  Test locations are the ones whose address starts with "E2E street" or "Frontend L3 street".
  For each of them the script collects: tables, dishes, waiter list entries, reservations of those
  tables, their orders, feedback, group reservations and guest sessions.

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

function Invoke-Aws([string[]]$Arguments) {
  $out = & aws @Arguments --region $Region --output json
  if ($LASTEXITCODE -ne 0) { throw "aws $($Arguments -join ' ') failed" }
  return ($out | Out-String | ConvertFrom-Json)
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

if ($locations.Count -eq 0) { Write-Host '  Nothing to clean up.' -ForegroundColor Green; return }

# Dishes created through POST /dishes are linked via location.specialtyDishIds (no locationId attribute).
# A dish is removed only if no real (non-test) location uses it.
$allLocations = @((Invoke-Aws @('dynamodb', 'scan', '--table-name', 'Locations')).Items)
$testIds = @($locations | ForEach-Object { $_.id.S })
$keepDishIds = @($allLocations | Where-Object { $testIds -notcontains $_.id.S } |
  ForEach-Object { @($_.specialtyDishIds.L) | ForEach-Object { $_.S } })
$dishIds = New-Object System.Collections.Generic.HashSet[string]

foreach ($location in $locations) {
  $locationId = $location.id.S
  Write-Host "  $($location.address.S)" -ForegroundColor Gray
  Add-Key 'Locations' @{ id = @{ S = $locationId } }

  foreach ($dish in Get-ByScan 'Dishes' 'locationId' $locationId) { [void]$dishIds.Add($dish.id.S) }
  foreach ($specialty in @($location.specialtyDishIds.L)) { if ($specialty.S) { [void]$dishIds.Add($specialty.S) } }
  foreach ($waiter in Get-ByKey 'Waiters' 'locationId-index' 'locationId' $locationId) { Add-Key 'Waiters' @{ userId = $waiter.userId } }

  foreach ($table in Get-ByKey 'Tables' 'locationId-tableNumber-index' 'locationId' $locationId) {
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
}

foreach ($dishId in $dishIds) {
  if ($keepDishIds -notcontains $dishId) { Add-Key 'Dishes' @{ id = @{ S = $dishId } } }
}

Write-Host "`nFound:" -ForegroundColor Cyan
$toDelete | Group-Object Table | Sort-Object Name | ForEach-Object { Write-Host ("  {0,-18} {1}" -f $_.Name, $_.Count) }

if (-not $Delete) {
  Write-Host "`nDry run: nothing was deleted. Run with -Delete to remove these items." -ForegroundColor Yellow
  return
}

$answer = Read-Host "`nDeletion cannot be undone. Type DELETE to remove $($toDelete.Count) items"
if ($answer -ne 'DELETE') { Write-Host 'Cancelled.' -ForegroundColor Yellow; return }

foreach ($group in $toDelete | Group-Object Table) {
  $items = @($group.Group)
  for ($i = 0; $i -lt $items.Count; $i += 25) {
    $chunk = $items[$i..([Math]::Min($i + 24, $items.Count - 1))]
    $request = @{ ($group.Name) = @($chunk | ForEach-Object { @{ DeleteRequest = @{ Key = $_.Key } } }) }
    $result = Invoke-Aws @('dynamodb', 'batch-write-item', '--request-items', (Write-JsonFile $request))
    if ($result.UnprocessedItems.PSObject.Properties.Count -gt 0) { throw "Some $($group.Name) items were not deleted - run the script again." }
  }
  Write-Host "  deleted $($group.Name): $($items.Count)" -ForegroundColor Green
}

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Done.' -ForegroundColor Green
