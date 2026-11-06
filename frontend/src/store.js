import { seededOrders } from './data.js';
import { defaultSettings, copySettings } from './settings.js';

const KEY = 'loyorder-demo-v1';
const initial = { cart: [], orders: seededOrders, activeOrderId: null, address: 'Av. Principal 123, Centro', location: { x: 52, y: 48 }, category: 'Todos', period: 'Hoy', delivery: 'local', payment: 'efectivo', restaurantSettings: copySettings(defaultSettings) };
let saved = {};
try { saved = JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch { /* Storage may be blocked */ }
export const state = { ...structuredClone(initial), ...saved, modal: null, drawer: false, mobileMenu: false };
state.restaurantSettings = { ...copySettings(defaultSettings), ...(saved.restaurantSettings || {}), delivery: { ...defaultSettings.delivery, ...(saved.restaurantSettings?.delivery || {}) }, payments: { ...defaultSettings.payments, ...(saved.restaurantSettings?.payments || {}) }, printer: { ...defaultSettings.printer, ...(saved.restaurantSettings?.printer || {}) } };

export function save() {
  try { localStorage.setItem(KEY, JSON.stringify({ cart: state.cart, orders: state.orders, activeOrderId: state.activeOrderId, address: state.address, location: state.location, category: state.category, period: state.period, delivery: state.delivery, payment: state.payment, restaurantSettings: state.restaurantSettings })); } catch { /* Demo also works without persistence */ }
}
export function update(patch) { Object.assign(state, patch); save(); }
