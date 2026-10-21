using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Akropolis.Net
{
    public sealed class LobbySeat
    {
        public string Name;
        public bool IsAI;
        public ulong ClientId;
    }

    /// Online play: a Unity Multiplayer session (Lobby + Relay) for codes and the public list, and
    /// Netcode custom messages for the game itself. The host owns the rules: clients send their moves,
    /// the host checks and relays them, and every peer replays the same moves on the same seeded GameState.
    public sealed class NetSession : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        const string Channel = "akropolis";

        enum Msg : byte { Hello, Lobby, Start, Action, SeatToAI, Chat, Emote, Bell, Cursor }

        public const int MaxChatLength = 140;

        public event Action LobbyChanged;
        /// seed, roster, local seat
        public event Action<int, List<(string name, bool ai)>, int> GameStarted;
        /// move, sender client id
        public event Action<GameAction, ulong> ActionReceived;
        public event Action<int> SeatLeft;
        /// Social messages from other players during a game: (seat, text), (seat, emote index), (seat).
        public event Action<int, string> ChatReceived;
        public event Action<int, int> EmoteReceived;
        /// seat, pitch, rare clip: the ringer rolls the sound once so every peer plays exactly the same one.
        public event Action<int, float, bool> BellRang;
        /// seat, world point under the cursor, visible (false when over the UI or out of the window).
        public event Action<int, Vector3, bool> CursorMoved;
        /// The session ended without us asking (host gone, kicked, connection lost).
        public event Action<string> Closed;

        public readonly List<LobbySeat> Seats = new List<LobbySeat>();
        public ISession Session { get; private set; }
        public bool IsHost => Session != null && Session.IsHost;
        public bool InGame { get; private set; }
        public string Code => Session?.Code;
        public ulong LocalClientId => nm != null ? nm.LocalClientId : 0;

        NetworkManager nm;
        string localName;
        bool leaving;
        readonly Dictionary<ulong, int> seatOfClient = new Dictionary<ulong, int>();

        // ---------------- Services ----------------

        static async Task EnsureSignedIn()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                // A random profile per launch lets several instances on one machine sign in as different players.
                var options = new InitializationOptions().SetProfile("p" + UnityEngine.Random.Range(0, 99999999));
                await UnityServices.InitializeAsync(options);
            }
            while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        public static async Task<IList<ISessionInfo>> QueryPublic()
        {
            await EnsureSignedIn();
            var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
            return results.Sessions;
        }

        public async Task Host(string name, bool isPrivate)
        {
            await Prepare(name);
            var options = new SessionOptions { Name = $"Partie de {name}", MaxPlayers = MaxPlayers, IsPrivate = isPrivate }
                .WithRelayNetwork();
            Attach(await MultiplayerService.Instance.CreateSessionAsync(options));
            Seats.Add(new LobbySeat { Name = name, ClientId = nm.LocalClientId });
            LobbyChanged?.Invoke();
        }

        public async Task JoinByCode(string code, string name)
        {
            await Prepare(name);
            Attach(await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant()));
        }

        public async Task JoinById(string id, string name)
        {
            await Prepare(name);
            Attach(await MultiplayerService.Instance.JoinSessionByIdAsync(id));
        }

        async Task Prepare(string name)
        {
            await Leave();
            localName = name;
            await EnsureSignedIn();
            EnsureNetworkManager();
        }

        void Attach(ISession session)
        {
            Session = session;
            leaving = false;
            session.Deleted += OnSessionGone;
            session.RemovedFromSession += OnSessionGone;
            RegisterHandlers();
        }

        void OnSessionGone()
        {
            if (!leaving && !IsHost) Close("L'hôte a fermé la partie.");
        }

        public async Task Leave()
        {
            if (Session == null) return;
            leaving = true;
            var session = Session;
            Session = null;
            Reset();
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] Leave: {e.Message}");
            }
            if (nm != null && nm.IsListening) nm.Shutdown();
        }

        void Reset()
        {
            Seats.Clear();
            seatOfClient.Clear();
            InGame = false;
        }

        void Close(string reason)
        {
            if (Session == null) return;
            _ = Leave();
            Closed?.Invoke(reason);
        }

        void OnDestroy()
        {
            if (Session != null) _ = Leave();
        }

        // ---------------- Netcode ----------------

        void EnsureNetworkManager()
        {
            nm = NetworkManager.Singleton;
            if (nm == null)
            {
                var go = new GameObject("NetworkManager");
                go.SetActive(false);
                var transport = go.AddComponent<UnityTransport>();
                nm = go.AddComponent<NetworkManager>();
                nm.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
                go.SetActive(true);
            }
            nm.OnClientStarted -= RegisterHandlers;
            nm.OnClientStarted += RegisterHandlers;
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
        }

        void RegisterHandlers()
        {
            var messaging = nm != null ? nm.CustomMessagingManager : null;
            if (messaging == null) return;
            messaging.UnregisterNamedMessageHandler(Channel);
            messaging.RegisterNamedMessageHandler(Channel, OnMessage);
        }

        void OnClientConnected(ulong clientId)
        {
            if (nm.IsServer || clientId != nm.LocalClientId) return;
            Send(NetworkManager.ServerClientId, Msg.Hello, w => w.WriteValueSafe(localName));
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (Session == null || leaving) return;
            if (!nm.IsServer)
            {
                if (clientId == nm.LocalClientId || clientId == NetworkManager.ServerClientId)
                    Close(string.IsNullOrEmpty(nm.DisconnectReason) ? "Connexion perdue avec l'hôte." : nm.DisconnectReason);
                return;
            }

            if (InGame)
            {
                if (!seatOfClient.TryGetValue(clientId, out int seat)) return;
                seatOfClient.Remove(clientId);
                Seats[seat].IsAI = true;
                Broadcast(Msg.SeatToAI, w => w.WriteValueSafe(seat));
                SeatLeft?.Invoke(seat);
            }
            else if (Seats.RemoveAll(s => !s.IsAI && s.ClientId == clientId) > 0) BroadcastLobby();
        }

        void OnMessage(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte type);
            switch ((Msg)type)
            {
                case Msg.Hello:
                    if (!IsHost) return;
                    r.ReadValueSafe(out string name);
                    OnHello(sender, name);
                    break;
                case Msg.Lobby:
                    ReadLobby(r);
                    LobbyChanged?.Invoke();
                    break;
                case Msg.Start:
                    ReadStart(r);
                    break;
                case Msg.Action:
                    r.ReadValueSafe(out byte actionType);
                    r.ReadValueSafe(out int seat);
                    r.ReadValueSafe(out int a);
                    r.ReadValueSafe(out int b);
                    ActionReceived?.Invoke(new GameAction((ActionType)actionType, seat, a, b), sender);
                    break;
                case Msg.Chat:
                case Msg.Emote:
                case Msg.Bell:
                    OnSocial((Msg)type, sender, r);
                    break;
                case Msg.Cursor:
                    OnCursor(sender, r);
                    break;
                case Msg.SeatToAI:
                    r.ReadValueSafe(out int leftSeat);
                    if (leftSeat >= 0 && leftSeat < Seats.Count) Seats[leftSeat].IsAI = true;
                    SeatLeft?.Invoke(leftSeat);
                    break;
            }
        }

        void Send(ulong client, Msg type, Action<FastBufferWriter> write,
            NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            if (nm == null || nm.CustomMessagingManager == null) return;
            var w = new FastBufferWriter(1024, Allocator.Temp);
            try
            {
                w.WriteValueSafe((byte)type);
                write?.Invoke(w);
                nm.CustomMessagingManager.SendNamedMessage(Channel, client, w, delivery);
            }
            finally
            {
                w.Dispose();
            }
        }

        /// Host only: to every connected client except the host itself and <paramref name="except"/>.
        void Broadcast(Msg type, Action<FastBufferWriter> write, ulong? except = null,
            NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            if (nm == null || !nm.IsServer) return;
            foreach (var id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId && id != except) Send(id, type, write, delivery);
        }

        // ---------------- Lobby ----------------

        void OnHello(ulong sender, string name)
        {
            if (InGame || Seats.Count >= MaxPlayers)
            {
                nm.DisconnectClient(sender, InGame ? "La partie a déjà commencé." : "La partie est complète.");
                return;
            }
            name = string.IsNullOrWhiteSpace(name) ? $"Joueur {Seats.Count + 1}" : name.Trim();
            if (name.Length > 18) name = name.Substring(0, 18);
            Seats.Add(new LobbySeat { Name = name, ClientId = sender });
            BroadcastLobby();
        }

        public void BroadcastLobby()
        {
            if (!IsHost) return;
            Broadcast(Msg.Lobby, w =>
            {
                w.WriteValueSafe(Seats.Count);
                foreach (var s in Seats)
                {
                    w.WriteValueSafe(s.Name);
                    w.WriteValueSafe(s.IsAI);
                    w.WriteValueSafe(s.ClientId);
                }
            });
            LobbyChanged?.Invoke();
        }

        void ReadLobby(FastBufferReader r)
        {
            r.ReadValueSafe(out int count);
            Seats.Clear();
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out string name);
                r.ReadValueSafe(out bool ai);
                r.ReadValueSafe(out ulong id);
                Seats.Add(new LobbySeat { Name = name, IsAI = ai, ClientId = id });
            }
        }

        public bool AddAI(string name)
        {
            if (!IsHost || Seats.Count >= MaxPlayers) return false;
            Seats.Add(new LobbySeat { Name = name, IsAI = true });
            BroadcastLobby();
            return true;
        }

        public void RemoveSeat(int index)
        {
            if (!IsHost || index <= 0 || index >= Seats.Count) return;
            var seat = Seats[index];
            Seats.RemoveAt(index);
            if (!seat.IsAI) nm.DisconnectClient(seat.ClientId, "L'hôte vous a retiré de la partie.");
            BroadcastLobby();
        }

        // ---------------- Game ----------------

        public void HostStart()
        {
            if (!IsHost || InGame) return;
            InGame = true;
            var host = Session.AsHost();
            host.IsLocked = true;
            _ = host.SavePropertiesAsync();

            int seed = Environment.TickCount;
            var roster = new List<(string name, bool ai)>();
            foreach (var s in Seats) roster.Add((s.Name, s.IsAI));
            for (int i = 0; i < Seats.Count; i++)
            {
                if (Seats[i].IsAI) continue;
                seatOfClient[Seats[i].ClientId] = i;
                if (Seats[i].ClientId == nm.LocalClientId) continue;
                int seat = i;
                Send(Seats[i].ClientId, Msg.Start, w =>
                {
                    w.WriteValueSafe(seed);
                    w.WriteValueSafe(roster.Count);
                    foreach (var (n, ai) in roster)
                    {
                        w.WriteValueSafe(n);
                        w.WriteValueSafe(ai);
                    }
                    w.WriteValueSafe(seat);
                });
            }
            GameStarted?.Invoke(seed, roster, seatOfClient[nm.LocalClientId]);
        }

        void ReadStart(FastBufferReader r)
        {
            r.ReadValueSafe(out int seed);
            r.ReadValueSafe(out int count);
            var roster = new List<(string name, bool ai)>();
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out string name);
                r.ReadValueSafe(out bool ai);
                roster.Add((name, ai));
            }
            r.ReadValueSafe(out int mySeat);
            InGame = true;
            GameStarted?.Invoke(seed, roster, mySeat);
        }

        // ---------------- Chat, emotes, bell ----------------

        public void SendChat(int seat, string text)
        {
            text = CleanChat(text);
            if (text.Length > 0) SendSocial(Msg.Chat, seat, text, 0);
        }

        public void SendEmote(int seat, int emote) => SendSocial(Msg.Emote, seat, null, emote);

        public void RingBell(int seat, float pitch, bool rare) => SendSocial(Msg.Bell, seat, null, 0, pitch, rare);

        /// Cursor positions are frequent and disposable: unreliable, only the latest one matters.
        public void SendCursor(int seat, Vector3 point, bool visible)
        {
            if (Session == null || !InGame) return;
            Action<FastBufferWriter> write = w => WriteCursor(w, seat, point, visible);
            if (IsHost) Broadcast(Msg.Cursor, write, null, NetworkDelivery.UnreliableSequenced);
            else Send(NetworkManager.ServerClientId, Msg.Cursor, write, NetworkDelivery.UnreliableSequenced);
        }

        static void WriteCursor(FastBufferWriter w, int seat, Vector3 p, bool visible)
        {
            w.WriteValueSafe(seat);
            w.WriteValueSafe(p.x);
            w.WriteValueSafe(p.y);
            w.WriteValueSafe(p.z);
            w.WriteValueSafe(visible);
        }

        void OnCursor(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int seat);
            r.ReadValueSafe(out float x);
            r.ReadValueSafe(out float y);
            r.ReadValueSafe(out float z);
            r.ReadValueSafe(out bool visible);
            var p = new Vector3(x, y, z);
            if (IsHost)
            {
                seat = SeatOf(sender);
                if (seat < 0) return;
                Broadcast(Msg.Cursor, w => WriteCursor(w, seat, p, visible), sender, NetworkDelivery.UnreliableSequenced);
            }
            CursorMoved?.Invoke(seat, p, visible);
        }

        public static string CleanChat(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length > MaxChatLength ? text.Substring(0, MaxChatLength) : text;
        }

        /// Social messages skip the rules: clients send them to the host, which stamps the real seat and relays.
        void SendSocial(Msg type, int seat, string text, int emote, float pitch = 1f, bool rare = false)
        {
            if (Session == null || !InGame) return;
            if (IsHost) Broadcast(type, w => WriteSocial(w, type, seat, text, emote, pitch, rare));
            else Send(NetworkManager.ServerClientId, type, w => WriteSocial(w, type, seat, text, emote, pitch, rare));
        }

        static void WriteSocial(FastBufferWriter w, Msg type, int seat, string text, int emote, float pitch, bool rare)
        {
            w.WriteValueSafe(seat);
            if (type == Msg.Chat) w.WriteValueSafe(text);
            else if (type == Msg.Emote) w.WriteValueSafe(emote);
            else if (type == Msg.Bell)
            {
                w.WriteValueSafe(pitch);
                w.WriteValueSafe(rare);
            }
        }

        void OnSocial(Msg type, ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int seat);
            string text = null;
            int emote = 0;
            if (type == Msg.Chat) r.ReadValueSafe(out text);
            else if (type == Msg.Emote) r.ReadValueSafe(out emote);
            float pitch = 1f;
            bool rare = false;
            if (type == Msg.Bell)
            {
                r.ReadValueSafe(out pitch);
                r.ReadValueSafe(out rare);
                pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            }

            if (IsHost)
            {
                // Never trust the seat a client claims.
                seat = SeatOf(sender);
                if (seat < 0) return;
                if (type == Msg.Chat)
                {
                    text = CleanChat(text);
                    if (text.Length == 0) return;
                }
                Broadcast(type, w => WriteSocial(w, type, seat, text, emote, pitch, rare), sender);
            }

            switch (type)
            {
                case Msg.Chat: ChatReceived?.Invoke(seat, text); break;
                case Msg.Emote: EmoteReceived?.Invoke(seat, emote); break;
                case Msg.Bell: BellRang?.Invoke(seat, pitch, rare); break;
            }
        }

        /// The seat a client plays, or -1.
        public int SeatOf(ulong clientId) => seatOfClient.TryGetValue(clientId, out int s) ? s : -1;

        /// A move made on this peer: clients send it to the host, the host to everyone.
        public void SendAction(GameAction a)
        {
            if (Session == null) return;
            if (IsHost) Relay(a, null);
            else Send(NetworkManager.ServerClientId, Msg.Action, w => WriteAction(w, a));
        }

        /// Host only: forward a move to every client but its author.
        public void Relay(GameAction a, ulong? except) => Broadcast(Msg.Action, w => WriteAction(w, a), except);

        static void WriteAction(FastBufferWriter w, GameAction a)
        {
            w.WriteValueSafe((byte)a.Type);
            w.WriteValueSafe(a.Seat);
            w.WriteValueSafe(a.A);
            w.WriteValueSafe(a.B);
        }
    }
}
