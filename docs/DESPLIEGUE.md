# Despliegue temprano en 3 equipos (actividad 7.1.7)

**Objetivo:** que LoyOrder corra repartido en **3 equipos distintos**: frontend, API y base de datos. Así lo pide la rúbrica en "Uso de servidores": "corre distribuido en al menos 3 equipos (Frontend, Backend y Base de datos)… mediante redes locales o mediante una VPN entre los equipos personales".

**Conexión:** los 3 equipos se conectan con una **VPN Tailscale**, así que cada integrante puede estar en su casa. Tailscale da a cada equipo una IP fija del rango `100.64.0.0/10`. Los scripts abren los puertos **solo para ese rango**: nadie fuera de la VPN puede entrar a la BD ni a la API.

Responde al diagrama de despliegue de la actividad 3.4.

## Arquitectura

```
Navegador (cualquier equipo de la VPN)
   │  http://IP_FRONT:8080
   ▼
Equipo 1 · FRONTEND ── Nginx: sirve la compilación de React y reenvía /api y /health
   │  http://IP_API:5080
   ▼
Equipo 2 · API ─────── ASP.NET Core (.NET 10) en modo Production
   │  PostgreSQL 5432 con el usuario loyorder_app_user (privilegios mínimos)
   ▼
Equipo 3 · BD ──────── PostgreSQL con la base loyorder (22 tablas, vistas, funciones y procedimientos)
```

| Equipo | Capa | Responsable | Software | Puerto abierto (solo VPN) |
|---|---|---|---|---|
| 1 | Frontend (React + Nginx) | [nombre] | Node 20.19+ o 22.12+, Nginx para Windows | 8080 |
| 2 | API REST (ASP.NET Core) | [nombre] | .NET SDK 10 | 5080 |
| 3 | Base de datos (PostgreSQL) | [nombre] | PostgreSQL 17 o 18 | 5432 |

Los 3 necesitan Git, el repo actualizado (`git pull origin main`) y Tailscale.

**Por qué Nginx reenvía la API:** el navegador solo habla con `http://IP_FRONT:8080`. React llama a `/api/v1/...` en ese mismo origen y Nginx lo pasa a la API. Así no hace falta abrir CORS para otras direcciones y la API no queda expuesta a los navegadores.

---

## Paso 0. VPN con Tailscale (los 3 equipos)

1. **Un integrante** crea la red en https://login.tailscale.com con su cuenta de Google o GitHub. El plan gratuito *Personal* admite hasta 6 usuarios.
2. Ese integrante invita a los otros 2 desde la consola de administración: **Users → Invite users**, con su correo.
3. **Cada integrante** instala Tailscale para Windows (https://tailscale.com/download/windows), inicia sesión con la cuenta invitada y deja el ícono en "Connected".
4. En **Machines** de la consola deben aparecer los 3 equipos. La IP de cada uno se ve ahí o con:
   ```powershell
   tailscale ip -4
   ```
5. Anoten las 3 IPs:

   | Capa | IP de Tailscale |
   |---|---|
   | BD | 100.___.___.___ |
   | API | 100.___.___.___ |
   | Frontend | 100.___.___.___ |

Windows bloquea el `ping` por defecto. Para probar la conexión usen los scripts, que revisan los puertos con `Test-NetConnection`.

---

## Paso 1. Equipo 3: base de datos

PowerShell **como administrador**, en la raíz del repo:
```powershell
powershell -ExecutionPolicy Bypass -File deploy\bd\preparar_bd.ps1 -DatosSemilla
```

El script hace lo siguiente:
- Pide la contraseña de `postgres` y una **contraseña nueva para `loyorder_app_user`**. Esa contraseña se le pasa en persona al responsable de la API; no se escribe en ningún archivo ni en el chat del equipo.
- Crea la base `loyorder` y corre `LoyOrderEsquema.sql`, `LoyOrderObjetos_y_Permisos.sql` y `LoyOrderAutenticacionCorreo.sql`. Si la base ya existe, solo corre la migración, que se puede repetir sin problema, y **no toca sus datos**.
- Con `-DatosSemilla`, si la base se acaba de crear, carga `LoyOrderDatosSemilla.sql`: el restaurante 9001, clientes, productos y pedidos de demostración. Ese script solo acepta una base vacía. Si el equipo de la BD ya tiene la base con la semilla cargada, basta con correr el script: solo configura la contraseña y el acceso remoto.
- Configura `listen_addresses = '*'` y agrega a `pg_hba.conf` esta regla (deja respaldo en `pg_hba.conf.antes_loyorder`):
  ```
  host    loyorder    loyorder_app_user    100.64.0.0/10    scram-sha-256
  ```
  Solo `loyorder_app_user` puede entrar desde la VPN, y solo a la base `loyorder`. `postgres` sigue siendo local.
- Abre el puerto 5432 en el firewall **solo para 100.64.0.0/10** y reinicia PostgreSQL.

Al final imprime la IP del equipo, que se le pasa al responsable de la API.

## Paso 2. Equipo 2: API

PowerShell **como administrador** la primera vez, para abrir el firewall:
```powershell
powershell -ExecutionPolicy Bypass -File deploy\api\iniciar_api.ps1 -IpBd 100.x.y.z -AbrirFirewall
```

Las veces siguientes basta una PowerShell normal:
```powershell
powershell -ExecutionPolicy Bypass -File deploy\api\iniciar_api.ps1 -IpBd 100.x.y.z
```
Si no cambió el código, se puede agregar `-SinCompilar` para que arranque más rápido.

El script hace lo siguiente:
- Comprueba que llega a la BD (puerto 5432).
- Compila la API en Release en `publish\api`. Esa carpeta está en `.gitignore`, no se sube.
- Pide la contraseña de `loyorder_app_user`.
- Genera la clave de firma de los JWT (`Auth__SigningKey`) en memoria y arranca en `http://0.0.0.0:5080` con `ASPNETCORE_ENVIRONMENT=Production`.

**La ventana debe quedarse abierta.** Ctrl+C detiene la API.

Comprobación desde el mismo equipo: `http://localhost:5080/health/ready` debe responder `{"estado":"Disponible"}`.

## Paso 3. Equipo 1: frontend

1. Descarga **Nginx para Windows** (versión *Stable*, zip) de https://nginx.org/en/download.html y descomprímelo en `C:\nginx`, de modo que quede `C:\nginx\nginx.exe`.
2. Si usarán el mapa, crea `frontend\.env.local` con el **token público** de Mapbox (`VITE_MAPBOX_ACCESS_TOKEN=pk...`). Si el token tiene restricción de URL, agrega `http://IP_FRONT:8080`.
3. En PowerShell **como administrador**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File deploy\frontend\preparar_frontend.ps1 -IpApi 100.x.y.z
   ```

El script hace lo siguiente:
- Compila React con `VITE_API_URL=/api/v1`.
- Copia `dist` a `C:\nginx\html\loyorder`.
- Genera `C:\nginx\conf\nginx.conf` desde `deploy\frontend\nginx.conf.plantilla`, con las rutas de React y el proxy de `/api` y `/health`. Guarda el original como `nginx.conf.original`.
- Abre el puerto 8080 solo para la VPN y arranca Nginx (o lo recarga si ya estaba corriendo).

Para detener Nginx: `cd C:\nginx; .\nginx.exe -s stop`.

## Paso 4. Verificación (desde cualquier equipo de la VPN)

```powershell
powershell -ExecutionPolicy Bypass -File deploy\verificar.ps1 -IpBd 100.a.b.c -IpApi 100.d.e.f -IpFront 100.g.h.i
```

Las 7 pruebas deben salir **OK**:
- Puerto de la BD abierto.
- Puerto de la API abierto.
- `/health/live`.
- `/health/ready`: la API llega a la BD.
- Nginx sirve `/login`.
- `/health/ready` a través de Nginx.
- `/api/v1/categorias` sin sesión responde **401**.

## Paso 5. Cuentas de prueba

1. Abre `http://IP_FRONT:8080/registro` y crea `cliente@loyorder.test`, `operador@loyorder.test` y `admin@loyorder.test`. La contraseña debe tener de 8 a 128 caracteres y al menos un símbolo.
Las cuentas de la semilla (`@loyorder.invalid`) **no pueden iniciar sesión**; su contraseña es inválida a propósito. Para la demostración se crean cuentas nuevas:

2. En el **equipo de la BD**, convierte 2 de ellas en personal del primer restaurante (el 9001 si se cargó la semilla). Esto se hace por SQL porque la gestión de personal (CU-07) todavía no está programada:
   ```powershell
   $psql = (Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" | Select-Object -Last 1).FullName
   & $psql -U postgres -d loyorder -c "INSERT INTO empleado(id_empleado,id_restaurante,rol) SELECT id_usuario,(SELECT min(id_restaurante) FROM restaurante),'OPERADOR' FROM usuario WHERE correo='operador@loyorder.test'"
   & $psql -U postgres -d loyorder -c "INSERT INTO empleado(id_empleado,id_restaurante,rol) SELECT id_usuario,(SELECT min(id_restaurante) FROM restaurante),'ADMINISTRADOR' FROM usuario WHERE correo='admin@loyorder.test'"
   ```
3. Inicia sesión con cada una. El cliente debe llegar a `/menu`, el operador a `/operacion` y el administrador a `/admin`.

## Evidencias para el documento del equipo

1. Consola de Tailscale, en **Machines**, con los 3 equipos conectados. Tapa los correos.
2. Ventana de la API corriendo con la IP del equipo, y `/health/ready` respondiendo "Disponible".
3. Resultado de `verificar.ps1` con las 7 pruebas en OK.
4. El login abierto en `http://IP_FRONT:8080` desde un **cuarto equipo o celular** conectado a la VPN, con cada rol entrando a su vista.
5. Las reglas de firewall `LoyOrder ... (Tailscale)` con el rango 100.64.0.0/10. Sirven también para el criterio de seguridad.

**Nunca** pongan en capturas ni en el documento las contraseñas, la clave de firma ni el token de Mapbox.

---

## Orden de arranque y apagado

- **Arranque:** BD (PostgreSQL arranca solo con Windows) → API (`iniciar_api.ps1 -SinCompilar`) → Nginx (`C:\nginx\nginx.exe`).
- **Apagado:** Ctrl+C en la API y `nginx.exe -s stop`. PostgreSQL puede quedarse encendido.
- **Si cambia el código:** el de la API vuelve a correr `iniciar_api.ps1` sin `-SinCompilar`; el del frontend vuelve a correr `preparar_frontend.ps1`.

## Si algo falla

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| `iniciar_api.ps1` dice que no hay conexión con :5432 | Tailscale desconectado, PostgreSQL sin reiniciar o firewall | Revisar que los 2 equipos digan "Connected" y que el script de la BD haya terminado; en la BD, `Get-Service postgresql*` debe decir Running |
| `/health/ready` responde 503 | Contraseña de `loyorder_app_user` distinta o falta la migración | Volver a correr `preparar_bd.ps1` y escribir la misma contraseña en la API |
| La API se cierra al arrancar con "Configure Auth__SigningKey" | Se arrancó la DLL sin el script | Arrancar siempre con `iniciar_api.ps1` |
| `http://IP_FRONT:8080` no abre | Nginx detenido o puerto cerrado | `cd C:\nginx; .\nginx.exe -t` y revisar `logs\error.log` |
| La página abre pero el login dice "No hay conexión con el servidor" | La API está apagada o Nginx apunta a otra IP | Revisar que `conf\nginx.conf` tenga la IP de la API y volver a correr `preparar_frontend.ps1 -IpApi ...` |
| Error 502 en `/api/...` | Nginx no llega a la API | Firewall del equipo de la API (correr `-AbrirFirewall` como administrador) |
| El mapa no carga | Falta el token público de Mapbox o tiene restricción de URL | Paso 3.2 |

## Limitaciones conocidas (pendientes para el despliegue completo 7.3.5)

- **Sin HTTPS.** El tráfico va cifrado dentro de la VPN, pero el sitio es `http://`. Para HTTPS se puede usar `tailscale cert` con MagicDNS.
- **La API corre en una ventana, no como servicio.** Si se cierra la ventana o se reinicia el equipo, hay que volver a arrancarla.
- **La clave de firma se genera en cada arranque.** Al reiniciar la API todos deben volver a iniciar sesión; las sesiones ya vivían en memoria.
- **Límite de intentos de acceso compartido.** El límite de 20 intentos por minuto se cuenta por IP. Detrás de Nginx todos los navegadores llegan con la IP del equipo del frontend, así que comparten ese límite. Para corregirlo, la API debe leer `X-Forwarded-For` (`UseForwardedHeaders`); Nginx ya lo envía.
- **Datos de demostración.** El menú, el seguimiento y el dashboard siguen con datos de demostración hasta que estén la sincronización con Loyverse (7.2.3) y el catálogo (7.2.4).
