<#
.SYNOPSIS
  End-to-end check of the group pre-order API (homework L1-L3, US13-US21) on the deployed backend.

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
  # The owner card shows the owner's own pre-order (Spring Salad x1 from POST /orders above).
  Check 'owner card = own pre-order, total 19, submitted' ([decimal]$participants[0].total -eq 19 -and $participants[0].isSubmitted -eq $true) "(got $($participants[0].total), $($participants[0].isSubmitted))"
  Check 'owner name comes from the profile' ($participants[0].name -eq 'Oleh') "(got $($participants[0].name))"
  $annaP = $participants | Where-Object { $_.id -eq $anna.sessionId }
  Check 'Anna total = 60' ([decimal]$annaP.total -eq 60) "(got $($annaP.total))"
  Check 'Anna isSubmitted = true' ($annaP.isSubmitted -eq $true)
  Check 'group total = 120 (owner 19 + Anna 60 + Olena 22 + Maksym 19)' ([decimal]$r.Json.total -eq 120) "(got $($r.Json.total))"
  Check 'total = sum of participants' ([decimal]$r.Json.total -eq (Sum-Of ($participants | ForEach-Object { $_.total })))
  $saladSummary = @($r.Json.summaryByDish) | Where-Object { $_.dishId -eq $salad }
  Check 'summaryByDish: Spring Salad x4 (owner 1 + Anna 2 + Maksym 1)' ($saladSummary.quantity -eq 4) "(got $($saladSummary.quantity))"

  $legacy = Invoke-Api GET "/guest/group/$groupId/sessions" $null $ownerToken
  Check 'legacy path /guest/group/{id}/sessions works' ($legacy.Status -eq 200 -and [decimal]$legacy.Json.total -eq 120) "(got $($legacy.Status), $($legacy.Json.total))"
  Expect (Invoke-Api GET "/guest/groups/$groupId/sessions" $null $tokens.stranger) 403 'not the owner'
  Expect (Invoke-Api GET "/guest/groups/00000000-0000-0000-0000-000000000000/sessions" $null $ownerToken) 404 'unknown group'
  Expect (Invoke-Api GET "/guest/groups/$groupId/sessions") 401 'no token'

  # ------------------------------------------------------------------ 4. US15/US16: owner removes a guest's dish
  Step '4. US15/US16  DELETE .../sessions/{sid}/dishes/{dishId} + guest notice'
  $r = Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($anna.sessionId)/dishes/$soup" $null $ownerToken
  Expect $r 200 'owner removes Tom Yum from Anna'
  Check 'group total = 98 after removal' ([decimal]$r.Json.total -eq 98) "(got $($r.Json.total))"
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
  Check 'new total = 79 (owner 19 + Anna 38 + Olena 22)' ([decimal]$r.Json.total -eq 79) "(got $($r.Json.total))"
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($maksym.sessionId)" $null $ownerToken) 404 'already removed'
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$ownerSessionId" $null $ownerToken) 400 "owner's own session"
  Expect (Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($olena.sessionId)" $null $tokens.stranger) 403 'not the owner'

  # ------------------------------------------------------------------ 5b. US13-US19 while the group is open
  Step '5b. US13/US14/US15/US16/US18/US19  before submit'
  $r = Invoke-Api GET "/guest/groups/$groupId/sessions" $null $ownerToken
  # Compare in UTC: Windows PowerShell turns "...Z" into local time when cast to [datetime].
  $cut = $r.Json.cutoffAt
  $cutText = if ($cut -is [datetime]) { $cut.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm') } elseif ($cut) { "$cut".Substring(0, 16) } else { '' }
  Check 'US13: cutoffAt is returned, 2 h before the start' ($cutText -eq $start.AddHours(-2).ToString('yyyy-MM-ddTHH:mm')) "(got $cut)"
  Check 'US13: secondsUntilCutoff > 0 and not locked' ([long]$r.Json.secondsUntilCutoff -gt 0 -and $r.Json.isLocked -eq $false) "(got $($r.Json.secondsUntilCutoff), $($r.Json.isLocked))"
  Check 'US14: auto-remind is on by default' ($r.Json.autoRemind -eq $true)
  Expect (Invoke-Api GET "/guest/groups/$groupId/payment" $null $ownerToken) 400 'US17: payment before submit'

  # US18/US19: guest note, serving time and allergies
  Expect (Invoke-Api PUT "/guest/sessions/$($anna.sessionId)/dishes/$salad" @{ note = 'No onion, please'; serveTime = 'AfterMains' } $anna.sessionToken) 200 'US18: Anna adds a note and serving time'
  Expect (Invoke-Api PUT "/guest/sessions/$($anna.sessionId)/allergies" @{ allergies = 'Nuts' } $anna.sessionToken) 200 'US18: Anna states an allergy'
  Expect (Invoke-Api PUT "/guest/sessions/$($anna.sessionId)/dishes/$salad" @{ note = ('a' * 81) } $anna.sessionToken) 400 'US18: note longer than 80'
  Expect (Invoke-Api PUT "/guest/sessions/$($anna.sessionId)/dishes/$salad" @{ serveTime = 'Later' } $anna.sessionToken) 400 'US19: unknown serving time'
  Expect (Invoke-Api PUT "/guest/sessions/$($anna.sessionId)/allergies" @{ allergies = 'x' } $olena.sessionToken) 401 'US18: another guest token'
  $r = Invoke-Api PUT "/guest/groups/$groupId/my-dishes/$salad" @{ note = 'Extra lime' } $ownerToken
  Expect $r 200 'US18: owner adds a note to own dish'
  $r = Invoke-Api PUT "/guest/groups/$groupId/my-allergies" @{ allergies = 'Gluten' } $ownerToken
  Expect $r 200 'US18: owner states own allergy'
  $ownerCard = @($r.Json.participants)[0]
  Check 'US18: owner card shows allergy and note' ($ownerCard.allergies -eq 'Gluten' -and @($ownerCard.items)[0].note -eq 'Extra lime') "(got $($ownerCard.allergies), $(@($ownerCard.items)[0].note))"
  $annaP = @($r.Json.participants) | Where-Object { $_.id -eq $anna.sessionId }
  $annaSalad = @($annaP.items) | Where-Object { $_.dish.id -eq $salad }
  Check 'US18/US19: owner sees Anna note, AfterMains and Nuts' ($annaSalad.note -eq 'No onion, please' -and $annaSalad.serveTime -eq 'AfterMains' -and $annaP.allergies -eq 'Nuts') "(got $($annaSalad.note), $($annaSalad.serveTime), $($annaP.allergies))"
  $annaSoup = @($annaP.items) | Where-Object { $_.dish.id -eq $soup }
  Check 'US19: main course defaults to OnArrival' ($null -eq $annaSoup -or $annaSoup.serveTime -eq 'OnArrival')

  # US15: Undo, "Changed by you", Ask to change order
  $r = Invoke-Api POST "/guest/groups/$groupId/sessions/$($anna.sessionId)/dishes/$soup/restore" @{ quantity = 1 } $ownerToken
  Expect $r 200 'US15: Undo puts Tom Yum back'
  $annaP = @($r.Json.participants) | Where-Object { $_.id -eq $anna.sessionId }
  Check 'US15: Anna total back to 60, not "changed"' ([decimal]$annaP.total -eq 60 -and $annaP.changedByOwner -eq $false) "(got $($annaP.total), $($annaP.changedByOwner))"
  $r = Invoke-Api DELETE "/guest/groups/$groupId/sessions/$($anna.sessionId)/dishes/$soup" $null $ownerToken
  $annaP = @($r.Json.participants) | Where-Object { $_.id -eq $anna.sessionId }
  Check 'US15: removed again, "Changed by you"' ([decimal]$annaP.total -eq 38 -and $annaP.changedByOwner -eq $true) "(got $($annaP.total), $($annaP.changedByOwner))"
  $g = Invoke-Api GET "/guest/sessions/$($anna.sessionId)" $null $anna.sessionToken
  $n = @($g.Json.notices | Where-Object { $_ }) | Select-Object -Last 1
  Check 'US16: notice has dish, old and new total' ($n.type -eq 'DishRemoved' -and $n.dishName -eq 'Tom Yum' -and [decimal]$n.oldTotal -eq 60 -and [decimal]$n.newTotal -eq 38) "(got $($n.type), $($n.dishName), $($n.oldTotal) -> $($n.newTotal))"
  Check 'US16: guest sees organizer name and cut-off' ($g.Json.ownerName -eq 'Oleh' -and $g.Json.cutoffAt -and $g.Json.isLocked -eq $false) "(got $($g.Json.ownerName), $($g.Json.cutoffAt))"
  $null = Invoke-Api POST "/guest/sessions/$($anna.sessionId)/notices/ack" $null $anna.sessionToken

  $r = Invoke-Api POST "/guest/groups/$groupId/sessions/$($olena.sessionId)/change-request" $null $ownerToken
  Expect $r 200 'US15: owner asks Olena to change her order'
  $olenaP = @($r.Json.participants) | Where-Object { $_.id -eq $olena.sessionId }
  Check 'US15: Olena is "still choosing" again' ($olenaP.isSubmitted -eq $false)
  Expect (Invoke-Api POST "/guest/sessions/$($olena.sessionId)/submit" $null $olena.sessionToken) 200 'Olena submits again'
  Expect (Invoke-Api POST "/guest/sessions/$($anna.sessionId)/reopen" $null $anna.sessionToken) 200 'US16: Anna reopens her cart (Pick another dish)'
  Expect (Invoke-Api POST "/guest/sessions/$($anna.sessionId)/submit" $null $anna.sessionToken) 200 'Anna submits again'

  # US14: reminders
  $iryna = New-Guest 'Iryna' @() $false
  $r = Invoke-Api POST "/guest/groups/$groupId/reminders" @{ } $ownerToken
  Expect $r 200 'US14: Remind N'
  Check 'US14: only Iryna (no dishes) is reminded' (@($r.Json.reminded).Count -eq 1 -and @($r.Json.reminded)[0].id -eq $iryna.sessionId) "(got $($r.Text))"
  $r = Invoke-Api POST "/guest/groups/$groupId/reminders" @{ guestSessionIds = @($iryna.sessionId) } $ownerToken
  Check 'US14: second reminder within 15 min is skipped' (@($r.Json.skipped).Count -eq 1 -and @($r.Json.reminded).Count -eq 0) "(got $($r.Text))"
  $g = Invoke-Api GET "/guest/sessions/$($iryna.sessionId)" $null $iryna.sessionToken
  Check 'US14: Iryna sees the reminder' (@($g.Json.notices | Where-Object { $_.type -eq 'Reminder' }).Count -eq 1) "(got $($g.Text))"
  $r = Invoke-Api PUT "/guest/groups/$groupId/settings" @{ autoRemind = $false } $ownerToken
  Check 'US14: auto-remind can be turned off' ($r.Status -eq 200 -and $r.Json.autoRemind -eq $false) "(got $($r.Status), $($r.Json.autoRemind))"
  Expect (Invoke-Api POST "/guest/groups/$groupId/reminders" @{ } $tokens.stranger) 403 'US14: not the owner'

  # US16 AC5: a removed guest learns about it
  $s = Invoke-Api GET "/guest/groups/$groupId/status?guestSessionId=$($maksym.sessionId)"
  Check 'US16: Maksym sees "removed from this group"' ($s.Status -eq 200 -and $s.Json.removed -eq $true) "(got $($s.Text))"

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

  # ------------------------------------------------------------------ 7. Owner keeps access after submit
  Step '7. Owner screen after submit (reload) and guest access'
  $r = Invoke-Api GET "/reservations/$reservationId/invite-token" $null $ownerToken
  Expect $r 200 'owner gets the invite token after submit'
  Expect (Invoke-Api GET "/guest/invites/$($invite.token)" $null $ownerToken) 200 'owner opens the invite after submit'
  $guestInvite = Invoke-Api GET "/guest/invites/$($invite.token)"
  Check 'a guest cannot open the submitted group' ($guestInvite.Status -ne 200) "(got $($guestInvite.Status))"
  $r = Invoke-Api GET "/guest/groups/$groupId/sessions" $null $ownerToken
  $ownerCard = @($r.Json.participants)[0]
  Check 'owner card after submit = own dishes only (Spring Salad x1)' (@($ownerCard.items).Count -eq 1 -and @($ownerCard.items)[0].quantity -eq 1) "(got $(@($ownerCard.items).Count) items)"
  Check 'US13: submittedAt is set, manual' ($r.Json.submittedAt -and $r.Json.submittedAutomatically -eq $false -and $r.Json.isLocked -eq $true) "(got $($r.Json.submittedAt), $($r.Json.submittedAutomatically))"
  Expect (Invoke-Api PUT "/guest/sessions/$($anna.sessionId)/allergies" @{ allergies = 'Milk' } $anna.sessionToken) 409 'US13: guest cannot change after submit'

  # ------------------------------------------------------------------ 8. US17: split the bill (simulated)
  Step '8. US17  payment'
  $r = Invoke-Api GET "/guest/groups/$groupId/payment" $null $ownerToken
  Expect $r 200 'payment view after submit'
  Check 'grand total = 79 (owner 19 + Anna 38 + Olena 22)' ([decimal]$r.Json.grandTotal -eq 79) "(got $($r.Json.grandTotal))"
  $r = Invoke-Api PUT "/guest/groups/$groupId/payment" @{ mode = 'SplitEqually' } $ownerToken
  $amounts = @($r.Json.participants | ForEach-Object { [decimal]$_.amount })
  Check 'Split equally: 26.34 + 26.33 + 26.33 = 79' ((Sum-Of $amounts) -eq 79 -and $amounts[0] -eq 26.34) "(got $($amounts -join ', '))"
  Expect (Invoke-Api PUT "/guest/groups/$groupId/payment" @{ mode = 'Crypto' } $ownerToken) 400 'unknown mode'
  $r = Invoke-Api PUT "/guest/groups/$groupId/payment" @{ mode = 'EachPaysOwn' } $ownerToken
  $annaPay = @($r.Json.participants) | Where-Object { $_.id -eq $anna.sessionId }
  Check 'Each pays own: Anna pays 38' ([decimal]$annaPay.amount -eq 38) "(got $($annaPay.amount))"
  Expect (Invoke-Api POST "/guest/groups/$groupId/payment/links" $null $ownerToken) 200 'send payment links'
  $g = Invoke-Api GET "/guest/sessions/$($anna.sessionId)" $null $anna.sessionToken
  Check 'Anna sees her share and the request' ([decimal]$g.Json.payment.amount -eq 38 -and @($g.Json.notices | Where-Object { $_.type -eq 'PaymentRequested' }).Count -ge 1) "(got $($g.Json.payment | ConvertTo-Json -Compress))"
  $p = Invoke-Api POST "/guest/sessions/$($anna.sessionId)/pay" $null $anna.sessionToken
  Check 'Anna pays (simulated)' ($p.Status -eq 200 -and $p.Json.isPaid -eq $true) "(got $($p.Status) $($p.Text))"
  $r = Invoke-Api POST "/guest/groups/$groupId/payment/$ownerSessionId/paid" $null $ownerToken
  Check 'owner marks own share paid: Paid 57 / Pending 22' ([decimal]$r.Json.paidTotal -eq 57 -and [decimal]$r.Json.pendingTotal -eq 22) "(got $($r.Json.paidTotal) / $($r.Json.pendingTotal))"
  $r = Invoke-Api POST "/guest/groups/$groupId/reminders" @{ } $ownerToken
  Check 'Remind at payment step: only Olena (unpaid)' (@($r.Json.reminded).Count -eq 1 -and @($r.Json.reminded)[0].id -eq $olena.sessionId) "(got $($r.Text))"
  Expect (Invoke-Api GET "/guest/groups/$groupId/payment" $null $tokens.stranger) 403 'not the owner'

  # ------------------------------------------------------------------ 9. US21: book again with the same group
  Step '9. US21  book again'
  $start2 = $start.AddDays(2)
  $res2 = Require (Invoke-Api POST '/reservations' @{
      tableId = $tableId; guestCount = 5
      startTime = $start2.ToString('yyyy-MM-ddTHH:mm:ssZ'); endTime = $start2.AddMinutes(90).ToString('yyyy-MM-ddTHH:mm:ssZ')
    } $ownerToken) 200, 201 'second reservation'
  $r = Invoke-Api POST "/reservations/$($res2.id)/rebook" @{ previousReservationId = $reservationId } $ownerToken
  Expect $r 200 'link the new reservation to the previous group'
  Check 'previous guests are invited (Anna, Olena, Iryna)' ([int]$r.Json.invitedParticipants -eq 3) "(got $($r.Text))"
  $g = Invoke-Api GET "/guest/sessions/$($anna.sessionId)" $null $anna.sessionToken
  Check 'Anna sees the new invite on her old link' ($g.Json.rebook.inviteToken -eq $r.Json.inviteToken) "(got $($g.Json.rebook | ConvertTo-Json -Compress))"
  Expect (Invoke-Api POST "/reservations/$($res2.id)/rebook" @{ previousReservationId = $reservationId } $tokens.stranger) 403 'not the owner'
  Expect (Invoke-Api POST "/reservations/$($res2.id)/rebook" @{ } $ownerToken) 400 'missing previousReservationId'

  # ------------------------------------------------------------------ 10. US13: automatic send at the cut-off
  Step '10. US13  automatic send after the cut-off'
  $soon = (Get-Date).ToUniversalTime().AddMinutes(100)
  $soon = $soon.AddSeconds(-$soon.Second)
  $res3 = Require (Invoke-Api POST '/reservations' @{
      tableId = $tableId; guestCount = 3
      startTime = $soon.ToString('yyyy-MM-ddTHH:mm:ssZ'); endTime = $soon.AddMinutes(60).ToString('yyyy-MM-ddTHH:mm:ssZ')
    } $ownerToken) 200, 201 'reservation in 100 minutes (cut-off already passed)'
  $null = Require (Invoke-Api POST '/orders' @{ reservationId = $res3.id; orderItems = @(@{ dishId = $soup; quantity = 1 }) } $ownerToken) 200, 201 'owner pre-order'
  $inv3 = Require (Invoke-Api GET "/reservations/$($res3.id)/invite-token" $null $ownerToken) 200 'invite token'
  $guestOpen = Invoke-Api GET "/guest/invites/$($inv3.token)"
  Check 'a guest cannot join after the cut-off' ($guestOpen.Status -ne 200) "(got $($guestOpen.Status))"
  $grp3 = Require (Invoke-Api GET "/guest/invites/$($inv3.token)" $null $ownerToken) 200 'owner opens the group'
  $r = Invoke-Api GET "/guest/groups/$($grp3.groupReservationId)/sessions" $null $ownerToken
  Check 'sent automatically and locked' ($r.Json.groupStatus -eq 'Submitted' -and $r.Json.submittedAutomatically -eq $true -and $r.Json.isLocked -eq $true) "(got $($r.Json.groupStatus), $($r.Json.submittedAutomatically))"
  $order3 = Invoke-Api GET "/orders/$($res3.id)" $null $ownerToken
  Check 'order went to the restaurant' (@('SubmittedByCustomer', '3', 3) -contains $order3.Json.status) "(got $($order3.Json.status))"
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
