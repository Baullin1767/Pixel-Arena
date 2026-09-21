using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixelArena
{
    /// <summary>Call Build in an empty arena scene at edit time, then save it (task 06).
    /// Materials are supplied by integration and must be persistent assets when saving.</summary>
    public static class ArenaBuilder
    {
        public static GameObject Build(Scene scene, Material structure = null, Material accent = null)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("A loaded scene is required.");
            foreach (var existing in scene.GetRootGameObjects())
                if (existing.GetComponentInChildren<ArenaBunker>(true) != null)
                    throw new InvalidOperationException("Arena already contains a bunker; use a new empty scene.");
            var root = new GameObject("Pixel Arena Geometry");
            SceneManager.MoveGameObjectToScene(root, scene);
            Cube(root.transform, "Floor", new Vector3(0,-.5f,0), new Vector3(48,1,48), structure);
            Cube(root.transform, "North wall", new Vector3(0,3,24), new Vector3(49,6,1), structure);
            Cube(root.transform, "South wall", new Vector3(0,3,-24), new Vector3(49,6,1), structure);
            Cube(root.transform, "East wall", new Vector3(24,3,0), new Vector3(1,6,48), structure);
            Cube(root.transform, "West wall", new Vector3(-24,3,0), new Vector3(1,6,48), structure);

            Cube(root.transform, "Upper west platform", new Vector3(-14,3.75f,0), new Vector3(8,.5f,16), structure);
            Cube(root.transform, "Upper east platform", new Vector3(14,3.75f,0), new Vector3(8,.5f,16), structure);
            Cube(root.transform, "Cross bridge", new Vector3(0,3.75f,5), new Vector3(20,.5f,3), accent);
            // Small risers are below the motor's capsule radius and form a walkable staircase.
            for (int i = 0; i < 20; i++)
            {
                float h = (i + 1) * .2f;
                Cube(root.transform, "West step " + i, new Vector3(-14,h*.5f,-18 + i*.5f),
                    new Vector3(4,h,.5f), structure);
            }
            Cube(root.transform, "Central cover A", new Vector3(-4,1,-3), new Vector3(2,2,6), accent);
            Cube(root.transform, "Central cover B", new Vector3(5,1,0), new Vector3(3,2,3), accent);
            Cube(root.transform, "South passage wall", new Vector3(0,1.5f,-14), new Vector3(12,3,1), structure);
            Cube(root.transform, "West support", new Vector3(-17,1.8f,5), new Vector3(1,3.6f,1), structure);
            Cube(root.transform, "East support", new Vector3(17,1.8f,5), new Vector3(1,3.6f,1), structure);

            var ladder = new GameObject("East ladder");
            ladder.transform.SetParent(root.transform, false);
            ladder.transform.localPosition = new Vector3(9.4f,2.4f,0);
            var ladderBox = ladder.AddComponent<BoxCollider>(); ladderBox.size = new Vector3(1.3f,4.8f,2);
            ladder.AddComponent<LadderVolume>();
            for (int i = 0; i < 11; i++)
                Cube(root.transform, "Ladder rung " + i, new Vector3(9.8f,.3f+i*.4f,0),
                    new Vector3(.15f,.08f,1.6f), accent, false);

            var bunkerRoot = new GameObject("Bunker");
            bunkerRoot.transform.SetParent(root.transform, false);
            bunkerRoot.transform.localPosition = new Vector3(0,0,17);
            var bunker = bunkerRoot.AddComponent<ArenaBunker>();
            Cube(bunkerRoot.transform, "Back", new Vector3(0,2,5), new Vector3(12,4,1), structure);
            Cube(bunkerRoot.transform, "Left", new Vector3(-6,2,0), new Vector3(1,4,10), structure);
            Cube(bunkerRoot.transform, "Right", new Vector3(6,2,0), new Vector3(1,4,10), structure);
            Cube(bunkerRoot.transform, "Front left", new Vector3(-3.75f,2,-5), new Vector3(4.5f,4,1), structure);
            Cube(bunkerRoot.transform, "Front right", new Vector3(3.75f,2,-5), new Vector3(4.5f,4,1), structure);
            Cube(bunkerRoot.transform, "Roof", new Vector3(0,4.25f,0), new Vector3(13,.5f,11), structure);
            bunker.closedDoorLocalPosition = new Vector3(0,2,-5);
            bunker.openDoorLocalPosition = new Vector3(0,6,-5);
            bunker.door = Cube(bunkerRoot.transform, "Blast door", bunker.openDoorLocalPosition,
                new Vector3(3,4,1), accent).transform;
            var safe = new GameObject("Authoritative safe volume");
            safe.transform.SetParent(bunkerRoot.transform, false);
            safe.transform.localPosition = new Vector3(0,2,0);
            bunker.safeVolume = safe.AddComponent<BoxCollider>();
            bunker.safeVolume.size = new Vector3(11,4,9);
            bunker.safeVolume.isTrigger = true;
            var button = Cube(bunkerRoot.transform, "Alarm button", new Vector3(2,1.5f,-5.65f),
                new Vector3(.6f,.6f,.3f), accent);
            bunker.button = button.transform;
            button.AddComponent<BunkerButton>().bunker = bunker;

            Vector3[] spawns = { new(-19,.1f,-19), new(19,.1f,-19), new(-19,.1f,16),
                new(19,.1f,16), new(-14,4.1f,3), new(14,4.1f,-3), new(0,.1f,-20), new(0,.1f,18) };
            for (int i = 0; i < spawns.Length; i++)
            {
                var point = new GameObject("Spawn " + (i+1));
                point.transform.SetParent(root.transform, false); point.transform.localPosition = spawns[i];
                point.transform.localRotation = Quaternion.LookRotation(new Vector3(-spawns[i].x,0,-spawns[i].z));
                point.AddComponent<ArenaSpawnPoint>();
            }
            var lightObject = new GameObject("Arena light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(50,-35,0);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
            // Cameras/listeners are owned exclusively by PlayerMotor.
            return root;
        }
        static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collision = true)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale;
            if (material != null) obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collision) obj.GetComponent<Collider>().enabled = false;
            return obj;
        }
    }
}