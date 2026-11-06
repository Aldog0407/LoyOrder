export function rutaInicial(usuario) {
  switch (usuario?.rol) {
    case 'CLIENTE': return '/menu';
    case 'OPERADOR': return '/operacion';
    case 'ADMINISTRADOR': return '/admin';
    default: return '/login';
  }
}
export function destinoProtegido(usuario, roles) {
  if (!usuario) return '/login';
  return roles.includes(usuario.rol) ? null : rutaInicial(usuario);
}
