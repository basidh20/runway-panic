using System;
using System.Collections.Generic;
using System.Text;
using RunwayPanic.AI;
using RunwayPanic.Core;
using RunwayPanic.Weapons;
using UnityEngine;

namespace RunwayPanic.Agents
{
    /// <summary>
    /// Test tool for the shared bird plumbing: logs every <see cref="PlayerEventHub"/> event, the
    /// <see cref="BirdPoint"/> counts per type at start, and every few seconds the living birds with their brain state
    /// and distance to the player. Every line starts with <c>[AgentsCheck]</c>, so the Console (or Logs/Editor.log)
    /// can be filtered. Drop it on any object in a sandbox; remove it from real scenes.
    /// </summary>
    public sealed class AgentsDebugLogger : MonoBehaviour
    {
        private const string Prefix = "[AgentsCheck] ";

        [Tooltip("Seconds between bird status lines. 0 = never.")]
        [SerializeField, Min(0f)] private float birdReportInterval = 2f;
        [Tooltip("At most this many birds per status line.")]
        [SerializeField, Min(1)] private int maxBirdsPerReport = 5;

        private readonly List<BirdPoint> points = new List<BirdPoint>(32);
        private readonly StringBuilder line = new StringBuilder(256);
        private PlayerEventHub hub;
        private float nextReportTime;

        private void Start()
        {
            hub = PlayerEventHub.Instance;
            if (hub == null)
                return;

            hub.ShotFired += OnShotFired;
            hub.ReloadStarted += OnReloadStarted;
            hub.ReloadFinished += OnReloadFinished;
            hub.WeaponEquipped += OnWeaponEquipped;
            hub.Interacted += OnInteracted;
            hub.ShieldActivated += OnShieldActivated;
            hub.ShieldExpired += OnShieldExpired;
            hub.HealthChanged += OnHealthChanged;
            hub.PlayerDied += OnPlayerDied;
            hub.PlayerRespawned += OnPlayerRespawned;
            BirdRegistry.BirdDied += OnBirdDied;

            line.Clear().Append(Prefix).Append("Start: player ").Append(hub.HasPlayer ? "found" : "NOT found yet")
                .Append(", grid ").Append(FlightGrid.Instance != null ? "present" : "missing (straight-line paths)")
                .Append(", BirdPoints:");
            foreach (BirdPointType type in Enum.GetValues(typeof(BirdPointType)))
            {
                BirdPoint.GetAll(type, points);
                line.Append(' ').Append(type).Append('=').Append(points.Count);
            }

            Debug.Log(line.ToString(), this);
        }

        private void OnDestroy()
        {
            BirdRegistry.BirdDied -= OnBirdDied;
            if (hub == null)
                return;

            hub.ShotFired -= OnShotFired;
            hub.ReloadStarted -= OnReloadStarted;
            hub.ReloadFinished -= OnReloadFinished;
            hub.WeaponEquipped -= OnWeaponEquipped;
            hub.Interacted -= OnInteracted;
            hub.ShieldActivated -= OnShieldActivated;
            hub.ShieldExpired -= OnShieldExpired;
            hub.HealthChanged -= OnHealthChanged;
            hub.PlayerDied -= OnPlayerDied;
            hub.PlayerRespawned -= OnPlayerRespawned;
        }

        private void Update()
        {
            if (birdReportInterval <= 0f || Time.time < nextReportTime)
                return;
            nextReportTime = Time.time + birdReportInterval;

            IReadOnlyList<BirdHealth> birds = BirdRegistry.Alive;
            line.Clear().Append(Prefix).Append("t=").Append(Time.time.ToString("F1")).Append("s birds alive=").Append(birds.Count);
            if (hub != null && hub.HasPlayer)
                line.Append(" playerSpeed=").Append(hub.PlayerVelocity.magnitude.ToString("F1"));

            for (int i = 0; i < birds.Count && i < maxBirdsPerReport; i++)
            {
                BirdHealth bird = birds[i];
                BirdBrain brain = bird.GetComponent<BirdBrain>();
                line.Append(" | ").Append(bird.name).Append(' ').Append(brain != null ? brain.CurrentStateName : "no brain");
                if (hub != null && hub.HasPlayer)
                    line.Append(" dist=").Append(Vector3.Distance(bird.transform.position, hub.PlayerPosition).ToString("F1"));
                line.Append(" y=").Append(bird.transform.position.y.ToString("F1"));
            }

            Debug.Log(line.ToString(), this);
        }

        private static void Log(string message) => Debug.Log(Prefix + message);

        private void OnShotFired(Vector3 origin, Vector3 end, IDamageable target) =>
            Log($"ShotFired origin={origin} end={end} hit={(target != null ? target.GetType().Name : "nothing")}");

        private void OnReloadStarted() => Log($"ReloadStarted (hub.IsReloading={hub.IsReloading})");

        private void OnReloadFinished() => Log($"ReloadFinished (hub.IsReloading={hub.IsReloading})");

        private void OnWeaponEquipped(WeaponBase weapon) =>
            Log($"WeaponEquipped {(weapon != null ? weapon.name : "none")} fireInterval={(weapon != null && weapon.Data != null ? weapon.Data.FireInterval.ToString("F2") : "-")}s");

        private void OnInteracted(IInteractable target) => Log($"Interacted {(target != null ? target.GetType().Name : "null")}");

        private void OnShieldActivated(float duration) => Log($"ShieldActivated {duration:F1}s");

        private void OnShieldExpired() => Log("ShieldExpired");

        private void OnHealthChanged(float current, float max) => Log($"HealthChanged {current:F0}/{max:F0}");

        private void OnPlayerDied() => Log("PlayerDied");

        private void OnPlayerRespawned() => Log("PlayerRespawned");

        private void OnBirdDied(BirdDeathInfo info) => Log($"BirdDied {info.Species} at {info.Position}");
    }
}
