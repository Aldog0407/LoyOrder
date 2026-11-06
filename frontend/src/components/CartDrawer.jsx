import { useState } from 'react';
import { useNavigate } from 'react-router';
import { getDish, money } from '../data/menu.js';
import { paymentCatalog } from '../config/defaults.js';
import { shippingFee } from '../utils/restaurant.js';
import { useStore } from '../state/StoreContext.jsx';
import LocationPicker from './LocationPicker.jsx';
import Modal from './Modal.jsx';

export default function CartDrawer() {
  const { state, dispatch } = useStore();
  const [confirm, setConfirm] = useState(false);
  const [mapOpen, setMapOpen] = useState(false);
  const navigate = useNavigate();
  const subtotal = state.cart.reduce((sum, item) => sum + item.price * item.quantity, 0);
  const fee = shippingFee(state.config, state.delivery);
  const total = subtotal + fee;
  const selected = paymentCatalog.find(p => p.id === state.payment);

  const placeOrder = () => {
    const order = { id: `LY-${Date.now().toString().slice(-6)}`, date: new Date().toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' }), items: state.cart.map(i => ({ ...i })), total, delivery: state.delivery, payment: state.payment, status: 0, rated: false };
    dispatch({ type: 'PLACE_ORDER', order }); dispatch({ type: 'NOTIFY', message: `¡Pedido ${order.id} recibido!` }); setConfirm(false); navigate('/seguimiento');
  };

  return <>{state.drawerOpen && <div className="backdrop drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) dispatch({ type: 'CLOSE_CART' }); }}><aside className="cart-drawer" role="dialog" aria-modal="true" aria-label="Tu carrito">
    <div className="drawer-header"><div><span className="eyebrow orange">TU SELECCIÓN</span><h2>Tu carrito ({state.cart.reduce((s, i) => s + i.quantity, 0)})</h2></div><button className="close-button" onClick={() => dispatch({ type: 'CLOSE_CART' })} aria-label="Cerrar">×</button></div>
    <div className="drawer-body">{state.cart.length ? <>
      <div className="cart-lines">{state.cart.map((item, index) => <div className="cart-line" key={`${item.dishId}:${item.variant}`}><div className="cart-emoji">{getDish(item.dishId)?.emoji}</div><div className="cart-line-info"><strong>{getDish(item.dishId)?.name}</strong><small>{item.variant}</small><span>{money(item.price * item.quantity)}</span><div className="line-actions"><button onClick={() => dispatch({ type: 'CHANGE_QUANTITY', index, delta: -1 })}>−</button><b>{item.quantity}</b><button onClick={() => dispatch({ type: 'CHANGE_QUANTITY', index, delta: 1 })}>+</button><button className="remove" onClick={() => dispatch({ type: 'REMOVE_ITEM', index })}>Eliminar</button></div></div></div>)}</div>
      <h3>¿Cómo lo quieres recibir?</h3><div className="choice-grid">{[['local', '🏠 Recoger en local'], ['domicilio', '🛵 A domicilio']].map(([value, label]) => <label className={`choice ${state.delivery === value ? 'chosen' : ''}`} key={value}><input type="radio" name="delivery" checked={state.delivery === value} onChange={() => dispatch({ type: 'SET_DELIVERY', value })}/>{label}</label>)}</div>
      {state.delivery === 'domicilio' && <div className="address-box"><div><small>Dirección de entrega</small><strong>{state.address}</strong></div><button onClick={() => setMapOpen(true)}>Cambiar →</button></div>}
      <h3>Método de pago</h3><div className="choice-grid">{paymentCatalog.filter(method => state.config.paymentMethods[method.id]).map(method => <label className={`choice ${state.payment === method.id ? 'chosen' : ''}`} key={method.id}><input type="radio" name="payment" checked={state.payment === method.id} onChange={() => dispatch({ type: 'SET_PAYMENT', value: method.id })}/>{method.icon} {method.label}</label>)}</div>
      <p className="fine-print">Cobro simulado. Nunca se solicitan datos bancarios.</p>
    </> : <div className="cart-empty"><span>🛒</span><h3>Tu carrito está vacío</h3><p>Agrega algo delicioso del menú.</p><button className="button outline" onClick={() => dispatch({ type: 'CLOSE_CART' })}>Volver al menú</button></div>}</div>
    {state.cart.length > 0 && <footer className="drawer-footer"><div><span>Subtotal</span><b>{money(subtotal)}</b></div><div><span>Envío</span><b>{fee ? money(fee) : 'Gratis'}</b></div><div className="grand-total"><span>Total</span><strong>{money(total)}</strong></div><button className="button primary full" onClick={() => { dispatch({ type: 'CLOSE_CART' }); setConfirm(true); }}>Confirmar pedido →</button></footer>}
  </aside></div>}
  {mapOpen && <LocationPicker onClose={() => setMapOpen(false)}/>}
  {confirm && <Modal label="Confirmar pedido" onClose={() => setConfirm(false)} className="confirm-modal"><div className="confirm-icon">✓</div><h2>¿Confirmamos tu pedido?</h2><p>Esta es una simulación, no se realizará ningún cargo.</p><div className="confirm-total"><span>Total</span><strong>{money(total)}</strong></div><p className="confirm-description">{state.delivery === 'local' ? 'Recoger en local' : 'Domicilio'} · {selected?.label}</p><button className="button primary full" onClick={placeOrder}>Realizar pedido</button><button className="button subtle full" onClick={() => { setConfirm(false); dispatch({ type: 'OPEN_CART' }); }}>Volver al carrito</button></Modal>}
  </>;
}
