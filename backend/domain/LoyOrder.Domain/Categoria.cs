namespace LoyOrder.Domain;
// Nombres y tipos del diccionario 3.3. No modifica el catálogo oficial.
public sealed record Categoria(long IdCategoria, long IdRestaurante, string? IdLoyverse, string Nombre, bool Activo);
