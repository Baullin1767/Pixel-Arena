using UnityEngine;
namespace PixelArena
{
    // Static arena marker: no NetworkIdentity in additive arena scenes.
    public sealed class ArenaBunker : MonoBehaviour
    {
        public BoxCollider safeVolume;
        public Transform door;
        public Vector3 closedDoorLocalPosition;
        public Vector3 openDoorLocalPosition;
        public Transform button;
        public void SetDoorClosed(bool closed)
        {
            if (door != null) door.localPosition = closed ? closedDoorLocalPosition : openDoorLocalPosition;
        }
        public bool ContainsPlayer(PlayerMotor player)
        {
            if (safeVolume == null || player == null || player.gameObject.scene != gameObject.scene) return false;
            var capsule = player.GetComponent<CapsuleCollider>();
            if (capsule == null || !capsule.enabled) return false;
            // Require the entire authoritative body inside, including at the doorway.
            var bounds = capsule.bounds;
            var half = safeVolume.size * .5f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var p = safeVolume.transform.InverseTransformPoint(corner) - safeVolume.center;
                if (Mathf.Abs(p.x) > half.x || Mathf.Abs(p.y) > half.y || Mathf.Abs(p.z) > half.z) return false;
            }
            return true;
        }
    }
}