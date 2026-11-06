import { useEffect } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router';
import Navbar from './components/Navbar.jsx';
import CartDrawer from './components/CartDrawer.jsx';
import AdminConfig from './components/AdminConfig.jsx';
import MenuView from './views/MenuView.jsx';
import TrackingView from './views/TrackingView.jsx';
import HistoryView from './views/HistoryView.jsx';
import AdminDashboard from './views/AdminDashboard.jsx';
import { useStore } from './state/StoreContext.jsx';

function Toast() {
  const { state, dispatch } = useStore();
  useEffect(() => {
    if (!state.toast) return;
    const timer = setTimeout(() => dispatch({ type: 'CLEAR_TOAST' }), 3200);
    return () => clearTimeout(timer);
  }, [state.toast, dispatch]);
  return <div id="toast-root" aria-live="polite">{state.toast && <div className={`toast ${state.toast.error ? 'error' : ''}`} role="status">{state.toast.message}</div>}</div>;
}

export default function App() {
  const { state, dispatch } = useStore();
  const { pathname } = useLocation();
  const admin = pathname.startsWith('/admin');
  const count = state.cart.reduce((sum, item) => sum + item.quantity, 0);
  useEffect(() => { document.title = `${admin ? 'Administración' : 'Pedidos'} · LoyOrder`; }, [admin]);
  return <><Navbar/><Routes>
    <Route path="/" element={<Navigate replace to="/menu"/>}/>
    <Route path="/menu" element={<MenuView/>}/>
    <Route path="/seguimiento" element={<TrackingView/>}/>
    <Route path="/historial" element={<HistoryView/>}/>
    <Route path="/admin" element={<AdminDashboard/>}/>
    <Route path="/admin/configuracion" element={<AdminConfig/>}/>
    <Route path="*" element={<Navigate replace to="/menu"/>}/>
  </Routes><footer className="site-footer"><div className="container"><span className="logo"><span className="logo-mark">✳</span>Loy<span>Order</span></span><p>Hecho con sabor y cariño · Demo React © 2026</p></div></footer>{!admin && <button className="cart-fab" aria-label="Abrir carrito" onClick={() => dispatch({ type: 'OPEN_CART' })}>🛒{count > 0 && <b>{count}</b>}</button>}<CartDrawer/><Toast/></>;
}
