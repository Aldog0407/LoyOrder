# LoyOrder — Frontend oficial base (React + Vite)

Base modular para el proyecto de pedidos de restaurante. Incluye flujos de cliente y panel administrativo con datos simulados. **No procesa pagos, no se conecta aún a Loyverse ni a hardware POS.**

## Requisitos

- Node.js **20.19+ o 22.12+** (requisito de Vite 7; se recomienda la versión LTS vigente compatible).
- npm.

## Instalación local

```bash
git checkout develop
cd frontend
npm install
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

La carpeta `dist/` es el artefacto compilado y no debe versionarse. Este ZIP se entrega como una carpeta completa; copia su contenido a la carpeta `frontend/` de tu repositorio, revisa `git status` y confirma sobre `develop`. **No sobrescribas modificaciones existentes sin compararlas.**

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
7. **Rutas protegidas**: agregar autenticación, roles y autorización del servidor antes de permitir acceso administrativo real. En esta demo `/admin` es pública deliberadamente.

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

1. `src/services/apiClient.js`: cliente HTTP centralizado, errores y manejo de sesión.
2. `src/features/auth/`: roles, login y `ProtectedRoute`, con verificación en API.
3. `src/features/orders/`: estado de órdenes desde servidor y actualizaciones en tiempo real.
4. `src/features/pos/`: sincronización Loyverse server-side, catálogo y métodos por tienda.
5. Pruebas unitarias con Vitest y pruebas E2E con Playwright (carrito, checkout, reordenación, permisos).

## Nota sobre despliegue

Vite genera una SPA. En el hosting, las solicitudes a rutas cliente deben redirigirse a `index.html`. Esto no reemplaza autorización del backend. Para una API futura, usa variables de entorno de servidor y un proxy apropiado, nunca credenciales secretas expuestas al navegador.
