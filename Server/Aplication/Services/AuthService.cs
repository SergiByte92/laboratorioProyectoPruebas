using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Infrastructure;
using static Server.Data.AppDbContext;

namespace Server.Application.Services;

/// <summary>
/// Implementación del servicio de autenticación.
///
/// POR QUÉ ESTÁ AQUÍ Y NO EN PROGRAM.CS:
/// Antes, CheckLogin() y AddUser() eran métodos estáticos en Program.cs.
/// Eso significa que para cambiar cómo se autentica un usuario había que
/// abrir el archivo que también gestiona sockets TCP y sesiones de grupo.
/// Eso viola el Single Responsibility Principle (SRP, la S de SOLID).
///
/// AHORA:
/// Esta clase tiene UNA razón para cambiar: la lógica de autenticación.
/// Si mañana añades BCrypt, hash de contraseñas, o LDAP, solo tocas aquí.
///
/// GANANCIA:
/// → Testeable de forma aislada (sin sockets, sin threads).
/// → Legible: alguien nuevo al proyecto entiende la auth leyendo solo este archivo.
/// → Si en el futuro quieres dos formas de auth (BBDD + OAuth),
///   creas AuthServiceOAuth : IAuthService sin tocar nada más.
/// </summary>
public sealed class AuthService : IAuthService
{
    // La cadena de conexión llega por constructor (inyección de dependencias).
    // AuthService no sabe de dónde viene, solo la usa.
    private readonly string _connectionString;

    public AuthService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public User? Login(string email, string password)
    {
        AppLogger.Debug("AuthService", $"Verificando credenciales para email: {email}");

        // Creamos el contexto aquí (scoped por operación).
        // En una refactorización futura esto pasaría a IUserRepository.
        using AppDbContext context = new AppDbContext(_connectionString);

        User? user = context.Users
            .AsNoTracking() // Solo lectura: más rápido, no trackea el objeto
            .FirstOrDefault(u => u.email == email && u.password == password);

        if (user is null)
            AppLogger.Warn("AuthService", $"Login fallido — email no encontrado o contraseña incorrecta: {email}");
        else
            AppLogger.Info("AuthService", $"[User:{user.username}] Login correcto.");

        return user;
    }

    /// <inheritdoc/>
    public void Register(string username, string email, string password, string birthDateString)
    {
        AppLogger.Info("AuthService", $"Intento de registro — usuario: {username}, email: {email}");

        // Validación de negocio: la fecha debe tener formato correcto
        // antes de llegar a la BBDD.
        if (!DateOnly.TryParse(birthDateString, out DateOnly birthDate))
        {
            AppLogger.Warn("AuthService", $"Fecha de nacimiento inválida: {birthDateString}");
            throw new ArgumentException($"Fecha de nacimiento no válida: {birthDateString}");
        }

        using AppDbContext context = new AppDbContext(_connectionString);

        // Validación de negocio: unicidad de username y email.
        // Hacemos una sola query con OR para evitar dos viajes a BBDD.
        bool exists = context.Users
            .Any(u => u.username == username || u.email == email);

        if (exists)
        {
            AppLogger.Warn("AuthService", $"Registro fallido — username o email ya en uso: {username} / {email}");
            throw new InvalidOperationException("El usuario o email ya existe.");
        }

        User newUser = new User
        {
            username = username,
            email = email,
            password = password,       // TODO: reemplazar por BCrypt hash en la siguiente iteración
            birth_date = birthDate,
            created_at = DateTime.UtcNow
        };

        context.Users.Add(newUser);
        context.SaveChanges();

        AppLogger.Info("AuthService", $"[User:{username}] Registrado correctamente en base de datos.");
    }
}