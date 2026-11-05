export const categories = ['Todos', 'Hamburguesas', 'Pizza', 'Ensaladas', 'Bebidas', 'Postres'];

export const dishes = [
  { id: 1, name: 'Burger de la casa', category: 'Hamburguesas', description: 'Carne a la parrilla, queso cheddar, lechuga fresca y nuestra salsa especial.', price: 149, emoji: '🍔', color: '#FFE7CE', rating: '4.9', available: true, variants: ['Clásica', 'Doble carne', 'Vegetariana'] },
  { id: 2, name: 'Pizza Margherita', category: 'Pizza', description: 'Masa artesanal, mozzarella, tomate y albahaca fresca.', price: 189, emoji: '🍕', color: '#FFE3CF', rating: '4.8', available: true, variants: ['Mediana', 'Grande'] },
  { id: 3, name: 'Bowl verde', category: 'Ensaladas', description: 'Mezcla de hojas, aguacate, quinoa y aderezo cítrico.', price: 129, emoji: '🥗', color: '#E6F4DD', rating: '4.7', available: true, variants: ['Aderezo aparte', 'Aderezo incluido'] },
  { id: 4, name: 'Papas crujientes', category: 'Hamburguesas', description: 'Papas doraditas con especias de la casa y dip de queso.', price: 75, emoji: '🍟', color: '#FFF0CD', rating: '4.8', available: true, variants: ['Chicas', 'Grandes'] },
  { id: 5, name: 'Limonada de frutos rojos', category: 'Bebidas', description: 'Refrescante limonada natural con frutos rojos.', price: 65, emoji: '🍹', color: '#FCE1E6', rating: '4.9', available: true, variants: ['Natural', 'Mineral'] },
  { id: 6, name: 'Cheesecake de fresa', category: 'Postres', description: 'Cremoso cheesecake con coulis de fresa artesanal.', price: 99, emoji: '🍰', color: '#FFE3ED', rating: '4.9', available: false, variants: ['Porción individual'] },
  { id: 7, name: 'Pizza pepperoni', category: 'Pizza', description: 'Mozzarella fundida, salsa de tomate y pepperoni.', price: 209, emoji: '🍕', color: '#FDE0C8', rating: '4.8', available: true, variants: ['Mediana', 'Grande'] },
  { id: 8, name: 'Brownie con helado', category: 'Postres', description: 'Brownie tibio de chocolate con una bola de helado.', price: 109, emoji: '🍫', color: '#EEE0D4', rating: '4.7', available: true, variants: ['Vainilla', 'Chocolate'] },
];

export const seededOrders = [
  { id: 'LY-1042', date: '03 nov 2026', items: [{ dishId: 2, variant: 'Mediana', quantity: 1, price: 189 }, { dishId: 5, variant: 'Natural', quantity: 2, price: 65 }], total: 319, status: 3, delivery: 'local', payment: 'tarjeta', rated: false },
  { id: 'LY-1038', date: '29 oct 2026', items: [{ dishId: 1, variant: 'Clásica', quantity: 2, price: 149 }], total: 333, status: 3, delivery: 'domicilio', payment: 'efectivo', rated: true },
  { id: 'LY-1029', date: '22 oct 2026', items: [{ dishId: 3, variant: 'Aderezo incluido', quantity: 1, price: 129 }], total: 129, status: 3, delivery: 'local', payment: 'tarjeta', rated: true },
];

export const money = value => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 0 }).format(value);
export const findDish = id => dishes.find(d => d.id === id);
