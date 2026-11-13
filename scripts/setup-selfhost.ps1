param(
    [string]$Source = ".env.selfhost.example",
    [string]$Target = ".env.selfhost"
)

if (-not (Test-Path $Source)) {
    throw "Missing template file: $Source"
}

if (-not (Test-Path $Target)) {
    Copy-Item $Source $Target
    Write-Host "Created $Target from template without overwriting an existing config."
} else {
    Write-Host "$Target already exists. Leaving it unchanged."
}

$required = @(
    "POSTGRES_DB",
    "POSTGRES_USER",
    "POSTGRES_PASSWORD",
    "ConnectionStrings__Default",
    "Jwt__Key",
    "Cors__AllowedOrigins"
)

$missing = @()
foreach ($name in $required) {
    $value = (Get-Content $Target -Raw) -match "^${name}="
    if (-not $value) {
        $missing += $name
    }
}

if ($missing.Count -gt 0) {
    throw "Missing required values in $Target: $($missing -join ', ')"
}

Write-Host "Validated required self-host settings in $Target."
