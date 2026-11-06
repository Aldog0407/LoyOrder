# LoyOrder — Frontend oficial base (React + Vite)

Base modular para el proyecto de pedidos de restaurante. Incluye flujos de cliente y panel administrativo con datos simulados. **No procesa pagos, no se conecta aún a Loyverse ni a hardware POS.**

## Requisitos

- Node.js **20.19+ o 22.12+** (requisito de Vite 7; se recomienda la versión LTS vigente compatible).
- npm.

## Instalación local

```bash
cd frontend
npm ci
npm run dev
```

Abre `http://localhost:5173/`. Los enlaces internos utilizan React Router; Vite resuelve las rutas en desarrollo. Para producción, configura el hosting para regresar `index.html` en rutas SPA como `/admin/configuracion`.

Comprobaciones:

```bash
npm run lint
npm test
npm run build
npm run preview
```

La carpeta `dist/` es el artefacto compilado y no debe versionarse. El equipo trabaja directamente en `main`. Revisar `git diff` antes de confirmar y combinar cambios recientes del equipo antes del push.

## Estructura

```text
frontend/
├── index.html
├── package.json
├── package-lock.json          # después de instalar
├── vite.config.js
├── eslint.config.js
├── public/                    # estáticos públicos sin importar
└── src/
    ├── main.jsx               # React root y proveedores
    ├── App.jsx                # rutas y shell
    ├── assets/                # SVG, imágenes importadas
    ├── components/            # UI reutilizable
    │   ├── Navbar.jsx
    │   ├── MenuCard.jsx
    │   ├── CartDrawer.jsx
    │   ├── AdminConfig.jsx
    │   ├── DishDetail.jsx
    │   ├── LocationPicker.jsx
    │   ├── RatingModal.jsx
    │   └── Modal.jsx
    ├── views/                 # pantallas vinculadas a rutas
    │   ├── MenuView.jsx
    │   ├── TrackingView.jsx
    │   ├── HistoryView.jsx
    │   └── AdminDashboard.jsx
    ├── state/StoreContext.jsx # estado UI, carrito y pedidos
    ├── services/loyverseAdapter.js
    ├── config/defaults.js
    ├── data/menu.js
    ├── utils/restaurant.js
    └── styles/styles.css      # tokens :root y componentes
```

## Rutas

| Ruta | Vista |
| --- | --- |
| `/login` | Inicio de sesión de los tres roles |
| `/registro` | Registro público de clientes |
| `/operacion` | Acceso confirmado del operador |
| `/menu` | Categorías, disponibilidad, modal con variantes |
| `/seguimiento` | Timeline y calificación |
| `/historial` | Historial y reordenar |
| `/admin` | Indicadores y pedidos |
| `/admin/configuracion` | Horario, envío, pago y caja |

## Convenciones para el equipo

1. **Vistas** en `views/`: ensamblan componentes, leen la ruta, no definen tokens.
2. **Componentes** en `components/`: interfaces con `props` claros y sin acceso directo a `localStorage`.
3. **Estado** en `state/`: Context + reducer sirve para este prototipo. Si el dominio crece, separar `cart`, `auth`, `orders` y `restaurantConfig` en módulos o usar herramientas específicas.
4. **Integraciones** en `services/`: toda llamada a Loyverse debe pasar por un backend. No colocar API keys en variables `VITE_*`, pues se publican en el bundle del cliente.
5. **Reglas** en `utils/`: cálculos y validaciones puros con pruebas unitarias cuando se incorpore un test runner.
6. **Tokens** en `styles/styles.css`: mantener colores, tipografía, radios, estados y sombras. Evitar duplicar estilos por componente.
7. **Rutas protegidas**: `ProtectedRoute` restringe cliente, operador y administrador, sin herencia de roles. `/admin` y `/admin/configuracion` requieren administrador. La API aplica la autorización de sus endpoints; una restricción React no sustituye la comprobación del servidor.

## Comportamiento de demostración

- Carrito, métodos de entrega, localización esquemática, pagos simulados, órdenes y configuración persistidos en `localStorage`.
- El horario abierto/cerrado usa la **zona horaria del navegador**; en producción debe evaluarse usando la zona horaria del restaurante.
- El costo por km tiene una función pura preparada, pero sin distancia real solo cobra la tarifa base.
- Las etiquetas `CASH`, `CARD` y `OTHER` son referencias conceptuales, **no** IDs de pago oficiales de un comercio Loyverse.
- La impresora se conecta y responde **solo en simulación**. El estado no corresponde a hardware real.
- El cambio de estado de la impresora se ensaya en el formulario; se persiste con «Guardar configuración» (y se reinicia como desconectada al recargar la app).
- El dashboard usa datos demo; el selector de periodo no ejecuta consultas temporales.
- Se incluyen cantidades, variantes requeridas, disponibilidad, confirmación de compra, seguimiento y reordenación. No hay correo, procesador de pagos o sincronización POS.

## Próximas iteraciones recomendadas

1. Extender el cliente HTTP existente en `src/auth.js` cuando se integren nuevos recursos de la API.
2. La autenticación ya está integrada mediante `AuthProvider`, `AccessView` y `ProtectedRoute`; conservar su conexión a la API al añadir funciones.
3. `src/features/orders/`: estado de órdenes desde servidor y actualizaciones en tiempo real.
4. `src/features/pos/`: sincronización Loyverse server-side, catálogo y métodos por tienda.
5. Ampliar las pruebas existentes (`npm test`) al integrar los recursos de pedidos y sincronización.

## Nota de producción

Este entregable corresponde únicamente al **proyecto base funcional del frontend**. La interfaz no constituye un sistema de pedidos operativo y los datos del navegador pueden borrarse en cualquier momento.


## Acceso 7.2.1

Se adaptaron a React /login, /registro y /operacion. Consultar `../docs/ACTIVIDAD_7_2_1.md`. El acceso ya usa la API; las funcionalidades de pedidos y los datos de negocio continúan simulados. La API debe ejecutarse en localhost:5080 y tener conexión a PostgreSQL para registrar o autenticar cuentas reales.


## Autenticación integrada a React

- `src/state/AuthProvider.jsx` comparte la identidad de la sesión y recibe su caducidad.
- `src/views/AccessView.jsx` contiene registro y login, con errores de API y validación de contraseña.
- `src/components/ProtectedRoute.jsx` y `src/utils/auth-routing.js` aplican las rutas por rol.
- `src/auth.js` reutiliza los endpoints de la API ya existentes y mantiene el token solo en memoria.
- `Navbar` incorpora logout y enlaces por rol; no permite cambiar libremente a administrador.
- El carrito, historial y dirección de demostración se limpian al iniciar/cerrar/caducar una sesión; la configuración local del restaurante se conserva. Esto no sustituye la persistencia futura por cliente en la API.

Para usar PostgreSQL real, encender primero la API en `http://localhost:5080` con la conexión configurada según `../backend/README.md`. Después, desde `frontend`, ejecutar `npm ci` (primera vez) y `npm run dev`. Abrir `http://localhost:5173/login`. Ya no se usa `python frontend/dev_server.py`.

Opcional: copiar `.env.example` a `.env.local` para configurar `VITE_API_URL` antes de arrancar Vite. La URL no es secreta; nunca colocar contraseñas, claves de firma o tokens en variables `VITE_*`. El valor predeterminado es `http://localhost:5080/api/v1`.

Al recargar la página hay que iniciar sesión nuevamente porque el token no se persiste. Las cuentas reales existentes no cambian. Dashboard, pedidos, impresión y ajustes siguen con el alcance de demostración del equipo.

Comprobado en la adaptación: `npm run build`, `npm run lint` y 11 pruebas `npm test` (8 de acceso y 3 previas de restaurante). Se comprobó además en navegador contra una instancia temporal de la misma API con repositorios falsos: registro, contraseña sin símbolo rechazada, login de los tres roles, logout, historial limpio y acceso administrativo/configuración. Falta repetir la interfaz React con las cuentas reales de la base local de Aldo; la integración anterior con PostgreSQL ya se había verificado antes de la migración.
