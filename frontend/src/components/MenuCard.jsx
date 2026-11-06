import { money } from '../data/menu.js';

export default function MenuCard({ dish, onSelect }) {
  return <article className={`dish-card ${dish.available ? '' : 'sold-out'}`}>
    <div className="dish-art" style={{ '--art': dish.color }}><span aria-hidden="true">{dish.emoji}</span>{!dish.available && <span className="stock-label">Agotado</span>}</div>
    <div className="dish-content"><div className="dish-meta"><span>{dish.category}</span><span className="rating">★ {dish.rating}</span></div>
      <h3>{dish.name}</h3><p>{dish.description}</p><div className="dish-footer"><strong>{money(dish.price)}</strong><button disabled={!dish.available} onClick={() => onSelect(dish)}>{dish.available ? '+ Agregar' : 'No disponible'}</button></div>
    </div>
  </article>;
}
