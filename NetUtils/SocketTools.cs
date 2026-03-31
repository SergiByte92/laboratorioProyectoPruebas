using System.Net.Sockets;
using System.Text;

namespace NetUtils
{
    public class SocketTools
    {
        public static byte[] ReceiveExact(Socket socket, int size)
        {
            byte[] buffer = new byte[size];
            int totalRead = 0;

            while (totalRead < size)
            {
                int read = socket.Receive(buffer, totalRead, size - totalRead, SocketFlags.None);

                if (read == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                totalRead += read;
            }

            return buffer;
        }

        public static void sendBool(Socket socket, bool value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            socket.Send(bytes);
        }

        public static bool receiveBool(Socket socket)
        {
            byte[] bytes = ReceiveExact(socket, sizeof(bool));
            return BitConverter.ToBoolean(bytes, 0);
        }
        public static void sendInt(Socket socket, int num)
        {
            byte[] bytes = BitConverter.GetBytes(num);
            socket.Send(bytes);
        }
        public static int receiveInt(Socket socket)
        {
            byte[] bytes = ReceiveExact(socket, sizeof(int));
            return BitConverter.ToInt32(bytes, 0);
        }
        public static string receiveString(Socket socket)
        {
            int length = receiveInt(socket);
            byte[] bytes = ReceiveExact(socket, length);
            return Encoding.UTF8.GetString(bytes);
        }
        public static void sendString(string message, Socket socket)
        {
            // 1. Convertimos el string a bytes PRIMERO
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            // 2. Calculamos el tamaño real en BYTES, no en caracteres
            int size = bytes.Length;

            // 3. Enviamos el tamaño (4 bytes del Int)
            byte[] sizeBytes = BitConverter.GetBytes(size);
            socket.Send(sizeBytes);

            // 4. Enviamos el contenido real
            socket.Send(bytes);
        }
        public static void sendDouble(double coordenadas, Socket socket)
        {
            byte[] bytes = BitConverter.GetBytes(coordenadas);
            socket.Send(bytes);
        }
        public static void sendDate(DateOnly date, Socket socket)
        {
            sendString(date.ToString("yyyy-MM-dd"), socket);
        }
    }
}
