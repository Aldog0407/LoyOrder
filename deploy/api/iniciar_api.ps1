<#
LoyOrder - Equipo de la API (actividad 7.1.7, despliegue temprano)

Compila la API en modo Release y la arranca escuchando en todas las interfaces, conectada a la base de datos
que está en OTRO equipo de la VPN Tailscale.

Uso (PowerShell, desde la raíz del repo):
  powershell -ExecutionPolicy Bypass -File deploy\api\iniciar_api.ps1 -IpBd 100.x.y.z -AbrirFirewall

  -IpBd           IP de Tailscale del equipo de la base de datos (la imprime preparar_bd.ps1).
  -AbrirFirewall  Abre el puerto 5080 solo para la red de Tailscale. Requiere PowerShell como administrador;
                  basta con usarlo la primera vez.
  -SinCompilar    Arranca la versión ya publicada en publish\api sin volver a compilar.

La contraseña de loyorder_app_user se pide en pantalla. Después se pide, de forma opcional, el token de la cuenta de
PRUEBA de Loyverse (actividad 7.2.3): con él la API sincroniza el catálogo y el inventario cada pocos minutos. Si se
deja vacío, la API arranca igual, sin sincronización automática. Nunca uses el token de la cuenta real del restaurante. La clave para firmar los tokens se genera al arrancar
y vive solo en memoria: al reiniciar la API hay que volver a iniciar sesión (las sesiones ya eran en memoria).
Deja esta ventana abierta mientras se use el sistema; Ctrl+C detiene la API.
#>
param(
    [Parameter(Mandatory = $true)][string]$IpBd,
    [int]$Puerto = 5080,
    [string]$RedVpn = "100.64.0.0/10",
    [switch]$AbrirFirewall,
    [switch]$SinCompilar
)
$ErrorActionPreference = "Stop"
$raiz = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$salida = Join-Path $raiz "publish\api"

if ($AbrirFirewall) {
    $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw "Para -AbrirFirewall abre PowerShell como administrador." }
    if (-not (Get-NetFirewallRule -DisplayName "LoyOrder API $Puerto (Tailscale)" -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName "LoyOrder API $Puerto (Tailscale)" -Direction Inbound -Protocol TCP `
            -LocalPort $Puerto -RemoteAddress $RedVpn -Action Allow -Profile Any | Out-Null
    }
    Write-Host "Firewall: puerto $Puerto abierto solo para $RedVpn"
}

Write-Host "Probando la conexión con la base de datos en ${IpBd}:5432..."
if (-not (Test-NetConnection -ComputerName $IpBd -Port 5432 -InformationLevel Quiet -WarningAction SilentlyContinue)) {
    throw "No hay conexión con ${IpBd}:5432. Revisa que Tailscale esté conectado en los dos equipos y que preparar_bd.ps1 haya terminado."
}

if (-not $SinCompilar) {
    Write-Host "Compilando la API (Release)..."
    dotnet publish (Join-Path $raiz "backend\presentation\LoyOrder.Api") -c Release -o $salida --nologo
    if ($LASTEXITCODE -ne 0) { throw "La compilación falló." }
}

$s = Read-Host "Contraseña de loyorder_app_user" -AsSecureString
$clave = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))

$t = Read-Host "Token de Loyverse de la cuenta de PRUEBA (Enter para omitir)" -AsSecureString
$tokenLoyverse = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($t))

$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://0.0.0.0:$Puerto"
$env:ConnectionStrings__LoyOrder = "Host=$IpBd;Port=5432;Database=loyorder;Username=loyorder_app_user;Password=$clave;Timeout=5;Command Timeout=10"
$env:Auth__SigningKey = [Convert]::ToBase64String($bytes)
if ($tokenLoyverse) {
    $env:Loyverse__Token = $tokenLoyverse
    Write-Host "Sincronización con Loyverse: activa (cuenta de prueba)."
} else {
    Remove-Item Env:\Loyverse__Token -ErrorAction SilentlyContinue
    Write-Host "Sincronización con Loyverse: desactivada (no se escribió token)."
}

$ip = $null
if (Get-Command tailscale -ErrorAction SilentlyContinue) { $ip = (tailscale ip -4 | Select-Object -First 1) }
Write-Host "`nAPI en http://${ip}:$Puerto  (pásale esta IP al equipo del frontend: -IpApi $ip)"
Write-Host "Comprobación: http://${ip}:$Puerto/health/ready debe responder estado = Disponible`n"
Set-Location $salida
dotnet LoyOrder.Api.dll
