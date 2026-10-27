<#
.SYNOPSIS
  Removes everything deploy.ps1 created, in reverse order.
  Frontend files -> Syndicate backend -> Terraform infra -> state bucket -> artifacts bucket.
#>
param([string]$Region = 'eu-north-1')

$ErrorActionPreference = 'Continue'   # best-effort cleanup: keep going if one step fails
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\project')).Path
$Infra    = Join-Path $ProjectRoot 'infra\terraform'
$Backend  = Join-Path $ProjectRoot 'backend\restaurant-backend-app'
$SdctConf = Join-Path $Backend '.syndicate-config-restaurant-backend-app'
$env:AWS_DEFAULT_REGION = $Region
function Native { param([scriptblock]$Block) $old=$ErrorActionPreference; $ErrorActionPreference='Continue'; try { & $Block 2>$null } finally { $ErrorActionPreference=$old } }

$AccountId = Native { aws sts get-caller-identity --query Account --output text }
if (-not $AccountId) { throw 'AWS CLI is not authenticated.' }

Write-Host "This DELETES the Green & Tasty deployment in AWS account $AccountId ($Region)." -ForegroundColor Yellow
if ((Read-Host 'Type DELETE to continue') -ne 'DELETE') { Write-Host 'Cancelled.'; exit 0 }

function Empty-VersionedBucket($b) {
  Native { aws s3api head-bucket --bucket $b } | Out-Null
  if ($LASTEXITCODE -ne 0) { return $false }
  aws s3 rm "s3://$b/" --recursive | Out-Null
  do {
    $v = aws s3api list-object-versions --bucket $b --max-items 500 --output json | ConvertFrom-Json
    $objs = @($v.Versions) + @($v.DeleteMarkers) | Where-Object { $_ } | ForEach-Object { @{ Key = $_.Key; VersionId = $_.VersionId } }
    if ($objs.Count -gt 0) {
      $tmp = New-TemporaryFile
      (@{ Objects = $objs; Quiet = $true } | ConvertTo-Json -Depth 4 -Compress) | Set-Content $tmp -Encoding ascii
      aws s3api delete-objects --bucket $b --delete "file://$tmp" | Out-Null
      Remove-Item $tmp
    }
  } while ($objs.Count -gt 0)
  return $true
}

Write-Host "`n1. Frontend files" -ForegroundColor Cyan
Empty-VersionedBucket "restaurant-frontend-$AccountId" | Out-Null

Write-Host "`n2. Backend (syndicate clean)" -ForegroundColor Cyan
Push-Location $Backend
try {
  if (Test-Path '.\syndicate_venv') { . .\syndicate_venv\Scripts\Activate.ps1 }
  $env:SDCT_CONF = $SdctConf
  syndicate clean
} finally { if (Get-Command deactivate -ErrorAction SilentlyContinue) { deactivate }; Pop-Location }

Write-Host "`n3. Terraform destroy" -ForegroundColor Cyan
foreach ($b in "restaurant-backend-dev-images-$AccountId", "restaurant-backend-dev-profile-images-$AccountId", "restaurant-backend-dev-swagger-ui-$AccountId") { Empty-VersionedBucket $b | Out-Null }
Push-Location $Infra
try { terraform destroy -input=false -auto-approve } finally { Pop-Location }

Write-Host "`n4-5. State and artifacts buckets" -ForegroundColor Cyan
foreach ($b in "restaurant-terraform-state-$AccountId", "marathon2026-restaurant-artifacts-$AccountId") {
  if (Empty-VersionedBucket $b) { aws s3api delete-bucket --bucket $b --region $Region; Write-Host "  deleted $b" }
}
Write-Host "`nCleanup finished." -ForegroundColor Green
