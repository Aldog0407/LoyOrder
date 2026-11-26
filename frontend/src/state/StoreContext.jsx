import { normalizeAddress } from '../utils/address.js';
import { createContext, useContext, useEffect, useReducer } from 'react';
import { defaultConfig } from '../config/defaults.js';
import { CANTIDAD_MAXIMA, esItemValido, mismaLinea } from '../utils/carrito.js';
import { placeOrderState, enabledPayments, deliveryOptions, firmaCheckout } from '../utils/checkout.js';

const STORAGE_KEY = 'loyorder-react-demo-v1';
const seedOrders = [{ id: 'LY-1042', date: '03 nov 2026', items: [{ dishId: 2, variant: 'Mediana', price: 189, quantity: 1 }], total: 189, delivery: 'local', payment: 'cash', status: 3, rated: false }];
const initial = { config: defaultConfig, cart: [], orders: seedOrders, activeOrderId: null, delivery: 'local', payment: 'cash', address: '', pin: null, drawerOpen: false, toast: null };

function initialize() {
  try {
    const raw = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    const config = { ...defaultConfig, ...raw.config, shipping: { ...defaultConfig.shipping, ...raw.config?.shipping }, paymentMethods: { ...defaultConfig.paymentMethods, ...raw.config?.paymentMethods }, printer: { connected: false } };
    const restored = { ...initial, ...raw, config, configDraft: null, drawerOpen: false, toast: null };
    // Carritos guardados con el menú de demostración (sin variantId) ya no son válidos con el catálogo real.
    restored.cart = Array.isArray(restored.cart) ? restored.cart.filter(esItemValido) : [];
    Object.assign(restored, normalizeAddress(restored.address, restored.pin) ?? { address: '', pin: null });
    const methods = enabledPayments(restored);
    if (!methods.some(method => method.id === restored.payment)) restored.payment = methods[0]?.id ?? null;
    if (!deliveryOptions.some(option => option.id === restored.delivery)) restored.delivery = 'local';
    return restored;
  } catch { return initial; }
}

function reducer(state, action) {
  switch (action.type) {
    case 'CLEAR_SESSION_DATA': return { ...state, configDraft: null, cart: [], orders: [], activeOrderId: null, address: '', pin: null, delivery: 'local', drawerOpen: false, toast: null };
    case 'OPEN_CART': return { ...state, drawerOpen: true };
    case 'CLOSE_CART': return { ...state, drawerOpen: false };
    case 'NOTIFY': return { ...state, toast: { id: Date.now(), message: action.message, error: !!action.error } };
    case 'CLEAR_TOAST': return { ...state, toast: null };
    case 'SET_CONFIG_DRAFT': return { ...state, configDraft: action.config };
    case 'SET_CONFIG': return { ...state, config: action.config, configDraft: null, payment: action.config.paymentMethods[state.payment] ? state.payment : Object.keys(action.config.paymentMethods).find(key => action.config.paymentMethods[key]) };
    case 'SET_DELIVERY': return deliveryOptions.some(option => option.id === action.value) ? { ...state, delivery: action.value } : state;
    case 'SET_PAYMENT': return enabledPayments(state).some(method => method.id === action.value) ? { ...state, payment: action.value } : state;
    case 'SET_ADDRESS': {
      const location = normalizeAddress(action.address, action.pin);
      return location ? { ...state, ...location } : state;
    }
    case 'ADD_ITEM': {
      // El artículo lo arma crearItemCarrito con el producto del catálogo (7.2.4).
      if (!esItemValido(action.item)) return state;
      const index = state.cart.findIndex(item => mismaLinea(item, action.item));
      const cart = [...state.cart];
      if (index < 0) cart.push({ ...action.item });
      else {
        const cantidad = cart[index].quantity + action.item.quantity;
        if (cantidad > CANTIDAD_MAXIMA) return { ...state, toast: { id: 'limite', message: `Máximo ${CANTIDAD_MAXIMA} unidades por variante.`, error: true } };
        cart[index] = { ...action.item, quantity: cantidad };
      }
      return { ...state, cart };
    }
    case 'CHANGE_QUANTITY': {
      const cart = state.cart.map((item, index) => index === action.index ? { ...item, quantity: Math.min(CANTIDAD_MAXIMA, item.quantity + action.delta) } : item).filter(item => item.quantity > 0);
      return { ...state, cart };
    }
    case 'REMOVE_ITEM': return { ...state, cart: state.cart.filter((_, index) => index !== action.index) };
    case 'RECONCILE_CART': {
      if (!action.firma || action.firma !== firmaCheckout(state)) return state;
      if (!Array.isArray(action.items) || action.items.some(item => !esItemValido(item))) return state;
      const unicos = new Set(action.items.map(item => `${item.dishId}:${item.variantId}`));
      if (unicos.size !== action.items.length) return state;
      return { ...state, cart: action.items.map(item => ({ ...item })),
        toast: { id: 'carrito-actualizado', message: 'Carrito actualizado. Revisa el total antes de confirmar.', error: false } };
    }
    case 'PLACE_ORDER': return placeOrderState(state, action);
    case 'ADVANCE_ORDER': return { ...state, orders: state.orders.map(o => o.id === action.id ? { ...o, status: Math.min(o.status + 1, 3) } : o) };
    case 'VIEW_ORDER': return { ...state, activeOrderId: action.id };
    case 'RATE_ORDER': return { ...state, orders: state.orders.map(o => o.id === action.id ? { ...o, rated: true, rating: action.rating } : o) };
    case 'REORDER': {
      // Solo admite las líneas normalizadas previamente con crearItemCarrito + catálogo actual.
      // La pantalla controla la consulta y pide confirmación de cambios de precio.
      let cart = [...state.cart];
      for (const item of action.items ?? []) {
        if (!esItemValido(item)) continue;
        const index = cart.findIndex(c => mismaLinea(c, item));
        if (index < 0) cart.push({ ...item });
        else if (cart[index].quantity + item.quantity <= CANTIDAD_MAXIMA) {
          cart = cart.map((c, i) => i === index ? { ...item, quantity: c.quantity + item.quantity } : c);
        }
      }
      return { ...state, cart, drawerOpen: cart.length > 0 };
    }
    default: return state;
  }
}

const StoreContext = createContext(null);

export function StoreProvider({ children }) {
  const [state, dispatch] = useReducer(reducer, undefined, initialize);
  useEffect(() => {
    try {
      const { drawerOpen: _drawer, toast: _toast, configDraft: _configDraft, ...persisted } = state;
      localStorage.setItem(STORAGE_KEY, JSON.stringify(persisted));
    } catch { /* Modo privado o almacenamiento deshabilitado. */ }
  }, [state]);
  return <StoreContext.Provider value={{ state, dispatch }}>{children}</StoreContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useStore() {
  const context = useContext(StoreContext);
  if (!context) throw new Error('useStore requiere StoreProvider');
  return context;
}
