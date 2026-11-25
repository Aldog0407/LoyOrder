<#
LoyOrder - Equipo del frontend (actividad 7.1.7, despliegue temprano)

Compila React con la API en la ruta relativa /api/v1 y lo publica con Nginx, que reenvía /api y /health
al equipo de la API por la VPN Tailscale. Así el navegador habla con un solo origen y no hace falta CORS.

Requisitos: Node 20.19+ o 22.12+, y Nginx para Windows descomprimido en C:\nginx (https://nginx.org/en/download.html).

Uso (PowerShell COMO ADMINISTRADOR, desde la raíz del repo):
  powershell -ExecutionPolicy Bypass -File deploy\frontend\preparar_frontend.ps1 -IpApi 100.x.y.z

  -IpApi   IP de Tailscale del equipo de la API (la imprime iniciar_api.ps1).
  -Nginx   Carpeta de Nginx (por defecto C:\nginx).
  -Puerto  Puerto donde se publica el sitio (por defecto 8080).

Mapbox: si existe frontend\.env.local con VITE_MAPBOX_ACCESS_TOKEN (token PÚBLICO pk.), Vite lo incluye.
#>
#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)][string]$IpApi,
    [string]$Nginx = "C:\nginx",
    [int]$Puerto = 8080,
    [string]$RedVpn = "100.64.0.0/10"
)
$ErrorActionPreference = "Stop"
$raiz = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not (Test-Path (Join-Path $Nginx "nginx.exe"))) { throw "No encontré nginx.exe en $Nginx. Descomprime Nginx para Windows ahí o usa -Nginx." }

Write-Host "Probando la API en ${IpApi}:5080..."
if (-not (Test-NetConnection -ComputerName $IpApi -Port 5080 -InformationLevel Quiet -WarningAction SilentlyContinue)) {
    Write-Warning "La API no responde todavía en ${IpApi}:5080. Se publica el sitio igual; las llamadas a /api fallarán hasta que la API arranque."
}

# 1. Compilar React con la API en el mismo origen
Push-Location (Join-Path $raiz "frontend")
try {
    $env:VITE_API_URL = "/api/v1"
    npm.cmd ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci falló." }
    npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build falló." }
} finally { Pop-Location; Remove-Item Env:\VITE_API_URL -ErrorAction SilentlyContinue }

# 2. Copiar la compilación y escribir la configuración de Nginx
$destino = Join-Path $Nginx "html\loyorder"
if (Test-Path $destino) { Remove-Item $destino -Recurse -Force }
Copy-Item (Join-Path $raiz "frontend\dist") $destino -Recurse
$conf = (Get-Content (Join-Path $PSScriptRoot "nginx.conf.plantilla") -Raw).Replace("{{IP_API}}", $IpApi).Replace("{{PUERTO}}", "$Puerto")
$confPath = Join-Path $Nginx "conf\nginx.conf"
if ((Test-Path $confPath) -and -not (Test-Path "$confPath.original")) { Copy-Item $confPath "$confPath.original" }
[IO.File]::WriteAllText($confPath, $conf, (New-Object Text.UTF8Encoding $false))

# 3. Firewall solo para la VPN
if (-not (Get-NetFirewallRule -DisplayName "LoyOrder Frontend $Puerto (Tailscale)" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "LoyOrder Frontend $Puerto (Tailscale)" -Direction Inbound -Protocol TCP `
        -LocalPort $Puerto -RemoteAddress $RedVpn -Action Allow -Profile Any | Out-Null
}

# 4. Probar y (re)arrancar Nginx
Push-Location $Nginx
try {
    & .\nginx.exe -t
    if ($LASTEXITCODE -ne 0) { throw "La configuración de Nginx tiene errores." }
    if (Get-Process nginx -ErrorAction SilentlyContinue) { & .\nginx.exe -s reload }
    else { Start-Process -FilePath (Join-Path $Nginx "nginx.exe") -WorkingDirectory $Nginx -WindowStyle Hidden }
} finally { Pop-Location }

$ip = $null
if (Get-Command tailscale -ErrorAction SilentlyContinue) { $ip = (tailscale ip -4 | Select-Object -First 1) }
Write-Host "`nListo. Abre http://${ip}:$Puerto/login desde cualquier equipo de la VPN."
Write-Host "Para detener Nginx: cd $Nginx; .\nginx.exe -s stop"
