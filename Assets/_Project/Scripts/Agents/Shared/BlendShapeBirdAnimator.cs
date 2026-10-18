using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Fallback <see cref="IBirdAnimator"/> until the Agent Controller's Animator-based one (F4):
    /// drives only the <c>Wings_Spread</c> blend shape on the model's <c>Bird_Mesh</c>, easing toward the requested value.
    /// Every other call does nothing. Put it on the model child of a bird prefab.
    /// </summary>
    public sealed class BlendShapeBirdAnimator : MonoBehaviour, IBirdAnimator
    {
        private const string MeshName = "Bird_Mesh";
        private const string WingsShape = "Wings_Spread";

        [Tooltip("How fast the wings move toward the requested spread, in full spreads per second.")]
        [SerializeField, Min(0.1f)] private float wingSpeed = 4f;

        private SkinnedMeshRenderer meshRenderer;
        private int wingsIndex = -1;
        private float wings = 1f;
        private float wingsTarget = 1f;

        private void Awake()
        {
            foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.name != MeshName || candidate.sharedMesh == null)
                    continue;
                meshRenderer = candidate;
                wingsIndex = candidate.sharedMesh.GetBlendShapeIndex(WingsShape);
                break;
            }

            if (wingsIndex < 0)
            {
                Debug.LogWarning($"{nameof(BlendShapeBirdAnimator)} on '{name}' found no '{MeshName}' with a '{WingsShape}' blend shape; wings will not move.", this);
                enabled = false;
                return;
            }

            // Start from the pose the prefab already has (the species variants are saved with the wings spread).
            wings = wingsTarget = meshRenderer.GetBlendShapeWeight(wingsIndex) / 100f;
        }

        private void Update()
        {
            if (Mathf.Approximately(wings, wingsTarget))
                return;
            wings = Mathf.MoveTowards(wings, wingsTarget, wingSpeed * Time.deltaTime);
            meshRenderer.SetBlendShapeWeight(wingsIndex, wings * 100f);
        }

        /// <inheritdoc />
        public void SetFlying(bool flying)
        {
        }

        /// <inheritdoc />
        public void SetDiving(bool diving)
        {
        }

        /// <inheritdoc />
        public void SetWingsSpread(float amount)
        {
            wingsTarget = Mathf.Clamp01(amount);
        }

        /// <inheritdoc />
        public void PlayHit()
        {
        }

        /// <inheritdoc />
        public void PlayDeath()
        {
        }
    }
}
