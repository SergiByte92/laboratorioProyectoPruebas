using static Server.Data.AppDbContext;

namespace Server.Application.Services;

/// <summary>
/// Contrato del servicio de autenticación.
///
/// POR QUÉ EXISTE ESTA INTERFAZ:
/// - AuthHandler (capa de presentación) necesita hablar con la lógica de auth.
/// - Si AuthHandler dependiera directamente de AuthService (clase concreta),
///   estaría acoplado a EF Core, a la cadena de conexión, a PostgreSQL.
/// - Al depender de esta interfaz, AuthHandler no sabe ni le importa
///   cómo se implementa la autenticación por debajo.
///
/// GANANCIA CONCRETA:
/// → En un test unitario puedes pasar un mock de IAuthService a AuthHandler
///   sin necesitar ni instalar PostgreSQL.
/// → Es Dependency Inversion Principle (DIP, la D de SOLID).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Verifica credenciales contra la base de datos.
    /// Devuelve el usuario si son correctas, null si fallan.
    /// La responsabilidad de enviar la respuesta por el socket
    /// es del AuthHandler, no de este servicio.
    /// </summary>
    User? Login(string email, string password);

    /// <summary>
    /// Crea un nuevo usuario en la base de datos.
    /// Lanza InvalidOperationException si el usuario o email ya existen.
    /// Lanza ArgumentException si la fecha no tiene formato válido.
    /// </summary>
    void Register(string username, string email, string password, string birthDateString);
}