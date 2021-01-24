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
    public class Player
    {
        public const string eom = "<EOM>";

        public enum ConnectionType { Tcp, Websocket };

        private TcpClient _Client;
        private NetworkStream _Stream;
        private const int _MaxSentHistory = 5;
        private string _CarryData;
        private string _ClientName;

        public bool InGame { get; set; }
        public int CurrentGameId { get; set; }

        public int ClientId { get; }


        public List<string> SentHistory { get; }
        public ConnectionType TypeOfConnection { get; set; }
        public Stopwatch LastMsgRecv;
        public Queue<string> RecvQueue { get; set; }

        public Player(TcpClient client, int clientId)
        {
            _Client = client;
            _Stream = client.GetStream();
            _CarryData = string.Empty;
            _ClientName = string.Empty;

            ClientId = clientId;
            SentHistory = new List<string>();
            TypeOfConnection = ConnectionType.Tcp;
            LastMsgRecv = new Stopwatch();
            LastMsgRecv.Start();
            RecvQueue = new Queue<string>();
            CurrentGameId = -1;
            InGame = false;

        }
        ~Player()
        {
            CloseConnection();
        }
        public static bool operator ==(Player p1, Player p2)
        {
            return (p1.ClientId == p2.ClientId && p1._Stream == p2._Stream);
        }
        public static bool operator !=(Player p1, Player p2)
        {
            return !(p1 == p2);
        }
        public override bool Equals(object o)
        {
            if (o is Player)
                return Equals((Player)o);
            else
                return base.Equals(o);
        }
        public bool Equals(Player p)
        {
            if (p == null)
                return false;
            if (this.ClientId == p.ClientId && this._Stream == p._Stream)
                return true;
            else
                return false;
        }
        public override int GetHashCode()
        {
            return ClientId.GetHashCode() ^ _Stream.GetHashCode();
        }
        public void JoinGame(int gameIdToJoin)
        {
            InGame = true;
            CurrentGameId = gameIdToJoin;
        }
        public void DropGame()
        {
            InGame = false;
            CurrentGameId = -1;
        }
        public void SetClientName(string name)
        {
            //edit name stuff
            name = name.Trim();
            name = name.ToLower();
            name = name[0].ToString().ToUpper() + ((name.Length > 1) ? name.Substring(1) : "");

            _ClientName = name;
        }
        public string GetClientName()
        {
            return (_ClientName == string.Empty) ? ClientId.ToString() : _ClientName;
        }
        public void GetInputBuffer()
        {
            if (!_Stream.DataAvailable)
            {
                Thread.Sleep(125);
                return;
            }

            Byte[] buffer = new Byte[5 * 1024];
            string data = _CarryData;
            _CarryData = string.Empty;
            int recvBuffer = _Stream.Read(buffer, 0, buffer.Length);
            LastMsgRecv.Restart();

            if (TypeOfConnection == ConnectionType.Tcp)
            {
                data += Encoding.UTF8.GetString(buffer, 0, recvBuffer);

                if (new Regex("^GET[^_]").IsMatch(data))
                {
                    Byte[] HandshakeResponse = ServerWebSock.ReplyToGETHandshake(data);
                    _Stream.Write(HandshakeResponse, 0, HandshakeResponse.Length);
                    TypeOfConnection = ConnectionType.Websocket;
                    return; //dont enqueue this mesage, no reason to
                }

            }
            else if (TypeOfConnection == ConnectionType.Websocket)
            {
                if ((buffer[0] & (byte)ServerWebSock.Opcode.CloseConnection) == (byte)ServerWebSock.Opcode.CloseConnection)
                {
                    return; //let server timeout kick the client for now
                }
                else
                {
                    Byte[] receivedPayload = ServerWebSock.ParsePayloadFromFrame(buffer);
                    data += Encoding.UTF8.GetString(receivedPayload);
                }
            }

            SplitAndEnqueueMsgs(data);
        }
        public void SplitAndEnqueueMsgs(string data)
        {
            //valid message to queue lets overs to carry data
            if (data.Contains(eom)) //Find the <EOM> tag
            {
                string[] splitInput = data.Split(new string[] { eom }, StringSplitOptions.RemoveEmptyEntries);

                //last message in is partial
                for (int ii = 0; ii < splitInput.Length - 1; ii++)
                {
                    AddSentToHistory(splitInput[ii]);
                    RecvQueue.Enqueue(splitInput[ii]);
                }

                if (data.EndsWith(eom)) //all messages are full
                {
                    AddSentToHistory(splitInput[splitInput.Length - 1]);
                    RecvQueue.Enqueue(splitInput[splitInput.Length - 1]);
                }
                else
                {
                    _CarryData = splitInput[splitInput.Length - 1];
                }
            }
            else //patial packet keep the string and append the next read
            {
                _CarryData = data;
            }
            //Console.WriteLine("Carrydate: " + _CarryData);
        }
        public void SendToPlayer(string msgToSend)
        {
            msgToSend += eom; //append EOM marker

            if (TypeOfConnection == ConnectionType.Websocket)
            {
                Byte[] dataToSend = ServerWebSock.CreateFrameFromString(msgToSend);
                _Stream.Write(dataToSend, 0, dataToSend.Length);
            }
            else
            {
                Byte[] msgBytes = System.Text.Encoding.UTF8.GetBytes(msgToSend);
                _Stream.Write(msgBytes, 0, msgBytes.Length);
            }
        }

        public void AddSentToHistory(string msgRecv)
        {
            if (msgRecv.Contains("PING"))
                return;

            if (SentHistory.Count >= _MaxSentHistory)
                SentHistory.RemoveAt(0);

            SentHistory.Add(msgRecv);
        }
        public bool IsStale()
        {
            if (LastMsgRecv.ElapsedMilliseconds > 10000)
                return true;
            else
                return false;
        }
        public void CloseConnection()
        {
            _Stream.Close();
            _Client.Close();
        }
    }
}
