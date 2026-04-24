using System.Collections.Concurrent;
using Server.API;
using Server.UserRouting;

namespace Server.Group.GroupSessions
{
    /// <summary>
    /// Mantiene el estado activo de un grupo en memoria, incluyendo miembros,
    /// ubicaciones recibidas y control del ciclo de vida de la sesión.
    ///
    /// CAMBIO: añade TryClaimRouteCalculation para garantizar una única
    /// consulta OTP por grupo, independientemente de cuántos usuarios
    /// envíen su ubicación simultáneamente.
    /// </summary>
    public sealed class GroupSession
    {
        public int GroupId { get; }
        public string GroupCode { get; }
        public int OwnerUserId { get; }
        public bool HasStarted { get; private set; }

        private readonly ConcurrentDictionary<int, GroupMember> _members = new();
        private readonly ConcurrentDictionary<int, UserLocation> _locations = new();

        // ── Cálculo de ruta centralizado ──────────────────────────────────────
        // Solo UN usuario por grupo ejecuta OTP.
        // El resto espera el mismo Task mediante TaskCompletionSource.
        private TaskCompletionSource<MeetingRouteResult?>? _routeResultTcs;
        private readonly object _routeLock = new();

        public int MemberCount => _members.Count;
        public int LocationCount => _locations.Count;

        public GroupSession(int groupId, string groupCode, int ownerUserId)
        {
            GroupId = groupId;
            GroupCode = groupCode;
            OwnerUserId = ownerUserId;
            HasStarted = false;
        }

        public void AddMember(int userId, string username)
        {
            if (HasStarted)
                return;

            _members.TryAdd(userId, new GroupMember(userId, username));
        }

        public GroupMember? GetMember(int userId)
        {
            _members.TryGetValue(userId, out var member);
            return member;
        }

        public bool RemoveMember(int userId)
        {
            _locations.TryRemove(userId, out _);
            return _members.TryRemove(userId, out _);
        }

        public bool Start()
        {
            if (HasStarted)
                return false;

            HasStarted = true;
            return true;
        }

        public void AddOrUpdateLocation(UserLocation location)
        {
            if (!HasStarted)
                return;

            _locations[location.UserId] = location;
        }

        public UserLocation? GetLocation(int userId)
        {
            _locations.TryGetValue(userId, out var location);
            return location;
        }

        public IReadOnlyCollection<UserLocation> GetAllLocations()
        {
            return _locations.Values.ToList().AsReadOnly();
        }

        public bool AreAllLocationsReceived()
        {
            return MemberCount > 0 && LocationCount == MemberCount;
        }

        // ── Coordinación de cálculo OTP ───────────────────────────────────────

        /// <summary>
        /// Intenta reclamar el derecho a ejecutar la consulta OTP para este grupo.
        ///
        /// Devuelve <c>true</c> si el caller debe ejecutar OTP.
        /// Devuelve <c>false</c> si ya hay un cálculo en curso o completado;
        /// en ese caso <paramref name="resultTask"/> permite esperar el resultado.
        ///
        /// Thread-safe mediante lock.
        /// </summary>
        public bool TryClaimRouteCalculation(out Task<MeetingRouteResult?> resultTask)
        {
            lock (_routeLock)
            {
                if (_routeResultTcs != null)
                {
                    // Ya hay un cálculo en curso o completado: devolver su Task
                    resultTask = _routeResultTcs.Task;
                    return false;
                }

                // Este caller es el responsable de calcular
                _routeResultTcs = new TaskCompletionSource<MeetingRouteResult?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                resultTask = _routeResultTcs.Task;
                return true;
            }
        }

        /// <summary>
        /// Publica el resultado del cálculo OTP para que todos los waiters lo reciban.
        /// </summary>
        public void SetRouteResult(MeetingRouteResult? result)
        {
            _routeResultTcs?.TrySetResult(result);
        }

        /// <summary>
        /// Publica un error del cálculo OTP para que todos los waiters lo reciban.
        /// </summary>
        public void SetRouteError(Exception ex)
        {
            _routeResultTcs?.TrySetException(ex);
        }
    }
}