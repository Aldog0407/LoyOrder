import { useState } from 'react';
import { useNavigate } from 'react-router';
import { getDish, money } from '../data/menu.js';
import { paymentCatalog } from '../config/defaults.js';
import { shippingFee } from '../utils/restaurant.js';
import { useStore } from '../state/StoreContext.jsx';
import LocationPicker from './LocationPicker.jsx';
import Modal from './Modal.jsx';

/** Un único flujo reutilizado por /carrito y por el drawer móvil/escritorio. */
export default function CartFlow({ onClose = () => {} }) {
  const { state, dispatch } = useStore();
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState(false);
  const [mapOpen, setMapOpen] = useState(false);
  const subtotal = state.cart.reduce((sum, item) => sum + item.price * item.quantity, 0);
  const shipping = shippingFee(state.config, state.delivery);
  const total = subtotal + shipping;
  const paymentOptions = paymentCatalog.filter(method => state.config.paymentMethods[method.id]);
  const chosenPayment = paymentOptions.find(method => method.id === state.payment);

  const placeOrder = () => {
    if (!state.cart.length || !chosenPayment) return;
    // Pendiente: integrar endpoint de pedidos. Nunca se envía al backend de auth.
    const order = { id: `LY-${Date.now().toString().slice(-6)}`, date: new Date().toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' }), items: state.cart.map(item => ({ ...item })), total, delivery: state.delivery, payment: state.payment, status: 0, rated: false };
    dispatch({ type: 'PLACE_ORDER', order });
    dispatch({ type: 'NOTIFY', message: `Pedido de demostración ${order.id} creado` });
    setConfirm(false); onClose(); navigate('/seguimiento');
  };

  return <><div className="drawer-body cart-flow-body">{state.cart.length ? <>
    <div className="cart-lines">{state.cart.map((item, index) => <div className="cart-line" key={`${item.dishId}:${item.variant}`}><div className="cart-emoji">{getDish(item.dishId)?.emoji}</div><div className="cart-line-info"><strong>{getDish(item.dishId)?.name}</strong><small>{item.variant}</small><span>{money(item.price * item.quantity)}</span><div className="line-actions"><button aria-label="Reducir cantidad" onClick={() => dispatch({ type: 'CHANGE_QUANTITY', index, delta: -1 })}>−</button><b>{item.quantity}</b><button aria-label="Aumentar cantidad" onClick={() => dispatch({ type: 'CHANGE_QUANTITY', index, delta: 1 })}>+</button><button className="remove" onClick={() => dispatch({ type: 'REMOVE_ITEM', index })}>Eliminar</button></div></div></div>)}</div>
    <h3>Modalidad de entrega</h3><div className="choice-grid">{[['local', '🏠 Recoger en local'], ['domicilio', '🛵 A domicilio']].map(([value, label]) => <label className={`choice ${state.delivery === value ? 'chosen' : ''}`} key={value}><input type="radio" name="delivery" checked={state.delivery === value} onChange={() => dispatch({ type: 'SET_DELIVERY', value })}/>{label}</label>)}</div>
    {state.delivery === 'domicilio' && <div className="address-box"><div><small>Dirección</small><strong>{state.address}</strong></div><button onClick={() => setMapOpen(true)}>Cambiar →</button></div>}
    <h3>Método de pago</h3><div className="choice-grid">{paymentOptions.map(method => <label className={`choice ${state.payment === method.id ? 'chosen' : ''}`} key={method.id}><input type="radio" name="payment" checked={state.payment === method.id} onChange={() => dispatch({ type: 'SET_PAYMENT', value: method.id })}/>{method.icon} {method.label}</label>)}</div>
    <p className="fine-print">Métodos configurables desde administración. La sincronización con Loyverse y el cobro real están pendientes.</p>
  </> : <div className="cart-empty"><span>🛒</span><h3>Tu carrito está vacío</h3><p>Selecciona un platillo para empezar.</p><button className="button outline" onClick={() => { onClose(); navigate('/menu'); }}>Explorar menú</button></div>}</div>
  {state.cart.length > 0 && <div className="drawer-footer cart-flow-footer"><div><span>Subtotal</span><b>{money(subtotal)}</b></div><div><span>Envío</span><b>{shipping ? money(shipping) : 'Gratis'}</b></div><div className="grand-total"><span>Total</span><strong>{money(total)}</strong></div><button className="button primary full" disabled={!chosenPayment} onClick={() => setConfirm(true)}>Realizar pedido de prueba →</button></div>}
  {mapOpen && <LocationPicker onClose={() => setMapOpen(false)}/>}
  {confirm && <Modal className="confirm-modal" label="Confirmación de pedido" onClose={() => setConfirm(false)}><div className="confirm-icon">✓</div><h2>¿Confirmamos tu pedido?</h2><p>Se creará solamente en este navegador; todavía no existe endpoint de pedidos.</p><div className="confirm-total"><span>Total</span><strong>{money(total)}</strong></div><p className="confirm-description">{state.delivery === 'local' ? 'Recoger en local' : 'A domicilio'} · {chosenPayment?.label}</p><button className="button primary full" onClick={placeOrder}>Confirmar pedido de prueba</button><button className="button subtle full" onClick={() => setConfirm(false)}>Volver</button></Modal>}
  </>;
}
