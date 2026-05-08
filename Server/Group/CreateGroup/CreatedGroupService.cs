using Microsoft.EntityFrameworkCore;
using NetUtils;
using Server.Data;
using Server.Group.GroupCode;
using Server.Infrastructure;
using System.Net.Sockets;
using static Server.Data.AppDbContext;

namespace Server.Group;

/// <summary>
/// Servicio de aplicación encargado de crear un grupo.
///
/// Responsabilidades:
/// - Leer del socket los datos necesarios para crear el grupo.
/// - Normalizar y validar la entrada.
/// - Generar un código único de grupo.
/// - Persistir el grupo en base de datos.
/// - Asociar el usuario creador al grupo.
/// - Responder al cliente con éxito/error y código de grupo.
///
/// Este servicio forma parte del flujo de lobby, pero no gestiona la sesión
/// en memoria. La creación de la GroupSession se realiza después en LobbyHandler.
/// </summary>
internal sealed class CreateGroupService
{
    #region Constants

    private const string LogContext = nameof(CreateGroupService);

    private const int MinGroupNameLength = 3;
    private const int MaxGroupNameLength = 50;
    private const int MaxGroupLabelLength = 50;
    private const int MaxGroupDescriptionLength = 250;
    private const int MaxGroupMethodLength = 30;

    /// <summary>
    /// Métodos de cálculo soportados actualmente por el servidor.
    ///
    /// De momento solo está habilitado "centroid", que calcula el punto de
    /// encuentro mediante centroide geográfico.
    /// </summary>
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "centroid"
    };

    #endregion

    #region Dependencies

    private readonly AppDbContext _context;

    #endregion

    #region Constructor

    public CreateGroupService(AppDbContext context)
    {
        _context = context;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Ejecuta el flujo completo de creación de grupo.
    ///
    /// Protocolo de entrada desde cliente:
    /// ← [string] groupName
    /// ← [string] groupLabel/category
    /// ← [string] groupDescription
    /// ← [string] groupMethod
    ///
    /// Protocolo de salida hacia cliente:
    /// → [bool] success
    /// → [string] groupCode, solo si success = true
    ///
    /// Devuelve además GroupId y GroupCode al caller para que LobbyHandler
    /// pueda crear la sesión en memoria.
    /// </summary>
    public async Task<(bool Success, int GroupId, string GroupCode)> ExecuteAsync(
        Socket socket,
        User currentUser)
    {
        #region Read payload

        string groupName = Normalize(SocketTools.receiveString(socket));
        string groupLabel = Normalize(SocketTools.receiveString(socket));
        string groupDescription = Normalize(SocketTools.receiveString(socket));
        string groupMethod = Normalize(SocketTools.receiveString(socket));

        #endregion

        #region Validate payload

        if (!IsValidCreateGroupInput(groupName, groupLabel, groupDescription, groupMethod))
        {
            AppLogger.Warn(
                LogContext,
                $"[User:{currentUser.username}] Payload de creación de grupo inválido.");

            SocketTools.sendBool(socket, false);
            return (false, 0, string.Empty);
        }

        #endregion

        #region Generate group code

        string groupCode = await GroupCodeGenerator.CreateUniqueGroupCode(_context);

        #endregion

        #region Persistence transaction

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var groupAdd = new AppDbContext.Group
            {
                code = groupCode,
                name = groupName,
                label = groupLabel,
                description = groupDescription,
                method = groupMethod,
                userId = currentUser.id,
                isActive = true,
                created_at = DateTime.UtcNow
            };

            _context.Groups.Add(groupAdd);
            await _context.SaveChangesAsync();

            var userInGroup = new AppDbContext.UserGroup
            {
                userId = currentUser.id,
                groupId = groupAdd.id
            };

            _context.UsersGroups.Add(userInGroup);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            SocketTools.sendBool(socket, true);
            SocketTools.sendString(groupCode, socket);

            AppLogger.Info(
                LogContext,
                $"[Group:{groupCode}] [User:{currentUser.username}] Grupo creado correctamente.");

            return (true, groupAdd.id, groupCode);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            SocketTools.sendBool(socket, false);

            AppLogger.Error(
                LogContext,
                $"[User:{currentUser.username}] Error creando grupo.\n{ex}");

            return (false, 0, string.Empty);
        }

        #endregion
    }

    #endregion

    #region Validation

    /// <summary>
    /// Valida el payload recibido antes de persistir el grupo.
    ///
    /// Se valida en servidor aunque el cliente también valide, porque el cliente
    /// no es una fuente confiable.
    /// </summary>
    private static bool IsValidCreateGroupInput(
        string groupName,
        string groupLabel,
        string groupDescription,
        string groupMethod)
    {
        if (string.IsNullOrWhiteSpace(groupName) ||
            groupName.Length < MinGroupNameLength ||
            groupName.Length > MaxGroupNameLength)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(groupLabel) ||
            groupLabel.Length > MaxGroupLabelLength)
        {
            return false;
        }

        if (groupDescription.Length > MaxGroupDescriptionLength)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(groupMethod) ||
            groupMethod.Length > MaxGroupMethodLength)
        {
            return false;
        }

        if (!SupportedMethods.Contains(groupMethod))
        {
            return false;
        }

        return true;
    }

    #endregion

    #region Normalization

    /// <summary>
    /// Normaliza texto recibido desde el socket.
    ///
    /// Convierte null en string.Empty y elimina espacios al inicio/final.
    /// </summary>
    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    #endregion
}