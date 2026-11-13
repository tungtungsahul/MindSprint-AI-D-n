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
