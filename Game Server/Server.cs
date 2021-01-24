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

        bool allowClientDebugPrint = true;

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
                //Console.Clear();
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
                                        int col = int.Parse(parseMsg[1]);
                                        int row = int.Parse(parseMsg[2]);
                                        int mines = int.Parse(parseMsg[3]);

                                        Game newGame = new Game(newGameId, ThisPlayer.ClientId, col, row, mines);
                                        Games.Add(newGame.GameId, newGame);

                                        Games[newGame.GameId].AddPlayer(ThisPlayer.ClientId);
                                        ThisPlayer.JoinGame(newGame.GameId);

                                        serverResponse = string.Join(",", "JOINED_GAME",
                                                                            (int)Games[newGame.GameId].GameState,
                                                                            Games[newGame.GameId].GetTurnPlayerId(),
                                                                            Players[Games[newGame.GameId].GetTurnPlayerId()].GetClientName(),
                                                                            newGame.GameId, 
                                                                            col,
                                                                            row,
                                                                            mines,
                                                                            Games[newGame.GameId].CurrentGameState);

                                        SendServerReponse(serverResponse, ThisPlayer.ClientId);
                                        BroadcastOutServerList();

                                        break;
                                    }
                                }
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

                                break;
                            case "GAME_INFO":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants Info on their game", clientID);

                                if (ThisPlayer.InGame)
                                {
                                    SendGameInfo(ThisPlayer.CurrentGameId, ThisPlayer.ClientId);
                                }

                                break;
                            case "JOIN_GAME":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants to Join a Game", clientID);

                                int gameIdToJoin = int.Parse(parseMsg[1]);

                                //If player in a curernt game drop that game
                                if (ThisPlayer.CurrentGameId != gameIdToJoin)
                                {
                                    if (ThisPlayer.InGame)
                                    {
                                        RemoveClientFromGames(ThisPlayer.ClientId);
                                        ThisPlayer.DropGame();
                                    }
                                    ThisPlayer.JoinGame(gameIdToJoin);
                                    Games[gameIdToJoin].AddPlayer(ThisPlayer.ClientId);

                                    if (Games[gameIdToJoin].GameState == Game.GamePhase.PreGame)
                                    {

                                        serverResponse = string.Join(",", "JOINED_GAME",
                                                                            (int)Games[gameIdToJoin].GameState,
                                                                            Games[gameIdToJoin].GetTurnPlayerId(),
                                                                            Players[Games[gameIdToJoin].GetTurnPlayerId()].GetClientName(),
                                                                            gameIdToJoin,
                                                                            Games[gameIdToJoin].col,
                                                                            Games[gameIdToJoin].row,
                                                                            Games[gameIdToJoin].mines,
                                                                            Games[gameIdToJoin].CurrentGameState);
                                    }
                                    else if (Games[gameIdToJoin].GameState == Game.GamePhase.Playing)
                                    {
                                        serverResponse = string.Join(",", "JOINED_GAME",
                                                                            (int)Games[gameIdToJoin].GameState,
                                                                            Games[gameIdToJoin].GetTurnPlayerId(),
                                                                            Players[Games[gameIdToJoin].GetTurnPlayerId()].GetClientName(),
                                                                            gameIdToJoin,
                                                                            Games[gameIdToJoin].col,
                                                                            Games[gameIdToJoin].row,
                                                                            Games[gameIdToJoin].mines,
                                                                            Games[gameIdToJoin].CurrentGameState);
                                    }
                                    else if (Games[gameIdToJoin].GameState == Game.GamePhase.Finished) //dont know what i want to do here
                                    {
                                        serverResponse = string.Join(",", "JOINED_GAME",
                                                                            (int)Games[gameIdToJoin].GameState,
                                                                            Games[gameIdToJoin].GetTurnPlayerId(),
                                                                            Players[Games[gameIdToJoin].GetTurnPlayerId()].GetClientName(),
                                                                            gameIdToJoin,
                                                                            Games[gameIdToJoin].col,
                                                                            Games[gameIdToJoin].row,
                                                                            Games[gameIdToJoin].mines,
                                                                            Games[gameIdToJoin].CurrentGameState);
                                    }
                                    SendServerReponse(serverResponse, ThisPlayer.ClientId);
                                    BroadcastOutServerList();
                                }

                                break;
                            case "MOVE":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} made a move: ", clientID);

                                gameId = ThisPlayer.CurrentGameId;
                                //Console.WriteLine(gameId);
                                if (Games[gameId].GameState != Game.GamePhase.Finished)
                                    Games[gameId].GameState = Game.GamePhase.Playing;

                                Games[gameId].CurrentGameState = userData;
                                Games[gameId].NextTurn();
                                //Console.WriteLine("here1");
                                //sendupdate to everyone
                                serverResponse = string.Join(",", "GAME_UPDATE",
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        Players[Games[gameId].GetTurnPlayerId()].GetClientName(),
                                                                        Games[gameId].CurrentGameState);
                                //Console.WriteLine(serverResponse);
                                SendServerReponse(serverResponse, Games[gameId].Players); 
                                break;
                            case "PASS":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} Passed: ", clientID);

                                gameId = ThisPlayer.CurrentGameId;
                                Games[gameId].NextTurn();
                                //Console.WriteLine("here1");
                                //sendupdate to everyone
                                serverResponse = string.Join(",", "GAME_UPDATE",
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        Players[Games[gameId].GetTurnPlayerId()].GetClientName(),
                                                                        Games[gameId].CurrentGameState);
                                //Console.WriteLine(serverResponse);
                                SendServerReponse(serverResponse, Games[gameId].Players);
                                break;
                            case "TURN_LIST":
                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wants the turn list: ", clientID);

                                gameId = ThisPlayer.CurrentGameId;

                                serverResponse = "TURN_LIST";
                                for (int ii = 0; ii < Games[gameId].Players.Count; ii++)
                                {
                                    int index = (Games[gameId].CurrentPlayerTurnIndex + ii) % Games[gameId].Players.Count;
                                    
                                    serverResponse = string.Join(",", serverResponse,
                                                                       Players[Games[gameId].Players[index]].GetClientName());
                                }

                                
                                //Console.WriteLine(serverResponse);
                                SendServerReponse(serverResponse, clientID);
                                break;
                            case "RESTART":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} wanted to play again: {1}", clientID, userData);

                                Games[ThisPlayer.CurrentGameId].GameState = Game.GamePhase.PreGame;

                                serverResponse = string.Join(",", "RESTART",
                                                                        ThisPlayer.CurrentGameId, 
                                                                        Games[ThisPlayer.CurrentGameId].GetTurnPlayerId(),
                                                                        Players[Games[ThisPlayer.CurrentGameId].GetTurnPlayerId()].GetClientName());

                                SendServerReponse(serverResponse, Games[ThisPlayer.CurrentGameId].Players);

                                break;
                            case "END_GAME":

                                if (allowClientDebugPrint)
                                    Console.WriteLine("Client {0} reported Game Over", clientID);

                                Games[ThisPlayer.CurrentGameId].GameState = Game.GamePhase.Finished;

                                break;
                            case "DROP_GAME":

                                if (allowClientDebugPrint) 
                                    Console.WriteLine("Client {0} dropped their game", clientID);

                                RemoveClientFromGames(ThisPlayer.ClientId);
                                ThisPlayer.DropGame();

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
                //Console.ReadLine();
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
                //Console.ReadLine();
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

        public void BroadcastOutServerList(int clientToExlude = -1)
        {
            string serverResponse = "GAME_LIST"; //Send to both i guess
            foreach (KeyValuePair<int, Game> game in Games)
            {
                serverResponse = string.Join(",", serverResponse, 
                                                    game.Key.ToString(), 
                                                    game.Value.Players.Count.ToString());
            }
            //foreach (KeyValuePair<int, List<int>> game in gameClientsList)
            //{
            //    serverResponse = string.Join(",", serverResponse, game.Key.ToString(), game.Value.Count.ToString());
            //}
            if (clientToExlude == -1)
                SendServerReponse(serverResponse);
            else
                SendServerReponse(serverResponse, Players.Keys.ToList<int>(), clientToExlude);
        }

        public void RemoveClientFromServer(int clientId)
        {
            RemoveClientFromGames(clientId);
            //Players[clientId].DropGame();
            if (Players.ContainsKey(clientId))
                Players.Remove(clientId);


        }
        public void RemoveClientFromGames(int clientId)
        {
            if (Players.ContainsKey(clientId))
            {
                if (Players[clientId].InGame)
                {
                    int gameId = Players[clientId].CurrentGameId;
                    if (Games.ContainsKey(gameId))
                    {
                        Games[gameId].DropPlayer(clientId);
                        if (Games[gameId].Players.Count != 0)
                        {
                            //string serverResponse = string.Join(",", "GAME_UPDATE",
                            //                                Games[gameId].GetTurnPlayerId(),
                            //                                Games[gameId].CurrentGameState);

                            string serverResponse = string.Join(",", "GAME_UPDATE",
                                                                        Games[gameId].GetTurnPlayerId(),
                                                                        Players[Games[gameId].GetTurnPlayerId()].GetClientName(),
                                                                        Games[gameId].CurrentGameState);

                            SendServerReponse(serverResponse, Games[gameId].Players);
                        }
                    }

                    if (Games.ContainsKey(gameId) && Games[gameId].Players.Count == 0)
                    {
                        RemoveGameFromServer(gameId);
                    }
                    else
                    {
                        BroadcastOutServerList(clientId);
                    }
                }
            }
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
