using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Game_Server
{
    public class Server
    {
        TcpListener server = null;
        public static Dictionary<int, NetworkStream> clientsList = new Dictionary<int, NetworkStream>();
        public static Dictionary<int, int> clientGameList = new Dictionary<int, int>(); 
        public static Dictionary<int, List<int>> gameClientsList = new Dictionary<int, List<int>>();
        public static Dictionary<int, int> gameTurnList = new Dictionary<int, int>();
        public static Dictionary<int, bool> gamePlayingList = new Dictionary<int, bool>();

        public static Dictionary<int, Queue<int>> gameJoiningActiveGame = new Dictionary<int, Queue<int>>();

        public const int maxConnections = 10; //randomly picked
        public const int maxGames = 10; //randomly picked

        public const string eom = "<EOM>";

        public bool isWebSocket = false;

        public Server(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Any; //vs IPAddress.Parse(ip);
            server = new TcpListener(localAddr, port);
            server.Start();
            StartListener();

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

                //new user, request basic information


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
                    string data = Encoding.UTF8.GetString(buffer, 0, inputBuffer);
                    
                    //This mis the newebsocket connection
                    if (new Regex("^GET").IsMatch(data))
                    {
                        Console.WriteLine(data);
                        Byte [] rsp = ServerWebSock.ReplyToGETHandshake(data);
                        stream.Write(rsp, 0, rsp.Length);

                        isWebSocket = true;
                        continue;
                    }

                    if(isWebSocket)
                    {
                        if ((buffer[0] & (byte)ServerWebSock.Opcode.CloseConnection) == (byte)ServerWebSock.Opcode.CloseConnection)
                        {
                            // Close connection request.
                            Console.WriteLine("Client disconnected.");
                            //fornow gonna let timeout code kick
                            //clientSocket.Close();
                            break;
                        }
                        else
                        {
                            Byte[] receivedPayload = ServerWebSock.ParsePayloadFromFrame(buffer);
                            data = Encoding.UTF8.GetString(receivedPayload);

                            Console.WriteLine($"Websocket Client: {data}");

                            //string response = $"ECHO: {data}";
                            //Byte[] dataToSend = ServerWebSock.CreateFrameFromString(response);

                            //Console.WriteLine($"Server: {response}");

                            ////clientSocket.Send(dataToSend);
                            //stream.Write(dataToSend, 0, dataToSend.Length);
                            //continue;
                        }
                    }


                    userData += data;

                    Queue<string> validMessages = new Queue<string>();
                    bool debugMsgQueueingAndCarry = false;
                    if (userData.Contains(eom)) //Find the <EOM> tag
                    {
                        //lets find a way to store all full messages right now
                        //just carry over partial message

                        string[] splitInput = userData.Split(new string[] { eom }, StringSplitOptions.RemoveEmptyEntries);

                        if (userData.EndsWith(eom))
                        {
                            //all messages are full
                            foreach (string msg in splitInput)
                            {
                                validMessages.Enqueue(msg.Replace(eom, ""));
                                if (debugMsgQueueingAndCarry) 
                                    Console.WriteLine("FullMsgQueued: " + msg);
                            }
                        }
                        else
                        {
                            //last message in is partial
                            for (int ii = 0; ii < splitInput.Length - 1; ii++)
                            {
                                validMessages.Enqueue(splitInput[ii].Replace(eom, ""));
                                if (debugMsgQueueingAndCarry) 
                                    Console.WriteLine("FullMsgQueued: " + splitInput[ii]);
                            }
                            carryData = splitInput[splitInput.Length - 1];
                            if (debugMsgQueueingAndCarry) 
                                Console.WriteLine("CarryData: " + carryData);
                        }
                    }
                    else //patial packet keep the string and append the next read
                    {
                        carryData = userData;

                        if (carryData != string.Empty)
                            if (debugMsgQueueingAndCarry) 
                                Console.WriteLine("carryData: " + carryData);

                        continue;
                    }
                    if (validMessages.Count == 0)
                        continue;
                    #endregion

                    while (validMessages.Count != 0)
                    {
                        userData = validMessages.Dequeue();

                        Console.WriteLine("{1}: Processing: {0}", userData, Thread.CurrentThread.ManagedThreadId);

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
                                        gamePlayingList.Add(newGameId, false);
                                        gameJoiningActiveGame.Add(newGameId, new Queue<int>());

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
                                serverResponse = string.Join(",", "JOINED_GAME", clientGameList[clientID]); //send to player who asked

                                SendServerReponse(serverResponse, clientID);
                                BroadcastOutServerList();
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


                                //if the game is already active
                                //added to midgame update queue and request from active player
                                if (gamePlayingList[gameIdToJoin])
                                {
                                    gameJoiningActiveGame[gameIdToJoin].Enqueue(clientID);
                                    GetGameFromPlayerTurn(gameIdToJoin);
                                }

                                BroadcastOutServerList();
                                break;
                            case "MID_GAME":

                                Console.WriteLine("Client {0} Sent a Mid Game Update: {1}", clientID, userData);
                                gameId = clientGameList[clientID];

                                //send this to whomeverasked
                                while (gameJoiningActiveGame[gameId].Count > 0)
                                {
                                    SendServerReponse(userData, gameJoiningActiveGame[gameId].Dequeue());
                                }
                                //SendServerReponse(userData, gameClientsList[gameId], clientID);

                                break;
                            case "TILE_CLICKED":

                                Console.WriteLine("Client {0} clicked a Tile: {1}", clientID, userData);
                                gameId = clientGameList[clientID];

                                SendServerReponse(userData, gameClientsList[gameId], clientID);

                                NextTurn(gameId);

                                break;
                            case "TILE_RIGHTCLICKED":

                                Console.WriteLine("Client {0} right clicked a Tile: {1}", clientID, userData);
                                gameId = clientGameList[clientID];
                                SendServerReponse(userData, gameClientsList[gameId], clientID);

                                break;
                            case "RESTART":

                                Console.WriteLine("Client {0} wanted to play again: {1}", clientID, userData);
                                gameId = clientGameList[clientID];
                                SendServerReponse(userData, gameClientsList[gameId], clientID);
                                NextTurn(gameId);
                                break;
                            case "START_GAME":

                                Console.WriteLine("Client {0} Sent a Bomb Grid and Tile Click", clientID);

                                //send it out to each player in the game
                                gameId = clientGameList[clientID];
                                gamePlayingList[gameId] = true;
                                SendServerReponse(userData, gameClientsList[gameId], clientID);

                                NextTurn(gameId);

                                break;
                            case "END_GAME":

                                Console.WriteLine("Client {0} reported Game Over", clientID);
                                int gameThatEnded = int.Parse(parseMsg[1]);
                                gamePlayingList[gameThatEnded] = false;
                                //original ide was to destory the game
                                //maybe keep it alive and just wait for a restart command
                                //right now we can jsut send YOUR TURN to everyoen to unlock restarting

                                SendServerReponse("YOUR_TURN", gameClientsList[gameThatEnded]);

                                //can use gameId in msg or look it up based on player id,
                                //lets use the message value for now;

                                //RemoveGameFromServerAndClients(gameThatEnded);

                                break;
                            case "DROP_GAME":

                                Console.WriteLine("Client {0} dropped their game", clientID);
                                //can use gameId in msg or look it up based on player id,
                                //lets use the message value for now;
                                int gameToDrop = int.Parse(parseMsg[1]); // dont use this, just drop client from all games for nwo
                                RemoveClientFromGames(clientID);

                                NextTurn(gameId);
                                BroadcastOutServerList();
                                break;
                            case "PING":

                                //DO nothing, pinging to keep connection alive

                                SendServerReponse("PONG", clientID);

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

                                

                                Console.WriteLine("Client {0} Pinged", clientID);

                                break;
                            default:

                                serverResponse = "Hey Device! Your Client ID is: " + clientID.ToString() + "\n";
                                SendServerReponse(serverResponse, clientID);

                                break;

                        }
                        //if (msgKey != "PING")
                        //{
                            PrintServerState();
                        //}

                    }
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
        public void BroadcastOutServerList()
        {
            string serverResponse = "GAME_LIST"; //Send to both i guess
            foreach (KeyValuePair<int, List<int>> game in gameClientsList)
            {
                serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Count.ToString());
            }
            SendServerReponse(serverResponse);
        }

        public void NextTurn(int gameId)
        {
            if (gameClientsList.ContainsKey(gameId))
            {
                gameTurnList[gameId] = (gameTurnList[gameId] + 1) % gameClientsList[gameId].Count; //icnrement whos turn it is

                //okay we will inform the next player
                int playerNumber = gameTurnList[gameId];
                int matchingClientid = gameClientsList[gameId][playerNumber];

                //Thread.Sleep(125);
                SendServerReponse("YOUR_TURN", matchingClientid);

                Console.WriteLine("Sent YOUR_TURN to {0}", matchingClientid);
            }
        }

        public void GetGameFromPlayerTurn(int gameId)
        {
            SendServerReponse("GET_MIDGAME", gameClientsList[gameId][gameTurnList[gameId]]);
        }
        public void RemoveClientFromServer(int clientId)
        {
            clientsList.Remove(clientId);

            RemoveClientFromGames(clientId);
        }
        public void RemoveClientFromGames(int clientId)
        {
            //clientsList.Remove(clientId);

            if (clientGameList.ContainsKey(clientId))
                clientGameList.Remove(clientId);

            

            foreach (KeyValuePair<int, List<int>> game in gameClientsList)
            {
                //incremnt turn on that game if it was his turn
                //i think its easier to incmrent then remove
                //id rather do it the other wya but i think it messes
                //up the mod math and i dont want to look at it right now
                if (game.Value.Contains(clientId))
                {
                    if (game.Value.Count > 1)
                    {
                        if (game.Value[gameTurnList[game.Key]] == clientId)
                        {
                            NextTurn(game.Key);
                        }
                    }

                    game.Value.Remove(clientId);
                

                    if (gameClientsList[game.Key].Count == 0)
                    {
                        gameClientsList.Remove(game.Key);
                        gameTurnList.Remove(game.Key);
                        gamePlayingList.Remove(game.Key);
                        gameJoiningActiveGame.Remove(game.Key);
                        BroadcastOutServerList();
                    }   

                }



            }
        }
        public void RemoveGameFromServerAndClients(int gameId)
        {
            if (gameClientsList.ContainsKey(gameId))
            {
                gameClientsList.Remove(gameId);
                gameTurnList.Remove(gameId);
                gamePlayingList.Remove(gameId);
                gameJoiningActiveGame.Remove(gameId);
                BroadcastOutServerList();
            }

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
                Console.WriteLine("Game {0} has these clients: {1}. It's {2} turn. Playing is {3}", k.Key, string.Join(",",k.Value), gameTurnList[k.Key], gamePlayingList[k.Key]);
            }
            Console.WriteLine("============END SERVER STATE=============");
            Console.WriteLine("");

        }


        //These shoudl be able to be combine but i had an issue with something
        
        public void SendServerReponse(string serverResponse, int clientId)
        {
            SendServer(clientsList[clientId], serverResponse);
        }
        public void SendServerReponse(string serverResponse, List<int> clientIdList)
        {
            foreach (int clientId in clientIdList)
            {
                SendServer(clientsList[clientId], serverResponse);
            }
        }
        public void SendServerReponse(string serverResponse)
        {
            foreach (KeyValuePair<int,NetworkStream> clientId in clientsList)
            {
                SendServer(clientId.Value, serverResponse);
            }
        }
        public void SendServerReponse(string serverResponse, List<int> clientIdList, int clientToExclude)
        {
            foreach (int clientId in clientIdList)
            {
                if (clientId == clientToExclude)
                    continue;
                SendServer(clientsList[clientId], serverResponse);
            }
        }
        public void SendServer(NetworkStream n, string msg)
        {
            msg += eom; //append EOM marker

            if(isWebSocket)
            {
                Byte[] dataToSend = ServerWebSock.CreateFrameFromString(msg);
                n.Write(dataToSend, 0, dataToSend.Length);
                Console.WriteLine($"Websock Server: {msg}");
            }
            else
            {

                Byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(msg);
                n.Write(msgBytes, 0, msgBytes.Length);
                Console.WriteLine("{1}: Sent: {0}", msg, Thread.CurrentThread.ManagedThreadId);
            }

            
        }
    }
}
