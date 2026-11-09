using System.Security.Cryptography;
using LoyOrder.Application;
using LoyOrder.Domain;
using Npgsql;

namespace LoyOrder.Infrastructure.Persistence;

public sealed class DesafioCorreoRepository(NpgsqlDataSource source, IUsuarioRepository usuarios) : IDesafioCorreoRepository
{
    public async Task<Usuario?> UsuarioParaReenvioAsync(Guid anterior, CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                SELECT id_usuario FROM public.desafio_correo WHERE id_desafio=$1 AND proposito=$2
                """);
            cmd.Parameters.AddWithValue(anterior);
            cmd.Parameters.AddWithValue(PropositoCorreo.Verificar);
            return await cmd.ExecuteScalarAsync(ct) is long id ? await usuarios.PorIdAsync(id, ct) : null;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<EmisionDesafio> EmitirAsync(Usuario usuario, NuevoDesafio desafio, Guid? anterior,
        bool reutilizar, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var tx = await conexion.BeginTransactionAsync(ct);
            var actual = await BloquearUsuarioAsync(tx, usuario.IdUsuario, ct);
            if (!PuedeUsar(actual, desafio.Proposito) || actual!.Correo != usuario.Correo ||
                actual.ContrasenaHash != usuario.ContrasenaHash || actual.VersionSesion != usuario.VersionSesion)
                throw new DesafioInvalidoException();

            var ultimo = await UltimoAsync(tx, usuario.IdUsuario, desafio.Proposito, ct);
            if (anterior is Guid id && (ultimo is null || ultimo.Id != id || ultimo.Usado || ultimo.Correo != actual.Correo ||
                ultimo.Edad >= TimeSpan.FromHours(24)))
                throw new DesafioInvalidoException();

            if (ultimo is { Edad.TotalSeconds: < 60 })
            {
                if (reutilizar && ultimo.Vigente && ultimo.Correo == actual.Correo)
                {
                    await tx.CommitAsync(ct);
                    return new(ultimo.Id, false);
                }
                throw new LimiteCorreoException();
            }

            await using (var conteo = Comando(tx, """
                SELECT count(*) FROM public.desafio_correo
                WHERE id_usuario=$1 AND proposito=$2 AND creado_en > clock_timestamp() - interval '1 hour'
                """, usuario.IdUsuario, desafio.Proposito))
            {
                if ((long)(await conteo.ExecuteScalarAsync(ct))! >= 5) throw new LimiteCorreoException();
            }

            await using (var invalidar = Comando(tx, """
                UPDATE public.desafio_correo SET invalidado_en=clock_timestamp()
                WHERE id_usuario=$1 AND proposito=$2 AND usado_en IS NULL AND invalidado_en IS NULL
                """, usuario.IdUsuario, desafio.Proposito))
                await invalidar.ExecuteNonQueryAsync(ct);

            await InsertarAsync(tx, usuario.IdUsuario, actual.Correo, desafio, ct);
            await tx.CommitAsync(ct);
            return new(desafio.Id, true);
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<bool> ConsumirAsync(Guid id, string proposito, byte[] secretoHash,
        Func<string>? nuevaContrasenaHash, CancellationToken ct)
    {
        try
        {
            await using var conexion = await source.OpenConnectionAsync(ct);
            await using var tx = await conexion.BeginTransactionAsync(ct);
            await using var localizar = Comando(tx, """
                SELECT id_usuario FROM public.desafio_correo WHERE id_desafio=$1 AND proposito=$2
                """, id, proposito);
            if (await localizar.ExecuteScalarAsync(ct) is not long usuarioId) return false;
            var usuario = await BloquearUsuarioAsync(tx, usuarioId, ct);
            if (!PuedeUsar(usuario, proposito)) return false;

            await using var consultar = Comando(tx, """
                SELECT secreto_hash FROM public.desafio_correo
                WHERE id_desafio=$1 AND correo=$2 AND usado_en IS NULL AND invalidado_en IS NULL
                    AND expira_en > clock_timestamp()
                """, id, usuario!.Correo);
            if (await consultar.ExecuteScalarAsync(ct) is not byte[] esperado) return false;

            if (!CryptographicOperations.FixedTimeEquals(esperado, secretoHash))
            {
                await using var intento = Comando(tx, """
                    UPDATE public.desafio_correo SET intentos=intentos+1,
                        invalidado_en=CASE WHEN intentos+1 >= 5 THEN clock_timestamp() END
                    WHERE id_desafio=$1
                    """, id);
                await intento.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
                return false;
            }

            // Volver a comprobar la vigencia al consumir y mantener el cambio de cuenta en la misma transacción.
            await using var consumir = Comando(tx, """
                UPDATE public.desafio_correo SET usado_en=clock_timestamp()
                WHERE id_desafio=$1 AND expira_en > clock_timestamp()
                """, id);
            if (await consumir.ExecuteNonQueryAsync(ct) != 1) return false;
            await using var cambiar = nuevaContrasenaHash is null
                ? Comando(tx, "UPDATE public.usuario SET correo_verificado_en=clock_timestamp() WHERE id_usuario=$1", usuarioId)
                : Comando(tx, """
                    UPDATE public.usuario SET contrasena_hash=$1,version_sesion=version_sesion+1 WHERE id_usuario=$2
                    """, nuevaContrasenaHash(), usuarioId);
            await cambiar.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (NpgsqlException) { throw new DependenciaNoDisponibleException(); }
        catch (TimeoutException) { throw new DependenciaNoDisponibleException(); }
    }

    public async Task<bool> DisponibleAsync(CancellationToken ct)
    {
        try
        {
            await using var cmd = source.CreateCommand("""
                SELECT u.correo_verificado_en,u.version_sesion,d.id_desafio,d.id_usuario,d.correo,d.proposito,
                    d.secreto_hash,d.creado_en,d.expira_en,d.usado_en,d.invalidado_en,d.intentos
                FROM public.usuario u CROSS JOIN public.desafio_correo d LIMIT 0
                """);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return true;
        }
        catch (NpgsqlException) { return false; }
        catch (TimeoutException) { return false; }
    }

    internal static async Task InsertarAsync(NpgsqlTransaction tx, long usuarioId, string correo,
        NuevoDesafio desafio, CancellationToken ct)
    {
        await using var insertar = Comando(tx, """
            INSERT INTO public.desafio_correo(id_desafio,id_usuario,correo,proposito,secreto_hash,creado_en,expira_en)
            VALUES ($1,$2,$3,$4,$5,statement_timestamp(),statement_timestamp()+$6)
            """, desafio.Id, usuarioId, correo, desafio.Proposito, desafio.SecretoHash, desafio.Vigencia);
        await insertar.ExecuteNonQueryAsync(ct);
    }

    private static bool PuedeUsar(Cuenta? cuenta, string proposito) =>
        cuenta is { Activo: true } && cuenta.Verificado == (proposito == PropositoCorreo.Restablecer);

    private static async Task<Cuenta?> BloquearUsuarioAsync(NpgsqlTransaction tx, long id, CancellationToken ct)
    {
        await using var cmd = Comando(tx, """
            SELECT correo,activo,correo_verificado_en IS NOT NULL,contrasena_hash,version_sesion
            FROM public.usuario WHERE id_usuario=$1 FOR UPDATE
            """, id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3), reader.GetInt32(4))
            : null;
    }

    private static async Task<UltimoDesafio?> UltimoAsync(NpgsqlTransaction tx, long id, string proposito, CancellationToken ct)
    {
        await using var cmd = Comando(tx, """
            SELECT id_desafio,correo,clock_timestamp()-creado_en,usado_en IS NOT NULL,
                usado_en IS NULL AND invalidado_en IS NULL AND expira_en > clock_timestamp()
            FROM public.desafio_correo WHERE id_usuario=$1 AND proposito=$2
            ORDER BY creado_en DESC,id_desafio DESC LIMIT 1
            """, id, proposito);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetString(1), reader.GetTimeSpan(2), reader.GetBoolean(3), reader.GetBoolean(4))
            : null;
    }

    private static NpgsqlCommand Comando(NpgsqlTransaction tx, string sql, params object[] valores)
    {
        var cmd = new NpgsqlCommand(sql, tx.Connection, tx);
        foreach (var valor in valores) cmd.Parameters.AddWithValue(valor);
        return cmd;
    }

    private sealed record Cuenta(string Correo, bool Activo, bool Verificado, string ContrasenaHash, int VersionSesion);
    private sealed record UltimoDesafio(Guid Id, string Correo, TimeSpan Edad, bool Usado, bool Vigente);
}
