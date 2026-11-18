import { isValidPin, normalizeAddress } from './address.js';
import { paymentCatalog } from '../config/defaults.js';
import { getDish } from '../data/menu.js';
import { shippingFee } from './restaurant.js';

export const deliveryOptions = [
  { id: 'local', label: 'Recoger en local', icon: '🏠' },
  { id: 'domicilio', label: 'A domicilio', icon: '🛵' },
];

// Una sola fuente de verdad para el resumen y el pedido persistido.
export function checkoutTotals(state) {
  const subtotal = state.cart.reduce((sum, item) => sum + Math.round(item.price * 100) * item.quantity, 0) / 100;
  const shipping = Math.round(shippingFee(state.config, state.delivery) * 100) / 100;
  return { subtotal, shipping, total: Math.round((subtotal + shipping) * 100) / 100 };
}

export function enabledPayments(state) {
  return paymentCatalog.filter(method => state.config.paymentMethods[method.id] === true);
}

export function validateCheckout(state) {
  if (!state.cart.length) return 'El carrito está vacío.';
  if (state.cart.some(item => {
    const dish = getDish(item.dishId);
    return !dish?.available || !dish.variants.includes(item.variant) ||
      !Number.isSafeInteger(item.quantity) || item.quantity < 1 ||
      !Number.isFinite(item.price) || item.price < 0;
  })) return 'Revisa los productos y cantidades del carrito.';
  if (!deliveryOptions.some(option => option.id === state.delivery)) return 'Selecciona una modalidad de entrega.';
  if (state.delivery === 'domicilio' && (typeof state.address !== 'string' || state.address.trim().length < 8)) return 'Ingresa una dirección de entrega completa.';
  if (state.delivery === 'domicilio' && !isValidPin(state.pin)) return 'Selecciona la ubicación en el mapa.';
  if (state.delivery === 'domicilio' && !normalizeAddress(state.address, state.pin)) return 'Revisa la dirección de entrega.';
  if (!enabledPayments(state).some(method => method.id === state.payment)) return 'Selecciona un método de pago habilitado.';
  if (Object.values(checkoutTotals(state)).some(value => !Number.isFinite(value) || value < 0)) return 'Los importes del pedido no son válidos.';
  return null;
}

// Generar fuera del reducer: React StrictMode puede invocar el reducer dos veces.
export function createPlaceOrderAction() {
  // getRandomValues también funciona en HTTP local/red, a diferencia de randomUUID
  // en algunos contextos no seguros. UUID v4 con 122 bits aleatorios.
  const bytes = globalThis.crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 15) | 64;
  bytes[8] = (bytes[8] & 63) | 128;
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
  const uuid = `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  return { type: 'PLACE_ORDER', folio: `LY-${uuid.toUpperCase()}`, createdAt: new Date().toISOString() };
}

// Transición pura: conserva el contrato id/delivery/payment/total/status existente.
export function placeOrderState(state, action) {
  // Acciones repetidas no deben crear otro pedido ni vaciar un carrito nuevo.
  if (state.orders.some(order => order.id === action.folio || order.folio === action.folio)) return state;
  const error = validateCheckout(state);
  if (error || !/^LY-[0-9A-F]{8}-[0-9A-F]{4}-4[0-9A-F]{3}-[89AB][0-9A-F]{3}-[0-9A-F]{12}$/.test(action.folio || '') || !Number.isFinite(Date.parse(action.createdAt))) {
    return { ...state, toast: { id: action.folio, message: error || 'No se pudo generar el folio.', error: true } };
  }
  const method = enabledPayments(state).find(payment => payment.id === state.payment);
  const order = {
    id: action.folio,
    folio: action.folio,
    createdAt: action.createdAt,
    date: new Date(action.createdAt).toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' }),
    items: state.cart.map(item => ({ ...item })),
    ...checkoutTotals(state),
    delivery: state.delivery,
    address: state.delivery === 'domicilio' ? state.address.trim() : null,
    pin: state.delivery === 'domicilio' && state.pin ? { ...state.pin } : null,
    payment: state.payment,
    paymentLabel: method.label,
    loyverseType: method.loyverseType,
    status: 0,
    rated: false,
  };
  return {
    ...state,
    orders: [order, ...state.orders],
    activeOrderId: order.id,
    cart: [],
    drawerOpen: false,
    toast: { id: order.id, message: `Pedido de prueba ${order.folio} creado.`, error: false },
  };
}
