using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameServer
{
    class Program
    {
        public const string ipAddr = "34.94.134.79";
        public const int port = 11111;
        public const bool thisTheServer = false;
        static void Main(string[] args)
        {
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

            Console.WriteLine("Server Started...!");
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
    public class Server
    {
        TcpListener server = null;
        public static Dictionary<int,NetworkStream> clientsList = new Dictionary<int, NetworkStream>();
        public const int maxConnections = 10; //randomly picked
        public static Dictionary<int, string> bombGrid = new Dictionary<int, string>();

        public Server(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Any; //vs IPAddress.Parse(ip);
            server = new TcpListener(localAddr, port);
            server.Start();
            StartListener();
            
        }
        public string NumberOfConnections()
        {
            return clientsList.Count.ToString();
        }
        public void ListConnectedUsers()
        {
            Console.Write("Connected Clients: ");
            foreach (KeyValuePair<int, NetworkStream> k in clientsList)
            {
                Console.Write(k.Key.ToString() + ", ");
            }
            Console.Write("\n");

        }
        public void StartListener()
        {
            try
            {
                while (true)
                {
                    Console.WriteLine("Waiting for a connection...");
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine("Connected!");
                    Thread t = new Thread(new ParameterizedThreadStart(HandleDeivce));
                    t.Start(client);  //this will end up rejecting if too many ppl
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException: {0}", e);
                server.Stop();
            }
        }
        public void HandleDeivce(Object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            int clientID = 0;

            //See if we have reached out max number of connecitons
            //If so just close it
            //Right now this does not inform what, probably should add that
            // TODO: inform user connection refused too many connections
            // future work would i guess spin up another vm or server?
            bool rejectConnection = (clientsList.Count >= maxConnections);
            if (rejectConnection)
            {
                client.GetStream().Close();
                client.Close();
                return;
            }

            //Add the new connection to the static dict;
            //Find an open connection number to assign this person
            for (int ii = 0; ii < maxConnections; ii++)
            {
                if(!clientsList.ContainsKey(ii))
                {
                    clientsList.Add(ii, stream);
                    clientID = ii;
                 
                    break;
                }
            }
            ListConnectedUsers();


            Byte[] buffer = new Byte[1024];
            int inputBuffer;

            try
            {
                Stopwatch lastComm = new Stopwatch();
                lastComm.Start();

                while (lastComm.ElapsedMilliseconds < 10000)
                {
                    if (!stream.DataAvailable)
                        continue;

                    inputBuffer = stream.Read(buffer, 0, buffer.Length);

                    lastComm.Restart();

                    string hex = BitConverter.ToString(buffer);
                    string userData = Encoding.ASCII.GetString(buffer, 0, inputBuffer);
                    Console.WriteLine("{1}: Received: {0}", userData, Thread.CurrentThread.ManagedThreadId);

                    string serverResponse = "";

                    string[] parseMsg = userData.Split(",");
                    if (parseMsg[0].Contains("COUNT"))
                    {
                        Console.WriteLine("Client {0} requested server Information", clientID);
                        serverResponse = "Number of Connections: " + NumberOfConnections() + "\n";
                    }
                    else if (parseMsg[0].Contains("BOMBS_GRID"))
                    {
                        Console.WriteLine("Client {0} Sent a Bomb Grid and Tile Click", clientID);
                        serverResponse = "Bomb Grid Stored!\n";
                        bombGrid.Add(clientID, userData); //store unparsed version to send back to a client who asks
                    }
                    else if (parseMsg[0].Contains("GET_BOMBS_GRID"))
                    {
                        string gameNum = (parseMsg.Length > 1) ? parseMsg[1] : "NA";
                        Console.WriteLine("Client {0} Requested a Bomb Grid from Client {1}", clientID, gameNum);
                        serverResponse = bombGrid[int.Parse(gameNum)];
                        bombGrid.Add(clientID, userData); //store unparsed version to send back to a client who asks
                    }
                    else
                    {
                        serverResponse += "Hey Device! Your Client ID is: " + clientID.ToString() + "\n";
                    }

                    
                    Byte[] serverResponseBytes = System.Text.Encoding.ASCII.GetBytes(serverResponse);
                    stream.Write(serverResponseBytes, 0, serverResponseBytes.Length);
                    Console.WriteLine("{1}: Sent: {0}", serverResponse, Thread.CurrentThread.ManagedThreadId);
                }

                Console.WriteLine("Closing Client ID {0}, timeout 5000ms", clientID);
                //remove clienbt id from list
                Console.WriteLine("");
                clientsList.Remove(clientID);
                client.GetStream().Close();
                client.Close();
            }
            catch (Exception e)
            {
                //Console.WriteLine("Exception: {0}", e.ToString());
                Console.WriteLine("Client ID Dropped!: {0}", clientID);
                //remove clienbt id from list
                clientsList.Remove(clientID);
            }
            ListConnectedUsers();
        }
    }

    public class Client
    {
        public void Connect(String server,int port, String message)
        {
            try
            {
                TcpClient client = new TcpClient(server, port);
                NetworkStream stream = client.GetStream();
                int count = 0;
                while (count++ < 3)
                {
                    // Translate the Message into ASCII.
                    Byte[] data = System.Text.Encoding.ASCII.GetBytes(message);
                    // Send the message to the connected TcpServer. 
                    stream.Write(data, 0, data.Length);
                    Console.WriteLine("Sent: {0}", message);

                    // Bytes Array to receive Server Response.
                    data = new Byte[256];
                    String response = String.Empty;
                    // Read the Tcp Server Response Bytes.
                    Int32 bytes = stream.Read(data, 0, data.Length);
                    response = System.Text.Encoding.ASCII.GetString(data, 0, bytes);
                    Console.WriteLine("Received: {0}", response);
                    Thread.Sleep(2000);
                }
                stream.Close();
                client.Close();
            }
            catch (Exception e)
            {
                //Console.WriteLine("Exception: {0}", e);
                Console.WriteLine("Server must have closed the connection!!!!");
            }
            Console.Read();
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
