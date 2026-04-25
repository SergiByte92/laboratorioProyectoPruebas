using NetUtils;
using Server.Application.Services;
using Server.Infrastructure;
using static Server.Data.AppDbContext;

namespace Server.Presentation.Handlers;

/// <summary>
/// Handler de autenticación — capa de presentación TCP.
///
/// POR QUÉ EXISTE ESTA CLASE:
/// Antes, la lógica de "leer bytes del socket → verificar credenciales →
/// escribir respuesta" estaba mezclada en Program.cs junto con
/// la gestión de grupos, el lobby y el cálculo de rutas.
///
/// AuthHandler tiene UNA responsabilidad: traducir entre el protocolo TCP
/// y las operaciones de IAuthService.
///
/// ANALOGÍA:
/// Es como un controlador en MVC. El controlador lee la HTTP request,
/// llama al servicio, y escribe la HTTP response. Aquí igual,
/// pero con bytes TCP en lugar de HTTP.
///
/// GANANCIA:
/// → Puedes leer todo el flujo de auth TCP en un solo archivo.
/// → Si cambias el protocolo (p.ej. a JSON sobre TCP), solo tocas aquí.
/// → AuthService no sabe nada de sockets; AuthHandler no sabe nada de BBDD.
/// </summary>
public sealed class AuthHandler
{
    // Dependemos de la interfaz, no de la clase concreta.
    // Esto es lo que permite el testing sin BBDD real.
    private readonly IAuthService _authService;

    public AuthHandler(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Protocolo de login:
    /// ← [string] email
    /// ← [string] password
    /// → [bool]   success
    ///
    /// Devuelve el User si el login fue correcto, null en caso contrario.
    /// El caller (Program.cs) decide qué hacer según el resultado.
    /// </summary>
    public User? HandleLogin(System.Net.Sockets.Socket socket)
    {
        // Leer del socket — responsabilidad de esta capa
        string email = SocketTools.receiveString(socket);
        string password = SocketTools.receiveString(socket);

        AppLogger.Info("AuthHandler", $"Procesando login para: {email}");

        // Delegar la lógica de negocio al servicio
        User? user = _authService.Login(email, password);

        // Escribir en el socket — responsabilidad de esta capa
        SocketTools.sendBool(socket, user is not null);

        return user;
    }

    /// <summary>
    /// Protocolo de registro:
    /// ← [string] username
    /// ← [string] email
    /// ← [string] password
    /// ← [string] birthDate (yyyy-MM-dd)
    /// → [bool]   success
    /// </summary>
    public void HandleRegister(System.Net.Sockets.Socket socket)
    {
        // Leer todos los datos antes de intentar el registro
        string username = SocketTools.receiveString(socket);
        string email = SocketTools.receiveString(socket);
        string password = SocketTools.receiveString(socket);
        string birthDate = SocketTools.receiveString(socket);

        AppLogger.Info("AuthHandler", $"Procesando registro para: {username}");

        try
        {
            _authService.Register(username, email, password, birthDate);
            SocketTools.sendBool(socket, true);
        }
        catch (Exception ex)
        {
            // El servicio lanza excepciones para indicar fallos de negocio.
            // El handler las captura y las traduce a respuesta TCP.
            AppLogger.Warn("AuthHandler", $"Registro fallido para {username}: {ex.Message}");
            SocketTools.sendBool(socket, false);
        }
    }

    /// <summary>
    /// Protocolo GetHomeData:
    /// → [string] username
    /// </summary>
    public void HandleGetHomeData(System.Net.Sockets.Socket socket, User user)
    {
        AppLogger.Debug("AuthHandler", $"[User:{user.username}] GetHomeData.");
        SocketTools.sendString(user.username, socket);
    }

    /// <summary>
    /// Protocolo GetProfileData:
    /// → [string] username
    /// → [string] email
    /// → [string] birthDate (dd/MM/yyyy)
    /// </summary>
    public void HandleGetProfileData(System.Net.Sockets.Socket socket, User user)
    {
        AppLogger.Info("AuthHandler", $"[User:{user.username}] GetProfileData.");
        SocketTools.sendString(user.username, socket);
        SocketTools.sendString(user.email, socket);
        SocketTools.sendString(user.birth_date.ToString("dd/MM/yyyy"), socket);
        AppLogger.Debug("AuthHandler", $"[User:{user.username}] Datos de perfil enviados.");
    }
}