import { isValidPin } from '../utils/address.js';
import { useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router';
import { getDish, money } from '../data/menu.js';
import { useStore } from '../state/StoreContext.jsx';
import { checkoutTotals, createPlaceOrderAction, deliveryOptions, enabledPayments, validateCheckout } from '../utils/checkout.js';
import LocationPicker from './LocationPicker.jsx';
import Modal from './Modal.jsx';

/** Checkout compartido: productos, entrega, pago y confirmación. */
export default function CartFlow({ onClose = () => {} }) {
  const { state, dispatch } = useStore();
  const navigate = useNavigate();
  const groupId = useId();
  const submitting = useRef(false);
  const [confirm, setConfirm] = useState(false);
  const [mapOpen, setMapOpen] = useState(false);
  const { subtotal, shipping, total } = checkoutTotals(state);
  const paymentOptions = enabledPayments(state);
  const chosenPayment = paymentOptions.find(method => method.id === state.payment);
  const error = validateCheckout(state);

  function placeOrder() {
    if (submitting.current) return;
    const validation = validateCheckout(state);
    if (validation) {
      dispatch({ type: 'NOTIFY', message: validation, error: true });
      return;
    }
    let action;
    try { action = createPlaceOrderAction(); }
    catch {
      dispatch({ type: 'NOTIFY', message: 'No se pudo generar el folio.', error: true });
      return;
    }
    // Verificar contra el historial antes de iniciar la transición.
    if (state.orders.some(order => order.id === action.folio || order.folio === action.folio)) {
      dispatch({ type: 'NOTIFY', message: 'Intenta confirmar nuevamente.', error: true });
      return;
    }
    submitting.current = true;
    dispatch(action); // El reducer construye el pedido usando el estado vigente.
    setConfirm(false);
    onClose();
    navigate('/seguimiento');
  }

  return <>
    <div className="drawer-body cart-flow-body">
      {state.cart.length ? <>
        <div className="cart-lines">
          {state.cart.map((item, index) => <div className="cart-line" key={`${item.dishId}:${item.variant}`}>
            <div className="cart-emoji">{getDish(item.dishId)?.emoji}</div>
            <div className="cart-line-info">
              <strong>{getDish(item.dishId)?.name}</strong><small>{item.variant}</small>
              <span>{money(item.price * item.quantity)}</span>
              <div className="line-actions">
                <button aria-label="Reducir cantidad" onClick={() => dispatch({ type: 'CHANGE_QUANTITY', index, delta: -1 })}>−</button>
                <b>{item.quantity}</b>
                <button aria-label="Aumentar cantidad" onClick={() => dispatch({ type: 'CHANGE_QUANTITY', index, delta: 1 })}>+</button>
                <button className="remove" onClick={() => dispatch({ type: 'REMOVE_ITEM', index })}>Eliminar</button>
              </div>
            </div>
          </div>)}
        </div>
        <fieldset className="checkout-group">
          <legend>Modalidad de entrega</legend>
          <div className="choice-grid">{deliveryOptions.map(option => <label className={`choice ${state.delivery === option.id ? 'chosen' : ''}`} key={option.id}>
            <input type="radio" name={`${groupId}-delivery`} checked={state.delivery === option.id} onChange={() => dispatch({ type: 'SET_DELIVERY', value: option.id })}/>
            {option.icon} {option.label}
          </label>)}</div>
        </fieldset>
        {state.delivery === 'domicilio' && <div className="address-box">
          <div><small>Dirección</small><strong>{state.address || 'Sin dirección'}</strong>{isValidPin(state.pin) && <small>{state.pin.lat.toFixed(5)}, {state.pin.lng.toFixed(5)}</small>}</div>
          <button onClick={() => setMapOpen(true)}>{isValidPin(state.pin) ? 'Cambiar →' : 'Seleccionar en mapa →'}</button>
        </div>}
        <fieldset className="checkout-group">
          <legend>Método de pago</legend>
          <div className="choice-grid">{paymentOptions.map(method => <label className={`choice ${state.payment === method.id ? 'chosen' : ''}`} key={method.id}>
            <input type="radio" name={`${groupId}-payment`} checked={state.payment === method.id} onChange={() => dispatch({ type: 'SET_PAYMENT', value: method.id })}/>
            {method.icon} {method.label}
          </label>)}</div>
        </fieldset>
        <p className="fine-print">Pago simulado; no se realizará ningún cargo. Catálogo local vinculado conceptualmente a Loyverse.</p>
        {error && <p className="checkout-error" role="alert">{error}</p>}
      </> : <div className="cart-empty"><span>🛒</span><h3>Tu carrito está vacío</h3><p>Selecciona un platillo para empezar.</p><button className="button outline" onClick={() => { onClose(); navigate('/menu'); }}>Explorar menú</button></div>}
    </div>
    {state.cart.length > 0 && <div className="drawer-footer cart-flow-footer">
      <div><span>Subtotal</span><b>{money(subtotal)}</b></div>
      <div><span>Envío</span><b>{shipping ? money(shipping) : 'Gratis'}</b></div>
      <div className="grand-total"><span>Total</span><strong>{money(total)}</strong></div>
      <button className="button primary full" disabled={!!error} onClick={() => setConfirm(true)}>Continuar al checkout →</button>
    </div>}
    {mapOpen && createPortal(<LocationPicker onClose={() => setMapOpen(false)}/>, document.body)}
    {confirm && createPortal(<Modal className="confirm-modal" label="Confirmación de pedido" onClose={() => setConfirm(false)}>
      <div className="confirm-icon">✓</div><h2>Confirma tu pedido</h2>
      <p>El pedido se registrará localmente en este navegador.</p>
      <div className="confirm-total"><span>Total</span><strong>{money(total)}</strong></div>
      <p className="confirm-description">{deliveryOptions.find(option => option.id === state.delivery)?.label} · {chosenPayment?.label}</p>
      {state.delivery === 'domicilio' && <p>{state.address}</p>}
      {error && <p className="checkout-error" role="alert">{error}</p>}
      <button className="button primary full" disabled={!!error} onClick={placeOrder}>Realizar pedido de prueba</button>
      <button className="button subtle full" onClick={() => setConfirm(false)}>Volver</button>
    </Modal>, document.body)}
  </>;
}
