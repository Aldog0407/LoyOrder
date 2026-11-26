import { isValidPin } from '../utils/address.js';
import { obtenerCatalogo } from '../services/catalogo.js';
import { verificarCheckoutConCatalogo } from '../utils/catalogoStock.js';
import { useEffect, useId, useRef, useState } from 'react';
import { obtenerOpcionesPedido, confirmarPedido } from '../services/pedidos.js';
import { prepararPedido } from '../utils/pedido-servidor.js';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router';
import { money } from '../utils/formato.js';
import ImagenPlatillo from './ImagenPlatillo.jsx';
import { useStore } from '../state/StoreContext.jsx';
import { checkoutTotals, deliveryOptions, enabledPayments, validateCheckout, firmaCheckout } from '../utils/checkout.js';
import LocationPicker from './LocationPicker.jsx';
import Modal from './Modal.jsx';

/** Checkout compartido: productos, entrega, pago y confirmación. */
export default function CartFlow({ onClose = () => {} }) {
  const { state, dispatch } = useStore();
  const navigate = useNavigate();
  const groupId = useId();
  const submitting = useRef(false);
  const stateActual = useRef(state);
  stateActual.current = state;
  const pendienteRef = useRef(state.pedidoPendiente);
  const [opcionesError, setOpcionesError] = useState('');
  const [opcionesListas, setOpcionesListas] = useState(false);
  useEffect(() => {
    let activo = true;
    obtenerOpcionesPedido().then(opciones => {
      if (!activo) return;
      dispatch({ type: 'SET_CHECKOUT_OPTIONS', opciones });
      setOpcionesListas(true);
    }).catch(error => { if (activo) setOpcionesError(error.message); });
    return () => { activo = false; };
  }, [dispatch]);
  const [confirm, setConfirm] = useState(false);
  const [mapOpen, setMapOpen] = useState(false);
  const verificando = !!state.confirmandoPedido;
  const setVerificando = valor => dispatch({ type: 'SET_CONFIRMING_ORDER', valor });
  const [incidencias, setIncidencias] = useState([]);
  const [revisionPendiente, setRevisionPendiente] = useState(null);
  const { subtotal, shipping, total } = checkoutTotals(state);
  const paymentOptions = enabledPayments(state);
  const chosenPayment = paymentOptions.find(method => method.id === state.payment);
  const validacion = validateCheckout(state);
  const direccionIncompleta = state.delivery === 'domicilio' && (!state.checkoutAddress?.calle?.trim() || !state.checkoutAddress?.numero?.trim());
  const error = state.pedidoPendiente ? null : opcionesError || (!opcionesListas ? 'Consultando opciones del restaurante…' : !state.checkoutOptions?.abierto ? 'El restaurante está cerrado.' : validacion || (direccionIncompleta ? 'Completa la calle y el número de entrega.' : null));

  async function placeOrder() {
    if (submitting.current) return;
    const validation = state.pedidoPendiente ? null : error;
    if (validation) {
      dispatch({ type: 'NOTIFY', message: validation, error: true });
      return;
    }
    submitting.current = true;
    setVerificando(true);
    setIncidencias([]);
    setRevisionPendiente(null);
    const firma = firmaCheckout(state);
    try {
      let pendiente = pendienteRef.current;
      if (!pendiente) {
        const catalogo = await obtenerCatalogo();
        const revision = verificarCheckoutConCatalogo(state.cart, catalogo);
        if (!revision.valido) {
          setIncidencias(revision.incidencias);
          setRevisionPendiente({ items: revision.disponibles, firma });
          dispatch({ type: 'NOTIFY', message: 'Revisa los cambios del catálogo antes de continuar.', error: true });
          setConfirm(false);
          return;
        }
        if (firma !== firmaCheckout(stateActual.current)) throw new Error('El carrito cambió. Revisa el pedido antes de confirmar.');
        pendiente = prepararPedido(state);
        pendienteRef.current = pendiente;
        dispatch({ type: 'SET_PENDING_ORDER', pendiente });
      }
      const pedido = await confirmarPedido(pendiente.solicitud);
      dispatch({ type: 'ORDER_SAVED', pendiente, pedido });
      pendienteRef.current = null;
      setConfirm(false);
      onClose();
      navigate(`/seguimiento/${encodeURIComponent(pedido.folio)}`);
    } catch (fallo) {
      if (fallo.status === 400 || fallo.status === 409) {
        pendienteRef.current = null;
        dispatch({ type: 'SET_PENDING_ORDER', pendiente: null });
      }
      dispatch({ type: 'NOTIFY', message: fallo.message || 'No fue posible confirmar el pedido. Puedes reintentar la misma operación.', error: true });
    } finally {
      submitting.current = false;
      setVerificando(false);
    }
  }

  return <>
    <div className="drawer-body cart-flow-body" inert={verificando}>
      {state.pedidoPendiente && <div role="status" className="checkout-error">Hay una confirmación pendiente por {money(state.pedidoPendiente.solicitud.totalEsperado)}. Reintenta esa operación para recuperar el mismo pedido.</div>}
      {state.pedidoPendiente && !state.cart.length && <button className="button primary full" disabled={verificando} onClick={placeOrder}>Reintentar confirmación pendiente</button>}
      {state.cart.length ? <>
        <div className="cart-lines">
          {state.cart.map((item, index) => <div className="cart-line" key={`${item.dishId}:${item.variantId}`}>
            <div className="cart-emoji"><ImagenPlatillo src={item.image}/></div>
            <div className="cart-line-info">
              <strong>{item.name}</strong>{item.variant && <small>{item.variant}</small>}
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
        {state.delivery === 'domicilio' && <div className="address-box"><div>
          <label>Calle<input maxLength={150} value={state.checkoutAddress?.calle ?? ''} onChange={e => dispatch({ type: 'SET_CHECKOUT_ADDRESS', campo: 'calle', valor: e.target.value })}/></label>
          <label>Número exterior o S/N<input maxLength={20} value={state.checkoutAddress?.numero ?? ''} onChange={e => dispatch({ type: 'SET_CHECKOUT_ADDRESS', campo: 'numero', valor: e.target.value })}/></label>
        </div></div>}
        <fieldset className="checkout-group">
          <legend>Método de pago</legend>
          <div className="choice-grid">{paymentOptions.map(method => <label className={`choice ${state.payment === method.id ? 'chosen' : ''}`} key={method.id}>
            <input type="radio" name={`${groupId}-payment`} checked={state.payment === method.id} onChange={() => dispatch({ type: 'SET_PAYMENT', value: method.id })}/>
            {method.icon} {method.label}
          </label>)}</div>
        </fieldset>
        <p className="fine-print">Selecciona cómo pagarás al restaurante. Los precios, envío y existencias se comprueban al confirmar. No se cobra en línea ni se reservan unidades.</p>
        {error && <p className="checkout-error" role="alert">{error}</p>}
        {incidencias.length > 0 && <div className="checkout-error" role="alert">
          <strong>El catálogo cambió. No se creó ningún pedido.</strong>
          <ul>{incidencias.map((incidencia, i) => <li key={i}>{incidencia.mensaje}</li>)}</ul>
          <p>Puedes aceptar los precios actuales y omitir los artículos agotados, o modificar el carrito manualmente.</p>
          {revisionPendiente && <button className="button outline" onClick={() => {
            if (revisionPendiente.firma !== firmaCheckout(state)) {
              dispatch({ type: 'NOTIFY', message: 'El carrito cambió. Repite la comprobación antes de actualizar.', error: true });
              return;
            }
            dispatch({ type: 'RECONCILE_CART', items: revisionPendiente.items, firma: revisionPendiente.firma });
            setRevisionPendiente(null);
            setIncidencias([]);
          }}>Actualizar carrito con artículos disponibles</button>}
        </div>}
      </> : <div className="cart-empty"><span>🛒</span><h3>Tu carrito está vacío</h3><p>Selecciona un platillo para empezar.</p><button className="button outline" onClick={() => { onClose(); navigate('/menu'); }}>Explorar menú</button></div>}
    </div>
    {state.cart.length > 0 && <div className="drawer-footer cart-flow-footer">
      <div><span>Subtotal</span><b>{money(subtotal)}</b></div>
      <div><span>Envío</span><b>{shipping ? money(shipping) : 'Gratis'}</b></div>
      <div className="grand-total"><span>Total</span><strong>{money(total)}</strong></div>
      <button className="button primary full" disabled={!!error || verificando} onClick={() => setConfirm(true)}>Continuar al checkout →</button>
    </div>}
    {mapOpen && createPortal(<LocationPicker onClose={() => setMapOpen(false)}/>, document.body)}
    {confirm && createPortal(<Modal className="confirm-modal" label="Confirmación de pedido" onClose={() => { if (!verificando) setConfirm(false); }}>
      <div className="confirm-icon">✓</div><h2>Confirma tu pedido</h2>
      <p>El pedido se guardará en el restaurante y recibirás su folio de seguimiento.</p>
      <div className="confirm-total"><span>Total</span><strong>{money(state.pedidoPendiente?.solicitud.totalEsperado ?? total)}</strong></div>
      <p className="confirm-description">{state.pedidoPendiente ? `${state.pedidoPendiente.solicitud.tipoEntrega} · ${state.pedidoPendiente.paymentLabel}` : `${deliveryOptions.find(option => option.id === state.delivery)?.label} · ${chosenPayment?.label}`}</p>
      {(state.pedidoPendiente?.direccionTexto || (!state.pedidoPendiente && state.delivery === 'domicilio')) && <p>{state.pedidoPendiente?.direccionTexto ?? state.address}</p>}
      {error && <p className="checkout-error" role="alert">{error}</p>}
      <button className="button primary full" disabled={!!error || verificando} onClick={placeOrder}>{verificando ? 'Confirmando pedido…' : state.pedidoPendiente ? 'Reintentar confirmación pendiente' : 'Confirmar pedido'}</button>
      <button className="button subtle full" disabled={verificando} onClick={() => setConfirm(false)}>Volver</button>
    </Modal>, document.body)}
  </>;
}
