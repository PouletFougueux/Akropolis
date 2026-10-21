using System.Collections;
using System.Collections.Generic;
using Akropolis.Audio;
using Akropolis.Net;
using Akropolis.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Akropolis
{
    /// <summary>Chef d'orchestre, sur le modèle d'HarmoniesGame : entrées, caméra, IA, enchaînement des animations.</summary>
    public sealed class GameController : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (FindAnyObjectByType<GameController>() == null)
                new GameObject("Akropolis").AddComponent<GameController>();
        }

        GameState game;
        TableView table;
        AkropolisUI ui;
        Camera cam;
        ScoreBreakdown[] scores = new ScoreBreakdown[0];

        int held = -1, rotation, hoverSite = -1, viewSeat = -1, focusSeat, animSeat = -1;
        bool busy, revealing;
        Coroutine aiTurn, reveal;
        List<int> ranking;

        Placement placement;
        bool hasPlacement;
        string placementKey, placementText;

        float camYaw, yawOffset, pitch = 52f, distance = 17f, shake, seatYaw;
        Vector3 focus;
        Vector2 rightDown;
        bool rightDragged;

        int lastHuman = -1;

        NetSession net;
        /// <summary>Siège joué sur cette machine en ligne ; -1 hors ligne (tous les humains sont locaux).</summary>
        int localSeat = -1;
        bool online;
        readonly Queue<(GameAction action, ulong sender)> remoteQueue = new Queue<(GameAction action, ulong sender)>();

        /// <summary>Hors ligne ou hôte : c'est cette machine qui fait jouer les IA.</summary>
        bool IsAuthority => !online || net.IsHost;

        bool Controls(int seat) => game != null && seat >= 0 && seat < game.Players.Count && !game.Players[seat].IsAI && (!online || seat == localSeat);

        bool HumanTurn => game != null && !game.IsOver && Controls(game.CurrentIndex);

        bool MyTurn => HumanTurn && !busy;

        int TurnSeat => animSeat >= 0 ? animSeat : game.CurrentIndex;

        /// <summary>Notre cité : le seul humain, ou en local à plusieurs le dernier humain qui a joué.</summary>
        int HomeSeat
        {
            get
            {
                if (game == null) return 0;
                if (online) return Mathf.Max(0, localSeat);
                int human = -1, humans = 0;
                for (int i = 0; i < game.Players.Count; i++)
                    if (!game.Players[i].IsAI)
                    {
                        humans++;
                        human = i;
                    }
                if (humans == 1) return human;
                if (humans == 0) return game.CurrentIndex;
                return lastHuman >= 0 ? lastHuman : human;
            }
        }

        /// <summary>
        /// Comme dans Harmonies : pendant notre tour, la caméra reste sur notre cité ; sinon elle ne suit pas qui joue,
        /// elle reste chez nous, et on peut aller voir n'importe quelle autre cité.
        /// </summary>
        int ViewedSeat => game == null ? 0
            : HumanTurn ? game.CurrentIndex
            : viewSeat >= 0 && viewSeat < game.Players.Count ? viewSeat : HomeSeat;

        void Awake()
        {
            Application.runInBackground = true;
            transform.position = Vector3.zero;
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Sky;
            cam.fieldOfView = 55f;
            cam.farClipPlane = 400f;
            // TextMesh's GUI shader reads this; without IMGUI it would disable depth testing.
            Shader.SetGlobalFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);

            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            table = new GameObject("Table").AddComponent<TableView>();
            new GameObject("Environment").AddComponent<EnvironmentView>();
            ui = new GameObject("UI").AddComponent<AkropolisUI>();

            var hand = Resources.Load<Texture2D>("UI/Cursors/hand_cursor");
            if (hand != null) Cursor.SetCursor(hand, new Vector2(15f, 1f), CursorMode.Auto);

            ui.Init();
            net = new GameObject("Net").AddComponent<NetSession>();
            net.GameStarted += (seed, roster, seat) => StartGame(roster, seed, seat);
            net.ActionReceived += (a, sender) => remoteQueue.Enqueue((a, sender));
            net.SeatLeft += OnSeatLeft;
            net.Closed += OnNetClosed;
            net.BellRang += (seat, pitch, rare) => RingBell(seat, false, pitch, rare);
            net.ChatReceived += ShowChat;
            net.EmoteReceived += ShowEmote;
            ui.ChatSubmitted += text =>
            {
                text = NetSession.CleanChat(text);
                if (!online || game == null || text.Length == 0) return;
                net.SendChat(localSeat, text);
                ShowChat(localSeat, text);
            };
            ui.EmoteClicked += emote =>
            {
                if (!online || game == null) return;
                net.SendEmote(localSeat, emote);
                ShowEmote(localSeat, emote);
            };
            ui.AttachNet(net);
            ui.StartRequested += StartGame;
            ui.RestartRequested += BackToMenu;
            ui.QuitConfirmed += BackToMenu;
            ui.PlayerClicked += ViewSeat;
            ui.ViewBoardRequested += ShowScoreInstant;
            ui.UndoRequested += Undo;
            ui.EndTurnRequested += EndTurn;
            ui.DistrictHovered += OnDistrictHovered;

            BuildPreview();
            Sound.Music(MusicMood.Menu);
        }

        void BuildPreview()
        {
            game = null;
            table.Build(new GameState(new string[0], new bool[0], System.Environment.TickCount, 4), true, false);
            focus = Vector3.zero;
        }

        void StartGame(List<(string name, bool ai)> roster) => StartGame(roster, System.Environment.TickCount, -1);

        void StartGame(List<(string name, bool ai)> roster, int seed, int seat)
        {
            StopAll();
            online = seat >= 0;
            localSeat = seat;
            var names = new List<string>();
            var ai = new List<bool>();
            foreach (var r in roster)
            {
                names.Add(r.name);
                ai.Add(r.ai);
            }
            game = new GameState(names, ai, seed);
            held = -1;
            rotation = 0;
            viewSeat = -1;
            animSeat = -1;
            lastHuman = game.Current.IsAI ? -1 : game.CurrentIndex;
            yawOffset = 0f;
            table.Build(game, true, true);
            seatYaw = table.Seats[ViewedSeat].Yaw;
            camYaw = seatYaw;
            scores = ComputeScores();
            ui.ShowGame();
            ui.SetChatVisible(online);
            ui.Banner(TurnBanner(), Palette.Player(game.CurrentIndex));
            Sound.Play(Sfx.GameStart);
            Sound.Music(MusicMood.Game);
            Refresh();
        }

        void BackToMenu()
        {
            StopAll();
            if (online) _ = net.Leave();
            online = false;
            localSeat = -1;
            ui.ShowSetup();
            BuildPreview();
            Sound.Music(MusicMood.Menu);
        }

        void StopAll()
        {
            StopAllCoroutines();
            aiTurn = null;
            reveal = null;
            busy = false;
            revealing = false;
            held = -1;
            hasPlacement = false;
            animSeat = -1;
            remoteQueue.Clear();
            ui.ClearWorldLabels();
        }

        ScoreBreakdown[] ComputeScores()
        {
            var s = new ScoreBreakdown[game.Players.Count];
            for (int i = 0; i < s.Length; i++) s[i] = game.Score(i);
            return s;
        }

        string TurnBanner() => Controls(game.CurrentIndex) ? $"À vous, {game.Current.Name} !" : $"Au tour de {game.Current.Name}";

        void Report(string error)
        {
            if (string.IsNullOrEmpty(error)) return;
            ui.Toast(error);
            Sound.Play(Sfx.Error);
        }

        void Refresh()
        {
            if (game == null || game.IsOver) return;
            ui.ViewedSeat = ViewedSeat;
            bool pending = game.HasPending && Controls(game.CurrentIndex);
            string undoText = pending ? "Annuler la pose" : held >= 0 ? "Reposer la tuile" : "Annuler";
            ui.Refresh(game, scores, Hint(), TurnSeat, MyTurn && (pending || held >= 0), MyTurn && pending, undoText);
        }

        string Hint()
        {
            if (busy || !Controls(game.CurrentIndex)) return $"{game.Players[TurnSeat].Name} construit…";
            if (game.HasPending) return "Tuile posée. Validez avec « Fin du tour » (Entrée), ou « Annuler » (Ctrl+Z) pour la reprendre.";
            if (held < 0)
                return "Prenez une tuile du Chantier : la première est gratuite, puis 1 pierre par tuile qui la précède.";
            if (!hasPlacement) return "Survolez votre cité pour placer la tuile. R pour la pivoter.";
            return placementText;
        }

        void ViewSeat(int seat)
        {
            if (game == null || game.IsOver || seat < 0 || seat >= game.Players.Count) return;
            if (MyTurn)
            {
                if (seat != game.CurrentIndex) ui.Toast("Pendant votre tour, la caméra reste sur votre cité.");
                return;
            }
            viewSeat = seat == HomeSeat ? -1 : seat;
            yawOffset = 0f;
            Refresh();
        }

        int hoveredDistrict = -1;

        /// <summary>Survol d'une ligne du score : on montre sur la cité les quartiers qui comptent (vert) ou pas (rouge).</summary>
        void OnDistrictHovered(int district)
        {
            hoveredDistrict = district;
            if (game == null || district < 1 || district > 5)
            {
                table.HideDistrictHighlight();
                return;
            }
            int seat = game.IsOver ? focusSeat : ViewedSeat;
            var city = game.Players[seat].City;
            var d = (District)district;
            var scoring = city.ScoringCoords(d);
            var set = new HashSet<HexCoord>(scoring);
            var ignored = new List<HexCoord>();
            foreach (var kv in city.Cells)
                if (kv.Value.Face.IsDistrict && kv.Value.Face.District == d && !set.Contains(kv.Key)) ignored.Add(kv.Key);
            table.ShowDistrictHighlight(seat, city, scoring, ignored);
        }

        // ---------------- Chat, émotes, cloche ----------------

        string SeatName(int seat) => game != null && seat >= 0 && seat < game.Players.Count ? game.Players[seat].Name : "?";

        void ShowChat(int seat, string text)
        {
            if (game == null) return;
            ui.AddChatLine(SeatName(seat), Palette.Player(seat), text, false);
            if (seat != localSeat) Sound.Play(Sfx.ChatMessage);
        }

        /// <summary>L'émote s'affiche en bulle au-dessus de la plaque du joueur, et dans la discussion.</summary>
        void ShowEmote(int seat, int emote)
        {
            if (game == null || emote < 0 || emote >= AkropolisUI.Emotes.Length || seat < 0 || seat >= table.Seats.Count) return;
            var text = AkropolisUI.Emotes[emote];
            ui.PopLabel(table.PlaquePosition(seat), text, Palette.Player(seat), 2.6f);
            ui.AddChatLine(SeatName(seat), Palette.Player(seat), text, true);
            Sound.Play(Sfx.Emote);
        }

        /// <summary>
        /// Tout le monde peut sonner, autant qu'il veut. Celui qui sonne tire la hauteur (et le clip rare) une seule fois
        /// et l'envoie : tout le monde entend exactement le même son.
        /// </summary>
        void RingBell(int seat, bool local, float pitch = 1f, bool rare = false)
        {
            if (local) Sound.Roll(Sfx.Bell, out pitch, out rare);
            table.RingBell();
            Sound.PlayExact(Sfx.Bell, pitch, rare);
            if (!online || game == null) return;
            if (local) net.RingBell(localSeat, pitch, rare);
            ui.PopLabel(table.BellTop, SeatName(seat), Palette.Player(seat), 0.9f);
        }

        void RingBell() => RingBell(localSeat, true);

        // ---------------- Frame loop ----------------

        void Update()
        {
            while (game != null && !busy && remoteQueue.Count > 0)
            {
                var (a, sender) = remoteQueue.Dequeue();
                OnRemoteAction(a, sender);
            }
            if (game != null && IsAuthority && !game.IsOver && !busy && game.Current.IsAI && !game.HasPending && aiTurn == null)
                aiTurn = StartCoroutine(AiTurn());
            HandleInput();
            UpdateCamera();
            ui.Tick(cam);
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            // Pendant qu'on écrit dans la discussion, les raccourcis clavier du jeu se taisent.
            var kb = ui.ChatFocused ? null : Keyboard.current;
            if (mouse == null) return;
            var screen = mouse.position.ReadValue();

            if (ui.ModalVisible)
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    ui.HideQuitConfirm();
                    ui.HideSettings();
                }
                table.SetSiteHover(-1);
                return;
            }

            bool overUI = ui.IsPointerOverUI(screen);
            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightDown = screen;
                rightDragged = false;
            }
            if (mouse.rightButton.isPressed)
            {
                if ((screen - rightDown).sqrMagnitude > 49f) rightDragged = true;
                if (rightDragged)
                {
                    var d = mouse.delta.ReadValue();
                    yawOffset += d.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - d.y * 0.2f, 25f, 82f);
                }
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (!overUI && Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * 1.1f, 7f, 30f);

            if (game == null || game.IsOver)
            {
                table.SetSiteHover(-1);
                return;
            }

            Pickable pick = null;
            if (!overUI && Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 300f))
                pick = hit.collider.GetComponentInParent<Pickable>();

            if (mouse.leftButton.wasPressedThisFrame && pick != null && pick.Kind == PickKind.Bell)
            {
                RingBell();
                return;
            }

            if (!MyTurn)
            {
                if (!busy) table.SetSiteHover(-1);
                if (mouse.leftButton.wasPressedThisFrame && pick != null && pick.Kind == PickKind.Board) ViewSeat(pick.Player);
                return;
            }

            bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            if (kb != null && kb.zKey.wasPressedThisFrame && ctrl)
            {
                Undo();
                return;
            }
            if (game.HasPending)
            {
                table.SetSiteHover(-1);
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) EndTurn();
                return;
            }

            int hoverIndex = pick != null && pick.Kind == PickKind.Site && pick.Index != held && pick.Index < game.Site.Count ? pick.Index : -1;
            if (hoverIndex != hoverSite)
            {
                if (hoverIndex >= 0) Sound.Play(Sfx.HoverTokens);
                hoverSite = hoverIndex;
            }
            table.SetSiteHover(hoverIndex);

            if (kb != null)
            {
                for (int i = 0; i < game.Site.Count && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame)
                    {
                        Take(i);
                        return;
                    }
                if (held >= 0 && kb.rKey.wasPressedThisFrame)
                {
                    rotation = (rotation + (kb.shiftKey.isPressed ? 5 : 1)) % 6;
                    placementKey = null;
                    Sound.Play(Sfx.RotateTile);
                    if (!hasPlacement) table.HoldIdle(rotation);
                }
                if (kb.escapeKey.wasPressedThisFrame) Cancel();
            }
            if (mouse.rightButton.wasReleasedThisFrame && !rightDragged) Cancel();

            if (mouse.leftButton.wasPressedThisFrame && hoverIndex >= 0)
            {
                Take(hoverIndex);
                return;
            }

            if (held < 0) return;
            UpdatePlacement(screen, overUI);
            if (mouse.leftButton.wasPressedThisFrame && !overUI && hasPlacement && hoverIndex < 0)
            {
                if (placement.Valid) Commit();
                else Report(placement.Reason);
            }
        }

        // ---------------- Actions (locales ou reçues du réseau) ----------------

        /// <summary>Joue un coup fait sur cette machine (humain local ou IA) et le partage en ligne.</summary>
        void Do(GameAction a)
        {
            if (Apply(a) && online) net.SendAction(a);
        }

        bool Apply(GameAction a)
        {
            if (game == null || game.IsOver || busy || a.Seat != game.CurrentIndex) return false;
            switch (a.Type)
            {
                case ActionType.Take: return ApplyTake(a.A);
                case ActionType.Cancel: return ApplyCancel();
                case ActionType.Place: return ApplyPlace(GameAction.Unpack(a.A), a.B);
                case ActionType.Undo: return ApplyUndo();
                case ActionType.EndTurn: return ApplyEndTurn();
                default: return false;
            }
        }

        void OnRemoteAction(GameAction a, ulong sender)
        {
            if (game == null || game.IsOver) return;
            if (net.IsHost)
            {
                // Seul le client qui joue ce siège peut le faire bouger.
                if (a.Seat != game.CurrentIndex || net.SeatOf(sender) != a.Seat || game.Players[a.Seat].IsAI) return;
                if (Apply(a)) net.Relay(a, sender);
            }
            else if (!Apply(a)) Debug.LogWarning($"[Net] Coup refusé localement : {a}");
        }

        void OnSeatLeft(int seat)
        {
            if (game == null || seat < 0 || seat >= game.Players.Count) return;
            game.Players[seat].IsAI = true;
            ui.Toast($"{game.Players[seat].Name} a quitté la partie : une IA prend le relais.");
            Refresh();
        }

        void OnNetClosed(string reason)
        {
            if (game != null && online)
            {
                BackToMenu();
                ui.Toast(reason);
            }
            ui.OnlineClosed(reason);
        }

        void Take(int index)
        {
            if (MyTurn && index != held && !game.HasPending) Do(new GameAction(ActionType.Take, game.CurrentIndex, index));
        }

        bool ApplyTake(int index)
        {
            if (index < 0 || index >= game.Site.Count || index == held || game.HasPending) return false;
            if (!game.CanAfford(index))
            {
                if (Controls(game.CurrentIndex))
                    Report($"Il vous faut {index} pierre{(index > 1 ? "s" : "")} pour prendre cette tuile (vous en avez {game.Current.Stones}).");
                return false;
            }
            held = index;
            rotation = 0;
            hasPlacement = false;
            placementKey = null;
            table.TakeTile(index, game.CurrentIndex);
            Sound.Play(Sfx.TakeTokens);
            Refresh();
            return true;
        }

        void Cancel()
        {
            if (MyTurn && held >= 0) Do(new GameAction(ActionType.Cancel, game.CurrentIndex));
        }

        bool ApplyCancel()
        {
            if (held < 0) return false;
            table.ReturnHeld();
            held = -1;
            hasPlacement = false;
            placementKey = null;
            Sound.Play(Sfx.CancelTokens);
            Refresh();
            return true;
        }

        void UpdatePlacement(Vector2 screen, bool overUI)
        {
            var city = game.Current.City;
            if (overUI || !table.TryCityCoord(game.CurrentIndex, cam.ScreenPointToRay(screen), city, out var coord))
            {
                if (hasPlacement)
                {
                    hasPlacement = false;
                    placementKey = null;
                    ui.SetHint(Hint());
                }
                table.HoldIdle(rotation);
                return;
            }
            string key = coord + "|" + rotation;
            hasPlacement = true;
            if (key == placementKey) return;
            placementKey = key;
            var tile = game.Site[held];
            placement = city.Evaluate(tile, coord, rotation);
            if (placement.Valid)
            {
                var clone = city.Clone();
                int gained = clone.Apply(placement, tile);
                int delta = clone.ComputeScore(0).DistrictTotal - scores[game.CurrentIndex].DistrictTotal;
                placementText = $"Niveau {placement.Level} : {(delta >= 0 ? "+" : "")}{delta} pt(s)"
                                + (gained > 0 ? $", +{gained} pierre(s)" : "")
                                + (held > 0 ? $" (coût : {held} pierre{(held > 1 ? "s" : "")})" : "") + ". Cliquez pour poser.";
            }
            else placementText = placement.Reason;
            table.ShowPlacement(game.CurrentIndex, city, placement, rotation);
            ui.SetHint(Hint());
        }

        void Commit()
        {
            if (held < 0 || !hasPlacement || !MyTurn) return;
            Do(new GameAction(ActionType.Place, game.CurrentIndex, GameAction.Pack(placement.Anchor), placement.Rotation));
        }

        bool ApplyPlace(HexCoord anchor, int rot)
        {
            if (held < 0) return false;
            int seat = game.CurrentIndex;
            var info = game.Play(held, anchor, rot, out var error);
            if (info == null)
            {
                if (Controls(seat)) Report(error);
                return false;
            }
            held = -1;
            hasPlacement = false;
            placementKey = null;
            busy = true;
            animSeat = seat;
            if (!game.Players[seat].IsAI) lastHuman = seat;
            StartCoroutine(PlaceTile(info));
            return true;
        }

        /// <summary>La tuile tombe à sa place ; le joueur valide ensuite avec « Fin du tour ».</summary>
        IEnumerator PlaceTile(TurnInfo info)
        {
            var g = game;
            if (info.Paid > 0)
            {
                table.PayStones(info.Seat, info.Paid);
                Sound.Play(Sfx.StonePay);
            }
            bool landed = false;
            table.PlaceHeld(info, () =>
            {
                landed = true;
                Sound.Play(Sfx.PlaceToken, 1f + (info.Level - 1) * 0.08f);
                shake = Mathf.Max(shake, 0.03f + 0.02f * info.Level);
                if (info.CoveredQuarries.Count > 0) table.GainStones(info.Seat, info.CoveredQuarries);
                scores = ComputeScores();
                Refresh();
            });
            float t = 0f;
            while (!landed && t < 2f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (g != game) yield break;
            busy = false;
            Refresh();
        }

        void Undo()
        {
            if (!MyTurn) return;
            if (game.HasPending) Do(new GameAction(ActionType.Undo, game.CurrentIndex));
            else Cancel();
        }

        bool ApplyUndo()
        {
            var info = game.Undo();
            if (info == null) return false;
            // Annuler la pose : la tuile revient en main. « Reposer la tuile » la renvoie ensuite au chantier.
            table.UndoPlace(info, true);
            held = info.SiteIndex;
            rotation = info.Placement.Rotation;
            hasPlacement = false;
            placementKey = null;
            animSeat = -1;
            scores = ComputeScores();
            Sound.Play(Sfx.Undo);
            if (info.Paid > 0 || info.Gained > 0) Sound.Play(Sfx.StonePay);
            Refresh();
            return true;
        }

        void EndTurn()
        {
            if (MyTurn && game.HasPending) Do(new GameAction(ActionType.EndTurn, game.CurrentIndex));
        }

        bool ApplyEndTurn()
        {
            var info = game.EndTurn();
            if (info == null) return false;
            busy = true;
            animSeat = info.Seat;
            StartCoroutine(FinishTurn(info));
            return true;
        }

        IEnumerator FinishTurn(TurnInfo info)
        {
            var g = game;
            Refresh();
            table.AfterTurn(game, info);
            if (info.Refilled)
            {
                ui.Toast(info.NewChief >= 0 ? $"Nouvelle manche : {game.Players[info.NewChief].Name} devient Architecte en chef." : "Le Chantier est réapprovisionné.");
                yield return new WaitForSeconds(0.5f + info.Dealt.Count * 0.13f);
                if (info.NewChief >= 0) Sound.Play(Sfx.ChiefPass);
                yield return new WaitForSeconds(0.4f);
            }
            else yield return new WaitForSeconds(0.35f);
            if (g != game) yield break;

            busy = false;
            animSeat = -1;
            scores = ComputeScores();
            if (game.IsOver)
            {
                EnterEnd();
                yield break;
            }
            yawOffset = 0f;
            if (Controls(game.CurrentIndex))
            {
                viewSeat = -1;
                lastHuman = game.CurrentIndex;
            }
            ui.Banner(TurnBanner(), Palette.Player(game.CurrentIndex));
            Sound.Play(Sfx.TurnStart);
            Refresh();
        }

        IEnumerator AiTurn()
        {
            var g = game;
            yield return new WaitForSeconds(1f);
            if (g != game || g.IsOver || busy || !g.Current.IsAI)
            {
                aiTurn = null;
                yield break;
            }
            int seat = g.CurrentIndex;
            var p = g.Current;
            // Un joueur parti en plein tour : l'IA reprend là où il en était.
            if (g.HasPending)
            {
                aiTurn = null;
                Do(new GameAction(ActionType.EndTurn, seat));
                yield break;
            }
            if (held >= 0)
            {
                Do(new GameAction(ActionType.Cancel, seat));
                yield return new WaitForSeconds(0.5f);
            }
            var move = AIPlanner.FindBest(p.City, p.Stones, g.Site, new System.Random());
            if (move.SiteIndex < 0)
            {
                aiTurn = null;
                yield break;
            }
            table.SetSiteHover(move.SiteIndex);
            Sound.Play(Sfx.HoverTokens);
            yield return new WaitForSeconds(0.45f);
            if (g != game) yield break;

            table.SetSiteHover(-1);
            Do(new GameAction(ActionType.Take, seat, move.SiteIndex));
            yield return new WaitForSeconds(0.7f);
            if (g != game) yield break;

            rotation = move.Placement.Rotation;
            table.ShowPlacement(seat, p.City, move.Placement, rotation);
            Sound.Play(Sfx.RotateTile);
            yield return new WaitForSeconds(0.7f);
            if (g != game) yield break;

            Do(new GameAction(ActionType.Place, seat, GameAction.Pack(move.Placement.Anchor), move.Placement.Rotation));
            while (busy && g == game) yield return null;
            yield return new WaitForSeconds(0.7f);
            if (g != game) yield break;
            aiTurn = null;
            Do(new GameAction(ActionType.EndTurn, seat));
        }

        // ---------------- End of game ----------------

        void EnterEnd()
        {
            scores = ComputeScores();
            ranking = new List<int>();
            for (int i = 0; i < game.Players.Count; i++) ranking.Add(i);
            ranking.Sort((a, b) =>
            {
                int diff = scores[b].Total.CompareTo(scores[a].Total);
                return diff != 0 ? diff : game.Players[b].Stones.CompareTo(game.Players[a].Stones);
            });
            ui.ShowEnd(game, ranking, scores);
            ui.Banner("Fin de partie !", Palette.Teal);
            Sound.Play(Sfx.GameOver);
            Sound.Music(MusicMood.End);
            focusSeat = ranking[0];
            if (reveal != null) StopCoroutine(reveal);
            reveal = StartCoroutine(Reveal(ranking[0], 1.6f));
        }

        IEnumerator Reveal(int seat, float delay)
        {
            revealing = true;
            focusSeat = seat;
            yawOffset = 0f;
            ui.ClearWorldLabels();
            ui.SetRevealTarget(game, seat, ranking);
            yield return new WaitForSeconds(0.9f + delay);

            var city = game.Players[seat].City;
            var sc = scores[seat];
            int total = 0, step = 0;
            foreach (var d in Rules.ScoringDistricts)
            {
                int i = (int)d;
                var cells = city.ScoringCoords(d);
                var plazas = city.PlazaCoords(d);
                if (cells.Count == 0 && plazas.Count == 0) continue;
                var color = Palette.DistrictColor(d);
                var all = new List<HexCoord>(cells);
                all.AddRange(plazas);
                table.Bounce(seat, all, color);
                ui.PopLabel(table.CellsCenter(seat, cells.Count > 0 ? cells : plazas) + Vector3.up * 1.1f,
                    $"{Rules.Name(d)} : {sc.Stars[i]} × {sc.Heights[i]} = {sc.Points[i]}", color, 0f);
                total += sc.Points[i];
                ui.SetRevealTotal(total);
                Sound.Play(Sfx.ScoreItem, 1f + Mathf.Min(step++ * 0.06f, 0.6f));
                shake = Mathf.Max(shake, 0.03f);
                yield return new WaitForSeconds(0.6f);
            }
            if (sc.Stones > 0)
            {
                table.BounceStones(seat);
                ui.PopLabel(table.TrayTop(seat), $"Pierres : +{sc.Stones}", Palette.Stone, 0f);
                total += sc.Stones;
                ui.SetRevealTotal(total);
                Sound.Play(Sfx.StoneGain);
                yield return new WaitForSeconds(0.5f);
            }
            if (seat == ranking[0])
            {
                Fx.Confetti(table.Seats[seat].Root.position + Vector3.up * 1.5f);
                Sound.Play(Sfx.Victory);
                shake = 0.2f;
            }
        }

        /// <summary>« Voir » : tout le détail d'un joueur d'un coup, sans l'animation.</summary>
        void ShowScoreInstant(int seat)
        {
            if (game == null || !game.IsOver) return;
            if (reveal != null) StopCoroutine(reveal);
            reveal = null;
            revealing = true;
            focusSeat = seat;
            yawOffset = 0f;
            ui.ClearWorldLabels();
            ui.SetRevealTarget(game, seat, ranking);
            var city = game.Players[seat].City;
            var sc = scores[seat];
            foreach (var d in Rules.ScoringDistricts)
            {
                int i = (int)d;
                var cells = city.ScoringCoords(d);
                var plazas = city.PlazaCoords(d);
                if (cells.Count == 0 && plazas.Count == 0) continue;
                ui.PopLabel(table.CellsCenter(seat, cells.Count > 0 ? cells : plazas) + Vector3.up * 1.1f,
                    $"{Rules.Name(d)} : {sc.Stars[i]} × {sc.Heights[i]} = {sc.Points[i]}", Palette.DistrictColor(d), 0f);
            }
            if (sc.Stones > 0) ui.PopLabel(table.TrayTop(seat), $"Pierres : +{sc.Stones}", Palette.Stone, 0f);
            ui.SetRevealTotal(sc.Total);
        }

        // ---------------- Camera ----------------

        void UpdateCamera()
        {
            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-3.2f * dt);
            float targetYaw;
            Vector3 targetFocus;
            float targetDistance = distance;

            if (game == null || table.Seats.Count == 0)
            {
                targetYaw = Time.time * 8f;
                targetFocus = Vector3.zero;
                targetDistance = 21f;
            }
            else if (game.IsOver)
            {
                var seat = table.Seats[focusSeat];
                seatYaw = Mathf.LerpAngle(seatYaw, seat.Yaw, k);
                table.SetCenterYaw(seatYaw);
                targetYaw = seatYaw + yawOffset;
                targetFocus = seat.Root.position * 0.85f;
                targetDistance = revealing ? Mathf.Min(distance, 13f) : distance;
            }
            else
            {
                var seat = table.Seats[ViewedSeat];
                seatYaw = Mathf.LerpAngle(seatYaw, seat.Yaw, k);
                table.SetCenterYaw(seatYaw);
                targetYaw = seatYaw + yawOffset;
                targetFocus = seat.Root.position * 0.62f;
            }

            camYaw = Mathf.LerpAngle(camYaw, targetYaw, k);
            focus = Vector3.Lerp(focus, targetFocus, k);
            var rot = Quaternion.Euler(pitch, camYaw, 0f);
            var pos = focus - rot * Vector3.forward * Mathf.Lerp((cam.transform.position - focus).magnitude, targetDistance, k);

            shake = Mathf.Max(0f, shake - dt * 0.6f);
            if (shake > 0f) pos += Random.insideUnitSphere * shake;
            cam.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
