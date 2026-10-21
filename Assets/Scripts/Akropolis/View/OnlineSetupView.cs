using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akropolis.Net;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Akropolis.View
{
    /// "En ligne" tab of the setup screen: host, join by code, public games list, then the waiting room.
    public sealed class OnlineSetupView
    {
        const string NamePref = "online-name";
        static readonly string[] AiNames = { "Périclès", "Aspasie", "Phidias", "Hypatie" };

        readonly VisualElement root;
        readonly NetSession net;
        readonly Label status = new Label();
        string playerName;
        string code = "";
        bool isPrivate;
        bool busy;
        IList<ISessionInfo> sessions;
        bool loadingList;

        public OnlineSetupView(VisualElement root, NetSession net)
        {
            this.root = root;
            this.net = net;
            playerName = PlayerPrefs.GetString(NamePref, "Joueur");
            status.AddToClassList("online-status");
            net.LobbyChanged += () =>
            {
                if (net.Session != null) Build();
            };
        }

        public void Show()
        {
            Build();
            if (net.Session == null && sessions == null) _ = RefreshList();
        }

        public void SetStatus(string text, bool error = false)
        {
            status.text = text ?? "";
            status.EnableInClassList("error", error);
        }

        public void Closed(string reason)
        {
            SetStatus(reason, true);
            Build();
        }

        void Build()
        {
            root.Clear();
            if (net.Session != null) BuildRoom();
            else BuildBrowser();
            root.Add(status);
            root.SetEnabled(!busy);
        }

        // ---------------- Browser ----------------

        void BuildBrowser()
        {
            Section("Votre nom");
            var name = new TextField { value = playerName, maxLength = 18 };
            name.AddToClassList("name-field");
            name.RegisterValueChangedCallback(e =>
            {
                playerName = e.newValue;
                PlayerPrefs.SetString(NamePref, playerName);
            });
            root.Add(name);

            Section("Héberger une partie");
            var host = Row();
            Segmented(host, new[] { "Publique", "Privée" }, isPrivate ? 1 : 0, i =>
            {
                isPrivate = i == 1;
                Build();
            });
            var create = Button("Créer", "btn-primary", () => Run(() => net.Host(CleanName(), isPrivate), "Création de la partie…"));
            create.AddToClassList("online-action");
            host.Add(create);
            root.Add(host);

            Section("Rejoindre avec un code");
            var join = Row();
            var codeField = new TextField { value = code, maxLength = 12 };
            codeField.AddToClassList("name-field");
            codeField.AddToClassList("grow");
            codeField.RegisterValueChangedCallback(e => code = e.newValue);
            join.Add(codeField);
            var joinButton = Button("Rejoindre", "btn-primary", () =>
            {
                if (string.IsNullOrWhiteSpace(code)) SetStatus("Entrez le code donné par l'hôte.", true);
                else Run(() => net.JoinByCode(code, CleanName()), "Connexion à la partie…");
            });
            joinButton.AddToClassList("online-action");
            join.Add(joinButton);
            root.Add(join);

            var header = Row();
            var title = new Label("Parties publiques");
            title.AddToClassList("section-title");
            title.AddToClassList("grow");
            header.Add(title);
            header.Add(Button(loadingList ? "…" : "Actualiser", "btn-ghost", () => _ = RefreshList()));
            root.Add(header);

            var list = new ScrollView();
            list.AddToClassList("session-list");
            if (sessions == null || sessions.Count == 0)
            {
                var empty = new Label(loadingList ? "Recherche des parties…" : "Aucune partie publique pour le moment.");
                empty.AddToClassList("muted");
                list.Add(empty);
            }
            else
                foreach (var s in sessions)
                {
                    var row = Row();
                    row.AddToClassList("session-row");
                    var label = new Label(s.Name);
                    label.AddToClassList("grow");
                    row.Add(label);
                    var count = new Label($"{s.MaxPlayers - s.AvailableSlots}/{s.MaxPlayers}");
                    count.AddToClassList("muted");
                    row.Add(count);
                    string id = s.Id;
                    row.Add(Button("Rejoindre", "btn-primary", () => Run(() => net.JoinById(id, CleanName()), "Connexion à la partie…")));
                    list.Add(row);
                }
            root.Add(list);
        }

        async Task RefreshList()
        {
            if (loadingList) return;
            loadingList = true;
            Build();
            try
            {
                sessions = await NetSession.QueryPublic();
            }
            catch (Exception e)
            {
                sessions = null;
                SetStatus(ServiceError(e), true);
            }
            loadingList = false;
            if (net.Session == null) Build();
        }

        // ---------------- Waiting room ----------------

        void BuildRoom()
        {
            Section("Code de la partie");
            var codeRow = Row();
            var codeLabel = new Label(net.Code ?? "…");
            codeLabel.AddToClassList("session-code");
            codeLabel.AddToClassList("grow");
            codeRow.Add(codeLabel);
            codeRow.Add(Button("Copier", "btn-ghost", () =>
            {
                GUIUtility.systemCopyBuffer = net.Code;
                SetStatus("Code copié : envoyez-le à vos amis.");
            }));
            root.Add(codeRow);

            Section($"Joueurs ({net.Seats.Count}/{NetSession.MaxPlayers})");
            for (int i = 0; i < net.Seats.Count; i++)
            {
                var seat = net.Seats[i];
                var row = Row();
                row.AddToClassList("name-row");
                var chip = new VisualElement();
                chip.AddToClassList("chip");
                chip.style.backgroundColor = Palette.Player(i);
                row.Add(chip);
                var label = new Label(seat.Name);
                label.AddToClassList("seat-name");
                row.Add(label);
                string tag = seat.IsAI ? "IA" : i == 0 ? "Hôte" : seat.ClientId == net.LocalClientId ? "Vous" : null;
                if (tag != null)
                {
                    var t = new Label(tag);
                    t.AddToClassList("ai-tag");
                    row.Add(t);
                }
                if (net.IsHost && i > 0)
                {
                    int index = i;
                    var remove = Button("×", "remove-btn", () => net.RemoveSeat(index));
                    remove.style.marginLeft = new StyleLength(StyleKeyword.Auto);
                    row.Add(remove);
                }
                root.Add(row);
            }

            if (net.IsHost)
            {
                var add = Button("+ IA", "btn-ghost", () => net.AddAI(NextAiName()));
                add.AddToClassList("add-btn");
                add.SetEnabled(net.Seats.Count < NetSession.MaxPlayers);
                root.Add(add);

                var start = Button("Lancer la partie", "btn-primary", net.HostStart);
                start.AddToClassList("btn-big");
                start.SetEnabled(net.Seats.Count >= 2);
                root.Add(start);
                if (net.Seats.Count < 2) SetStatus("Partagez le code ou attendez qu'un joueur rejoigne (ou ajoutez une IA).");
            }
            else
            {
                SetStatus("En attente du lancement par l'hôte…");
            }

            var leave = Button("Quitter", "btn-ghost", () =>
            {
                _ = net.Leave();
                SetStatus("");
                Build();
            });
            leave.AddToClassList("online-leave");
            root.Add(leave);
        }

        string NextAiName()
        {
            foreach (var n in AiNames)
                if (!net.Seats.Exists(s => s.Name == n)) return n;
            return $"IA {net.Seats.Count + 1}";
        }

        // ---------------- Helpers ----------------

        async void Run(Func<Task> action, string pending)
        {
            if (busy) return;
            busy = true;
            SetStatus(pending);
            Build();
            try
            {
                await action();
                SetStatus("");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] {e}");
                SetStatus(ServiceError(e), true);
                _ = net.Leave();
            }
            busy = false;
            Build();
        }

        static string ServiceError(Exception e)
        {
            string msg = e is AggregateException a && a.InnerException != null ? a.InnerException.Message : e.Message;
            if (e is SessionException se && se.Error == SessionError.SessionNotFound)
                return "Aucune partie ne correspond à ce code.";
            if (Unity.Services.Core.UnityServices.State != Unity.Services.Core.ServicesInitializationState.Initialized)
                return "Services en ligne indisponibles : liez le projet à Unity Cloud (Edit > Project Settings > Services) " +
                       "puis vérifiez votre connexion internet.";
            return $"Connexion impossible : {msg}";
        }

        string CleanName() => string.IsNullOrWhiteSpace(playerName) ? "Joueur" : playerName.Trim();

        void Section(string text)
        {
            var l = new Label(text);
            l.AddToClassList("section-title");
            root.Add(l);
        }

        static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("online-row");
            return row;
        }

        static Label OptionLabel(string text)
        {
            var l = new Label(text);
            l.AddToClassList("option-label");
            return l;
        }

        static Button Button(string text, string style, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            b.AddToClassList(style);
            return b;
        }

        static void Segmented(VisualElement parent, string[] labels, int selected, Action<int> onSelect)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var b = Button(labels[i], "seg-btn", () => onSelect(index));
                if (i == selected) b.AddToClassList("selected");
                parent.Add(b);
            }
        }
    }
}
