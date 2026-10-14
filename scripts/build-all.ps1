$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\SkfuAdmin\SkfuAdmin.csproj'
foreach ($tf in 'net6.0-windows','net8.0-windows','net10.0-windows') {
    Write-Host ('Сборка ' + $tf)
    dotnet build $proj -c Release -f $tf
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
