using UnityEngine;
using UnityEngine.Rendering;

namespace PixelArena
{
    /// <summary>
    /// Lightweight procedural weapon models. Remote players carry a world model;
    /// the owner gets a dedicated first-person model parented to the FPS camera.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(PlayerCombat), typeof(PlayerMotor))]
    public sealed class PlayerWeaponVisuals : MonoBehaviour
    {
        PlayerCombat combat;
        PlayerMotor motor;
        Transform worldRig;
        Transform viewRig;
        GameObject[] worldWeapons;
        GameObject[] viewWeapons;
        Material darkMaterial;
        Material metalMaterial;
        Material accentMaterial;
        Material gloveMaterial;
        int displayedWeapon = -1;

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            motor = GetComponent<PlayerMotor>();
            CreateMaterials();
            worldRig = BuildRig("World Weapon Rig", transform, false, out worldWeapons);
            worldRig.localPosition = new Vector3(0.31f, 1.35f, 0.43f);
            worldRig.localRotation = Quaternion.Euler(-4f, 0f, 0f);
            worldRig.localScale = Vector3.one * 0.82f;
        }

        void LateUpdate()
        {
            if (combat == null || motor == null) return;

            if (combat.isLocalPlayer && viewRig == null && motor.LocalCamera != null)
            {
                viewRig = BuildRig("First Person Weapon Rig", motor.LocalCamera.transform, true, out viewWeapons);
                viewRig.localPosition = new Vector3(0.28f, -0.24f, 0.56f);
                viewRig.localRotation = Quaternion.Euler(1.5f, -2f, 0f);
                viewRig.localScale = Vector3.one * 0.78f;
                displayedWeapon = -1;
            }

            bool alive = combat.IsAlive;
            if (worldRig != null) worldRig.gameObject.SetActive(alive && !combat.isLocalPlayer);
            if (viewRig != null) viewRig.gameObject.SetActive(alive && combat.isLocalPlayer);

            int selected = Mathf.Clamp(combat.SelectedWeaponIndex, 0, 4);
            if (selected != displayedWeapon)
            {
                SetSelected(worldWeapons, selected);
                SetSelected(viewWeapons, selected);
                displayedWeapon = selected;
            }

            if (viewRig != null && viewRig.gameObject.activeSelf)
            {
                float bob = Mathf.Sin(Time.time * 7f) * 0.006f;
                float reloadDip = combat.IsReloading ? Mathf.Sin(combat.ReloadProgress * Mathf.PI) * 18f : 0f;
                viewRig.localPosition = new Vector3(0.28f, -0.24f + bob, 0.56f);
                viewRig.localRotation = Quaternion.Euler(1.5f + reloadDip, -2f, bob * 80f);
            }
        }

        Transform BuildRig(string name, Transform parent, bool firstPerson, out GameObject[] weapons)
        {
            var rig = new GameObject(name).transform;
            rig.SetParent(parent, false);

            BuildHands(rig, firstPerson);
            weapons = new GameObject[5];
            for (int i = 0; i < weapons.Length; i++)
            {
                var weapon = new GameObject(((WeaponKind)i) + " Visual");
                weapon.transform.SetParent(rig, false);
                BuildWeapon(weapon.transform, (WeaponKind)i, firstPerson);
                weapon.SetActive(i == 0);
                weapons[i] = weapon;
            }
            return rig;
        }

        void BuildHands(Transform rig, bool firstPerson)
        {
            if (firstPerson)
            {
                AddLimb(rig, "Left Forearm", new Vector3(-0.42f, -0.38f, -0.12f), new Vector3(-0.13f, -0.06f, 0.24f), 0.09f);
                AddLimb(rig, "Right Forearm", new Vector3(0.35f, -0.42f, -0.12f), new Vector3(0.08f, -0.13f, 0.08f), 0.09f);
                AddSphere(rig, "Left Hand", new Vector3(-0.13f, -0.06f, 0.24f), Vector3.one * 0.16f, gloveMaterial, true);
                AddSphere(rig, "Right Hand", new Vector3(0.08f, -0.13f, 0.08f), Vector3.one * 0.16f, gloveMaterial, true);
            }
            else
            {
                AddLimb(rig, "Left Arm", new Vector3(-0.43f, 0.04f, -0.18f), new Vector3(-0.12f, -0.03f, 0.2f), 0.1f);
                AddLimb(rig, "Right Arm", new Vector3(0.43f, 0.04f, -0.18f), new Vector3(0.1f, -0.1f, 0.05f), 0.1f);
                AddSphere(rig, "Left Hand", new Vector3(-0.12f, -0.03f, 0.2f), Vector3.one * 0.17f, gloveMaterial, false);
                AddSphere(rig, "Right Hand", new Vector3(0.1f, -0.1f, 0.05f), Vector3.one * 0.17f, gloveMaterial, false);
            }
        }

        void BuildWeapon(Transform root, WeaponKind kind, bool firstPerson)
        {
            switch (kind)
            {
                case WeaponKind.Pistol:
                    AddBox(root, "Pistol Frame", new Vector3(0f, 0f, 0.2f), new Vector3(.18f, .16f, .5f), darkMaterial, firstPerson);
                    AddBox(root, "Pistol Slide", new Vector3(0f, .09f, .23f), new Vector3(.19f, .09f, .48f), metalMaterial, firstPerson);
                    AddBox(root, "Pistol Grip", new Vector3(0f, -.2f, .02f), new Vector3(.15f, .34f, .17f), darkMaterial, firstPerson, new Vector3(-13f, 0f, 0f));
                    AddCylinder(root, "Pistol Muzzle", new Vector3(0f, .03f, .5f), .055f, .16f, metalMaterial, firstPerson);
                    break;
                case WeaponKind.Rifle:
                    AddBox(root, "Rifle Receiver", new Vector3(0f, .01f, .28f), new Vector3(.2f, .2f, .72f), darkMaterial, firstPerson);
                    AddBox(root, "Rifle Stock", new Vector3(0f, -.01f, -.22f), new Vector3(.22f, .24f, .38f), metalMaterial, firstPerson, new Vector3(8f, 0f, 0f));
                    AddCylinder(root, "Rifle Barrel", new Vector3(0f, .04f, .77f), .055f, .62f, metalMaterial, firstPerson);
                    AddBox(root, "Rifle Magazine", new Vector3(0f, -.23f, .23f), new Vector3(.15f, .35f, .2f), accentMaterial, firstPerson, new Vector3(-10f, 0f, 0f));
                    AddBox(root, "Rifle Sight", new Vector3(0f, .18f, .3f), new Vector3(.08f, .08f, .2f), accentMaterial, firstPerson);
                    break;
                case WeaponKind.RocketLauncher:
                    AddCylinder(root, "Launcher Tube", new Vector3(0f, .04f, .38f), .16f, 1.08f, darkMaterial, firstPerson);
                    AddCylinder(root, "Launcher Front Ring", new Vector3(0f, .04f, .91f), .205f, .12f, accentMaterial, firstPerson);
                    AddCylinder(root, "Launcher Rear Ring", new Vector3(0f, .04f, -.14f), .19f, .12f, metalMaterial, firstPerson);
                    AddBox(root, "Launcher Grip", new Vector3(0f, -.24f, .18f), new Vector3(.15f, .35f, .16f), darkMaterial, firstPerson, new Vector3(-8f, 0f, 0f));
                    break;
                case WeaponKind.RicochetGun:
                    AddBox(root, "Ricochet Body", new Vector3(0f, .02f, .26f), new Vector3(.24f, .22f, .68f), accentMaterial, firstPerson);
                    AddCylinder(root, "Ricochet Barrel", new Vector3(0f, .04f, .7f), .075f, .38f, metalMaterial, firstPerson);
                    AddBox(root, "Ricochet Coil", new Vector3(0f, .17f, .28f), new Vector3(.13f, .12f, .35f), metalMaterial, firstPerson);
                    AddBox(root, "Ricochet Grip", new Vector3(0f, -.22f, .08f), new Vector3(.16f, .35f, .18f), darkMaterial, firstPerson, new Vector3(-12f, 0f, 0f));
                    break;
                case WeaponKind.GrenadeLauncher:
                    AddBox(root, "Grenade Receiver", new Vector3(0f, 0f, .2f), new Vector3(.24f, .24f, .5f), darkMaterial, firstPerson);
                    AddCylinder(root, "Grenade Barrel", new Vector3(0f, .06f, .58f), .14f, .48f, metalMaterial, firstPerson);
                    AddCylinder(root, "Grenade Muzzle", new Vector3(0f, .06f, .82f), .18f, .12f, accentMaterial, firstPerson);
                    AddBox(root, "Grenade Drum", new Vector3(0f, -.11f, .25f), new Vector3(.34f, .34f, .3f), metalMaterial, firstPerson, new Vector3(0f, 0f, 45f));
                    AddBox(root, "Grenade Grip", new Vector3(0f, -.29f, .02f), new Vector3(.16f, .34f, .17f), darkMaterial, firstPerson, new Vector3(-12f, 0f, 0f));
                    break;
            }
        }

        void AddLimb(Transform parent, string name, Vector3 start, Vector3 end, float radius)
        {
            Vector3 direction = end - start;
            var limb = AddPrimitive(PrimitiveType.Capsule, parent, name, (start + end) * .5f,
                new Vector3(radius, direction.magnitude * .5f, radius), gloveMaterial, true);
            limb.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        }

        GameObject AddBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material,
            bool firstPerson, Vector3? euler = null)
        {
            var value = AddPrimitive(PrimitiveType.Cube, parent, name, position, scale, material, firstPerson);
            if (euler.HasValue) value.transform.localRotation = Quaternion.Euler(euler.Value);
            return value;
        }

        GameObject AddSphere(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool firstPerson)
            => AddPrimitive(PrimitiveType.Sphere, parent, name, position, scale, material, firstPerson);

        GameObject AddCylinder(Transform parent, string name, Vector3 position, float radius, float length,
            Material material, bool firstPerson)
        {
            var value = AddPrimitive(PrimitiveType.Cylinder, parent, name, position,
                new Vector3(radius, length * .5f, radius), material, firstPerson);
            value.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return value;
        }

        GameObject AddPrimitive(PrimitiveType type, Transform parent, string name, Vector3 position,
            Vector3 scale, Material material, bool firstPerson)
        {
            var value = GameObject.CreatePrimitive(type);
            value.name = name;
            value.transform.SetParent(parent, false);
            value.transform.localPosition = position;
            value.transform.localScale = scale;
            var collider = value.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var renderer = value.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = firstPerson ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = !firstPerson;
            return value;
        }

        void CreateMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            darkMaterial = MakeMaterial(shader, "Weapon Dark", new Color(.055f, .07f, .085f));
            metalMaterial = MakeMaterial(shader, "Weapon Metal", new Color(.24f, .29f, .34f));
            accentMaterial = MakeMaterial(shader, "Weapon Accent", new Color(1f, .34f, .06f));
            gloveMaterial = MakeMaterial(shader, "Weapon Gloves", new Color(.08f, .12f, .16f));
        }

        static Material MakeMaterial(Shader shader, string name, Color color)
        {
            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .48f);
            return material;
        }

        static void SetSelected(GameObject[] weapons, int selected)
        {
            if (weapons == null) return;
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] != null) weapons[i].SetActive(i == selected);
        }

        void OnDestroy()
        {
            Destroy(darkMaterial);
            Destroy(metalMaterial);
            Destroy(accentMaterial);
            Destroy(gloveMaterial);
        }
    }
}
