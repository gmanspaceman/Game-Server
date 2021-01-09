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
    public class Game
    {
        public enum GameType { Coop };
        public enum GamePhase { PreGame, Playing, Finished };

        public List<int> Players;
        public int CurrentPlayerTurnIndex;
        public int GameId;
        public string CurrentGameState { get; set; }
        public GamePhase GameState;

        public Game(int gameId, int clientId)
        {
            Players = new List<int>();
            Players.Add(clientId);
            GameId = gameId;
            CurrentGameState = string.Empty;
            CurrentPlayerTurnIndex = 0;
            GameState = GamePhase.PreGame;
        }
        public int GetTurnPlayerId()
        {
            return Players[CurrentPlayerTurnIndex];
        }
        public void AddPlayer(int playerIdWantingToJoin)
        {
            foreach (int playerIdAlreadyInGame in Players)
            {
                if (playerIdWantingToJoin == playerIdAlreadyInGame)
                    return;
            }

            Players.Add(playerIdWantingToJoin);

            if (Players.Count == 1)
            {
                CurrentPlayerTurnIndex = 0; //force it to be their turn
            }
        }
        public void DropPlayer(int playerIdWantingToDrop)
        {
            if (Players.Contains(playerIdWantingToDrop))
                Players.Remove(playerIdWantingToDrop);

            if (CurrentPlayerTurnIndex >= Players.Count)
                CurrentPlayerTurnIndex = 0;

            //if (Players.Contains(CurrentPlayerTurnIndex))
            //{
            //    if (Players[CurrentPlayerTurnIndex] == playerIdWantingToDrop)
            //    {
            //        NextTurn();

            //        //auto notify people of turn change
            //        if (Players.Contains(playerIdWantingToDrop))
            //            Players.Remove(playerIdWantingToDrop);

            //        return true;
            //    }
            //}
            //if(Players.Contains(playerIdWantingToDrop))
            //    Players.Remove(playerIdWantingToDrop);
           
            //return false;
        }
        public void NextTurn()
        {
            CurrentPlayerTurnIndex = (CurrentPlayerTurnIndex + 1) % Players.Count;
        }

        public static bool operator ==(Game g1, Game g2)
        {
            return (g1.GameId == g2.GameId);
        }
        public static bool operator !=(Game g1, Game g2)
        {
            return !(g1 == g2);
        }
        public override bool Equals(object o)
        {
            if (o is Game)
                return Equals((Game)o);
            else
                return base.Equals(o);
        }
        public bool Equals(Game g)
        {
            if (g == null)
                return false;
            if (this.GameId == g.GameId)
                return true;
            else
                return false;
        }
    }
}
