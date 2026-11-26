import { peticionAutenticada } from '../auth.js';

export const obtenerOpcionesPedido = () => peticionAutenticada('/pedidos/opciones', null, 'GET');
export const confirmarPedido = solicitud => peticionAutenticada('/pedidos', solicitud);

export const obtenerHistorialPedidos = (pagina = 1) => peticionAutenticada(`/pedidos?pagina=${pagina}&tamano=20`, null, 'GET');

export const obtenerPedidosOperador = (automatica = false) => peticionAutenticada('/operacion/pedidos', null, 'GET', !automatica);
export const cambiarEstadoPedido = (folio, estado, motivo = null) => peticionAutenticada(`/operacion/pedidos/${encodeURIComponent(folio)}/estado`, { estado, motivo });

export const reintentarVenta = folio => peticionAutenticada(`/operacion/pedidos/${encodeURIComponent(folio)}/venta/reintentar`, null);
