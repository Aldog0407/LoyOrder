import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router';
import { actual, iniciar, cerrar, cuandoVenza, cuandoSeAcerqueElVencimiento, registrarActividad } from '../auth.js';
import { AuthContext } from './AuthContext.js';
import { useStore } from './StoreContext.jsx';

export default function AuthProvider({ children }) {
  const [usuario, setUsuario] = useState(actual);
  const [avisoSesion, setAvisoSesion] = useState(false);
  const [retomar, setRetomar] = useState(null);
  const interrumpida = useRef(null);
  const { pathname, search, hash, key } = useLocation();
  const { dispatch } = useStore();

  useEffect(() => {
    cuandoVenza(identidad => {
      interrumpida.current = { identidad, ruta: pathname + search + hash };
      setUsuario(null);
      dispatch({ type: 'NOTIFY', message: 'La sesión venció. Inicia sesión para retomar tu trabajo.', error: true });
    });
    cuandoSeAcerqueElVencimiento(setAvisoSesion);
    return () => {
      cuandoVenza(() => {});
      cuandoSeAcerqueElVencimiento(() => {});
    };
  }, [dispatch, pathname, search, hash]);

  useEffect(() => {
    const eventos = ['pointerdown', 'keydown', 'input', 'wheel'];
    const actividad = event => {
      if (event.isTrusted && document.visibilityState === 'visible') registrarActividad();
    };
    for (const evento of eventos) window.addEventListener(evento, actividad, { passive: true });
    return () => { for (const evento of eventos) window.removeEventListener(evento, actividad); };
  }, []);

  const iniciarSesion = useCallback(async datos => {
    const identidad = await iniciar(datos);
    const anterior = interrumpida.current;
    const mismaCuenta = anterior && anterior.identidad.idUsuario === identidad.idUsuario &&
      anterior.identidad.rol === identidad.rol;
    if (!mismaCuenta) dispatch({ type: 'CLEAR_SESSION_DATA' });
    interrumpida.current = null;
    setRetomar(mismaCuenta ? { ruta: anterior.ruta, desde: key } : null);
    setUsuario(identidad);
  }, [dispatch, key]);

  const cerrarSesion = useCallback(async () => {
    await cerrar();
    interrumpida.current = null;
    setRetomar(null);
    dispatch({ type: 'CLEAR_SESSION_DATA' });
    setUsuario(null);
  }, [dispatch]);

  const destinoAcceso = retomar?.desde === key ? retomar.ruta : null;
  const value = useMemo(() => ({ usuario, avisoSesion, destinoAcceso, iniciarSesion, cerrarSesion }),
    [usuario, avisoSesion, destinoAcceso, iniciarSesion, cerrarSesion]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
