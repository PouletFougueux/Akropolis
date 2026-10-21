using System;
using System.Collections.Generic;
using Akropolis.Audio;
using Akropolis.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace Akropolis.View
{
    /// <summary>Interface UI Toolkit, reprise du style d'Harmonies (même feuille de style, bannières, toasts, étiquettes).</summary>
    public sealed class AkropolisUI : MonoBehaviour
    {
        public event Action<List<(string name, bool ai)>> StartRequested;
        public event Action RestartRequested;
        public event Action QuitConfirmed;
        public event Action<int> PlayerClicked;
        public event Action<int> ViewBoardRequested;
        public event Action UndoRequested;
        public event Action EndTurnRequested;
        public event Action<int> DistrictHovered;
        public event Action<string> ChatSubmitted;
        public event Action<int> EmoteClicked;

        public static readonly string[] Emotes = { "Bonjour !", "Bien joué !", "Merci !", "Wow !", "Oups…", "Hmm…", "Désolé !", "GG !" };

        const int MaxPlayers = 4;
        static readonly string[] AiNames = { "Périclès", "Aspasie", "Phidias", "Hypatie" };

        UIDocument doc;
        VisualElement root, setup, hud, end, worldLayer, players, ranking, banner, turnDot, namesBox, rosterActions, titleRow, cityPanel, modal;
        Label turnName, turnInfo, lastRound, hint, toast, bannerText, endSub, revealName, revealTotal;
        readonly List<(string name, bool ai)> roster = new List<(string name, bool ai)> { ("Joueur 1", false), ("Périclès", true) };
        readonly Dictionary<int, int> shownScores = new Dictionary<int, int>();
        readonly Dictionary<int, int> shownDistricts = new Dictionary<int, int>();
        readonly List<VisualElement> rankRows = new List<VisualElement>();
        IVisualElementScheduledItem toastHide;
        VisualElement settings;
        Button undoBtn, endTurnBtn;
        readonly List<(Slider slider, Label value, Func<float> get)> volumeSliders = new List<(Slider slider, Label value, Func<float> get)>();
        float lastVolumePreview;
        VisualElement modeTabs, localSetup, onlineSetup;
        OnlineSetupView onlineView;
        NetSession net;
        bool onlineMode;
        Label quitTitle, quitMessage;
        bool quitToDesktop;
        VisualElement chatPanel;
        ScrollView chatLog;
        TextField chatInput;
        int cityOwner = -1;

        sealed class WorldLabel
        {
            public Label El;
            public Vector3 World;
            public float Born, Life;
        }

        readonly List<WorldLabel> labels = new List<WorldLabel>();

        public int ViewedSeat { get; set; }
        public bool ModalVisible => (modal != null && !modal.ClassListContains("hidden")) || SettingsVisible;
        public bool SettingsVisible => settings != null && !settings.ClassListContains("hidden");

        // ---------------- Construction ----------------

        public void Init()
        {
            gameObject.SetActive(false);
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>("UI/PanelSettings");
            gameObject.SetActive(true);

            root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.styleSheets.Add(Resources.Load<StyleSheet>("UI/AkropolisStyle"));
            root.RegisterCallback<PointerOverEvent>(e =>
            {
                if (e.target is Button b && b.enabledInHierarchy) Sound.Play(Sfx.UiHover);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is Button b && b.enabledInHierarchy) Sound.Play(Sfx.UiClick);
            }, TrickleDown.TrickleDown);
            Build();
            ShowSetup();
        }

        static VisualElement El(string classes, VisualElement parent, bool ignore = false)
        {
            var e = new VisualElement();
            foreach (var c in classes.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            if (ignore) e.pickingMode = PickingMode.Ignore;
            parent?.Add(e);
            return e;
        }

        static Label Lbl(string text, string classes, VisualElement parent, bool ignore = false)
        {
            var l = new Label(text);
            foreach (var c in classes.Split(' ')) if (c.Length > 0) l.AddToClassList(c);
            if (ignore) l.pickingMode = PickingMode.Ignore;
            parent?.Add(l);
            return l;
        }

        static Button Btn(string text, string classes, VisualElement parent, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            foreach (var c in classes.Split(' ')) if (c.Length > 0) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        void Build()
        {
            var app = El("app", root, true);
            worldLayer = El("layer", app, true);

            setup = El("layer setup", app);
            var card = El("setup-card", setup);
            titleRow = El("title-row", card, true);
            Lbl("Bâtissez la cité la plus prestigieuse de la Grèce antique", "subtitle", card);
            modeTabs = El("option-row mode-tabs", card);
            localSetup = El("", card);
            Lbl("Architectes", "section-title", localSetup);
            namesBox = El("names", localSetup);
            rosterActions = El("roster-actions", localSetup);
            Btn("Jouer", "btn btn-primary btn-big", localSetup, () => StartRequested?.Invoke(RosterForStart()));
            onlineSetup = El("hidden", card);
            Lbl("Police : Fredoka (OFL) · Sons et musique : Kenney, Juhani Junkala, Spring Spring (CC0) · Akropolis © Gigamic, adaptation non officielle",
                "credits", setup);

            hud = El("layer hud", app, true);
            var left = El("left-col", hud, true);
            var turn = El("panel turn-panel", left);
            var row = El("row", turn);
            turnDot = El("dot", row);
            turnName = Lbl("", "turn-name", row);
            turnInfo = Lbl("", "muted", turn);
            lastRound = Lbl("Dernière manche !", "last-round", turn);
            hint = Lbl("", "hint", turn);
            players = El("panel players", left);
            BuildChat(left);
            var right = El("right-col", hud, true);
            cityPanel = El("panel city-panel", right);
            var bottom = El("bottom-bar", hud, true);
            toast = Lbl("", "toast", bottom, true);
            var actionsRow = El("row", bottom, true);
            undoBtn = Btn("Annuler", "btn btn-ghost btn-big undo", actionsRow, () => UndoRequested?.Invoke());
            endTurnBtn = Btn("Fin du tour", "btn btn-primary btn-big end-turn", actionsRow, () => EndTurnRequested?.Invoke());
            endTurnBtn.schedule.Execute(() =>
            {
                if (endTurnBtn.enabledSelf) endTurnBtn.ToggleInClassList("pulse");
                else endTurnBtn.RemoveFromClassList("pulse");
            }).Every(550);
            Lbl("Clic gauche : prendre / poser · R : pivoter · Échap : reposer · Ctrl+Z : annuler · Entrée : fin du tour · Clic droit glissé : tourner · Molette : zoom",
                "controls-help", hud, true);

            end = El("layer end", app, true);
            var endPanel = El("panel end-panel", end);
            Lbl("Fin de partie", "end-title", endPanel);
            endSub = Lbl("", "muted end-sub", endPanel);
            ranking = El("ranking", endPanel);
            var endButtons = El("row end-buttons", endPanel);
            Btn("Nouvelle partie", "btn btn-primary", endButtons, () => RestartRequested?.Invoke());
            var reveal = El("reveal", end, true);
            revealName = Lbl("", "reveal-name", reveal, true);
            revealTotal = Lbl("0", "reveal-total", reveal, true);

            modal = El("layer modal hidden", app);
            modal.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == modal) HideQuitConfirm();
            });
            var quitPanel = El("panel quit-panel", modal);
            quitTitle = Lbl("Quitter la partie ?", "modal-title", quitPanel);
            quitMessage = Lbl("La partie en cours sera perdue.", "quit-message", quitPanel);
            var actions = El("row quit-actions", quitPanel);
            Btn("Continuer", "btn btn-ghost", actions, HideQuitConfirm);
            Btn("Quitter", "btn btn-primary", actions, () =>
            {
                HideQuitConfirm();
                if (quitToDesktop) QuitApplication();
                else QuitConfirmed?.Invoke();
            });

            Btn("Son", "btn btn-ghost settings-open", app, ShowSettings);
            // À côté du bouton Son : quitter la partie en jeu, quitter le jeu depuis l'accueil.
            var quit = Btn("Quitter", "btn btn-ghost quit-btn", app, ShowQuitConfirm);
            quit.style.top = 18;
            quit.style.right = 520;
            modal.BringToFront();
            settings = El("layer modal hidden", app);
            settings.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == settings) HideSettings();
            });
            BuildSettings();

            banner = El("banner", app, true);
            bannerText = Lbl("", "banner-text", banner, true);

            BuildTitle();
            BuildRoster();
        }

        // ---------------- Setup ----------------

        void BuildTitle()
        {
            const string word = "Akropolis";
            var colors = new[] { "#3F7FD0", "#EDB93A", "#D4553E", "#8E62C6", "#4FA052", "#3F7FD0", "#EDB93A", "#D4553E", "#8E62C6" };
            for (int i = 0; i < word.Length; i++)
            {
                var letter = new Label(word[i].ToString());
                letter.AddToClassList("title-letter");
                letter.style.color = Palette.Hex(colors[i]);
                letter.style.rotate = new Rotate(new Angle((i % 2 == 0 ? -1f : 1f) * (3f + i % 3 * 2f), AngleUnit.Degree));
                letter.pickingMode = PickingMode.Ignore;
                titleRow.Add(letter);
            }
        }

        void AnimateTitle()
        {
            for (int i = 0; i < titleRow.childCount; i++)
            {
                var letter = titleRow[i];
                letter.AddToClassList("start");
                letter.schedule.Execute(() => letter.RemoveFromClassList("start")).StartingIn(80 + i * 70);
            }
        }

        List<(string name, bool ai)> RosterForStart()
        {
            var list = new List<(string name, bool ai)>();
            for (int i = 0; i < roster.Count; i++)
            {
                var (n, ai) = roster[i];
                list.Add((string.IsNullOrWhiteSpace(n) ? (ai ? AiNames[i] : $"Joueur {i + 1}") : n.Trim(), ai));
            }
            return list;
        }

        void BuildRoster(int animateFrom = 0)
        {
            namesBox.Clear();
            for (int i = 0; i < roster.Count; i++)
            {
                int index = i;
                var (playerName, ai) = roster[i];
                var row = El("name-row", namesBox);
                var chip = El("chip", row);
                chip.style.backgroundColor = Palette.Player(i);
                var field = new TextField { value = playerName, maxLength = 18 };
                field.AddToClassList("name-field");
                field.RegisterValueChangedCallback(e => roster[index] = (e.newValue, roster[index].ai));
                row.Add(field);
                var type = Btn(ai ? "IA" : "Humain", "btn type-btn", row, () =>
                {
                    bool toAi = !roster[index].ai;
                    roster[index] = (DefaultName(index, toAi), toAi);
                    BuildRoster(roster.Count);
                });
                if (ai) type.AddToClassList("ai");
                var remove = Btn("×", "btn remove-btn", row, () =>
                {
                    roster.RemoveAt(index);
                    BuildRoster(roster.Count);
                });
                remove.SetEnabled(roster.Count > 2);
                if (i >= animateFrom)
                {
                    row.AddToClassList("start");
                    row.schedule.Execute(() => row.RemoveFromClassList("start")).StartingIn(30 + (i - animateFrom) * 60);
                }
            }
            rosterActions.Clear();
            bool full = roster.Count >= MaxPlayers;
            foreach (var b in new[] { Btn("+ Joueur", "btn btn-ghost add-btn", rosterActions, () => AddPlayer(false)),
                                      Btn("+ IA", "btn btn-ghost add-btn", rosterActions, () => AddPlayer(true)) })
                b.SetEnabled(!full);
        }

        void AddPlayer(bool ai)
        {
            if (roster.Count >= MaxPlayers) return;
            roster.Add((DefaultName(roster.Count, ai), ai));
            BuildRoster(roster.Count - 1);
        }

        string DefaultName(int index, bool ai)
        {
            if (!ai) return $"Joueur {index + 1}";
            foreach (var n in AiNames)
                if (!roster.Exists(r => r.name == n)) return n;
            return $"IA {index + 1}";
        }

        // ---------------- Online ----------------

        public void AttachNet(NetSession session)
        {
            net = session;
            onlineView = new OnlineSetupView(onlineSetup, net);
            BuildModeTabs();
        }

        void BuildModeTabs()
        {
            modeTabs.Clear();
            string[] tabs = { "Partie locale", "En ligne" };
            for (int i = 0; i < tabs.Length; i++)
            {
                bool isOnline = i == 1;
                var b = Btn(tabs[i], "btn seg-btn", modeTabs, () =>
                {
                    if (onlineMode == isOnline) return;
                    onlineMode = isOnline;
                    if (!onlineMode && net != null && net.Session != null) _ = net.Leave();
                    BuildModeTabs();
                });
                if (isOnline == onlineMode) b.AddToClassList("selected");
            }
            localSetup.EnableInClassList("hidden", onlineMode);
            onlineSetup.EnableInClassList("hidden", !onlineMode);
            if (onlineMode) onlineView?.Show();
        }

        /// <summary>La session en ligne s'est terminée d'elle-même (hôte parti, exclusion, connexion perdue).</summary>
        public void OnlineClosed(string reason) => onlineView?.Closed(reason);

        // ---------------- Chat & émotes (parties en ligne) ----------------

        void BuildChat(VisualElement parent)
        {
            chatPanel = El("panel chat-panel hidden", parent);
            Lbl("Discussion", "panel-title", chatPanel);
            chatLog = new ScrollView(ScrollViewMode.Vertical);
            chatLog.AddToClassList("chat-log");
            chatPanel.Add(chatLog);

            var inputRow = El("row chat-input-row", chatPanel);
            chatInput = new TextField { maxLength = NetSession.MaxChatLength };
            chatInput.AddToClassList("chat-input");
            chatInput.textEdition.placeholder = "Écrire un message…";
            chatInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.character != '\n') return;
                SubmitChat();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            inputRow.Add(chatInput);
            Btn("Envoyer", "btn btn-primary chat-send", inputRow, SubmitChat);

            var emotes = El("emote-row", chatPanel);
            for (int i = 0; i < Emotes.Length; i++)
            {
                int index = i;
                Btn(Emotes[i], "btn btn-ghost emote", emotes, () => EmoteClicked?.Invoke(index));
            }
        }

        void SubmitChat()
        {
            var text = chatInput.value;
            chatInput.SetValueWithoutNotify("");
            if (!string.IsNullOrWhiteSpace(text)) ChatSubmitted?.Invoke(text);
            chatInput.schedule.Execute(() => chatInput.Focus());
        }

        /// <summary>Vrai pendant qu'on écrit, pour que les raccourcis du jeu se taisent.</summary>
        public bool ChatFocused
        {
            get
            {
                var focused = chatInput?.focusController?.focusedElement as VisualElement;
                return focused != null && (focused == chatInput || chatInput.Contains(focused));
            }
        }

        public void SetChatVisible(bool visible) => chatPanel.EnableInClassList("hidden", !visible);

        public void AddChatLine(string author, Color color, string text, bool emote)
        {
            // Un seul label en texte riche : les longs messages reviennent à la ligne juste après le nom.
            string safe = text.Replace("<", "<\u200B");
            string body = emote ? $"<i>{safe}</i>" : safe;
            var line = new Label($"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{author} :</b></color> {body}");
            line.AddToClassList("chat-line");
            chatLog.Add(line);
            while (chatLog.contentContainer.childCount > 60) chatLog.contentContainer.RemoveAt(0);
            chatLog.schedule.Execute(() => chatLog.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        // ---------------- Screens ----------------

        public void ShowSetup()
        {
            setup.RemoveFromClassList("hidden");
            hud.AddToClassList("hidden");
            end.AddToClassList("hidden");
            HideQuitConfirm();
            ClearWorldLabels();
            AnimateTitle();
            BuildRoster();
            if (net != null) BuildModeTabs();
        }

        public void ShowGame()
        {
            setup.AddToClassList("hidden");
            hud.RemoveFromClassList("hidden");
            end.AddToClassList("hidden");
            chatLog.Clear();
            chatInput.SetValueWithoutNotify("");
            shownScores.Clear();
            shownDistricts.Clear();
            cityOwner = -1;
            ClearWorldLabels();
        }

        public void ShowQuitConfirm()
        {
            bool inGame = !hud.ClassListContains("hidden") || !end.ClassListContains("hidden");
            quitToDesktop = !inGame;
            bool connected = net != null && net.Session != null;
            if (quitToDesktop)
            {
                quitTitle.text = "Quitter le jeu ?";
                quitMessage.text = connected ? "Vous quitterez aussi la partie en ligne en attente." : "À bientôt sur l'Acropole !";
            }
            else
            {
                quitTitle.text = "Quitter la partie ?";
                quitMessage.text = !connected ? "La partie en cours sera perdue."
                    : net.IsHost ? "Vous êtes l'hôte : la partie s'arrêtera pour tous les joueurs."
                    : "Vous quitterez la partie en ligne ; une IA prendra votre place.";
            }
            modal.RemoveFromClassList("hidden");
        }

        static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void HideQuitConfirm() => modal?.AddToClassList("hidden");

        // ---------------- Sound settings ----------------

        void BuildSettings()
        {
            settings.Clear();
            volumeSliders.Clear();
            var audio = AudioManager.Instance;
            var panel = El("panel settings-panel", settings);
            Lbl("Réglages du son", "modal-title", panel);
            AddVolumeRow(panel, "Général", () => audio.MasterVolume, v => audio.MasterVolume = v, false);
            AddVolumeRow(panel, "Musique", () => audio.MusicVolume, v => audio.MusicVolume = v, false);
            AddVolumeRow(panel, "Ambiance", () => audio.AmbienceVolume, v => audio.AmbienceVolume = v, false);
            AddVolumeRow(panel, "Effets", () => audio.SfxVolume, v => audio.SfxVolume = v, true);
            Btn("Fermer", "btn btn-primary settings-close", panel, HideSettings);
        }

        void AddVolumeRow(VisualElement parent, string title, Func<float> get, Action<float> set, bool preview)
        {
            var row = El("volume-row", parent);
            Lbl(title, "volume-label", row);
            var slider = new Slider(0f, 100f);
            slider.AddToClassList("volume-slider");
            row.Add(slider);
            var value = Lbl("", "volume-value", row);
            slider.RegisterValueChangedCallback(e =>
            {
                set(e.newValue / 100f);
                value.text = $"{Mathf.RoundToInt(e.newValue)} %";
                // On entend le nouveau niveau des effets pendant qu'on glisse, sans saturer.
                if (preview && Time.unscaledTime - lastVolumePreview > 0.18f)
                {
                    lastVolumePreview = Time.unscaledTime;
                    Sound.Play(Sfx.PlaceToken);
                }
            });
            volumeSliders.Add((slider, value, get));
        }

        public void ShowSettings()
        {
            foreach (var (slider, value, get) in volumeSliders)
            {
                float v = get() * 100f;
                slider.SetValueWithoutNotify(v);
                value.text = $"{Mathf.RoundToInt(v)} %";
            }
            settings.RemoveFromClassList("hidden");
        }

        public void HideSettings()
        {
            if (!SettingsVisible) return;
            settings.AddToClassList("hidden");
            PlayerPrefs.Save();
        }

        // ---------------- HUD ----------------

        public void SetHint(string text) => hint.text = text;

        public void Refresh(GameState g, ScoreBreakdown[] scores, string hintText, int turnSeat, bool canUndo, bool canEnd, string undoText)
        {
            undoBtn.text = undoText;
            undoBtn.SetEnabled(canUndo);
            endTurnBtn.SetEnabled(canEnd);
            var current = g.Players[turnSeat];
            turnName.text = current.Name;
            turnDot.style.backgroundColor = Palette.Player(turnSeat);
            int tiles = g.TilesInPiles;
            turnInfo.text = $"Manche {g.Round}  ·  {tiles} tuile(s) en pile";
            turnInfo.style.whiteSpace = WhiteSpace.Normal;
            lastRound.EnableInClassList("hidden", !g.LastRound);
            hint.text = hintText;

            players.Clear();
            Lbl("Architectes", "panel-title", players);
            for (int i = 0; i < g.Players.Count; i++)
            {
                var p = g.Players[i];
                int score = scores[i].Total;
                var row = El("player-row compact", players);
                if (i == turnSeat) row.AddToClassList("current");
                if (i == ViewedSeat && i != turnSeat) row.AddToClassList("viewing");
                int rowSeat = i;
                row.RegisterCallback<ClickEvent>(_ => PlayerClicked?.Invoke(rowSeat));
                var dot = El("dot", row);
                dot.style.backgroundColor = Palette.Player(i);
                var info = El("player-info", row);
                var nameRow = El("row", info);
                var name = Lbl(p.Name, "player-name", nameRow);
                name.style.flexGrow = 0;
                if (p.IsAI) Lbl("IA", "ai-tag", nameRow);
                if (i == g.ChiefIndex) Lbl("Chef", "chief-tag", nameRow);
                var sc = scores[i];
                var meta = Lbl($"Quartiers : {sc.DistrictTotal} pts  ·  Pierres : {sc.Stones} pts  ·  {p.City.Cells.Count} hexagones visibles", "player-meta", row);
                var scoreLabel = Lbl("", "player-score", row);
                int previous = shownScores.TryGetValue(i, out var s) ? s : score;
                shownScores[i] = score;
                if (previous != score)
                {
                    scoreLabel.AddToClassList("bump");
                    CountUp(scoreLabel, previous, score, 500);
                    scoreLabel.schedule.Execute(() => scoreLabel.RemoveFromClassList("bump")).StartingIn(350);
                }
                else scoreLabel.text = score.ToString();
            }
            RefreshCity(g, scores);
        }

        /// <summary>Détail du score de la cité regardée : étoiles × valeur des quartiers, par couleur.</summary>
        void RefreshCity(GameState g, ScoreBreakdown[] scores)
        {
            int seat = Mathf.Clamp(ViewedSeat, 0, g.Players.Count - 1);
            if (cityOwner != seat)
            {
                shownDistricts.Clear();
                cityOwner = seat;
            }
            var sc = scores[seat];
            cityPanel.Clear();
            var head = El("city-head", cityPanel);
            var title = Lbl($"Cité de {g.Players[seat].Name}", "panel-title", head);
            title.style.color = Palette.Player(seat);
            Lbl(sc.Total + " pts", "city-total", head);
            Lbl("étoiles des places × valeur des quartiers bien placés", "muted small", cityPanel);
            foreach (var d in Rules.ScoringDistricts)
            {
                int i = (int)d;
                var row = El("district-row", cityPanel);
                if (sc.Points[i] == 0) row.AddToClassList("zero");
                var chip = El("district-chip", row);
                chip.style.backgroundColor = Palette.DistrictColor(d);
                var info = El("district-info", row);
                var name = Lbl(Rules.Name(d), "district-name", info);
                name.style.color = Palette.DistrictColor(d) * 0.8f;
                var city = g.Players[seat].City;
                int total = 0;
                foreach (var kv in city.Cells) if (kv.Value.Face.IsDistrict && kv.Value.Face.District == d) total++;
                int counted = city.ScoringCoords(d).Count;
                string detail = total == 0 ? "" : d == District.Houses
                    ? $" · {counted} sur {total} dans le plus grand groupe"
                    : d == District.Gardens ? $" · {total} jardin(s)" : $" · {counted} sur {total} bien placé(s)";
                Lbl(Rules.Condition(d) + detail, "district-rule", info);
                int hovered = i;
                row.RegisterCallback<PointerEnterEvent>(_ => DistrictHovered?.Invoke(hovered));
                row.RegisterCallback<PointerLeaveEvent>(_ => DistrictHovered?.Invoke(-1));
                Lbl($"{sc.Stars[i]} × {sc.Heights[i]}", "district-calc", row);
                var pts = Lbl("", "district-pts", row);
                int key = i;
                int previous = shownDistricts.TryGetValue(key, out var v) ? v : sc.Points[i];
                shownDistricts[key] = sc.Points[i];
                if (previous != sc.Points[i])
                {
                    pts.AddToClassList("bump");
                    CountUp(pts, previous, sc.Points[i], 500);
                    pts.schedule.Execute(() => pts.RemoveFromClassList("bump")).StartingIn(350);
                }
                else pts.text = sc.Points[i].ToString();
            }
            var stones = El("district-row", cityPanel);
            var stoneChip = El("district-chip", stones);
            stoneChip.style.backgroundColor = Palette.Stone;
            var stoneInfo = El("district-info", stones);
            Lbl("Pierres", "district-name", stoneInfo);
            Lbl("1 point par pierre en fin de partie", "district-rule", stoneInfo);
            Lbl(sc.Stones.ToString(), "district-pts", stones);
        }

        public void Toast(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            toast.text = message;
            toast.AddToClassList("show");
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => toast.RemoveFromClassList("show")).StartingIn(2600);
        }

        public void Banner(string text, Color color)
        {
            bannerText.text = text;
            bannerText.style.backgroundColor = color;
            banner.RemoveFromClassList("leave");
            banner.RemoveFromClassList("show");
            banner.schedule.Execute(() => banner.AddToClassList("show")).StartingIn(20);
            banner.schedule.Execute(() =>
            {
                banner.AddToClassList("leave");
                banner.RemoveFromClassList("show");
            }).StartingIn(1500);
            banner.schedule.Execute(() =>
            {
                banner.style.transitionDuration = new List<TimeValue> { new TimeValue(0f) };
                banner.RemoveFromClassList("leave");
                banner.schedule.Execute(() => banner.style.transitionDuration = StyleKeyword.Null).StartingIn(30);
            }).StartingIn(2200);
        }

        // ---------------- End ----------------

        public void ShowEnd(GameState g, List<int> order, ScoreBreakdown[] scores)
        {
            hud.AddToClassList("hidden");
            end.RemoveFromClassList("hidden");
            ranking.Clear();
            rankRows.Clear();
            var winner = g.Players[order[0]];
            endSub.text = $"{winner.Name} remporte la partie !";
            int best = 1;
            foreach (int p in order) best = Mathf.Max(best, scores[p].Total);

            for (int rank = 0; rank < order.Count; rank++)
            {
                int p = order[rank];
                var sheet = scores[p];
                var row = El("rank-row start", ranking);
                if (rank == 0) row.AddToClassList("winner");
                var head = El("row", row);
                var medal = El("rank-medal", head);
                medal.style.backgroundColor = Palette.Player(p);
                medal.Add(new Label((rank + 1).ToString()));
                Lbl(g.Players[p].Name, "rank-name", head);
                var total = Lbl("0", "rank-total", head);
                Btn("Voir", "btn btn-ghost", head, () => ViewBoardRequested?.Invoke(p));

                var bar = El("rank-bar", row);
                var segs = new List<(VisualElement, float)>();
                for (int i = 1; i <= 6; i++)
                {
                    int pts = i <= 5 ? sheet.Points[i] : sheet.Stones;
                    if (pts <= 0) continue;
                    var seg = El("rank-seg", bar);
                    seg.style.backgroundColor = i <= 5 ? Palette.DistrictColor((District)i) : Palette.Stone;
                    seg.style.width = Length.Percent(0);
                    seg.Add(new Label(pts.ToString()));
                    segs.Add((seg, 100f * pts / best));
                }
                rankRows.Add(row);
                int delay = 300 + rank * 250;
                row.schedule.Execute(() =>
                {
                    row.RemoveFromClassList("start");
                    foreach (var (seg, pct) in segs) seg.style.width = Length.Percent(pct);
                    CountUp(total, 0, sheet.Total, 1000);
                }).StartingIn(delay);
            }

            var legend = El("legend", ranking);
            for (int i = 1; i <= 6; i++)
            {
                var item = El("legend-item", legend);
                var dot = El("legend-dot", item);
                dot.style.backgroundColor = i <= 5 ? Palette.DistrictColor((District)i) : Palette.Stone;
                Lbl(i <= 5 ? Rules.Name((District)i) : "Pierres", "small", item);
            }
        }

        public void SetRevealTarget(GameState g, int player, List<int> order)
        {
            revealName.text = g.Players[player].Name;
            revealName.style.color = Palette.Player(player);
            revealTotal.text = "0";
            for (int i = 0; i < rankRows.Count; i++) rankRows[i].EnableInClassList("focused", order[i] == player);
        }

        public void SetRevealTotal(int total)
        {
            revealTotal.text = total.ToString();
            revealTotal.style.scale = new Scale(new Vector3(1.25f, 1.25f, 1f));
            revealTotal.schedule.Execute(() => revealTotal.style.scale = new Scale(Vector3.one)).StartingIn(90);
        }

        // ---------------- World labels ----------------

        public void PopLabel(Vector3 world, string text, Color color, float life = 1.6f)
        {
            var el = new Label(text);
            el.AddToClassList("wlabel");
            el.AddToClassList("start");
            el.pickingMode = PickingMode.Ignore;
            el.style.backgroundColor = color;
            worldLayer.Add(el);
            labels.Add(new WorldLabel { El = el, World = world, Born = Time.time, Life = life });
            el.schedule.Execute(() => el.RemoveFromClassList("start")).StartingIn(20);
        }

        public void ClearWorldLabels()
        {
            foreach (var l in labels) l.El.RemoveFromHierarchy();
            labels.Clear();
        }

        public void Tick(Camera cam)
        {
            var panel = worldLayer?.panel;
            if (panel == null || cam == null) return;
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                var l = labels[i];
                float age = Time.time - l.Born;
                if (l.Life > 0f && age > l.Life)
                {
                    l.El.RemoveFromHierarchy();
                    labels.RemoveAt(i);
                    continue;
                }
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, l.World, cam);
                float rise = l.Life > 0f ? age * 40f : 0f;
                var size = l.El.layout.size;
                if (float.IsNaN(size.x)) size = Vector2.zero;
                l.El.style.left = p.x - size.x * 0.5f;
                l.El.style.top = p.y - size.y - rise;
                if (l.Life > 0f) l.El.style.opacity = Mathf.Clamp01((l.Life - age) / 0.4f);
                bool behind = Vector3.Dot(cam.transform.forward, l.World - cam.transform.position) < 0f;
                l.El.style.display = behind ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public bool IsPointerOverUI(Vector2 screenPos)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            return panel.Pick(p) != null;
        }

        static void CountUp(Label label, int from, int to, long durationMs)
        {
            long start = System.Environment.TickCount;
            label.text = from.ToString();
            label.schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((System.Environment.TickCount - start) / (float)durationMs);
                k = 1f - Mathf.Pow(1f - k, 3f);
                label.text = Mathf.RoundToInt(Mathf.Lerp(from, to, k)).ToString();
            }).Every(30).Until(() => System.Environment.TickCount - start > durationMs + 40);
        }
    }
}
