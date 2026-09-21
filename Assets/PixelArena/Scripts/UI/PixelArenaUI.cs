using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace PixelArena
{
    /// <summary>
    /// Self-bootstrapping runtime menu and HUD for the LAN MVP.
    /// It consumes replicated gameplay state and never mutates combat or bunker authority.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class PixelArenaUI : MonoBehaviour
    {
        enum StartMode : byte { None, Host, Server, Client }

        sealed class FeedEntry
        {
            public string Text;
            public float ExpiresAt;
        }

        const float VirtualWidth = 1920f;
        const float VirtualHeight = 1080f;
        const float FeedLifetime = 6f;
        const int MaxFeedEntries = 6;
        const float HitMarkerDuration = 0.16f;
        const float DamageFlashDuration = 0.42f;

        static PixelArenaUI instance;

        NetManager manager;
        NetworkIdentity localIdentity;
        PlayerMotor localMotor;
        PlayerCombat localCombat;
        BunkerController localBunker;
        readonly List<FeedEntry> feed = new();

        string address = "localhost";
        string localStatus = string.Empty;
        string errorStatus = string.Empty;
        string interactionPrompt = string.Empty;
        StartMode lastMode;
        float nextPromptRefresh;
        float hitMarkerUntil;
        float damageFlashStarted;
        float damageFlashIntensity;
        bool retrying;

        GUIStyle titleStyle;
        GUIStyle headingStyle;
        GUIStyle bodyStyle;
        GUIStyle centeredStyle;
        GUIStyle smallStyle;
        GUIStyle buttonStyle;
        GUIStyle textFieldStyle;
        GUIStyle hudStyle;
        GUIStyle hudLargeStyle;
        Texture2D panelTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null) return;
            var root = new GameObject("Pixel Arena Runtime UI");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<PixelArenaUI>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            UnbindManager();
            if (panelTexture != null) Destroy(panelTexture);
            if (instance == this) instance = null;
        }

        void Update()
        {
            BindManager();
            RefreshLocalPlayer();
            TrimFeed();

            bool playing = manager != null && manager.ClientPhase == MatchPhase.Playing && localMotor != null;
            if (!playing || !localMotor.LocalInputEnabled)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (playing && localMotor.LocalInputEnabled && Time.unscaledTime >= nextPromptRefresh)
            {
                nextPromptRefresh = Time.unscaledTime + 0.1f;
                interactionPrompt = localMotor.GetInteractionPrompt();
            }
            else if (!playing || !localMotor.LocalInputEnabled)
            {
                interactionPrompt = string.Empty;
            }

            localBunker = playing ? BunkerController.Local : null;
        }

        void BindManager()
        {
            var current = NetworkManager.singleton as NetManager;
            if (current == manager) return;

            UnbindManager();
            manager = current;
            if (manager == null) return;

            address = string.IsNullOrWhiteSpace(manager.networkAddress) ? "localhost" : manager.networkAddress;
            manager.ClientStatusChanged += OnClientStatusChanged;
            manager.KillFeedReceived += OnKillFeedReceived;
        }

        void UnbindManager()
        {
            if (manager != null)
            {
                manager.ClientStatusChanged -= OnClientStatusChanged;
                manager.KillFeedReceived -= OnKillFeedReceived;
            }

            manager = null;
            localIdentity = null;
            localMotor = null;
            SetLocalCombat(null);
            localBunker = null;
        }

        void RefreshLocalPlayer()
        {
            var identity = NetworkClient.localPlayer;
            if (identity == localIdentity) return;

            localIdentity = identity;
            localMotor = identity != null ? identity.GetComponent<PlayerMotor>() : null;
            SetLocalCombat(identity != null ? identity.GetComponent<PlayerCombat>() : null);
            interactionPrompt = string.Empty;
        }

        void SetLocalCombat(PlayerCombat value)
        {
            if (localCombat == value) return;
            if (localCombat != null)
            {
                localCombat.LocalDamageTaken -= OnLocalDamageTaken;
                localCombat.LocalHitConfirmed -= OnLocalHitConfirmed;
            }
            localCombat = value;
            if (localCombat != null)
            {
                localCombat.LocalDamageTaken += OnLocalDamageTaken;
                localCombat.LocalHitConfirmed += OnLocalHitConfirmed;
            }
            hitMarkerUntil = 0f;
            damageFlashIntensity = 0f;
        }

        void OnLocalDamageTaken(float normalizedDamage)
        {
            damageFlashStarted = Time.unscaledTime;
            damageFlashIntensity = Mathf.Clamp(0.28f + normalizedDamage * 1.6f, 0.3f, 0.72f);
        }

        void OnLocalHitConfirmed()
        {
            hitMarkerUntil = Time.unscaledTime + HitMarkerDuration;
        }

        void OnClientStatusChanged(MatchPhase phase, string detail)
        {
            localStatus = detail ?? string.Empty;
            if (phase == MatchPhase.Error) errorStatus = localStatus;
            else if (phase == MatchPhase.Loading || phase == MatchPhase.Playing) errorStatus = string.Empty;
            if (phase == MatchPhase.Loading) feed.Clear();
        }

        void OnKillFeedReceived(KillFeedMessage message)
        {
            uint localNetId = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0;
            string victim = message.victimNetId == localNetId ? "You" : "Player " + message.victimNetId;
            string cause = string.IsNullOrWhiteSpace(message.cause) ? "defeated" : message.cause;
            string text;

            if (message.killerNetId == 0)
                text = victim + " — " + cause;
            else
            {
                string killer = message.killerNetId == localNetId ? "You" : "Player " + message.killerNetId;
                text = killer + " → " + victim + "  [" + cause + "]";
            }

            feed.Add(new FeedEntry { Text = text, ExpiresAt = Time.unscaledTime + FeedLifetime });
            TrimFeed();
        }

        void TrimFeed()
        {
            float now = Time.unscaledTime;
            for (int i = feed.Count - 1; i >= 0; i--)
                if (feed[i].ExpiresAt <= now) feed.RemoveAt(i);
            while (feed.Count > MaxFeedEntries) feed.RemoveAt(0);
        }

        void OnGUI()
        {
            EnsureStyles();

            float scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            if (scale <= 0f) return;

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            bool playing = manager != null && manager.ClientPhase == MatchPhase.Playing && localIdentity != null;
            if (playing)
            {
                DrawHud();
                if (localMotor == null || !localMotor.LocalInputEnabled) DrawPauseMenu();
            }
            else DrawConnectionMenu();

            GUI.matrix = previousMatrix;
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;

            panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelTexture.SetPixel(0, 0, new Color(0.035f, 0.045f, 0.065f, 0.94f));
            panelTexture.Apply();

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 42,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.3f, 0.9f, 1f) }
            };
            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.92f, 0.96f) }
            };
            centeredStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter };
            smallStyle = new GUIStyle(bodyStyle) { fontSize = 15 };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 19,
                fixedHeight = 44,
                fontStyle = FontStyle.Bold
            };
            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 18,
                fixedHeight = 38,
                padding = new RectOffset(10, 10, 6, 6)
            };
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            hudLargeStyle = new GUIStyle(hudStyle)
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleCenter
            };
        }

        void DrawConnectionMenu()
        {
            Rect panel = new Rect((VirtualWidth - 600f) * 0.5f, (VirtualHeight - 610f) * 0.5f, 600f, 610f);
            DrawPanel(panel);

            GUILayout.BeginArea(new Rect(panel.x + 38f, panel.y + 28f, panel.width - 76f, panel.height - 56f));
            GUILayout.Label("PIXEL ARENA", titleStyle, GUILayout.Height(64f));
            GUILayout.Label("LAN MULTIPLAYER PROTOTYPE", centeredStyle, GUILayout.Height(28f));
            GUILayout.Space(22f);

            if (manager == null)
            {
                GUILayout.Label("NetManager is not present in the loaded menu scene.", bodyStyle);
                GUILayout.Label("Task 06 must add the configured networking root.", smallStyle);
                GUILayout.EndArea();
                return;
            }

            bool clientActive = NetworkClient.active;
            bool connected = NetworkClient.isConnected;
            bool serverActive = NetworkServer.active;
            string status = !string.IsNullOrWhiteSpace(errorStatus) ? errorStatus
                : (string.IsNullOrWhiteSpace(manager.StatusText) ? localStatus : manager.StatusText);

            if (serverActive && !clientActive)
            {
                GUILayout.Label("DEDICATED SERVER RUNNING", headingStyle);
                GUILayout.Label("Rooms are created when clients press Play.", bodyStyle);
                GUILayout.Space(18f);
                if (GUILayout.Button("Stop Server", buttonStyle)) manager.StopServer();
                GUILayout.EndArea();
                return;
            }

            if (clientActive && !connected)
            {
                GUILayout.Label("CONNECTING", headingStyle);
                GUILayout.Label(string.IsNullOrWhiteSpace(status) ? "Connecting to " + address + "…" : status, bodyStyle);
                GUILayout.Space(18f);
                if (GUILayout.Button("Cancel", buttonStyle)) manager.StopClient();
                GUILayout.EndArea();
                return;
            }

            if (connected)
            {
                GUILayout.Label(NetworkServer.active ? "HOST CONNECTED" : "CLIENT CONNECTED", headingStyle);
                GUILayout.Label(status, bodyStyle, GUILayout.Height(58f));

                bool busy = manager.ClientPhase == MatchPhase.Loading || manager.ClientPhase == MatchPhase.Leaving;
                GUI.enabled = !busy && manager.ClientPhase != MatchPhase.Playing;
                if (GUILayout.Button(manager.ClientPhase == MatchPhase.Error ? "Retry Match" : "Play / Find Match", buttonStyle))
                    manager.ClickJoinRoom();
                GUI.enabled = true;

                if (manager.ClientMatchId != Guid.Empty)
                {
                    GUILayout.Space(8f);
                    if (GUILayout.Button("Leave Match", buttonStyle)) manager.ClickLeaveRoom();
                }

                GUILayout.Space(8f);
                if (GUILayout.Button(NetworkServer.active ? "Stop Host" : "Disconnect", buttonStyle))
                    StopNetwork();

                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("SERVER ADDRESS", smallStyle);
            address = GUILayout.TextField(address, 128, textFieldStyle);
            GUILayout.Space(14f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", buttonStyle)) StartNetwork(StartMode.Host);
            GUILayout.Space(8f);
            if (GUILayout.Button("Connect", buttonStyle)) StartNetwork(StartMode.Client);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            if (GUILayout.Button("Dedicated Server", buttonStyle)) StartNetwork(StartMode.Server);

            if (!string.IsNullOrWhiteSpace(localStatus))
            {
                GUILayout.Space(18f);
                GUILayout.Label(localStatus, bodyStyle);
            }

            if (lastMode != StartMode.None && !retrying)
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Retry Last Connection", buttonStyle)) StartCoroutine(RetryLast());
            }

            GUILayout.EndArea();
        }

        void DrawPauseMenu()
        {
            Rect panel = new Rect((VirtualWidth - 430f) * 0.5f, (VirtualHeight - 360f) * 0.5f, 430f, 360f);
            DrawPanel(panel);
            GUILayout.BeginArea(new Rect(panel.x + 34f, panel.y + 28f, panel.width - 68f, panel.height - 56f));
            GUILayout.Label("PAUSED", titleStyle, GUILayout.Height(62f));
            GUILayout.Label("Escape releases the cursor.", centeredStyle);
            GUILayout.Space(24f);

            GUI.enabled = localMotor != null && localMotor.IsAlive && localMotor.MovementEnabled;
            if (GUILayout.Button("Resume", buttonStyle)) localMotor.SetLocalInputEnabled(true);
            GUI.enabled = true;

            GUILayout.Space(8f);
            if (GUILayout.Button("Leave Match", buttonStyle)) manager.ClickLeaveRoom();
            GUILayout.Space(8f);
            if (GUILayout.Button(NetworkServer.active ? "Stop Host" : "Disconnect", buttonStyle)) StopNetwork();
            GUILayout.EndArea();
        }

        void DrawHud()
        {
            DrawDamageEdges();
            DrawCrosshair();
            DrawHealth();
            DrawWeapon();
            DrawKillFeed();
            DrawBunker();

            if (!string.IsNullOrWhiteSpace(interactionPrompt))
                GUI.Label(new Rect(560f, 625f, 800f, 42f), interactionPrompt, hudLargeStyle);

            if (localCombat != null && localCombat.IsDead)
                GUI.Label(new Rect(560f, 465f, 800f, 70f), "RESPAWNING…", titleStyle);
        }

        void DrawCrosshair()
        {
            Color previous = GUI.color;
            GUI.color = Time.unscaledTime < hitMarkerUntil
                ? new Color(1f, 0.08f, 0.04f, 1f)
                : new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(VirtualWidth * 0.5f - 12f, VirtualHeight * 0.5f - 1f, 9f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(VirtualWidth * 0.5f + 3f, VirtualHeight * 0.5f - 1f, 9f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(VirtualWidth * 0.5f - 1f, VirtualHeight * 0.5f - 12f, 2f, 9f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(VirtualWidth * 0.5f - 1f, VirtualHeight * 0.5f + 3f, 2f, 9f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawDamageEdges()
        {
            float elapsed = Time.unscaledTime - damageFlashStarted;
            if (damageFlashIntensity <= 0f || elapsed >= DamageFlashDuration)
            {
                damageFlashIntensity = 0f;
                return;
            }

            float fade = 1f - Mathf.Clamp01(elapsed / DamageFlashDuration);
            Color previous = GUI.color;
            const float band = 24f;
            for (int layer = 0; layer < 5; layer++)
            {
                float inset = layer * band;
                float alpha = damageFlashIntensity * fade * (1f - layer / 5f);
                GUI.color = new Color(0.9f, 0.02f, 0.01f, alpha);
                GUI.DrawTexture(new Rect(inset, inset, VirtualWidth - inset * 2f, band), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(inset, VirtualHeight - inset - band, VirtualWidth - inset * 2f, band), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(inset, inset + band, band, VirtualHeight - inset * 2f - band * 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(VirtualWidth - inset - band, inset + band, band,
                    VirtualHeight - inset * 2f - band * 2f), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        void DrawHealth()
        {
            if (localCombat == null) return;

            Rect background = new Rect(42f, VirtualHeight - 94f, 310f, 34f);
            float ratio = localCombat.MaxHealth > 0f ? Mathf.Clamp01(localCombat.Health / localCombat.MaxHealth) : 0f;
            DrawBar(background, ratio, new Color(0.1f, 0.85f, 0.4f, 0.95f));
            GUI.Label(new Rect(background.x + 10f, background.y - 1f, background.width - 20f, background.height),
                "HP  " + Mathf.CeilToInt(localCombat.Health) + " / " + Mathf.CeilToInt(localCombat.MaxHealth), hudStyle);
        }

        void DrawWeapon()
        {
            if (localCombat == null) return;

            WeaponSettings weapon = localCombat.SelectedWeapon;
            string name = weapon != null ? weapon.displayName : "Weapon";
            GUI.Label(new Rect(VirtualWidth - 480f, VirtualHeight - 132f, 430f, 42f), name, hudLargeStyle);
            GUI.Label(new Rect(VirtualWidth - 480f, VirtualHeight - 88f, 430f, 42f),
                localCombat.MagazineAmmo + "  /  " + localCombat.ReserveAmmo, hudLargeStyle);

            if (localCombat.IsReloading)
            {
                Rect reload = new Rect(VirtualWidth - 450f, VirtualHeight - 38f, 370f, 14f);
                DrawBar(reload, localCombat.ReloadProgress, new Color(0.25f, 0.75f, 1f, 1f));
                GUI.Label(new Rect(reload.x, reload.y - 28f, reload.width, 26f), "RELOADING", centeredStyle);
            }
        }

        void DrawKillFeed()
        {
            float y = 42f;
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                GUI.Label(new Rect(VirtualWidth - 610f, y, 560f, 30f), feed[i].Text, hudStyle);
                y += 32f;
            }
        }

        void DrawBunker()
        {
            if (localBunker == null) return;

            string text;
            switch (localBunker.Phase)
            {
                case BunkerPhase.Countdown:
                    text = "BUNKER DETONATION  " + FormatTime(localBunker.RemainingSeconds);
                    break;
                case BunkerPhase.Cooldown:
                    text = (localBunker.DoorsClosed ? "BUNKER SEALED  " : "BUNKER COOLDOWN  ")
                        + FormatTime(localBunker.RemainingSeconds);
                    break;
                default:
                    text = "BUNKER READY";
                    break;
            }

            GUI.Label(new Rect(610f, 34f, 700f, 48f), text, hudLargeStyle);
        }

        static string FormatTime(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        void DrawPanel(Rect rect)
        {
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, panelTexture);
            GUI.color = new Color(0.22f, 0.8f, 0.95f, 0.85f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 4f, rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void DrawBar(Rect rect, float ratio, Color fill)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 3f, (rect.width - 6f) * Mathf.Clamp01(ratio), rect.height - 6f),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void StartNetwork(StartMode mode)
        {
            if (manager == null) return;

            localStatus = string.Empty;
            errorStatus = string.Empty;
            lastMode = mode;
            try
            {
                switch (mode)
                {
                    case StartMode.Host:
                        manager.StartHost();
                        break;
                    case StartMode.Server:
                        manager.StartServer();
                        break;
                    case StartMode.Client:
                        string target = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
                        address = target;
                        manager.networkAddress = target;
                        manager.StartClient();
                        break;
                }
            }
            catch (Exception exception)
            {
                localStatus = exception.Message;
                Debug.LogException(exception);
            }
        }

        IEnumerator RetryLast()
        {
            if (retrying || lastMode == StartMode.None) yield break;
            retrying = true;
            StopNetwork();
            yield return null;
            BindManager();
            StartNetwork(lastMode);
            retrying = false;
        }

        void StopNetwork()
        {
            if (manager == null) return;

            if (NetworkServer.active && NetworkClient.active) manager.StopHost();
            else if (NetworkClient.active) manager.StopClient();
            else if (NetworkServer.active) manager.StopServer();

            localIdentity = null;
            localMotor = null;
            SetLocalCombat(null);
            localBunker = null;
            interactionPrompt = string.Empty;
            feed.Clear();
        }
    }
}
