<#
.SYNOPSIS
  Compila la APK de Android (Trusted Web Activity de https://miparte.pages.dev) y la firma.

.DESCRIPTION
  Compila el proyecto de android/ con Gradle, alinea la APK y la firma con android/miparte.keystore.
  Si el almacén de claves no existe lo crea con keytool, que pide la contraseña y los datos del
  certificado. Las contraseñas se teclean siempre en la consola: el script no las recibe ni las guarda.
  Al terminar escribe la huella SHA-256 del certificado en src/Web/wwwroot/.well-known/assetlinks.json;
  sin esa huella publicada en la web, la app se abre con la barra de direcciones del navegador.

  Guarda una copia de android/miparte.keystore y de su contraseña fuera del equipo: sin ellas no se
  puede publicar una actualización de la app ya instalada.

.EXAMPLE
  ./scripts/compilar-apk.ps1
.EXAMPLE
  ./scripts/compilar-apk.ps1 -Depuracion     # APK de depuración, sin clave propia (muestra la barra de direcciones)
#>
param(
    [switch] $Depuracion,
    [string] $Jdk = $(if ($env:JAVA_HOME) { $env:JAVA_HOME } else { 'C:\Program Files\Android\openjdk\jdk-21.0.8' }),
    [string] $Sdk = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { 'C:\Program Files (x86)\Android\android-sdk' }),
    [string] $BuildTools = '36.0.0'
)

$ErrorActionPreference = 'Stop'

$raiz = Resolve-Path (Join-Path $PSScriptRoot '..')
$android = Join-Path $raiz 'android'
$almacen = Join-Path $android 'miparte.keystore'
$alias = 'miparte'
$salida = Join-Path $android 'salida'
$herramientas = Join-Path $Sdk "build-tools\$BuildTools"

foreach ($ruta in @($Jdk, $Sdk, $herramientas)) {
    if (-not (Test-Path $ruta)) { Write-Host "No existe $ruta. Indica -Jdk, -Sdk o -BuildTools." -ForegroundColor Red; exit 1 }
}
$env:JAVA_HOME = $Jdk
$env:ANDROID_HOME = $Sdk

function Ejecutar([string] $programa, [string[]] $argumentos) {
    & $programa @argumentos
    if ($LASTEXITCODE -ne 0) { Write-Host "Falló: $programa" -ForegroundColor Red; exit 1 }
}

New-Item -ItemType Directory -Force $salida | Out-Null
Push-Location $android
try {
    if ($Depuracion) {
        Ejecutar '.\gradlew.bat' @('assembleDebug', '--console=plain', '-q')
        Copy-Item 'app\build\outputs\apk\debug\app-debug.apk' (Join-Path $salida 'miparte-depuracion.apk') -Force
        Write-Host "APK de depuración: $(Join-Path $salida 'miparte-depuracion.apk')" -ForegroundColor Green
        exit 0
    }

    if (-not (Test-Path $almacen)) {
        Write-Host 'No hay almacén de claves: se crea ahora. Elige una contraseña y guárdala bien.' -ForegroundColor Yellow
        Ejecutar (Join-Path $Jdk 'bin\keytool.exe') @('-genkeypair', '-v', '-keystore', $almacen, '-alias', $alias,
            '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000', '-storetype', 'PKCS12')
    }

    Ejecutar '.\gradlew.bat' @('assembleRelease', '--console=plain', '-q')
    $sinFirmar = 'app\build\outputs\apk\release\app-release-unsigned.apk'
    $alineada = Join-Path $salida 'miparte-alineada.apk'
    $apk = Join-Path $salida 'miparte.apk'
    Ejecutar (Join-Path $herramientas 'zipalign.exe') @('-f', '-p', '4', $sinFirmar, $alineada)
    Write-Host 'Firma de la APK (apksigner pide la contraseña del almacén):' -ForegroundColor Yellow
    Ejecutar (Join-Path $herramientas 'apksigner.bat') @('sign', '--ks', $almacen, '--ks-key-alias', $alias, '--out', $apk, $alineada)
    Remove-Item $alineada -Force

    # La huella sale de la APK ya firmada, así que no hace falta volver a pedir la contraseña.
    $certificado = & (Join-Path $herramientas 'apksigner.bat') verify --print-certs $apk
    if ($LASTEXITCODE -ne 0) { Write-Host 'La APK firmada no supera la verificación.' -ForegroundColor Red; exit 1 }
    $linea = $certificado | Where-Object { $_ -match 'SHA-256 digest:\s*([0-9a-fA-F]{64})' } | Select-Object -First 1
    if (-not $linea) { Write-Host 'No se pudo leer la huella SHA-256 de la APK.' -ForegroundColor Red; exit 1 }
    $null = $linea -match 'SHA-256 digest:\s*([0-9a-fA-F]{64})'
    $huella = (($Matches[1].ToUpper() -split '(..)') | Where-Object { $_ }) -join ':'
}
finally { Pop-Location }

$enlaces = Join-Path $raiz 'src\Web\wwwroot\.well-known\assetlinks.json'
$anterior = if (Test-Path $enlaces) { [IO.File]::ReadAllText($enlaces) } else { '' }
# Se conservan las huellas que ya hubiera (p. ej. la de Google Play, que vuelve a firmar la app): el fichero solo
# declara este paquete, así que todas las huellas que contiene son suyas.
$huellas = @(@([regex]::Matches($anterior, '(?:[0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}') | ForEach-Object { $_.Value.ToUpper() }) + $huella |
    Select-Object -Unique)
$lista = ($huellas | ForEach-Object { "`"$_`"" }) -join ', '
$json = @"
[
  {
    "relation": ["delegate_permission/common.handle_all_urls"],
    "target": {
      "namespace": "android_app",
      "package_name": "dev.pages.miparte.twa",
      "sha256_cert_fingerprints": [$lista]
    }
  }
]
"@
# Sin BOM y con saltos de línea de Unix, como el resto de wwwroot.
[IO.File]::WriteAllText($enlaces, ($json -replace "`r`n", "`n") + "`n", (New-Object Text.UTF8Encoding $false))

Write-Host "APK firmada: $apk" -ForegroundColor Green
Write-Host "Huella SHA-256: $huella"
if ($anterior -notmatch [regex]::Escape($huella)) {
    Write-Host 'assetlinks.json ha cambiado: súbelo a master y espera al despliegue de la web antes de probar la app.' -ForegroundColor Yellow
}
