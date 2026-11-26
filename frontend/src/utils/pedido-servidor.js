import { createPlaceOrderAction, firmaCheckout, checkoutTotals, validateCheckout } from './checkout.js';

export function prepararPedido(state) {
  if (!state.checkoutOptions?.abierto) throw new Error('El restaurante está cerrado o falta consultar sus opciones.');
  if (!state.checkoutOptions.metodosPago.some(pago => String(pago.id) === state.payment)) throw new Error('Selecciona un método de pago disponible.');
  const error = validateCheckout(state);
  if (error) throw new Error(error);
  const domicilio = state.delivery === 'domicilio';
  const calle = state.checkoutAddress?.calle?.trim();
  const numero = state.checkoutAddress?.numero?.trim();
  if (domicilio && (!calle || !numero)) throw new Error('Completa la calle y el número de entrega.');
  const firma = firmaCheckout(state);
  const solicitud = {
    idOperacion: createPlaceOrderAction(firma).folio.slice(3),
    tipoEntrega: domicilio ? 'Domicilio' : 'Recolección',
    idMetodoPago: Number(state.payment),
    totalEsperado: checkoutTotals(state).total,
    articulos: state.cart.map(item => ({ idVariante: item.variantId, cantidad: item.quantity, precioEsperado: item.price })),
    direccion: domicilio ? { calle, numero, referencias: state.address,
      latitud: state.pin.lat, longitud: state.pin.lng } : null,
  };
  return { firma, solicitud, items: state.cart.map(item => ({ ...item })),
    direccionTexto: domicilio ? `${calle} ${numero}. ${state.address}` : null,
    pin: domicilio ? { ...state.pin } : null,
    paymentLabel: state.checkoutOptions.metodosPago.find(pago => String(pago.id) === state.payment).nombre };
}

export function guardarPedidoConfirmado(state, action) {
  const { pendiente, pedido } = action;
  const coincide = JSON.stringify(state.cart) === JSON.stringify(pendiente.items);
  const order = {
    id: pedido.folio, folio: pedido.folio, persistido: true, createdAt: pedido.fechaCreacion,
    date: new Date(pedido.fechaCreacion).toLocaleDateString('es-MX'), items: pendiente.items,
    subtotal: pedido.subtotal, shipping: pedido.costoEnvio, total: pedido.total,
    delivery: pendiente.solicitud.tipoEntrega === 'Domicilio' ? 'domicilio' : 'local',
    address: pendiente.direccionTexto, pin: pendiente.pin, payment: String(pendiente.solicitud.idMetodoPago),
    paymentLabel: pendiente.paymentLabel, status: 0, rated: false,
  };
  return { ...state, confirmandoPedido: false, pedidoPendiente: null, orders: [order, ...state.orders.filter(o => o.id !== order.id)],
    cart: coincide ? [] : state.cart, activeOrderId: order.id, drawerOpen: false,
    toast: { id: order.id, message: `Pedido ${order.folio} guardado correctamente.`, error: false } };
}


export function adaptarHistorialPedido(pedido) {
  const estados = { 'Recibido': 0, 'En preparación': 1, 'Enviado': 2, 'Listo para recoger': 2, 'Entregado': 3, 'Cancelado': -1 };
  return { id: pedido.folio, folio: pedido.folio, persistido: true,
    date: new Date(pedido.fechaCreacion).toLocaleDateString('es-MX'), total: pedido.total,
    estado: pedido.estado, status: estados[pedido.estado], rated: pedido.calificacion != null,
    items: pedido.articulos.map(a => ({ dishId: a.idProducto, variantId: a.idVariante, name: a.nombreProducto,
      variant: a.variante ?? '', image: null, quantity: a.cantidad, price: a.precioUnitario })) };
}
