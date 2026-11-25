<#
LoyOrder - Equipo de base de datos (actividad 7.1.7, despliegue temprano)

Prepara PostgreSQL para que la API, desde OTRO equipo de la VPN Tailscale, se conecte a la base loyorder:
  1. Crea la base loyorder y corre el esquema y los objetos del equipo (solo si la base no existe).
  2. Corre las migraciones de autenticación por correo y de sincronización (son seguras de repetir).
  3. Pone la contraseña de loyorder_app_user (el usuario con privilegios mínimos que usa la API).
  4. Abre PostgreSQL solo a la red de Tailscale (100.64.0.0/10): listen_addresses, pg_hba.conf y firewall.

Uso (PowerShell COMO ADMINISTRADOR, desde la raíz del repo):
  powershell -ExecutionPolicy Bypass -File deploy\bd\preparar_bd.ps1 -DatosSemilla

-DatosSemilla carga database\scripts\LoyOrderDatosSemilla.sql (datos de demostración de Gabriel) cuando la base
se acaba de crear. Ese script solo acepta una base sin usuarios, pedidos ni restaurante.
Si la base ya existe (por ejemplo, porque ya tiene la semilla), el script no toca sus datos.
Las contraseñas se piden en pantalla; no se guardan en ningún archivo.
#>
#Requires -RunAsAdministrator
param(
    [string]$Psql = "",
    [string]$RedVpn = "100.64.0.0/10",
    [switch]$DatosSemilla
)
$ErrorActionPreference = "Stop"
$raiz = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$scripts = Join-Path $raiz "database\scripts"

function Texto([Security.SecureString]$s) {
    [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}

if (-not $Psql) {
    $encontrado = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($encontrado) { $Psql = $encontrado.FullName }
}
if (-not $Psql -or -not (Test-Path $Psql)) { throw "No encontré psql.exe. Pasa la ruta con -Psql 'C:\Program Files\PostgreSQL\18\bin\psql.exe'." }
Write-Host "psql: $Psql"

$env:PGCLIENTENCODING = "UTF8"
$env:PGPASSWORD = Texto (Read-Host "Contraseña del usuario postgres" -AsSecureString)

function Consulta([string]$db, [string]$sql) {
    $r = & $Psql -U postgres -h localhost -d $db -v ON_ERROR_STOP=1 -tAc $sql
    if ($LASTEXITCODE -ne 0) { throw "Falló la consulta: $sql" }
    return $r
}
function CorrerArchivo([string]$nombre) {
    Write-Host "  -> $nombre"
    & $Psql -U postgres -h localhost -d loyorder -v ON_ERROR_STOP=1 -q -f (Join-Path $scripts $nombre)
    if ($LASTEXITCODE -ne 0) { throw "Falló el script $nombre" }
}

# 1. Base y esquema
Write-Host "`n[1/4] Base de datos"
$nueva = $false
if ((Consulta "postgres" "SELECT 1 FROM pg_database WHERE datname='loyorder'") -ne "1") {
    Consulta "postgres" "CREATE DATABASE loyorder" | Out-Null
    CorrerArchivo "LoyOrderEsquema.sql"
    CorrerArchivo "LoyOrderObjetos_y_Permisos.sql"
    $nueva = $true
} else {
    Write-Host "  La base loyorder ya existe; no se vuelven a correr el esquema ni los objetos, y sus datos no se tocan."
}
CorrerArchivo "LoyOrderAutenticacionCorreo.sql"   # usa IF NOT EXISTS: se puede repetir
CorrerArchivo "LoyOrderSincronizacion.sql"         # 7.2.3: también se puede repetir
if ($DatosSemilla) {
    if (-not (Test-Path (Join-Path $scripts "LoyOrderDatosSemilla.sql"))) {
        Write-Warning "No está database\scripts\LoyOrderDatosSemilla.sql en el repo; se continúa sin datos de demostración."
    } elseif ($nueva) {
        CorrerArchivo "LoyOrderDatosSemilla.sql"
    } else {
        Write-Host "  La semilla solo se carga en una base recién creada; se omite."
    }
}
Write-Host ("  Tablas: " + (Consulta "loyorder" "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'"))

# 2. Usuario de la aplicación
Write-Host "`n[2/4] Contraseña de loyorder_app_user (la misma que se escribirá en el equipo de la API)"
$appPass = Texto (Read-Host "Nueva contraseña para loyorder_app_user (sin ; ni comillas)" -AsSecureString)
if ($appPass.Length -lt 8 -or $appPass -match "[;'`"]") { throw "Usa al menos 8 caracteres y no uses ; ni comillas." }
# Se manda por la entrada estándar para que psql sustituya la variable con el escape correcto
"ALTER ROLE loyorder_app_user PASSWORD :'clave';" | & $Psql -U postgres -h localhost -d loyorder -v ON_ERROR_STOP=1 -q -v "clave=$appPass"
if ($LASTEXITCODE -ne 0) { throw "No se pudo cambiar la contraseña de loyorder_app_user." }


# 3. Acceso remoto solo desde la VPN
Write-Host "`n[3/4] Acceso remoto desde la red de Tailscale ($RedVpn)"
Consulta "postgres" "ALTER SYSTEM SET listen_addresses = '*'" | Out-Null
$hba = Consulta "postgres" "SHOW hba_file"
$regla = "host    loyorder    loyorder_app_user    $RedVpn    scram-sha-256"
if (-not (Select-String -Path $hba -SimpleMatch $regla -Quiet)) {
    Copy-Item $hba "$hba.antes_loyorder" -Force
    Add-Content -Path $hba -Value "`r`n# LoyOrder: la API entra solo por la VPN`r`n$regla" -Encoding Ascii
    Write-Host "  Regla agregada a $hba (respaldo: pg_hba.conf.antes_loyorder)"
} else { Write-Host "  La regla ya estaba en pg_hba.conf" }

if (-not (Get-NetFirewallRule -DisplayName "LoyOrder BD 5432 (Tailscale)" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "LoyOrder BD 5432 (Tailscale)" -Direction Inbound -Protocol TCP -LocalPort 5432 `
        -RemoteAddress $RedVpn -Action Allow -Profile Any | Out-Null
    Write-Host "  Firewall: puerto 5432 abierto solo para $RedVpn"
}

# 4. Reinicio para aplicar listen_addresses
Write-Host "`n[4/4] Reiniciando PostgreSQL"
Get-Service -Name "postgresql*" | Restart-Service
Start-Sleep -Seconds 3
Remove-Item Env:\PGPASSWORD

$ip = $null
if (Get-Command tailscale -ErrorAction SilentlyContinue) { $ip = (tailscale ip -4 | Select-Object -First 1) }
Write-Host "`nListo. IP de este equipo en Tailscale: $ip"
Write-Host "Pásale esa IP al equipo de la API (parámetro -IpBd)."
