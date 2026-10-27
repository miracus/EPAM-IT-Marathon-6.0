<#
.SYNOPSIS
  End-to-end check of the owner group pre-order API (homework L1-L3, US15/US16) on the deployed backend.

  Creates its own test data through the API (owner/waiter/stranger users on @example.com, a location,
  a table, dishes, a 5-guest reservation, 3 guests) and then calls every new endpoint, comparing
  status codes and totals with what is expected. Each run uses fresh data, so it can be re-run.
  Nothing here costs money: it is a few dozen Lambda/DynamoDB/Cognito calls inside the Free plan.

.EXAMPLE
  .\e2e-preorder.ps1
  .\e2e-preorder.ps1 -ApiUrl https://abc123.execute-api.eu-north-1.amazonaws.com/develop
#>
param(
  [string]$ApiUrl = '',
  [string]$Region = 'eu-north-1'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12   # Windows PowerShell 5.1
$Log = Join-Path $PSScriptRoot ("e2e-log-{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
Start-Transcript -Path $Log | Out-Null

$script:Passed = 0
$script:Failed = 0

function Step($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }

function Check([string]$Name, [bool]$Condition, [string]$Detail = '') {
  if ($Condition) {
    $script:Passed++
    Write-Host "  PASS  $Name" -ForegroundColor Green
  } else {
    $script:Failed++
    Write-Host "  FAIL  $Name  $Detail" -ForegroundColor Red
  }
}

function Invoke-Api {
  param([string]$Method, [string]$Path, $Body = $null, [string]$Token = '')
  $headers = @{}
  if ($Token) { $headers['Authorization'] = "Bearer $Token" }
  $params = @{
    Uri             = "$script:Base$Path"
    Method          = $Method
    Headers         = $headers
    UseBasicParsing = $true
    ContentType     = 'application/json; charset=utf-8'
  }
  if ($null -ne $Body) {
    $params['Body'] = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
  }

  $status = 0; $text = ''
  try {
    $r = Invoke-WebRequest @params
    $status = [int]$r.StatusCode
    $text = $r.Content
  } catch {
    $resp = $_.Exception.Response
    if ($null -eq $resp) { throw }
    $status = [int]$resp.StatusCode
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
      $text = $_.ErrorDetails.Message
    } else {
      try { $text = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { $text = '' }
    }
  }

  $json = $null
  if ($text) { try { $json = $text | ConvertFrom-Json } catch { } }
  return [pscustomobject]@{ Status = $status; Json = $json; Text = $text }
}

# Fails the whole run: used for setup steps the checks depend on.
function Require($Resp, [int[]]$Codes, [string]$What) {
  if ($Codes -notcontains $Resp.Status) {
    throw "$What failed: HTTP $($Resp.Status) $($Resp.Text)"
  }
  return $Resp.Json
}

function Expect($Resp, [int]$Code, [string]$Name) {
  Check "$Name -> $Code" ($Resp.Status -eq $Code) "(got $($Resp.Status): $($Resp.Text))"
}

function Sum-Of($Items) { $s = 0; foreach ($i in @($Items)) { $s += [decimal]$i }; return $s }

try {
  # ------------------------------------------------------------------ 0. API URL
  Step '0. API URL'
  if (-not $ApiUrl) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $apiId = aws apigateway get-rest-apis --region $Region --query "items[?name=='restaurant-backend-api'].id | [0]" --output text 2>$null
    $ErrorActionPreference = $old
    if (-not $apiId -or $apiId -eq 'None') { throw 'API Gateway restaurant-backend-api not found. Pass -ApiUrl or deploy the backend.' }
    $ApiUrl = "https://$apiId.execute-api.$Region.amazonaws.com/develop"
  }
  $script:Base = $ApiUrl.TrimEnd('/')
  Write-Host "  $script:Base"

  # ------------------------------------------------------------------ 1. Test data
  Step '1. Test data (fresh for every run)'
  $stamp = Get-Date -Format 'MMddHHmmss'
  $password = 'Gp#' + $stamp + 'aA'   # 15 chars: upper, lower, digit, symbol
  $users = @{
    owner    = @{ firstName = 'Oleh';   lastName = 'Tester'; email = "gp.owner.$stamp@example.com" }
    waiter   = @{ firstName = 'Ivan';   lastName = 'Tester'; email = "gp.waiter.$stamp@example.com" }
    stranger = @{ firstName = 'Petro';  lastName = 'Tester'; email = "gp.stranger.$stamp@example.com" }
  }
  $tokens = @{}
  foreach ($key in 'owner', 'waiter', 'stranger') {
    $u = $users[$key]
    $null = Require (Invoke-Api POST '/register' @{
        firstName = $u.firstName; lastName = $u.lastName; email = $u.email
        password = $password; confirmPassword = $password
      }) 200, 201 "register $key"
    $signIn = Require (Invoke-Api POST '/signin' @{ email = $u.email; password = $password }) 200 "sign in $key"
    $tokens[$key] = $signIn.idToken
  }
  Write-Host "  users: $($users.owner.email), $($users.waiter.email), $($users.stranger.email)"
  $ownerToken = $tokens.owner

  $location = Require (Invoke-Api POST '/locations' @{
      address = "E2E street $stamp"; description = 'E2E test location'; totalCapacity = 20
      averageOccupancy = '50%'; rating = 4.5
    } $ownerToken) 200, 201 'create location'
  $locationId = $location.id

  $table = Require (Invoke-Api POST '/tables' @{ locationId = $locationId; capacity = 6; tableNumber = 1 } $ownerToken) 200, 201 'create table'
  $tableId = $table.id

  function New-Dish($name, $price, $type) {
    $d = Require (Invoke-Api POST '/dishes' @{
        name = $name; description = "$name (e2e)"; dishType = $type; price = $price
        weight = '300 g'; calories = '200'; proteins = '10'; fats = '5'; carbohydrates = '20'; vitamins = 'A'
        state = 'Available'; locationIds = @($locationId)
      } $ownerToken) 200, 201 "create dish $name"
    return $d.id
  }
  $salad = New-Dish 'Spring Salad' 19 'Appetizer'
  $soup  = New-Dish 'Tom Yum' 22 'MainCourse'

  $null = Require (Invoke-Api POST '/waiters' @{ email = $users.waiter.email; locationId = $locationId } $ownerToken) 200, 201 'add waiter'

  $start = (Get-Date).ToUniversalTime().Date.AddDays(2).AddHours(12)
  $reservation = Require (Invoke-Api POST '/reservations' @{
      tableId = $tableId; guestCount = 5
      startTime = $start.ToString('yyyy-MM-ddTHH:mm:ssZ'); endTime = $start.AddMinutes(90).ToString('yyyy-MM-ddTHH:mm:ssZ')
    } $ownerToken) 200, 201 'create reservation'
  $reservationId = $reservation.id

  $null = Require (Invoke-Api POST '/orders' @{ reservationId = $reservationId; orderItems = @(@{ dishId = $salad; quantity = 1 }) } $ownerToken) 200, 201 'create pre-order'

  $invite = Require (Invoke-Api GET "/reservations/$reservationId/invite-token" $null $ownerToken) 200 'invite token'
  $group = Require (Invoke-Api GET "/guest/invites/$($invite.token)") 200 'open invite'
  $groupId = $group.groupReservationId
  Write-Host "  reservation $reservationId, group $groupId"

  function New-Guest($name, $dishes, [bool]$submit) {
    $s = Require (Invoke-Api POST '/guest/sessions' @{ groupReservationId = $groupId; guestName = $name }) 200, 201 "guest $name"
    foreach ($d in $dishes) {
      $null = Require (Invoke-Api POST "/guest/sessions/$($s.sessionId)/dishes" @{ dishId = $d[0]; quantity = $d[1] } $s.sessionToken) 200, 201 "$name adds dish"
    }
    if ($submit) { $null = Require (Invoke-Api POST "/guest/sessions/$($s.sessionId)/submit" $null $s.sessionToken) 200 "$name submits" }
    return $s
  }
  # Anna: 2 x 19 + 1 x 22 = 60 (submitted); Olena: 22 (submitted); Maksym: 19 (not submitted)
  $anna   = New-Guest 'Anna'   @(@($salad, 2), @($soup, 1)) $true
  $olena  = New-Guest 'Olena'  @(, @($soup, 1)) $true
  $maksym = New-Guest 'Maksym' @(, @($salad, 1)) $false
  Write-Host '  guests: Anna (60, submitted), Olena (22, submitted), Maksym (19, not submitted)'

  # ------------------------------------------------------------------ 2. Level 2a: owner session id
  Step '2. L2  GET /reservations/{id}/guest-session-id'
  $r = Invoke-Api GET "/reservations/$reservationId/guest-session-id" $null $ownerToken
  Expect $r 200 'owner gets own session id'
  $ownerSessionId = $r.Json.guestSessionId
  Check 'guestSessionId is returned' ([bool]$ownerSessionId)
  $r2 = Invoke-Api GET "/reservations/$reservationId/guest-session-id" $null $ownerToken
  Check 'second call returns the same session' ([bool]$ownerSessionId -and $r2.Json.guestSessionId -eq $ownerSessionId) "($($r2.Json.guestSessionId))"
  Expect (Invoke-Api GET "/reservations/$reservationId/guest-session-id" $null $tokens.stranger) 403 'not the owner'
  Expect (Invoke-Api GET "/reservations/00000000-0000-0000-0000-000000000000/guest-session-id" $null $ownerToken) 404 'unknown reservation'

  # ------------------------------------------------------------------ 3. Level 1: sessions snapshot
  Step '3. L1  GET /guest/groups/{id}/sessions'
  $r = Invoke-Api GET "/guest/groups/$groupId/sessions" $null $ownerToken
  Expect $r 200 'owner gets the snapshot'
  $participants = @($r.Json.participants)
  Check '4 participants (owner + 3 guests)' ($participants.Count -eq 4) "(got $($participants.Count))"
  Check 'owner is first' ($participants[0].type -eq 'Owner') "(got $($participants[0].type))"
  Check 'owner name comes from the profile' ($participants[0].name -eq 'Oleh') "(got $($participants[0].name))"
  $annaP = $participants | Where-Object { $_.id -eq $anna.sessionId }
  Check 'Anna total = 60' ([decimal]$annaP.total -eq 60) "(got $($annaP.total))"
  Check 'Anna isSubmitted = true' ($annaP.isSubmitted -eq $true)
  Check 'group total = 101' ([decimal]$r.Json.total -eq 101) "(got $($r.Json.total))"
  Check 'total = sum of participants' ([decimal]$r.Json.total -eq (Sum-Of ($participants | ForEach-Object { $_.total })))
  $saladSummary = @($r.Json.summaryByDish) | Where-Object { $_.dishId -eq $salad }
  Check 'summaryByDish: Spring Salad x3' ($saladSummary.quantity -eq 3) "(got $($saladSummary.quantity))"

  $legacy = Invoke-Api GET "/guest/group/$groupId/sessions" $null $ownerToken
  Check 'legacy path /guest/group/{id}/sessions works' ($legacy.Status -eq 200 -and [decimal]$legacy.Json.total -eq 101) "(got $($legacy.Status))"
  Expect (Invoke-Api GET "/guest/groups/$groupId/sessions" $null $tokens.stranger) 403 'not the owner'
  Expect (Invoke-Api GET "/guest/groups/00000000-0000-0000-0000-000000000000/sessions" $null $ownerToken) 404 'unknown group'
  Expect (Invoke-Api GET "/guest/groups/$groupId/sessions") 401 'no token'

  # ------------------------------------------------------------------ 4. US15/US16: owner removes a guest's dish
  Step '4. US15/US16  DELETE .../sessions/{sid}/dishes/{dishId} + guest notice'
  $r = Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($anna.sessionId)/dishes/$soup" $null $ownerToken
  Expect $r 200 'owner removes Tom Yum from Anna'
  Check 'group total = 79 after removal' ([decimal]$r.Json.total -eq 79) "(got $($r.Json.total))"
  $g = Invoke-Api GET "/guest/sessions/$($anna.sessionId)" $null $anna.sessionToken
  $notices = @($g.Json.notices | Where-Object { $_ })
  Check 'Anna sees 1 notice' ($g.Status -eq 200 -and $notices.Count -eq 1) "(status $($g.Status), notices $($notices.Count))"
  if ($notices.Count -gt 0) { Write-Host "        notice: $($notices[0].message)" -ForegroundColor DarkGray }
  Expect (Invoke-Api POST "/guest/sessions/$($anna.sessionId)/notices/ack" $null $anna.sessionToken) 200 'Anna acknowledges'
  $g = Invoke-Api GET "/guest/sessions/$($anna.sessionId)" $null $anna.sessionToken
  Check 'notices are cleared' ($g.Status -eq 200 -and @($g.Json.notices | Where-Object { $_ }).Count -eq 0)
  Expect (Invoke-Api POST "/guest/sessions/$($anna.sessionId)/notices/ack" $null 'wrong-token') 401 'ack with a wrong guest token'
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($anna.sessionId)/dishes/$soup" $null $ownerToken) 404 'dish already removed'
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($olena.sessionId)/dishes/$soup" $null $tokens.stranger) 403 'not the owner'

  # ------------------------------------------------------------------ 5. Level 3: delete a guest session
  Step '5. L3  DELETE /guest/groups/{id}/sessions/{sid}'
  $r = Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($maksym.sessionId)" $null $ownerToken
  Expect $r 200 'owner removes Maksym'
  $participants = @($r.Json.participants)
  Check '3 participants left' ($participants.Count -eq 3) "(got $($participants.Count))"
  Check 'Maksym is gone' (-not ($participants | Where-Object { $_.id -eq $maksym.sessionId }))
  Check 'new total = 60 (Anna 38 + Olena 22)' ([decimal]$r.Json.total -eq 60) "(got $($r.Json.total))"
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($maksym.sessionId)" $null $ownerToken) 404 'already removed'
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$ownerSessionId" $null $ownerToken) 400 "owner's own session"
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($olena.sessionId)" $null $tokens.stranger) 403 'not the owner'

  # ------------------------------------------------------------------ 6. Level 2b: submit
  Step '6. L2  POST /group-reservations/{id}/submit'
  Expect (Invoke-Api POST "/group-reservations/$groupId/submit" @{ reservationId = $reservationId } $tokens.stranger) 403 'not the owner'
  Expect (Invoke-Api POST "/group-reservations/$groupId/submit" @{ reservationId = 'other-reservation' } $ownerToken) 400 'reservation of another group'
  Expect (Invoke-Api POST "/group-reservations/$groupId/submit" @{ } $ownerToken) 400 'missing reservationId'
  Expect (Invoke-Api POST "/group-reservations/00000000-0000-0000-0000-000000000000/submit" @{ reservationId = $reservationId } $ownerToken) 404 'unknown group'

  $r = Invoke-Api POST "/group-reservations/$groupId/submit" @{ reservationId = $reservationId } $ownerToken
  Expect $r 200 'owner submits the group pre-order'
  $lines = @($r.Json)
  $saladLine = $lines | Where-Object { $_.dishId -eq $salad }
  $soupLine = $lines | Where-Object { $_.dishId -eq $soup }
  Check 'merged: Spring Salad x2 (Anna)' ($saladLine.quantity -eq 2) "(got $($saladLine.quantity))"
  Check 'merged: Tom Yum x1 (Olena)' ($soupLine.quantity -eq 1) "(got $($soupLine.quantity))"
  Write-Host "        response: $($r.Text)" -ForegroundColor DarkGray

  $order = Invoke-Api GET "/orders/$reservationId" $null $ownerToken
  $orderSalad = @($order.Json.orderItems) | Where-Object { $_.dish.id -eq $salad }
  Check 'order status = SubmittedByCustomer' (@('SubmittedByCustomer', '3', 3) -contains $order.Json.status) "(got $($order.Json.status))"
  Check 'order: Spring Salad 1 + 2 = 3' ($orderSalad.quantity -eq 3) "(got $($orderSalad.quantity); $($order.Text))"

  $status = Invoke-Api GET "/guest/groups/$groupId/status" $null $ownerToken
  Check 'group status = Submitted' ($status.Json.status -eq 'Submitted') "(got $($status.Status) $($status.Text))"
  Expect (Invoke-Api POST "/group-reservations/$groupId/submit" @{ reservationId = $reservationId } $ownerToken) 409 'second submit'
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($olena.sessionId)" $null $ownerToken) 409 'delete after submit'
}
catch {
  $script:Failed++
  Write-Host "`n  STOPPED: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
  Step 'Result'
  $color = if ($script:Failed -eq 0) { 'Green' } else { 'Red' }
  Write-Host "  passed: $script:Passed, failed: $script:Failed" -ForegroundColor $color
  if ($script:Failed -eq 0) { Write-Host '  ALL E2E CHECKS PASSED' -ForegroundColor Green }
  Write-Host "  Log: $Log" -ForegroundColor Yellow
  Stop-Transcript | Out-Null
}
