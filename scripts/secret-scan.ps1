$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patterns = @(
  'AIza[0-9A-Za-z_-]{20,}',
  'keychain_secret_[A-Za-z0-9]{16,}',
  'whsec_[A-Za-z0-9]{16,}',
  'discord\.com/api/webhooks/[0-9]+/[A-Za-z0-9_-]+',
  'profile_id=botp_[A-Za-z0-9]+'
)

$include = @('*.cs','*.json','*.md','*.js','*.mjs','*.ts','*.env')
$excludeDirs = @('.git','node_modules','bin','obj','AI/lib')

$files = Get-ChildItem -Path $root -Recurse -File -Include $include |
  Where-Object {
    $full = $_.FullName
    -not ($excludeDirs | ForEach-Object { $full -like "*\\$_\\*" } | Where-Object { $_ })
  }

$hits = @()
foreach ($file in $files) {
  $content = Get-Content -Path $file.FullName -Raw
  foreach ($pattern in $patterns) {
    if ($content -match $pattern) {
      $hits += [PSCustomObject]@{ File = $file.FullName; Pattern = $pattern }
    }
  }
}

if ($hits.Count -gt 0) {
  Write-Host 'Potential secrets detected:' -ForegroundColor Red
  $hits | Sort-Object File, Pattern -Unique | Format-Table -AutoSize
  exit 1
}

Write-Host 'No token-like secrets detected.' -ForegroundColor Green
