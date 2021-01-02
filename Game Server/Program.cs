using System;
using System.Net.Sockets;
using System.Threading;

namespace Game_Server
{
    public class Program
    {
        public const string ipAddr = "34.94.134.79";
        public const int port = 11111;

        public static bool thisTheServer = false;
        static void Main(string[] args)
        {
            if (args.Length == 1)
                if (args[0] == "server")
                    thisTheServer = true;

            if (thisTheServer)
            {
                StartServer();
            }
            else
            {
                Console.WriteLine("Press Enter to spawn a New Client. Enter any key to quit."); 
                while (true)
                {
                    StartClient();
                    string k = Console.ReadLine();

                    if (k.Length > 0 )
                        break;
                }
                
            }
        }

        public static void StartServer()
        {
            Thread t = new Thread(delegate ()
            {
                Server myserver = new Server(ipAddr, port);
            });
            t.Start();

            Console.WriteLine("Server Started...! Version 2.1");
        }

        public static void StartClient()
        {
            new Thread(() =>
            {
                Client client = new Client();
                Thread.CurrentThread.IsBackground = true;
                client.Connect(ipAddr, port, "COUNT Hello I'm Device 1...");
            }).Start();
        }
    }

    public static class SocketExtensions
    {
        public static bool IsConnected(this Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
        }
    }
}
