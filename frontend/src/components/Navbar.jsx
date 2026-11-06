import { NavLink, useLocation } from 'react-router';
import { useState } from 'react';
import { useStore } from '../state/StoreContext.jsx';

export default function Navbar() {
  const { state, dispatch } = useStore();
  const [mobileMenu, setMobileMenu] = useState(false);
  const admin = useLocation().pathname.startsWith('/admin');
  const count = state.cart.reduce((sum, item) => sum + item.quantity, 0);
  const links = admin ? [['/admin', 'Dashboard'], ['/admin/configuracion', 'Configuración']] : [['/menu', 'Explorar menú'], ['/seguimiento', 'Mi pedido'], ['/historial', 'Historial']];
  return <header className="site-header"><div className="header-inner">
    <NavLink to="/menu" className="logo"><span className="logo-mark">✳</span>Loy<span>Order</span></NavLink>
    <nav className={`main-nav ${mobileMenu ? 'open' : ''}`} aria-label="Navegación principal">{links.map(([to, label]) => <NavLink key={to} end to={to} onClick={() => setMobileMenu(false)} className={({ isActive }) => isActive ? 'active' : ''}>{label}</NavLink>)}</nav>
    <div className="header-actions"><NavLink className="role-switch" to={admin ? '/menu' : '/admin'} onClick={() => setMobileMenu(false)}>{admin ? '← Vista cliente' : 'Vista admin ↗'}</NavLink>{!admin && <button className="cart-top" onClick={() => dispatch({ type: 'OPEN_CART' })}>🛒 <span>Carrito</span><b>{count}</b></button>}<button className="menu-toggle" onClick={() => setMobileMenu(!mobileMenu)} aria-label={mobileMenu ? 'Cerrar menú' : 'Abrir menú'} aria-expanded={mobileMenu}>☰</button></div>
  </div></header>;
}
