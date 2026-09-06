# ============================================================
# bump_version.ps1 — 递增版本号并同步写入 csproj + version.json
# 用法: .\scripts\bump_version.ps1 [-Part patch|minor|major]
# ============================================================
param(
    [ValidateSet('patch','minor','major')]
    [string]$Part = 'patch'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csprojPath = Join-Path $root 'Dsh\Dsh.csproj'
$versionJsonPath = Join-Path $root 'Dsh\version.json'

# 读取当前版本号
$csproj = Get-Content $csprojPath -Raw
if ($csproj -match '<Version>([^<]+)</Version>') {
    $current = $matches[1].Trim()
} else {
    Write-Host "ERROR: <Version> not found in csproj" -ForegroundColor Red
    exit 1
}

$parts = $current.Split('.')
if ($parts.Length -lt 3) { $parts = @($parts[0], '0', '0') }
$maj = [int]$parts[0]
$min = [int]$parts[1]
$pat = [int]$parts[2]

switch ($Part) {
    'patch' {
        $pat++
        if ($pat -ge 10) { $pat = 0; $min++ }
        if ($min -ge 10) { $min = 0; $maj++ }
    }
    'minor' { $min++; $pat = 0 }
    'major' { $maj++; $min = 0; $pat = 0 }
}

$newVer = "$maj.$min.$pat"
Write-Host "Bump: $current -> $newVer ($Part)"

# 更新 csproj
$newCsproj = $csproj -replace '<Version>[^<]+</Version>', "<Version>$newVer</Version>"
Set-Content -Path $csprojPath -Value $newCsproj -NoNewline -Encoding UTF8

# 更新 version.json（保留 notes 和 url，仅替换 version 字段）
if (Test-Path $versionJsonPath) {
    $vj = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
    $vj.version = $newVer
    # UTF-8 无 BOM：JsonSerializer 要求无 BOM，否则 Parse 拒绝
    $json = $vj | ConvertTo-Json -Depth 10
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($versionJsonPath, $json, $utf8NoBom)
}

Write-Host "Done: version bumped to $newVer" -ForegroundColor Green
# 输出版本号供调用方使用
Write-Output $newVer
