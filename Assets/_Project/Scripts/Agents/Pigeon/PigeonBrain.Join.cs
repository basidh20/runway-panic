namespace RunwayPanic.Agents.Pigeon
{
    /// <summary>
    /// Flock membership: a new pigeon joins the scene's <see cref="PigeonFlock"/> as soon as it exists, so a bird that
    /// spawns while the flock is already busy simply becomes one more member and takes the role the flock's current
    /// mode gives it. Leaving goes through one method, <see cref="ReleaseFlockClaims"/>, called on death, disable and
    /// destroy, so a removed bird can never keep a roost slot or a dive token.
    /// </summary>
    public sealed partial class PigeonBrain
    {
        // The flock this bird joined, kept so the same one is left (reading PigeonFlock.Instance while a scene is torn
        // down could create a new coordinator).
        private PigeonFlock flock;

        /// <summary>The flock this bird belongs to, or null (not joined yet, flock full, or dead).</summary>
        public PigeonFlock Flock => flock;

        partial void EnterJoinFlock()
        {
            TryJoinFlock();
        }

        partial void TickJoinFlock()
        {
            // A full flock (or none at all) leaves this bird on its own.
            ChangeState(flock != null ? PigeonState.Flocking : PigeonState.Solo);
        }

        private bool TryJoinFlock()
        {
            if (flock != null)
                return true;

            PigeonFlock candidate = PigeonFlock.Instance;
            if (candidate == null || !candidate.Join(this))
                return false;

            flock = candidate;
            return true;
        }

        /// <summary>Leaves the flock, which also frees anything the flock reserved for this bird. Safe to call repeatedly.</summary>
        private void ReleaseFlockClaims()
        {
            // Null = never joined or already left; a destroyed coordinator (scene teardown) has nothing left to free.
            if (flock != null)
                flock.Leave(this);
            flock = null;
        }
    }
}
