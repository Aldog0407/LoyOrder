<#
LoyOrder - Verificación del despliegue en 3 equipos (actividad 7.1.7)

Se corre desde cualquier equipo conectado a la VPN Tailscale y revisa que cada capa responda:
  powershell -ExecutionPolicy Bypass -File deploy\verificar.ps1 -IpBd 100.a.b.c -IpApi 100.d.e.f -IpFront 100.g.h.i
#>
param(
    [Parameter(Mandatory = $true)][string]$IpBd,
    [Parameter(Mandatory = $true)][string]$IpApi,
    [Parameter(Mandatory = $true)][string]$IpFront,
    [int]$PuertoFront = 8080
)
$resultados = @()
function Revisar([string]$capa, [string]$prueba, [scriptblock]$accion) {
    try { $detalle = & $accion; $ok = $true } catch { $detalle = $_.Exception.Message; $ok = $false }
    $script:resultados += [pscustomobject]@{ Capa = $capa; Prueba = $prueba; Resultado = $(if ($ok) { "OK" } else { "FALLA" }); Detalle = $detalle }
}
function Puerto([string]$ip, [int]$p) {
    if (-not (Test-NetConnection -ComputerName $ip -Port $p -InformationLevel Quiet -WarningAction SilentlyContinue)) { throw "Puerto $p cerrado" }
    "Puerto $p abierto"
}

Revisar "BD"       "PostgreSQL en ${IpBd}:5432"           { Puerto $IpBd 5432 }
Revisar "API"      "API en ${IpApi}:5080"                 { Puerto $IpApi 5080 }
Revisar "API"      "GET /health/live"                     { (Invoke-RestMethod "http://${IpApi}:5080/health/live" -TimeoutSec 5).estado }
Revisar "API + BD" "GET /health/ready (la API llega a la BD)" { (Invoke-RestMethod "http://${IpApi}:5080/health/ready" -TimeoutSec 10).estado }
Revisar "Frontend" "GET /login (Nginx sirve React)"       {
    $r = Invoke-WebRequest "http://${IpFront}:$PuertoFront/login" -UseBasicParsing -TimeoutSec 5
    if ($r.Content -notmatch "LoyOrder") { throw "La página no parece ser LoyOrder" }; "HTTP $($r.StatusCode)"
}
Revisar "Frontend + API" "GET /health/ready a través de Nginx" { (Invoke-RestMethod "http://${IpFront}:$PuertoFront/health/ready" -TimeoutSec 10).estado }
Revisar "Seguridad" "GET /api/v1/categorias sin sesión responde 401" {
    $codigo = 0
    try {
        Invoke-WebRequest "http://${IpFront}:$PuertoFront/api/v1/categorias?idRestaurante=1&pagina=1&tamano=5" -UseBasicParsing -TimeoutSec 5 | Out-Null
        $codigo = 200
    } catch {
        if ($_.Exception.Response) { $codigo = [int]$_.Exception.Response.StatusCode } else { throw }
    }
    if ($codigo -ne 401) { throw "Respondió $codigo; debía pedir sesión (401)" }
    "HTTP 401"
}

$resultados | Format-Table -AutoSize
$fallas = @($resultados | Where-Object Resultado -eq "FALLA").Count
if ($fallas -eq 0) { Write-Host "Las 3 capas responden en equipos distintos." -ForegroundColor Green }
else { Write-Host "$fallas prueba(s) fallaron. Revisa la sección 'Si algo falla' de docs\DESPLIEGUE.md." -ForegroundColor Yellow }
