using UnityEngine;
namespace PixelArena
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class LadderVolume : MonoBehaviour
    {
        [Min(0.1f)] public float climbSpeed = 3f;
        void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        void Awake() { GetComponent<BoxCollider>().isTrigger = true; }
    }
}
