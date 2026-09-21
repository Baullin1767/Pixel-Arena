using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PixelArena.EditorTests
{
    public sealed class MvpValidationTests
    {
        static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
        static bool Has(GameObject value, string type) => value.GetComponents<Component>().Any(c => c.GetType().FullName == type);

        [Test]
        public void GeneratedPrefabsContainRequiredGameplayComposition()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PixelArena/Prefabs/Player.prefab");
            var match = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PixelArena/Prefabs/MatchManager.prefab");
            var projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PixelArena/Prefabs/Projectile.prefab");
            Assert.That(player, Is.Not.Null);
            Assert.That(new[] { "Mirror.NetworkIdentity", "Mirror.NetworkMatch", "PixelArena.RoomMember",
                "PixelArena.PlayerMotor", "PixelArena.PlayerCombat", "PixelArena.PlayerWeaponVisuals" }.All(t => Has(player, t)), Is.True);
            Assert.That(Has(match, "MatchManager") && Has(match, "PixelArena.BunkerController"), Is.True);
            Assert.That(Has(projectile, "PixelArena.CombatProjectile"), Is.True);
            Assert.That(projectile.GetComponent<Rigidbody>(), Is.Null,
                "The shared projectile prefab must stay non-physical; grenade physics is added at runtime only.");
            Assert.That(projectile.GetComponent<Collider>(), Is.Null);
        }

        [Test]
        public void WeaponDefaultsCoverFiveWeaponsAndRequiredCadence()
        {
            var settings = RuntimeType("PixelArena.WeaponSettings");
            var weapons = (Array)settings.GetMethod("CreatePrototypeDefaults").Invoke(null, null);
            Assert.That(weapons.Length, Is.EqualTo(5));
            var rifle = weapons.GetValue(1);
            Assert.That((bool)settings.GetField("automatic").GetValue(rifle), Is.True);
            Assert.That((float)settings.GetField("shotInterval").GetValue(rifle), Is.EqualTo(.12f).Within(.0001f));
            Assert.That((int)settings.GetField("maxBounces").GetValue(weapons.GetValue(3)), Is.EqualTo(10));
            Assert.That((float)settings.GetField("projectileBounciness").GetValue(weapons.GetValue(4)),
                Is.GreaterThan(0f));
        }

        [Test]
        public void ArenaHasEightSpawnsBunkerAndNoSceneNetworkIdentities()
        {
            var scene = EditorSceneManager.OpenScene("Assets/PixelArena/Scenes/Arena.unity", OpenSceneMode.Additive);
            try
            {
                var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
                Assert.That(components.Count(c => c.GetType().FullName == "PixelArena.ArenaSpawnPoint"), Is.EqualTo(8));
                Assert.That(components.Count(c => c.GetType().FullName == "PixelArena.ArenaBunker"), Is.EqualTo(1));
                Assert.That(components.Any(c => c.GetType().FullName == "Mirror.NetworkIdentity"), Is.False);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void MenuUsesWebGlCompatibleSimpleWebTransport()
        {
            var scene = EditorSceneManager.OpenScene("Assets/PixelArena/Scenes/Menu.unity", OpenSceneMode.Additive);
            try
            {
                var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
                Assert.That(components.Any(c => c.GetType().FullName == "Mirror.SimpleWeb.SimpleWebTransport"), Is.True);
                Assert.That(components.Any(c => c.GetType().FullName == "kcp2k.KcpTransport"), Is.False);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
