export const mensajeContrasena = 'La contraseña debe tener entre 8 y 128 caracteres e incluir al menos un símbolo, como !, # o $.';
export const cumpleContrasena = clave => typeof clave === 'string' && clave.length >= 8 && clave.length <= 128 && /[\p{P}\p{S}]/u.test(clave);
