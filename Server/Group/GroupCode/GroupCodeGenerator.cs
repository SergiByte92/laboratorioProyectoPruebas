using Microsoft.EntityFrameworkCore;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Server.Group.GroupCode
{
    public static class GroupCodeGenerator
    {
        // Eliminamos letras y números confusos (0, O, 1, I, L)
        private static readonly char[] _alphabet =
            "23456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();

        public static string Generate(int length = 6)
        {
            var result = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                // Genera un número aleatorio criptográficamente seguro
                int index = RandomNumberGenerator.GetInt32(_alphabet.Length);
                result.Append(_alphabet[index]);
            }

            return result.ToString();
        }

        public static async  Task<string> CreateUniqueGroupCode(AppDbContext context)
        {
            bool isUnique = false;
            string newCode = string.Empty;

            while (!isUnique)
            {
                // 2. Generamos un código de 6 caracteres
                newCode = GroupCodeGenerator.Generate(6);

                // 3. Comprobamos si existe en la base de datos
                // Asegúrate de usar 'Code' con C mayúscula si así está en tu modelo
                var exists = await context.Groups
                    .AnyAsync(g => g.code == newCode && g.isActive);

                // Si NO existe, hemos terminado (isUnique = true)
                if (!exists)
                {
                    isUnique = true;
                }
            }

            return newCode;
        }

    }
}
