<#
.SYNOPSIS
  Arranca Core.Api en local cargando antes las variables del archivo .env de la raíz.

.DESCRIPTION
  `dotnet run` no lee el .env (solo lo hace `docker compose`). Este script lo carga en la
  sesión actual y lanza Core.Api. No muestra valores de secretos: solo los nombres de las
  variables cargadas y la URL de Supabase.

.EXAMPLE
  ./scripts/arrancar-core.ps1
.EXAMPLE
  ./scripts/arrancar-core.ps1 -Perfil https
#>
param(
    [string] $ArchivoEnv = (Join-Path $PSScriptRoot '..\.env'),
    [ValidateSet('http', 'https')] [string] $Perfil = 'http'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ArchivoEnv)) {
    Write-Host "No existe $ArchivoEnv. Copia .env.example a .env y rellénalo." -ForegroundColor Red
    exit 1
}

$cargadas = @()
foreach ($linea in Get-Content $ArchivoEnv -Encoding UTF8) {
    $l = $linea.Trim()
    if ($l -eq '' -or $l.StartsWith('#')) { continue }
    $i = $l.IndexOf('=')
    if ($i -lt 1) { continue }
    $nombre = $l.Substring(0, $i).Trim()
    $valor = $l.Substring($i + 1).Trim()
    if ($valor.Length -ge 2 -and (($valor.StartsWith('"') -and $valor.EndsWith('"')) -or ($valor.StartsWith("'") -and $valor.EndsWith("'")))) {
        $valor = $valor.Substring(1, $valor.Length - 2)
    }
    if ($valor -eq '') { continue }   # las variables vacías no se establecen
    if ($valor.StartsWith("$nombre=", [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "ERROR: en el .env el valor de $nombre empieza repitiendo '$nombre='. Deja solo el valor, tras el primer '='." -ForegroundColor Red
        exit 1
    }
    if ($nombre -eq 'ConnectionStrings__Default' -and $valor -notmatch '(?i)(^|;)\s*(host|server)\s*=') {
        Write-Host "ERROR: ConnectionStrings__Default debe incluir 'Host=<servidor>' (formato: Host=...;Port=5432;Database=postgres;Username=...;Password=...)." -ForegroundColor Red
        exit 1
    }
    Set-Item -Path "Env:$nombre" -Value $valor
    $cargadas += $nombre
}

Write-Host "Variables cargadas desde .env: $($cargadas -join ', ')" -ForegroundColor DarkGray

$faltan = @('Supabase__Url', 'ConnectionStrings__Default') | Where-Object { -not (Get-Item "Env:$_" -ErrorAction SilentlyContinue) }
if ($faltan) {
    Write-Host "AVISO: faltan en el .env: $($faltan -join ', ')" -ForegroundColor Yellow
}
if ($env:Supabase__Url -and $env:Supabase__Url -notmatch '^https?://') {
    Write-Host "ERROR: Supabase__Url debe ser la URL completa (https://<project-ref>.supabase.co) y es '$($env:Supabase__Url)'." -ForegroundColor Red
    exit 1
}
if ($env:Supabase__Url) {
    Write-Host "Emisor esperado: $($env:Supabase__Url.TrimEnd('/'))/auth/v1" -ForegroundColor Cyan
}
if ($env:Supabase__JwtSecret) {
    Write-Host 'Modo de firma: HS256 (secreto legado)' -ForegroundColor Cyan
} else {
    Write-Host 'Modo de firma: asimétrico (JWKS del proyecto)' -ForegroundColor Cyan
}

dotnet run --project (Join-Path $PSScriptRoot '..\src\Core\Core.Api') --launch-profile $Perfil
# $ErrorActionPreference no cubre los programas externos: propaga el fallo de dotnet run a quien llame al script.
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
