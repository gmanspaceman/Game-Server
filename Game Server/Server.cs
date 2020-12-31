using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Game_Server
{
    public class Server
    {
        TcpListener server = null;
        public static Dictionary<int, NetworkStream> clientsList = new Dictionary<int, NetworkStream>();
        
        public static Dictionary<int, List<int>> gameClientsList = new Dictionary<int, List<int>>();
        public static Dictionary<int, int> clientGameList = new Dictionary<int, int>();

        public static Dictionary<int, int> gameTurnList = new Dictionary<int, int>();

        public const int maxConnections = 10; //randomly picked
        public const int maxGames = 10; //randomly picked

        public const string eom = "<EOM>";

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
                if (!clientsList.ContainsKey(ii))
                {
                    clientsList.Add(ii, stream);
                    clientID = ii;
                    break;
                }
            }
            ListConnectedUsers();


            Byte[] buffer = new Byte[1024];
            int inputBuffer;
            string userData = string.Empty;
            string carryData = string.Empty;
            
            try
            {
                Stopwatch lastComm = new Stopwatch();
                lastComm.Start();

                while (lastComm.ElapsedMilliseconds < 10000)
                {
                    if (!stream.DataAvailable)
                    {
                        Thread.Sleep(125); //slowdown thread
                        continue;
                    }
                    lastComm.Restart();


                    #region Carry Data
                    //will need to just dump carry data if its getting to obigt
                    //this implies more message traffice than the loop can keep up with
                    //should only happen in a debug enviorment
                    userData = carryData;
                    carryData = string.Empty;

                    inputBuffer = stream.Read(buffer, 0, buffer.Length);
                    userData = Encoding.ASCII.GetString(buffer, 0, inputBuffer);

                    if (userData.Contains(eom)) //Find the <EOM> tag
                    {
                        string[] splitInput = userData.Split(new string[] { eom }, StringSplitOptions.None);
                        userData = splitInput[0];

                        for (int ii = 1; ii < splitInput.Length - 1; ii++)
                            carryData += splitInput[ii] + eom;
                        Console.WriteLine("{1}: Extra Data: {0}", carryData, Thread.CurrentThread.ManagedThreadId);
                    }
                    else //patial packet keep the string and append the next read
                    {
                        carryData = userData;
                        Console.WriteLine("{1}: Partial Data: {0}", carryData, Thread.CurrentThread.ManagedThreadId);
                        continue;
                    }
                    userData = userData.Replace(eom, "");
                    #endregion


                    Console.WriteLine("{1}: Received: {0}", userData, Thread.CurrentThread.ManagedThreadId);





                    string serverResponse = "";
                    string[] parseMsg = userData.Split(",");
                    string msgKey = parseMsg[0];
                    int gameId = -1;


                    switch (msgKey)
                    {
                        case "MAKE_GAME":
                            Console.WriteLine("Client {0} wants to start a Game", clientID);

                            for (int newGameId = 0; newGameId < maxGames; newGameId++)
                            {
                                if (!gameClientsList.ContainsKey(newGameId))
                                {
                                    gameClientsList.Add(newGameId, new List<int>());
                                    gameClientsList[newGameId].Add(clientID);
                                    if (!clientGameList.ContainsKey(clientID))
                                    {
                                        clientGameList.Add(clientID, newGameId);
                                    }
                                    else
                                    {
                                        clientGameList[clientID] = newGameId;
                                    }

                                    if (gameTurnList.ContainsKey(newGameId))
                                        gameTurnList.Remove(newGameId);
                                    gameTurnList[newGameId] = 0; //its players 0s turn

                                    break;
                                }
                            }
                            //find lowest availble gameIDnot used
                            //add it to the dict
                            //add this client to player list

                            //serverResponse = "MADE_GAME," + clientGameList[clientID]; //send to player who asked
                            serverResponse = string.Join("," , "JOINED_GAME" , clientGameList[clientID]); //send to player who asked

                            SendServerReponse(serverResponse, clientID);

                            break;
                        case "GET_GAMES":

                            Console.WriteLine("Client {0} wants a List of the Games", clientID);
                            
                            serverResponse = "GAME_LIST"; //Send to both i guess

                            foreach (KeyValuePair<int, List<int>> game in gameClientsList)
                            {
                                serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Count.ToString());
                            }

                            SendServerReponse(serverResponse, clientID);
                            
                            break;
                        case "GAME_INFO":

                            Console.WriteLine("Client {0} wants Info on their game", clientID);

                            if (clientGameList.ContainsKey(clientID) && 
                                gameClientsList.ContainsKey(clientGameList[clientID]))
                            {
                                gameId = clientGameList[clientID];
                                serverResponse = string.Join(",", "GAME_INFO",
                                                                gameId,
                                                                gameClientsList[gameId].Count,
                                                                gameTurnList[gameId]);

                                SendServerReponse(serverResponse, clientID);
                            }

                            break;
                        case "JOIN_GAME":

                            Console.WriteLine("Client {0} wants to Join a Game", clientID);

                            int gameIdToJoin = int.Parse(parseMsg[1]);

                            if (!gameClientsList[gameIdToJoin].Contains(clientID))
                                gameClientsList[gameIdToJoin].Add(clientID);

                            if (!clientGameList.ContainsKey(clientID))
                            {
                                clientGameList.Add(clientID, gameIdToJoin);
                            }
                            else
                            {
                                clientGameList[clientID] = gameIdToJoin;
                            }


                            serverResponse = string.Join(",", "JOINED_GAME", clientGameList[clientID]); //send to player who asked

                            SendServerReponse(serverResponse, clientID);

                            break;
                        case "TILE_CLICKED":

                            Console.WriteLine("Client {0} clicked a Tile: {1}", clientID, userData);
                            
                            SendServerReponse(userData, gameClientsList[clientGameList[clientID]], clientID);

                            gameId = clientGameList[clientID];

                            NextTurn(gameId);

                            break;
                        case "START_GAME":
                            
                            Console.WriteLine("Client {0} Sent a Bomb Grid and Tile Click", clientID);

                            //send it out to each player in the game
                            gameId = clientGameList[clientID];
                            SendServerReponse(userData, gameClientsList[gameId], clientID);

                            NextTurn(gameId);

                            break;
                        case "END_GAME":

                            Console.WriteLine("Client {0} reported Game Over", clientID);
                            //can use gameId in msg or look it up based on player id,
                            //lets use the message value for now;
                            int gameToEnd = int.Parse(parseMsg[1]);
                            RemoveGameFromServerAndClients(gameToEnd);

                            break;
                        case "DROP_GAME":

                            Console.WriteLine("Client {0} dropped their game", clientID);
                            //can use gameId in msg or look it up based on player id,
                            //lets use the message value for now;
                            int gameToDrop = int.Parse(parseMsg[1]); // dont use this, just drop client from all games for nwo
                            RemoveClientFromGames(clientID);

                            NextTurn(gameId);

                            break;
                        case "PING":

                            //DO nothing, pinging to keep connection alive

                            //If connected to a game send back game info 
                            //else just update the server list
                            if (clientGameList.ContainsKey(clientID) &&
                                gameClientsList.ContainsKey(clientGameList[clientID]))
                            {
                                gameId = clientGameList[clientID];
                                serverResponse = string.Join(",", "GAME_INFO",
                                                                gameId,
                                                                gameClientsList[gameId].Count,
                                                                gameTurnList[gameId]);

                                SendServerReponse(serverResponse, clientID);
                            }

                            serverResponse = "GAME_LIST"; //Send to both i guess
                            foreach (KeyValuePair<int, List<int>> game in gameClientsList)
                            {
                                serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Count.ToString());
                            }
                            SendServerReponse(serverResponse, clientID);
                            
                            Console.WriteLine("Client {0} Pinged", clientID);

                            break;
                        default:

                            serverResponse = "Hey Device! Your Client ID is: " + clientID.ToString() + "\n";

                            break;

                    }
                    PrintServerState();


                }

                Console.WriteLine("Closing Client ID {0}, timeout 5000ms", clientID);
                //remove clienbt id from list, and any game ariftifacts
                Console.WriteLine("");

                RemoveClientFromServer(clientID);

                client.GetStream().Close();
                client.Close();
            }
            catch (Exception e)
            {
                //Console.WriteLine("Exception: {0}", e.ToString());
                Console.WriteLine("Client ID Dropped!: {0}", clientID);
                //remove clienbt id from list
                RemoveClientFromServer(clientID);
            }
            ListConnectedUsers();
        }
        public void NextTurn(int gameId)
        {
            gameTurnList[gameId] = (gameTurnList[gameId] + 1) % gameClientsList[gameId].Count; //icnrement whos turn it is

            //okay we will inform the next player
            int playerNumber = gameTurnList[gameId];
            int matchingClientid = gameClientsList[gameId][playerNumber];

            //Thread.Sleep(125);

            string msgKey = "YOUR_TURN";
            SendServerReponse(msgKey, matchingClientid);

            Console.WriteLine("Sent YOUR_TURN to {0}", matchingClientid);
        }
        public void RemoveClientFromServer(int clientId)
        {
            clientsList.Remove(clientId);

            RemoveClientFromGames(clientId);
        }
        public void RemoveClientFromGames(int clientId)
        {
            clientsList.Remove(clientId);

            if (clientGameList.ContainsKey(clientId))
                clientGameList.Remove(clientId);

            foreach (KeyValuePair<int, List<int>> game in gameClientsList)
            {
                if (game.Value.Contains(clientId))
                    game.Value.Remove(clientId);

                if (gameClientsList[game.Key].Count == 0)
                    gameClientsList.Remove(game.Key);
            }
        }
        public void RemoveGameFromServerAndClients(int gameId)
        {
            if (gameClientsList.ContainsKey(gameId))
                gameClientsList.Remove(gameId);

            foreach (KeyValuePair<int, int> client in clientGameList)
            {
                if (client.Value == gameId)
                    clientGameList[client.Key] = -1; //maybe i can remove client form this list instead
            }
        }
        public void PrintServerState()
        {
            Console.WriteLine("");
            Console.WriteLine("==============SERVER STATE===============");
            Console.WriteLine("-----------CONNECTED CLIENTS-------------");
            foreach (int clientId in clientsList.Keys)
                Console.WriteLine("Client {0} is connected", clientId);
            Console.WriteLine("--------------ACTIVE GAMES---------------");
            foreach (KeyValuePair<int, List<int>> k in gameClientsList)
            {
                Console.WriteLine("Game {0} has these clients: {1}. It's {2} turn.", k.Key, string.Join(",",k.Value), gameTurnList[k.Key]);
            }
            Console.WriteLine("============END SERVER STATE=============");
            Console.WriteLine("");

        }


        //These shoudl be able to be combine but i had an issue with something
        
        public void SendServerReponse(string serverResponse, int clientId)
        {
            SendServer(clientsList[clientId], serverResponse);
            //Byte[] serverResponseBytes = System.Text.Encoding.ASCII.GetBytes(serverResponse);
            //clientsList[clientId].Write(serverResponseBytes, 0, serverResponseBytes.Length);
            //Console.WriteLine("{1}: Sent: {0}", serverResponse, Thread.CurrentThread.ManagedThreadId);
        }
        public void SendServerReponse(string serverResponse, List<int> clientIdList)
        {
            //Byte[] serverResponseBytes = System.Text.Encoding.ASCII.GetBytes(serverResponse);
            foreach (int clientId in clientIdList)
            {
                SendServer(clientsList[clientId], serverResponse);
                //clientsList[clientId].Write(serverResponseBytes, 0, serverResponseBytes.Length);
                //Console.WriteLine("{1}: Sent: {0}", serverResponse, Thread.CurrentThread.ManagedThreadId);
            }
        }
        public void SendServerReponse(string serverResponse, List<int> clientIdList, int clientToExclude)
        {
            //Byte[] serverResponseBytes = System.Text.Encoding.ASCII.GetBytes(serverResponse);
            foreach (int clientId in clientIdList)
            {
                if (clientId == clientToExclude)
                    continue;
                SendServer(clientsList[clientId], serverResponse);

                //clientsList[clientId].Write(serverResponseBytes, 0, serverResponseBytes.Length);
                //Console.WriteLine("{1}: Sent: {0}", serverResponse, Thread.CurrentThread.ManagedThreadId);
            }
        }
        public void SendServer(NetworkStream n, string msg)
        {
            msg += eom; //append EOM marker

            Byte[] msgBytes = System.Text.Encoding.ASCII.GetBytes(msg);
            n.Write(msgBytes, 0, msgBytes.Length);
            Console.WriteLine("{1}: Sent: {0}", msg, Thread.CurrentThread.ManagedThreadId);
        }
    }
}
