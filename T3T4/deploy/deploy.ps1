<#
.SYNOPSIS
  Deploys Green & Tasty (EPAM IT Marathon 6.0) to AWS: Terraform -> Syndicate backend -> Angular frontend.

.EXAMPLE
  .\deploy.ps1 -Email you@example.com
  .\deploy.ps1 -Email you@example.com -Only frontend     # redeploy just one stage
#>
param(
  [Parameter(Mandatory = $true)][string]$Email,
  [ValidateSet('all', 'infra', 'backend', 'frontend')][string]$Only = 'all',
  [string]$Region = 'eu-north-1',   # new AWS 'Projects' accounts are locked to the project's region
  [switch]$CloudFront                # use only after AWS Support has verified the account for CloudFront
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\project')).Path
$Infra    = Join-Path $ProjectRoot 'infra\terraform'
$Backend  = Join-Path $ProjectRoot 'backend\restaurant-backend-app'
$Frontend = Join-Path $ProjectRoot 'frontend\restaurant-frontend-app'
$SdctConf = Join-Path $Backend '.syndicate-config-restaurant-backend-app'

$LogFile = Join-Path $PSScriptRoot ("deploy-log-{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
Start-Transcript -Path $LogFile | Out-Null

function Step($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "  OK  $t" -ForegroundColor Green }
function Run {
  param([string]$Cmd)
  Write-Host "  > $Cmd" -ForegroundColor DarkGray
  # Judge success by exit code only: many tools print progress/warnings to stderr.
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { Invoke-Expression $Cmd } finally { $ErrorActionPreference = $old }
  if ($LASTEXITCODE -ne 0) { throw "Command failed (exit $LASTEXITCODE): $Cmd" }
}

function Native {
  # Run a native command without PowerShell 5.1 turning its stderr into a terminating error.
  param([scriptblock]$Block)
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { $out = & $Block 2>$null } finally { $ErrorActionPreference = $old }
  return $out
}
function Test-Bucket($b) { Native { aws s3api head-bucket --bucket $b } | Out-Null; return ($LASTEXITCODE -eq 0) }

try {
  # ------------------------------------------------------------------ 0. Preflight
  Step '0. Preflight checks'
  foreach ($tool in 'aws', 'terraform', 'dotnet', 'node', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' is not installed or not in PATH. See DEPLOY.md, step 1." }
  }
  Ok 'tools found'

  $AccountId = Native { aws sts get-caller-identity --query Account --output text }
  if ($LASTEXITCODE -ne 0 -or -not $AccountId) { throw "AWS CLI is not authenticated. Run 'aws configure' first (DEPLOY.md, step 2)." }
  $Caller = Native { aws sts get-caller-identity --query Arn --output text }
  Ok "AWS account $AccountId ($Caller)"

  # Free plan guard: never deploy into a paid account by accident.
  $planType = $null
  $planType = Native { aws freetier get-account-plan-state --region us-east-1 --query accountPlanType --output text }
  if ($planType -eq 'FREE') {
    Ok 'account plan: FREE (no charges possible until you click Upgrade)'
  } elseif ($planType -eq 'PAID') {
    throw 'This account is on the PAID plan - charges are possible. Deployment stopped.'
  } else {
    Write-Host '  ??  Could not read the account plan via CLI (old AWS CLI?).' -ForegroundColor Yellow
    $ans = Read-Host '      Billing page shows "Current plan: Free"? Type YES to continue'
    if ($ans -ne 'YES') { throw 'Stopped by user.' }
  }

  $StateBucket    = "restaurant-terraform-state-$AccountId"
  $FrontendBucket = "restaurant-frontend-$AccountId"
  $env:AWS_DEFAULT_REGION = $Region

  # ------------------------------------------------------------------ 1. Terraform
  if ($Only -in 'all', 'infra') {
    Step '1. Terraform: shared infrastructure'

    if (-not (Test-Bucket $StateBucket)) {
      Run "aws s3api create-bucket --bucket $StateBucket --region $Region --create-bucket-configuration LocationConstraint=$Region"
      Run "aws s3api put-bucket-versioning --bucket $StateBucket --versioning-configuration Status=Enabled"
      Run "aws s3api put-public-access-block --bucket $StateBucket --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
      Ok "state bucket $StateBucket created"
    } else { Ok "state bucket $StateBucket already exists" }

    # Variables go into a gitignored tfvars file; backend.tf is not edited (bucket passed via -backend-config).
    @"
account_id   = "$AccountId"
aws_region   = "$Region"
alert_emails = ["$Email"]
use_cloudfront = $(if ($CloudFront) { 'true' } else { 'false' })
"@ | Set-Content -Encoding ascii (Join-Path $Infra 'terraform.tfvars')

    Push-Location $Infra
    try {
      Run "terraform init -input=false -reconfigure -backend-config=`"bucket=$StateBucket`" -backend-config=`"region=$Region`""
      Run 'terraform validate'
      Run 'terraform plan -input=false -out=tfplan'
      Run 'terraform apply -input=false tfplan'
      Remove-Item tfplan -ErrorAction SilentlyContinue
    } finally { Pop-Location }
    Ok 'infrastructure deployed'
  }

  # ------------------------------------------------------------------ 2. Backend (Syndicate)
  if ($Only -in 'all', 'backend') {
    Step '2. Backend: AWS Syndicate'

    # Fill placeholders in Syndicate configs (idempotent).
    foreach ($f in 'syndicate.yml', 'syndicate_aliases.yml') {
      $p = Join-Path $SdctConf $f
      $c = Get-Content $p -Raw
      $c = ($c -replace '\{your_account_id\}', $AccountId)
      $c = ($c -replace '"your_account_id"', ('"' + $AccountId + '"'))
      $c = ($c -replace '(?m)^region:.*$', ('region: "' + $Region + '"'))
      if ($f -eq 'syndicate.yml') {
        $c = $c -replace '(?m)^project_path:.*$', ('project_path: "' + ($Backend -replace '\\', '/') + '"')
      }
      [System.IO.File]::WriteAllText($p, $c, (New-Object System.Text.UTF8Encoding $false))   # UTF-8 without BOM
    }
    Ok 'syndicate configs filled'

    Push-Location $Backend
    try {
      $py = if (Get-Command py -ErrorAction SilentlyContinue) { 'py -3.11' } else { 'python' }
      if (-not (Test-Path '.\syndicate_venv')) { Run "$py -m venv syndicate_venv" }
      . .\syndicate_venv\Scripts\Activate.ps1
      Run 'python -m pip install --quiet --upgrade pip aws-syndicate'
      $env:SDCT_CONF = $SdctConf

      $deployBucket = "marathon2026-restaurant-artifacts-$AccountId"
      if (-not (Test-Bucket $deployBucket)) { Run 'syndicate create-deploy-target-bucket' } else { Ok "artifacts bucket $deployBucket already exists" }

      Run 'syndicate build'
      Run 'syndicate deploy --deploy_name dev --replace_output'   # --replace_output makes re-runs safe
    } finally { if (Get-Command deactivate -ErrorAction SilentlyContinue) { deactivate }; Pop-Location }
    Ok 'backend deployed'
  }

  # ------------------------------------------------------------------ 3. Frontend
  if ($Only -in 'all', 'frontend') {
    Step '3. Frontend: Angular -> S3 -> CloudFront'
    $ApiId = (aws apigateway get-rest-apis --region $Region --query "items[?name=='restaurant-backend-api'].id | [0]" --output text)
    if (-not $ApiId -or $ApiId -eq 'None') { throw 'API Gateway restaurant-backend-api not found - deploy the backend first.' }
    $env:BACKEND_URL = "https://$ApiId.execute-api.$Region.amazonaws.com/develop"
    Ok "BACKEND_URL = $env:BACKEND_URL"

    Push-Location $Frontend
    try {
      Run 'npm ci --no-audit --no-fund'
      Run 'npm run build'
      Run "aws s3 sync dist/restaurant-frontend-app/browser s3://$FrontendBucket/ --delete"
    } finally { Pop-Location }

    Push-Location $Infra
    try {
      $SiteUrl = (terraform output -raw frontend_url)
      if ($CloudFront) {
        $cfId = (terraform output -raw cloudfront_distribution_id)
        Run "aws cloudfront create-invalidation --distribution-id $cfId --paths `"/*`" --query Invalidation.Id --output text"
      }
    } finally { Pop-Location }

    Write-Host "`n  SITE:     $SiteUrl" -ForegroundColor Green
    Write-Host   "  API:      $env:BACKEND_URL" -ForegroundColor Green
  }

  Write-Host "`nDone. Log: $LogFile" -ForegroundColor Green
}
catch {
  Write-Host "`nFAILED: $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "Send this log to Claude: $LogFile" -ForegroundColor Yellow
  exit 1
}
finally { Stop-Transcript | Out-Null }
