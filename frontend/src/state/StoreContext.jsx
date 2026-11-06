import { createContext, useContext, useEffect, useReducer } from 'react';
import { defaultConfig } from '../config/defaults.js';
import { getDish } from '../data/menu.js';

const STORAGE_KEY = 'loyorder-react-demo-v1';
const seedOrders = [{ id: 'LY-1042', date: '03 nov 2026', items: [{ dishId: 2, variant: 'Mediana', price: 189, quantity: 1 }], total: 189, delivery: 'local', payment: 'cash', status: 3, rated: false }];
const initial = { config: defaultConfig, cart: [], orders: seedOrders, activeOrderId: null, delivery: 'local', payment: 'cash', address: 'Av. Principal 123, Centro', pin: { x: 52, y: 48 }, drawerOpen: false, toast: null };

function initialize() {
  try {
    const raw = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    const config = { ...defaultConfig, ...raw.config, shipping: { ...defaultConfig.shipping, ...raw.config?.shipping }, paymentMethods: { ...defaultConfig.paymentMethods, ...raw.config?.paymentMethods }, printer: { connected: false } };
    return { ...initial, ...raw, config, drawerOpen: false, toast: null };
  } catch { return initial; }
}

function reducer(state, action) {
  switch (action.type) {
    case 'OPEN_CART': return { ...state, drawerOpen: true };
    case 'CLOSE_CART': return { ...state, drawerOpen: false };
    case 'NOTIFY': return { ...state, toast: { id: Date.now(), message: action.message, error: !!action.error } };
    case 'CLEAR_TOAST': return { ...state, toast: null };
    case 'SET_CONFIG': return { ...state, config: action.config, payment: action.config.paymentMethods[state.payment] ? state.payment : Object.keys(action.config.paymentMethods).find(key => action.config.paymentMethods[key]) };
    case 'SET_DELIVERY': return { ...state, delivery: action.value };
    case 'SET_PAYMENT': return state.config.paymentMethods[action.value] ? { ...state, payment: action.value } : state;
    case 'SET_ADDRESS': return { ...state, address: action.address, pin: action.pin };
    case 'ADD_ITEM': {
      const dish = getDish(action.item.dishId);
      if (!dish?.available || !dish.variants.includes(action.item.variant)) return state;
      const index = state.cart.findIndex(item => item.dishId === dish.id && item.variant === action.item.variant);
      const cart = [...state.cart];
      if (index < 0) cart.push({ ...action.item, price: dish.price });
      else cart[index] = { ...cart[index], quantity: cart[index].quantity + action.item.quantity };
      return { ...state, cart };
    }
    case 'CHANGE_QUANTITY': {
      const cart = state.cart.map((item, index) => index === action.index ? { ...item, quantity: item.quantity + action.delta } : item).filter(item => item.quantity > 0);
      return { ...state, cart };
    }
    case 'REMOVE_ITEM': return { ...state, cart: state.cart.filter((_, index) => index !== action.index) };
    case 'PLACE_ORDER': return { ...state, orders: [action.order, ...state.orders], activeOrderId: action.order.id, cart: [], drawerOpen: false };
    case 'ADVANCE_ORDER': return { ...state, orders: state.orders.map(o => o.id === action.id ? { ...o, status: Math.min(o.status + 1, 3) } : o) };
    case 'VIEW_ORDER': return { ...state, activeOrderId: action.id };
    case 'RATE_ORDER': return { ...state, orders: state.orders.map(o => o.id === action.id ? { ...o, rated: true, rating: action.rating } : o) };
    case 'REORDER': {
      let cart = [...state.cart];
      for (const item of action.items) {
        if (!getDish(item.dishId)?.available) continue;
        const index = cart.findIndex(c => c.dishId === item.dishId && c.variant === item.variant);
        if (index < 0) cart.push({ ...item });
        else cart = cart.map((c, i) => i === index ? { ...c, quantity: c.quantity + item.quantity } : c);
      }
      return { ...state, cart, drawerOpen: true };
    }
    default: return state;
  }
}

const StoreContext = createContext(null);

export function StoreProvider({ children }) {
  const [state, dispatch] = useReducer(reducer, undefined, initialize);
  useEffect(() => {
    try {
      const { drawerOpen: _drawer, toast: _toast, ...persisted } = state;
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
