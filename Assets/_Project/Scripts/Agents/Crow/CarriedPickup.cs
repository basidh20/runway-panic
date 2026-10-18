using RunwayPanic.Interaction;
using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// A pickup held by a crow. While carried its colliders are off, so the player cannot collect it in mid-air;
    /// dropping puts it back on the ground with its colliders on, ready to be collected again.
    /// Uses only the pickup's GameObject (no Pickup internals), so the Systems Engineer's code is untouched.
    /// </summary>
    public sealed class CarriedPickup
    {
        private static readonly Vector3 BeakOffset = new Vector3(0f, -0.05f, 0.12f);
        private const float GroundProbeHeight = 3f;

        private Collider[] colliders;

        /// <summary>The pickup being carried.</summary>
        public Pickup Item { get; private set; }

        /// <summary>Takes <paramref name="pickup"/>: disables its colliders and attaches it under <paramref name="socket"/>.</summary>
        public void Attach(Pickup pickup, Transform socket)
        {
            Item = pickup;
            colliders = pickup.GetComponentsInChildren<Collider>(true);
            foreach (Collider c in colliders)
                c.enabled = false;

            // Keep the world scale: the crow's bones are scaled and would shrink the pickup.
            pickup.transform.SetParent(socket, true);
            pickup.transform.position = socket.TransformPoint(BeakOffset);
        }

        /// <summary>Puts the pickup on the ground below <paramref name="near"/> and makes it collectable again.</summary>
        public void Drop(Vector3 near)
        {
            if (Item == null)
                return;

            Transform t = Item.transform;
            t.SetParent(null, true);
            t.SetPositionAndRotation(GroundBelow(near), Quaternion.identity);

            foreach (Collider c in colliders)
                if (c != null)
                    c.enabled = true;

            Item = null;
            colliders = null;
        }

        private static readonly RaycastHit[] hits = new RaycastHit[8];

        private static Vector3 GroundBelow(Vector3 position)
        {
            Vector3 from = position + Vector3.up * GroundProbeHeight;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, hits, GroundProbeHeight + 30f, ~0, QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;
            Vector3 point = new Vector3(position.x, 0f, position.z);
            for (int i = 0; i < count; i++)
            {
                // Ignore birds and the player; land on the first solid surface below.
                if (RunwayPanic.AI.FlightGrid.IsAgentCollider(hits[i].collider))
                    continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    point = hits[i].point;
                }
            }

            return point;
        }
    }
}
