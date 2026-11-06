import { useState } from 'react';
import { categories, dishes } from '../data/menu.js';
import MenuCard from '../components/MenuCard.jsx';
import DishDetail from '../components/DishDetail.jsx';

export default function MenuView() {
  const [category, setCategory] = useState('Todos');
  const [selected, setSelected] = useState(null);
  return <main><section className="hero container"><div className="hero-copy"><span className="eyebrow">✦ SABOR QUE TE HACE VOLVER</span><h1>Tu antojo favorito,<br/><em>a un clic de distancia.</em></h1><p>De nuestra cocina a tu mesa. Explora platillos hechos con amor.</p><a className="button light" href="#platillos">Explorar el menú ↓</a></div><div className="hero-visual"><div className="hero-orbit">✦</div><span className="hero-food">🍔</span><span className="hero-floating">🔥 Preparado al momento</span></div></section><section className="container menu-section" id="platillos"><div className="section-top"><div><span className="eyebrow orange">NUESTRO MENÚ</span><h2>¿Qué se te antoja hoy?</h2><p>Encuentra algo delicioso para cada momento.</p></div><span className="availability">● Menú de demostración</span></div><div className="categories" role="group" aria-label="Categorías">{categories.map(value => <button className={`category ${category === value ? 'active' : ''}`} key={value} onClick={() => setCategory(value)}>{value}</button>)}</div><div className="dish-grid">{dishes.filter(d => category === 'Todos' || d.category === category).map(dish => <MenuCard key={dish.id} dish={dish} onSelect={setSelected}/>)}</div></section>{selected && <DishDetail dish={selected} onClose={() => setSelected(null)}/>}</main>;
}
