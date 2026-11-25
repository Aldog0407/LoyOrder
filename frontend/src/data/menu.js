// DATOS DE DEMOSTRACIÓN. El menú del cliente ya viene de la API (7.2.4, services/catalogo.js).
// Solo los siguen usando el historial y el seguimiento de demostración (7.2.14 y 7.2.10).
export { money } from '../utils/formato.js';
export const dishes = [
  { id: 1, name: 'Burger de la casa', category: 'Hamburguesas', description: 'Carne a la parrilla, cheddar y salsa especial.', price: 149, emoji: '🍔', color: '#FFE7CE', rating: 4.9, available: true, variants: ['Clásica', 'Doble carne', 'Vegetariana'] },
  { id: 2, name: 'Pizza Margherita', category: 'Pizza', description: 'Mozzarella, tomate y albahaca fresca.', price: 189, emoji: '🍕', color: '#FFE3CF', rating: 4.8, available: true, variants: ['Mediana', 'Grande'] },
  { id: 3, name: 'Bowl verde', category: 'Ensaladas', description: 'Hojas frescas, aguacate, quinoa y cítricos.', price: 129, emoji: '🥗', color: '#E6F4DD', rating: 4.7, available: true, variants: ['Aderezo aparte', 'Aderezo incluido'] },
  { id: 4, name: 'Papas crujientes', category: 'Hamburguesas', description: 'Papas con especias y dip de queso.', price: 75, emoji: '🍟', color: '#FFF0CD', rating: 4.8, available: true, variants: ['Chicas', 'Grandes'] },
  { id: 5, name: 'Limonada de frutos rojos', category: 'Bebidas', description: 'Limonada natural con frutos rojos.', price: 65, emoji: '🍹', color: '#FCE1E6', rating: 4.9, available: true, variants: ['Natural', 'Mineral'] },
  { id: 6, name: 'Cheesecake de fresa', category: 'Postres', description: 'Cheesecake con coulis de fresa.', price: 99, emoji: '🍰', color: '#FFE3ED', rating: 4.9, available: false, variants: ['Individual'] },
];
export const getDish = id => dishes.find(dish => dish.id === id);
