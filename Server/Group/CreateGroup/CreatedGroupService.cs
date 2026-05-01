using Microsoft.EntityFrameworkCore;
using NetUtils;
using Server.Data;
using Server.Group.GroupCode;
using System.Net.Sockets;
using static Server.Data.AppDbContext;

namespace Server.Group
{
    /// <summary>
    /// Orquesta la creación de un grupo a partir de los datos recibidos del cliente,
    /// validando la entrada, generando un código único y persistiendo la información
    /// necesaria en la base de datos.
    /// </summary>
    internal class CreateGroupService
    {
        private const int MinGroupNameLength = 3;
        private const int MaxGroupNameLength = 50;
        private const int MaxGroupLabelLength = 50;
        private const int MaxGroupDescriptionLength = 250;
        private const int MaxGroupMethodLength = 30;

        private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "centroid"
        };

        private readonly AppDbContext _context;

        public CreateGroupService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(bool Success, int GroupId, string GroupCode)> ExecuteAsync(
            Socket socket,
            User currentUser)
        {
            string groupName = Normalize(SocketTools.receiveString(socket));
            string groupLabel = Normalize(SocketTools.receiveString(socket));
            string groupDescription = Normalize(SocketTools.receiveString(socket));
            string groupMethod = Normalize(SocketTools.receiveString(socket));

            if (!IsValidCreateGroupInput(groupName, groupLabel, groupDescription, groupMethod))
            {
                SocketTools.sendBool(socket, false);
                return (false, 0, string.Empty);
            }

            string groupCode = await GroupCodeGenerator.CreateUniqueGroupCode(_context);

            using var transaction = await _context.Database.BeginTransactionAsync();

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

                return (true, groupAdd.id, groupCode);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                SocketTools.sendBool(socket, false);

                Console.WriteLine("Error creando grupo:");
                Console.WriteLine(ex);

                return (false, 0, string.Empty);
            }
        }

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
                return false;

            if (string.IsNullOrWhiteSpace(groupMethod) ||
                groupMethod.Length > MaxGroupMethodLength)
            {
                return false;
            }

            if (!SupportedMethods.Contains(groupMethod))
                return false;

            return true;
        }

        private static string Normalize(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
