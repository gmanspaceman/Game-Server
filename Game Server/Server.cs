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
        //public static Dictionary<int, NetworkStream> clientsList = new Dictionary<int, NetworkStream>();

        //public static Dictionary<int, string> clientNames = new Dictionary<int, string>();
        //public static Dictionary<int, bool> clientIsWebsock = new Dictionary<int, bool>();
        //public static Dictionary<int, int> clientGameList = new Dictionary<int, int>();
        //public static Dictionary<int, List<string>> clientCommandHistory = new Dictionary<int, List<string>>();

        //public static Dictionary<int, List<int>> gameClientsList = new Dictionary<int, List<int>>();
        //public static Dictionary<int, int> gameTurnList = new Dictionary<int, int>();
        //public static Dictionary<int, bool> gamePlayingList = new Dictionary<int, bool>();

        //public static Dictionary<int, Queue<int>> gameJoiningActiveGame = new Dictionary<int, Queue<int>>();

        public static Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public static Dictionary<int, Game> Games = new Dictionary<int, Game>();


        public const int maxConnections = 10; //randomly picked
        public const int maxGames = 10; //randomly picked

        public const string eom = "<EOM>";

        bool allowClientDebugPrint = false;

        public Server(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Any; //vs IPAddress.Parse(ip);
            server = new TcpListener(localAddr, port);
            server.Start();

            Thread serverStatus = new Thread(serverStatusThread);
            serverStatus.Start();

            StartListener();

        }
        public void serverStatusThread()
        {
            while (true)
            {
                Console.Clear();
                PrintServerState();
                PrintUserHistory();
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(100);
                    Console.Write('.');
                }
            }
        }
        public void PrintUserHistory()
        {
            foreach (KeyValuePair<int, Player> k in Players)
            {
                Console.WriteLine("----Client Id: {0} History----", k.Key);
                foreach (string cmd in k.Value.SentHistory)
                {
                    
                    Console.WriteLine(cmd.Contains("MOVE") ? "MOVE..." : cmd);
                }
                Console.WriteLine("---------------------------");
            }

            //foreach(KeyValuePair<int, List<string>> k in clientCommandHistory)
            //{
            //    Console.WriteLine("----Client Id: {0} History----", k.Key);
            //    foreach (string s in k.Value)
            //    {
            //        Console.WriteLine(s);
            //    }
            //    Console.WriteLine("---------------------------");
            //}
        }
        public void PrintServerState()
        {
            Console.WriteLine("");
            Console.WriteLine("==============SERVER STATE===============");
            Console.WriteLine("-----------CONNECTED CLIENTS-------------");
            foreach (int clientId in Players.Keys)
                Console.WriteLine("Client {0} is connected", clientId);
            Console.WriteLine("--------------ACTIVE GAMES---------------");
            foreach (KeyValuePair<int, Game> k in Games)
            {
                Console.WriteLine("Game {0} has these clients: {1}. It's {2} turn. GameState is {3}",
                                    k.Key,
                                    string.Join(",", k.Value.Players),
                                    k.Value.GetTurnPlayerId(),
                                    Enum.GetName(typeof(Game.GamePhase), k.Value.GameState));
            }
            Console.WriteLine("============END SERVER STATE=============");
            Console.WriteLine("");

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
            int clientID = 0;

            if (Players.Count >= maxConnections)
            {
                TcpClient client = (TcpClient)obj;
                client.GetStream().Close();
                client.Close();
                return;
            }
            else
            {
                for (int newClientId = 0; newClientId < maxConnections; newClientId++)
                {
                    if (!Players.ContainsKey(newClientId))
                    {
                        clientID = newClientId;
                        break;
                    }
                }
            }

            Player ThisPlayer = new Player((TcpClient)obj, clientID);
            Players.Add(ThisPlayer.ClientId, ThisPlayer);


            try
            {
                
                while (!ThisPlayer.IsStale())
                {
                    ThisPlayer.GetInputBuffer();

                    while (ThisPlayer.RecvQueue.Count != 0)
                    {
                        string userData = ThisPlayer.RecvQueue.Dequeue();

                        if (allowClientDebugPrint)
                        {
                            Console.WriteLine("{1}: Processing: {0}", userData, Thread.CurrentThread.ManagedThreadId);
                        }

                        string serverResponse = "";
                        string[] parseMsg = userData.Split(",");
                        string msgKey = parseMsg[0];
                        int gameId = -1;

                        switch (msgKey)
                        {
                            case "WHOAMI":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} asked for their ID", clientID);

                                serverResponse = string.Join(",", "HELLO",
                                                                    ThisPlayer.ClientId); //send to player who asked

                                SendServerReponse(serverResponse, ThisPlayer.ClientId);

                                break;
                            case "I_AM":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} gave their self a name", clientID);

                                ThisPlayer.SetClientName(parseMsg[1]);

                                break;
                            case "MAKE_GAME":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants to start a Game", clientID);
                                
                                for (int newGameId = 0; newGameId < maxGames; newGameId++)
                                {
                                    if(!Games.ContainsKey(newGameId))
                                    {
                                        Game newGame = new Game(newGameId, ThisPlayer.ClientId);
                                        Games.Add(newGame.GameId, newGame);

                                        Games[newGame.GameId].AddPlayer(ThisPlayer.ClientId);
                                        ThisPlayer.JoinGame(newGame.GameId);


                                        serverResponse = string.Join(",", "JOINED_GAME",
                                                                            newGame.GameId,
                                                                            (int)newGame.GameState,
                                                                            newGame.GameId); //send to player who asked
                                        
                                        SendServerReponse(serverResponse, ThisPlayer.ClientId);
                                        BroadcastOutServerList();

                                        break;
                                    }
                                }


                                //for (int newGameId = 0; newGameId < maxGames; newGameId++)
                                //{
                                //    if (!gameClientsList.ContainsKey(newGameId))
                                //    {
                                //        gameClientsList.Add(newGameId, new List<int>());
                                //        gamePlayingList.Add(newGameId, false);
                                //        gameJoiningActiveGame.Add(newGameId, new Queue<int>());

                                //        gameClientsList[newGameId].Add(clientID);
                                //        if (!clientGameList.ContainsKey(clientID))
                                //        {
                                //            clientGameList.Add(clientID, newGameId);
                                //        }
                                //        else
                                //        {
                                //            clientGameList[clientID] = newGameId;
                                //        }

                                //        if (gameTurnList.ContainsKey(newGameId))
                                //            gameTurnList.Remove(newGameId);
                                //        gameTurnList[newGameId] = 0; //its players 0s turn

                                //        break;
                                //    }
                                //}

                                //find lowest availble gameIDnot used
                                //add it to the dict
                                //add this client to player list

                                //serverResponse = "MADE_GAME," + clientGameList[clientID]; //send to player who asked
                                //serverResponse = string.Join(",", "JOINED_GAME", clientGameList[clientID], clientID); //send to player who asked

                                //SendServerReponse(serverResponse, clientID);
                                //BroadcastOutServerList();
                                break;
                            case "GET_GAMES":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants a List of the Games", clientID);

                                foreach (KeyValuePair<int, Game> game in Games)
                                {
                                    serverResponse = string.Join(",", "GAME_LIST", 
                                                                        game.Key.ToString(), 
                                                                        game.Value.Players.Count());
                                }

                                SendServerReponse(serverResponse, ThisPlayer.ClientId);

                                //foreach (KeyValuePair<int, List<int>> game in gameClientsList)
                                //{
                                //    serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Count.ToString());
                                //}

                                break;
                            case "GAME_INFO":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants Info on their game", clientID);

                                if (ThisPlayer.InGame)
                                {
                                    SendGameInfo(ThisPlayer.CurrentGameId, ThisPlayer.ClientId);
                                }

                                //if (clientGameList.ContainsKey(clientID) &&
                                //    gameClientsList.ContainsKey(clientGameList[clientID]))
                                //{
                                //    gameId = clientGameList[clientID];

                                //    int playerTurnId = gameClientsList[gameId][gameTurnList[gameId]];

                                //    string playerIdent = clientNames.ContainsKey(playerTurnId) ? clientNames[playerTurnId] : playerTurnId.ToString();
                                //    serverResponse = string.Join(",", "GAME_INFO",
                                //                                    gameId,
                                //                                    gameClientsList[gameId].Count,
                                //                                    gameClientsList[gameId][gameTurnList[gameId]],
                                //                                    playerIdent);

                                //    SendServerReponse(serverResponse, clientID);
                                //}

                                break;
                            case "JOIN_GAME":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants to Join a Game", clientID);

                                int gameIdToJoin = int.Parse(parseMsg[1]);

                                //If player in a curernt game drop that game
                                if(ThisPlayer.InGame)
                                {
                                    Games[ThisPlayer.CurrentGameId].DropPlayer(ThisPlayer.ClientId);
                                    ThisPlayer.DropGame();
                                }
                                ThisPlayer.JoinGame(gameIdToJoin);
                                Games[gameIdToJoin].AddPlayer(ThisPlayer.ClientId);

                                //if (!gameClientsList[gameIdToJoin].Contains(clientID))
                                //    gameClientsList[gameIdToJoin].Add(clientID);

                                //if (!clientGameList.ContainsKey(clientID))
                                //{
                                //    clientGameList.Add(clientID, gameIdToJoin);
                                //}
                                //else
                                //{
                                //    clientGameList[clientID] = gameIdToJoin;
                                //}

                                if (Games[gameIdToJoin].GameState == Game.GamePhase.PreGame)
                                {

                                    serverResponse = string.Join(",", "JOINED_GAME",
                                                                        (int)Games[gameIdToJoin].GameState,
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        gameIdToJoin);
                                }
                                else if (Games[gameIdToJoin].GameState == Game.GamePhase.Playing)
                                {
                                    serverResponse = string.Join(",", "JOINED_GAME",
                                                                        (int)Games[gameIdToJoin].GameState,
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        gameIdToJoin,
                                                                        Games[gameIdToJoin].CurrentGameState);
                                }
                                else if (Games[gameIdToJoin].GameState == Game.GamePhase.Finished) //dont know what i want to do here
                                {
                                    serverResponse = string.Join(",", "JOINED_GAME",
                                                                        (int)Games[gameIdToJoin].GameState,
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        gameIdToJoin);
                                }
                                SendServerReponse(serverResponse, ThisPlayer.ClientId);
                                BroadcastOutServerList();

                                ////if the game is already active
                                ////added to midgame update queue and request from active player
                                //if (gamePlayingList[gameIdToJoin])
                                //{
                                //    gameJoiningActiveGame[gameIdToJoin].Enqueue(clientID);
                                //    GetGameFromPlayerTurn(gameIdToJoin);
                                //}

                                
                                break;
                            case "MOVE":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} made a move: ", clientID);

                                gameId = ThisPlayer.CurrentGameId;

                                Games[gameId].CurrentGameState = userData;
                                Games[gameId].NextTurn();

                                //sendupdate to everyone
                                serverResponse = string.Join(",", "GAME_UPDATE",
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        Games[gameId].CurrentGameState);

                                SendServerReponse(serverResponse, Games[gameId].Players); 
                                break;
                            //case "MID_GAME":

                            //    if (allowClientDebugPrint)
                            //        Console.WriteLine("Client {0} Sent a Mid Game Update: {1}", clientID, userData);
                            //    gameId = clientGameList[clientID];

                            //    //send this to whomeverasked
                            //    while (gameJoiningActiveGame[gameId].Count > 0)
                            //    {
                            //        SendServerReponse(userData, gameJoiningActiveGame[gameId].Dequeue());
                            //    }
                            //    //SendServerReponse(userData, gameClientsList[gameId], clientID);

                            //    break;
                            //case "TILE_CLICKED":

                            //    if (allowClientDebugPrint)
                            //        Console.WriteLine("Client {0} clicked a Tile: {1}", clientID, userData);
                                
                            //    gameId = clientGameList[clientID];

                            //    SendServerReponse(userData, gameClientsList[gameId], clientID);

                            //    NextTurn(gameId);

                            //    break;
                            //case "TILE_LEFTANDRIGHTCLICKED":

                            //    if (allowClientDebugPrint)
                            //        Console.WriteLine("Client {0} left and right clicked a Tile: {1}", clientID, userData);

                            //    gameId = clientGameList[clientID];

                            //    SendServerReponse(userData, gameClientsList[gameId], clientID);

                            //    NextTurn(gameId);

                            //    break;
                            //case "TILE_RIGHTCLICKED":

                            //    if (allowClientDebugPrint)
                            //        Console.WriteLine("Client {0} right clicked a Tile: {1}", clientID, userData);
                                
                            //    gameId = clientGameList[clientID];
                            //    SendServerReponse(userData, gameClientsList[gameId], clientID);

                            //    break;
                            case "RESTART":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wanted to play again: {1}", clientID, userData);

                                Games[ThisPlayer.CurrentGameId].GameState = Game.GamePhase.PreGame;

                                serverResponse = string.Join(",", "RESTART",
                                                                        ThisPlayer.CurrentGameId, 
                                                                        Games[ThisPlayer.CurrentGameId].GetTurnPlayerId());

                                SendServerReponse(serverResponse, Games[ThisPlayer.CurrentGameId].Players);
                                //gameId = clientGameList[clientID];
                                //SendServerReponse(userData, gameClientsList[gameId], clientID);
                                //NextTurn(gameId);

                                break;
                            //case "START_GAME":

                            //    if (allowClientDebugPrint)
                            //        Console.WriteLine("Client {0} Sent a Bomb Grid and Tile Click", clientID);

                            //    //send it out to each player in the game
                            //    gameId = clientGameList[clientID];
                            //    gamePlayingList[gameId] = true;
                            //    SendServerReponse(userData, gameClientsList[gameId], clientID);

                            //    NextTurn(gameId);

                            //    break;
                            case "END_GAME":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} reported Game Over", clientID);

                                Games[ThisPlayer.CurrentGameId].GameState = Game.GamePhase.Finished;

                                //int gameThatEnded = int.Parse(parseMsg[1]);
                                //gamePlayingList[gameThatEnded] = false;
                                //original ide was to destory the game
                                //maybe keep it alive and just wait for a restart command
                                //right now we can jsut send YOUR TURN to everyoen to unlock restarting

                                //SendServerReponse("YOUR_TURN", gameClientsList[gameThatEnded]);

                                //can use gameId in msg or look it up based on player id,
                                //lets use the message value for now;

                                //RemoveGameFromServerAndClients(gameThatEnded);

                                break;
                            case "DROP_GAME":

                                if (allowClientDebugPrint) 
                                    Console.WriteLine("Client {0} dropped their game", clientID);

                                RemoveClientFromGames(ThisPlayer.ClientId);


                                ////can use gameId in msg or look it up based on player id,
                                ////lets use the message value for now;
                                //int gameToDrop = int.Parse(parseMsg[1]); // dont use this, just drop client from all games for nwo
                                //RemoveClientFromGames(clientID);

                                //NextTurn(gameId);
                                //BroadcastOutServerList();
                                break;
                            case "PING":

                                //DO nothing, pinging to keep connection alive
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} Pinged", clientID);

                                SendServerReponse("PONG", ThisPlayer.ClientId);

                                if (ThisPlayer.InGame)
                                {
                                    SendGameInfo(ThisPlayer.CurrentGameId, ThisPlayer.ClientId);
                                }

                                //If connected to a game send back game info 
                                //else just update the server list
                                //if (clientGameList.ContainsKey(clientID) &&
                                //    gameClientsList.ContainsKey(clientGameList[clientID]))
                                //{
                                //    gameId = clientGameList[clientID];
                                //    int playerTurnId = gameClientsList[gameId][gameTurnList[gameId]];

                                //    string playerIdent = clientNames.ContainsKey(playerTurnId) ? clientNames[playerTurnId] : playerTurnId.ToString();
                                //    serverResponse = string.Join(",", "GAME_INFO",
                                //                                    gameId,
                                //                                    gameClientsList[gameId].Count,
                                //                                    gameClientsList[gameId][gameTurnList[gameId]],
                                //                                    playerIdent);

                                //    SendServerReponse(serverResponse, clientID);
                                //}




                                break;
                            default:

                                if (allowClientDebugPrint)
                                    serverResponse = "Hey Device! Your Client ID is: " + clientID.ToString() + "\n";
                                
                                SendServerReponse(serverResponse, clientID);

                                break;

                        }
                        //if (msgKey != "PING")
                        //{
                            //PrintServerState();
                        //}

                    }
                }
                if (allowClientDebugPrint)
                {
                    Console.WriteLine("Closing Client ID {0}, timeout 5000ms", clientID);
                    //remove clienbt id from list, and any game ariftifacts
                    Console.WriteLine("");
                }
                Console.ReadLine();
                RemoveClientFromServer(clientID);

                ThisPlayer.CloseConnection();
                //client.GetStream().Close();
                //client.Close();
            }
            catch (Exception e)
            {
                //Console.WriteLine("Exception: {0}", e.ToString());
                if (allowClientDebugPrint)
                {
                    Console.WriteLine("Client ID Dropped!: {0}", clientID);
                }
                //remove clienbt id from list
                Console.ReadLine();
                RemoveClientFromServer(clientID);
            }
        }
        public void SendGameInfo(int gameId, int clientId)
        {
            string serverResponse = string.Join(",", "GAME_INFO",
                                    Games[gameId].GameId,
                                    Games[gameId].Players.Count,
                                    Players[Games[gameId].Players[Games[gameId].CurrentPlayerTurnIndex]].ClientId,
                                    Players[Games[gameId].Players[Games[gameId].CurrentPlayerTurnIndex]].GetClientName());

            SendServerReponse(serverResponse, clientId);
        }

        public void BroadcastOutServerList()
        {
            string serverResponse = "GAME_LIST"; //Send to both i guess
            foreach (KeyValuePair<int, Game> game in Games)
            {
                serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Players.Count.ToString());
            }
            //foreach (KeyValuePair<int, List<int>> game in gameClientsList)
            //{
            //    serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Count.ToString());
            //}
            SendServerReponse(serverResponse);
        }

        //public void NextTurn(int gameId)
        //{
        //    if (gameClientsList.ContainsKey(gameId))
        //    {
        //        gameTurnList[gameId] = (gameTurnList[gameId] + 1) % gameClientsList[gameId].Count; //icnrement whos turn it is

        //        //okay we will inform the next player
        //        int playerNumber = gameTurnList[gameId];
        //        int matchingClientid = gameClientsList[gameId][playerNumber];

        //        //Thread.Sleep(125);
        //        SendServerReponse("YOUR_TURN", matchingClientid);
                
        //        if (allowClientDebugPrint)
        //            Console.WriteLine("Sent YOUR_TURN to {0}", matchingClientid);
        //    }
        //}

        //public void GetGameFromPlayerTurn(int gameId)
        //{
        //    SendServerReponse("GET_MIDGAME", gameClientsList[gameId][gameTurnList[gameId]]);
        //}
        public void RemoveClientFromServer(int clientId)
        {
            RemoveClientFromGames(clientId);
            //Players[clientId].DropGame();
            if (Players.ContainsKey(clientId))
                Players.Remove(clientId);

            //clientsList.Remove(clientId);
            //clientCommandHistory.Remove(clientId);
            //clientIsWebsock.Remove(clientId);
            //if (clientNames.ContainsKey(clientId))
            //    clientNames.Remove(clientId);

            //RemoveClientFromGames(clientId);
        }
        public void RemoveClientFromGames(int clientId)
        {
            Console.WriteLine("here1");
            if (Players.ContainsKey(clientId))
            {
                Console.WriteLine("here2");
                if (Players[clientId].InGame)
                {
                    int gameId = Players[clientId].CurrentGameId;
                    Console.WriteLine("here3");
                    if (Games.ContainsKey(gameId))
                    {
                        Console.WriteLine("here4");
                        if (Games[gameId].DropPlayer(clientId))
                        {
                            Console.WriteLine("here5");
                            if (Games[gameId].Players.Count != 0)
                            {
                                Console.WriteLine("here6");
                                string serverResponse = string.Join(",", "GAME_UPDATE",
                                                                Games[gameId].GetTurnPlayerId(),
                                                                (int)Games[gameId].GameState);
                                Console.WriteLine("here7");
                                SendServerReponse(serverResponse, Games[gameId].Players);
                            }
                        }
                    }
                    Console.WriteLine("here8");

                    if (Games.ContainsKey(gameId) && Games[gameId].Players.Count == 0)
                    {
                        Console.WriteLine("here9");
                        RemoveGameFromServer(gameId);
                        Console.WriteLine("here10");
                    }
                    else
                    {
                        Console.WriteLine("here11");
                        BroadcastOutServerList();
                        Console.WriteLine("here12");
                    }
                }
            }

            
            ////clientsList.Remove(clientId);
            ////better if go through those games and remove client and then remove game
            ////todo for later i guess
            //if (clientGameList.ContainsKey(clientId))
            //{
            //    int gameId = clientGameList[clientId];

            //    if (gameClientsList.ContainsKey(gameId))        //has to contain it 
            //        gameClientsList[gameId].Remove(clientId);

            //    clientGameList.Remove(clientId);

            //    if (gameClientsList[gameId].Count == 0)     //if no one left in the game destroy it
            //    {
            //        RemoveGameFromServerAndClients(gameId);
            //        //this broadcasts its own
            //    }
            //    else
            //    {
            //        NextTurn(gameId);
            //        BroadcastOutServerList();
            //    }
            //}
        }
        public void RemoveGameFromServer(int gameId)
        {
            if (Games.ContainsKey(gameId))
            {
                foreach (int clietnId in Games[gameId].Players)
                {
                    Players[clietnId].DropGame();
                }
                Games.Remove(gameId);

                BroadcastOutServerList();
            }
            
            //if (gameClientsList.ContainsKey(gameId))
            //{
            //    gameClientsList.Remove(gameId);
            //    gameTurnList.Remove(gameId);
            //    gamePlayingList.Remove(gameId);
            //    gameJoiningActiveGame.Remove(gameId);

            //    foreach (int client in gameClientsList[gameId])
            //    {
            //        clientGameList.Remove(client);
            //    }

            //    BroadcastOutServerList();
            //}
        }



        //These shoudl be able to be combine but i had an issue with something
        public void SendServerReponse(string serverResponse, int clientId)
        {
            SendServer(clientId, serverResponse);
        }
        public void SendServerReponse(string serverResponse, List<int> clientIdList)
        {
            foreach (int clientId in clientIdList)
            {
                SendServer(clientId, serverResponse);
            }
        }
        public void SendServerReponse(string serverResponse)
        {
            foreach (int clientId in Players.Keys)
            {
                SendServer(clientId, serverResponse);
            }
            //foreach (KeyValuePair<int,NetworkStream> clientId in clientsList)
            //{
            //    SendServer(clientId.Key, serverResponse);
            //}
        }
        public void SendServerReponse(string serverResponse, List<int> clientIdList, int clientToExclude)
        {
            foreach (int clientId in clientIdList)
            {
                if (clientId == clientToExclude)
                    continue;
                SendServer(clientId, serverResponse);
            }
        }
        public void SendServer(int clientId, string msg)
        {
            Players[clientId].SendToPlayer(msg);

            //NetworkStream n = clientsList[clientId];
            //msg += eom; //append EOM marker

            //if (clientIsWebsock[clientId])
            //{
            //    Byte[] dataToSend = ServerWebSock.CreateFrameFromString(msg);
            //    n.Write(dataToSend, 0, dataToSend.Length);
            //    if (allowClientDebugPrint)
            //        Console.WriteLine($"Websock Server: {msg}");
            //}
            //else
            //{
            //    Byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(msg);
            //    n.Write(msgBytes, 0, msgBytes.Length);
            //    if (allowClientDebugPrint)
            //        Console.WriteLine("{1}: Sent: {0}", msg, Thread.CurrentThread.ManagedThreadId);
            //}
        }
    }
}
