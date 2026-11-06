using LoyOrder.Application;
using LoyOrder.Domain;
using Npgsql;
namespace LoyOrder.Infrastructure.Persistence;
public sealed class UsuarioRepository(NpgsqlDataSource source) : IUsuarioRepository
{
    private const string Campos = "SELECT u.id_usuario,u.nombre,u.correo,u.contrasena_hash,u.activo,CASE WHEN e.id_empleado IS NOT NULL THEN e.rol WHEN c.id_cliente IS NOT NULL THEN 'CLIENTE' ELSE '' END FROM usuario u LEFT JOIN empleado e ON e.id_empleado=u.id_usuario LEFT JOIN cliente c ON c.id_cliente=u.id_usuario ";
    public Task<Usuario?> PorCorreoAsync(string correo, CancellationToken ct) => Buscar(Campos+"WHERE lower(trim(u.correo))=$1", correo,ct);
    public Task<Usuario?> PorIdAsync(long id, CancellationToken ct) => Buscar(Campos+"WHERE u.id_usuario=$1", id,ct);
    private async Task<Usuario?> Buscar(string sql, object valor, CancellationToken ct)
    {
        try
        {
            await using var cmd=source.CreateCommand(sql); cmd.Parameters.AddWithValue(valor);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct)) return null;
            var usuario=new Usuario(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetBoolean(4),reader.GetString(5));
            if(await reader.ReadAsync(ct)) throw new DependenciaNoDisponibleException(); // Correos heredados ambiguos.
            return usuario;
        }
        catch(NpgsqlException){throw new DependenciaNoDisponibleException();}
        catch(TimeoutException){throw new DependenciaNoDisponibleException();}
    }
    public async Task<Usuario> RegistrarClienteAsync(string nombre,string correo,string hash,string? telefono,CancellationToken ct)
    {
        try
        {
            await using var connection=await source.OpenConnectionAsync(ct);
            await using var transaction=await connection.BeginTransactionAsync(ct);
            // Serializa el registro de la misma dirección, también con clientes concurrentes.
            await using(var bloqueo=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1,0))",connection,transaction))
            {bloqueo.Parameters.AddWithValue(correo);await bloqueo.ExecuteNonQueryAsync(ct);}
            await using(var existe=new NpgsqlCommand("SELECT 1 FROM usuario WHERE lower(trim(correo))=$1",connection,transaction))
            {existe.Parameters.AddWithValue(correo);if(await existe.ExecuteScalarAsync(ct) is not null) throw new CorreoDuplicadoException();}
            await using var cmd=new NpgsqlCommand("INSERT INTO usuario(nombre,correo,contrasena_hash) VALUES($1,$2,$3) RETURNING id_usuario",connection,transaction);
            cmd.Parameters.AddWithValue(nombre);cmd.Parameters.AddWithValue(correo);cmd.Parameters.AddWithValue(hash);
            var id=(long)(await cmd.ExecuteScalarAsync(ct))!;
            await using var cliente=new NpgsqlCommand("INSERT INTO cliente(id_cliente,telefono) VALUES($1,$2)",connection,transaction);
            cliente.Parameters.AddWithValue(id);cliente.Parameters.Add(new NpgsqlParameter { NpgsqlDbType=NpgsqlTypes.NpgsqlDbType.Varchar,Value=(object?)telefono??DBNull.Value });
            await cliente.ExecuteNonQueryAsync(ct);await transaction.CommitAsync(ct);
            return new(id,nombre,correo,hash,true,Roles.Cliente);
        }
        catch(PostgresException e) when(e.SqlState=="23505"){throw new CorreoDuplicadoException();}
        catch(NpgsqlException){throw new DependenciaNoDisponibleException();}
        catch(TimeoutException){throw new DependenciaNoDisponibleException();}
    }
}
