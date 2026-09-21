using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixelArena
{
    public static class MvpBuild
    {
        public const string MenuPath = "Assets/PixelArena/Scenes/Menu.unity";
        public const string ArenaPath = "Assets/PixelArena/Scenes/Arena.unity";
        const string Prefabs = "Assets/PixelArena/Prefabs/";
        [MenuItem("Pixel Arena/Rebuild MVP Scenes and Prefabs")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scenes before rebuilding.");
            EnsureFolder("Assets/PixelArena/Scenes");
            EnsureFolder("Assets/PixelArena/Prefabs");
            EnsureFolder("Assets/PixelArena/Materials");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(stage);
                // The generated scene may be the one currently open. Close that
                // loaded instance before saving a replacement to the same path;
                // RestoreSceneManagerSetup reopens the rebuilt asset afterwards.
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    var loaded = SceneManager.GetSceneAt(i);
                    if (loaded != stage && IsGeneratedScene(loaded.path))
                        EditorSceneManager.CloseScene(loaded, true);
                }
                var structure = Material("Structure", new Color(.27f,.34f,.42f));
                var accent = Material("Accent", new Color(.95f,.48f,.12f));
                var bodyMaterial = Material("Player", new Color(.16f,.7f,.85f));
                var projectile = NetworkRoot("Projectile");
                projectile.AddComponent<RoomMember>();
                projectile.AddComponent<CombatProjectile>();
                var projectilePrefab = PrefabUtility.SaveAsPrefabAsset(projectile, Prefabs + "Projectile.prefab");
                UnityEngine.Object.DestroyImmediate(projectile);
                var player = NetworkRoot("Player");
                player.AddComponent<RoomMember>();
                var capsule = player.AddComponent<CapsuleCollider>();
                capsule.height = 2; capsule.radius = .4f; capsule.center = Vector3.up;
                var rigidbody = player.AddComponent<Rigidbody>();
                rigidbody.useGravity = false; rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body"; body.transform.SetParent(player.transform, false);
                body.transform.localPosition = Vector3.up;
                body.transform.localScale = new Vector3(.8f,1,.8f);
                UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
                player.AddComponent<PlayerMotor>();
                player.AddComponent<PlayerCombat>().projectilePrefab = projectilePrefab;
                player.AddComponent<PlayerWeaponVisuals>();
                var playerPrefab = PrefabUtility.SaveAsPrefabAsset(player, Prefabs + "Player.prefab");
                UnityEngine.Object.DestroyImmediate(player);
                var match = NetworkRoot("MatchManager");
                match.AddComponent<MatchManager>(); match.AddComponent<BunkerController>();
                var matchPrefab = PrefabUtility.SaveAsPrefabAsset(match, Prefabs + "MatchManager.prefab");
                UnityEngine.Object.DestroyImmediate(match);
                ArenaBuilder.Build(stage, structure, accent);
                EditorSceneManager.SaveScene(stage, ArenaPath);
                var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(menu);
                // Keep one scene loaded at all times; Unity warns and refuses when
                // asked to close the last loaded scene.
                EditorSceneManager.CloseScene(stage, true);
                var network = new GameObject("Pixel Arena Network");
                var transport = network.AddComponent<kcp2k.KcpTransport>(); transport.Port = 7777;
                var manager = network.AddComponent<NetManager>();
                manager.transport = transport; manager.playerPrefab = playerPrefab;
                manager.matchManager = matchPrefab.GetComponent<MatchManager>();
                manager.arenaScene = ArenaPath; manager.roomCapacity = 5;
                manager.maxConnections = 100;
                manager.autoCreatePlayer = false;
                manager.offlineScene = ""; manager.onlineScene = "";
                manager.headlessStartMode = HeadlessStartOptions.AutoStartServer;
                manager.editorAutoStart = false;
                manager.spawnPrefabs = new List<GameObject> { matchPrefab, projectilePrefab };
                var camera = new GameObject("Menu Background Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.025f,.035f,.055f);
                camera.cullingMask = 0; camera.depth = -100;
                var light = new GameObject("Menu Light").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 0;
                EditorSceneManager.SaveScene(menu, MenuPath);
                var previous = EditorBuildSettings.scenes.Where(s => s.path != MenuPath && s.path != ArenaPath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuPath,true), new EditorBuildSettingsScene(ArenaPath,true) }.Concat(previous).ToArray();
                AssetDatabase.SaveAssets();
                Debug.Log("[PixelArena] MVP scenes and prefabs rebuilt.");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
        static GameObject NetworkRoot(string name)
        {
            var root = new GameObject(name);
            root.AddComponent<NetworkIdentity>(); root.AddComponent<NetworkMatch>();
            return root;
        }
        static bool IsGeneratedScene(string path) => path == MenuPath || path == ArenaPath;
        static Material Material(string name, Color color)
        {
            string path = "Assets/PixelArena/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader missing.");
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
        [MenuItem("Pixel Arena/Build Windows MVP")]
        public static void BuildWindows()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { MenuPath, ArenaPath },
                locationPathName = "Builds/Windows/PixelArena.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("MVP build failed: " + report.summary.result);
            Debug.Log("[PixelArena] Windows build succeeded: " + report.summary.outputPath);
        }
    }
}
