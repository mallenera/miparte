<#
.SYNOPSIS
  Prueba de humo de Core.Api contra un proyecto Supabase real: login, /api/yo y alta de hogar.

.DESCRIPTION
  1. GET /health
  2. Login en Supabase Auth (email + contraseña) y obtención del access_token
  3. GET /api/yo               -> debe devolver tu usuario y tus hogares
  4. POST /api/hogares         -> solo con -CrearHogar; siembra 4 perfiles y 6 categorías
  5. Con un hogar: GET /api/perfiles, /api/categorias y /api/miembros (X-Hogar-Id)

  La anon key es pública, pero no se guarda en el repo: pásala por parámetro o variable de
  entorno SUPABASE_ANON_KEY. La contraseña se pide por consola y no se escribe en disco.

.EXAMPLE
  ./scripts/probar-api.ps1 -SupabaseUrl https://abcd.supabase.co -Email prueba@ejemplo.com
.EXAMPLE
  ./scripts/probar-api.ps1 -SupabaseUrl https://abcd.supabase.co -Email prueba@ejemplo.com -CrearHogar
#>
param(
    [Parameter(Mandatory)] [string] $SupabaseUrl,
    [Parameter(Mandatory)] [string] $Email,
    [string] $AnonKey = $env:SUPABASE_ANON_KEY,
    [string] $CoreUrl = 'http://localhost:5098',
    [string] $HogarId,
    [switch] $CrearHogar,
    [string] $NombreHogar = 'Hogar de prueba',
    [string] $NombreMiembro = 'Yo'
)

$ErrorActionPreference = 'Stop'
$SupabaseUrl = $SupabaseUrl.TrimEnd('/')
$CoreUrl = $CoreUrl.TrimEnd('/')

if (-not $AnonKey) {
    $AnonKey = Read-Host 'Anon key de Supabase (Settings -> API; es pública)'
}

function Paso($texto) { Write-Host "`n== $texto" -ForegroundColor Cyan }

function Llamar($metodo, $ruta, $token, $hogar, $cuerpo) {
    $cab = @{ Authorization = "Bearer $token" }
    if ($hogar) { $cab['X-Hogar-Id'] = $hogar }
    $p = @{ Method = $metodo; Uri = "$CoreUrl$ruta"; Headers = $cab; ContentType = 'application/json' }
    if ($cuerpo) { $p.Body = ($cuerpo | ConvertTo-Json -Depth 5) }
    try {
        Invoke-RestMethod @p
    } catch {
        $codigo = $_.Exception.Response.StatusCode.value__
        Write-Host "   HTTP $codigo en $metodo $ruta" -ForegroundColor Red
        if ($_.ErrorDetails.Message) { Write-Host "   $($_.ErrorDetails.Message)" -ForegroundColor Red }
        $motivo = $_.Exception.Response.Headers['WWW-Authenticate']
        if ($motivo) { Write-Host "   Motivo del servidor (WWW-Authenticate): $motivo" -ForegroundColor Yellow }
        throw
    }
}

Paso "1/5 Salud de Core.Api ($CoreUrl)"
Invoke-RestMethod "$CoreUrl/health" | ConvertTo-Json -Compress

Paso "2/5 Login en Supabase ($SupabaseUrl)"
$clave = Read-Host "Contraseña de $Email" -AsSecureString
$claveTxt = [System.Net.NetworkCredential]::new('', $clave).Password
try {
    $sesion = Invoke-RestMethod -Method Post -Uri "$SupabaseUrl/auth/v1/token?grant_type=password" `
        -Headers @{ apikey = $AnonKey } -ContentType 'application/json' `
        -Body (@{ email = $Email; password = $claveTxt } | ConvertTo-Json)
} catch {
    Write-Host "   Login fallido: $($_.ErrorDetails.Message)" -ForegroundColor Red
    throw
} finally {
    $claveTxt = $null
}
$token = $sesion.access_token
Write-Host "   OK. Usuario: $($sesion.user.id)  (el token caduca en $($sesion.expires_in) s; no se muestra)"

# Diagnóstico: cabecera y claims del token (sin la firma) para comparar con la configuración de Core.
function DecodificarJwt($parte) {
    $b = $parte.Replace('-', '+').Replace('_', '/')
    switch ($b.Length % 4) { 2 { $b += '==' } 3 { $b += '=' } }
    [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b)) | ConvertFrom-Json
}
$partes = $token.Split('.')
$cabecera = DecodificarJwt $partes[0]
$claims = DecodificarJwt $partes[1]
Write-Host "   Token: alg=$($cabecera.alg) kid=$($cabecera.kid) iss=$($claims.iss) aud=$($claims.aud)" -ForegroundColor DarkGray
Write-Host "   Core espera: iss=$SupabaseUrl/auth/v1 aud=authenticated" -ForegroundColor DarkGray
if ($cabecera.alg -eq 'HS256') {
    Write-Host '   AVISO: el token va firmado con HS256 (secreto legado). Core necesita Supabase__JwtSecret para aceptarlo.' -ForegroundColor Yellow
}

Paso '3/5 GET /api/yo'
$yo = Llamar 'GET' '/api/yo' $token $null $null
$yo | ConvertTo-Json -Depth 5

if ($CrearHogar) {
    Paso "4/5 POST /api/hogares ('$NombreHogar')"
    $nuevo = Llamar 'POST' '/api/hogares' $token $null @{ nombreHogar = $NombreHogar; nombreMiembro = $NombreMiembro }
    $nuevo | ConvertTo-Json
    $HogarId = $nuevo.id
} else {
    Paso '4/5 POST /api/hogares (omitido; usa -CrearHogar para crear uno)'
}

if (-not $HogarId -and $yo.hogares.Count -ge 1) { $HogarId = $yo.hogares[0].id }

if ($HogarId) {
    Paso "5/5 Datos del hogar $HogarId"
    Write-Host '-- Perfiles' -ForegroundColor DarkCyan
    (Llamar 'GET' '/api/perfiles' $token $HogarId $null) | Select-Object nombre, modo | Format-Table -AutoSize
    Write-Host '-- Categorías' -ForegroundColor DarkCyan
    (Llamar 'GET' '/api/categorias' $token $HogarId $null) | Select-Object nombre | Format-Table -AutoSize
    Write-Host '-- Miembros' -ForegroundColor DarkCyan
    (Llamar 'GET' '/api/miembros' $token $HogarId $null) | Select-Object nombre, tipo, rol, vinculado | Format-Table -AutoSize
} else {
    Paso '5/5 Sin hogares todavía: ejecuta de nuevo con -CrearHogar'
}

Write-Host "`nPrueba terminada." -ForegroundColor Green
