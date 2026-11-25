import { useState } from 'react';
import { money } from '../utils/formato.js';
import { CANTIDAD_MAXIMA, crearItemCarrito } from '../utils/carrito.js';
import { useStore } from '../state/StoreContext.jsx';
import ImagenPlatillo from './ImagenPlatillo.jsx';
import Modal from './Modal.jsx';

export default function DishDetail({ dish, onClose }) {
  const { dispatch } = useStore();
  const disponibles = dish.variantes.filter(v => !v.agotado);
  // Con una sola variante disponible (o un artículo sin opciones en Loyverse) se elige sola.
  const [idVariante, setIdVariante] = useState(disponibles.length === 1 ? disponibles[0].id : null);
  const [quantity, setQuantity] = useState(1);
  const variante = dish.variantes.find(v => v.id === idVariante);
  const conOpciones = dish.variantes.length > 1 || !!dish.variantes[0]?.nombre;
  const precio = variante?.precio ?? dish.precioDesde;
  const add = () => {
    if (!variante) { dispatch({ type: 'NOTIFY', message: 'Selecciona una variante.', error: true }); return; }
    const item = crearItemCarrito(dish, variante, quantity);
    if (!item) { dispatch({ type: 'NOTIFY', message: 'Ese artículo ya no está disponible.', error: true }); return; }
    dispatch({ type: 'ADD_ITEM', item });
    dispatch({ type: 'NOTIFY', message: `${dish.nombre} agregado al carrito` }); onClose();
  };
  return <Modal label={`Detalle de ${dish.nombre}`} className="detail-modal" onClose={onClose}>
    <div className="detail-art"><ImagenPlatillo src={dish.imagenUrl}/></div>
    <div className="detail-body"><span className="eyebrow orange">{dish.categoria}</span><h2>{dish.nombre}</h2>{dish.descripcion && <p className="detail-description">{dish.descripcion}</p>}
      <strong className="detail-price">{!variante && dish.precioVariable && <small>Desde </small>}{money(precio)}</strong>
      {conOpciones && <fieldset className="variant-field"><legend>Selecciona una variante <span>* Requerida</span></legend>
        {dish.variantes.map(v => <label className={`variant-option ${v.agotado ? 'agotada' : ''}`} key={v.id}>
          <input type="radio" name="variant" disabled={v.agotado} checked={idVariante === v.id} onChange={() => setIdVariante(v.id)}/>
          <span className="variant-name">{v.nombre ?? dish.nombre}</span><span className="variant-price">{v.agotado ? 'Agotado' : money(v.precio)}</span>
        </label>)}</fieldset>}
      <div className="detail-bottom"><div className="quantity"><button aria-label="Reducir cantidad" onClick={() => setQuantity(Math.max(1, quantity - 1))}>−</button><b aria-live="polite">{quantity}</b><button aria-label="Aumentar cantidad" onClick={() => setQuantity(Math.min(CANTIDAD_MAXIMA, quantity + 1))}>+</button></div>
        <button className="button primary flex-button" disabled={!disponibles.length} onClick={add}>Agregar · {money(precio * quantity)}</button></div>
    </div>
  </Modal>;
}
