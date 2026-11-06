import { useState } from 'react';
import { money } from '../data/menu.js';
import { useStore } from '../state/StoreContext.jsx';
import Modal from './Modal.jsx';

export default function DishDetail({ dish, onClose }) {
  const { dispatch } = useStore();
  const [variant, setVariant] = useState('');
  const [quantity, setQuantity] = useState(1);
  const add = () => {
    if (!variant) { dispatch({ type: 'NOTIFY', message: 'Selecciona una variante.', error: true }); return; }
    dispatch({ type: 'ADD_ITEM', item: { dishId: dish.id, variant, quantity } });
    dispatch({ type: 'NOTIFY', message: `${dish.name} agregado al carrito` }); onClose();
  };
  return <Modal label="Detalle de platillo" className="detail-modal" onClose={onClose}>
    <div className="detail-art" style={{ '--art': dish.color }}>{dish.emoji}</div><div className="detail-body"><span className="eyebrow orange">{dish.category} · ★ {dish.rating}</span><h2>{dish.name}</h2><p>{dish.description}</p><strong className="detail-price">{money(dish.price)}</strong>
      <fieldset className="variant-field"><legend>Selecciona una variante <span>* Requerida</span></legend>{dish.variants.map(value => <label className="variant-option" key={value}><input type="radio" name="variant" checked={variant === value} onChange={() => setVariant(value)}/>{value}</label>)}</fieldset>
      <div className="detail-bottom"><div className="quantity"><button onClick={() => setQuantity(Math.max(1, quantity - 1))}>−</button><b>{quantity}</b><button onClick={() => setQuantity(Math.min(20, quantity + 1))}>+</button></div><button className="button primary flex-button" onClick={add}>Agregar · {money(dish.price * quantity)}</button></div>
    </div>
  </Modal>;
}
