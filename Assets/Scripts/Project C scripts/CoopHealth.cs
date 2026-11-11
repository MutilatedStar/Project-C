using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ilumisoft.HealthSystem
{
    /// <summary>
    /// The player version of Health. Put it on Player 1 and Player 2 INSTEAD of Health (remove the old Health
    /// component first: two health components on one object would fight). It extends Health, so the foes,
    /// Hitbox, Healthbar and PlayerHealthbar keep working with it unchanged.
    ///
    /// Each player has their own health, so damage is split per player by default. On top of that:
    ///   CoopRules.SharedDamage      a hit on one player also hits every other player that counts (full amount)
    ///   CoopRules.SinglePlayerMode  a player who doesn't count takes no damage at all
    ///   Invincible                  while true this player takes no damage at all (PlayerEffects sets it for the
    ///                               invincibility power-up). A hit that is blocked is not passed on to the other player either.
    ///   Grace                       the same, owned by CoopDowned: a short safe moment after a revive or a respawn.
    ///   Airborne                    the same, but owned by the jump: TopDownJump sets it in the air and clears it on landing. It is a
    ///                               separate flag so landing can never switch the invincibility power-up off.
    ///
    /// Helpers for other scripts:
    ///   CoopHealth.Nearest(position)   the closest living player that counts (foes: "who do I chase / hit?")
    ///   CoopHealth.Get(number)         Player 1's or Player 2's health, or null if they aren't in the game right now
    ///   CoopHealth.AllCountedDead      true once every player that counts is dead
    ///   CoopHealth.TeamWiped           event raised when the last player that counts dies (use it for the Failure Ending)
    ///
    /// Reads the Player Number from this character's PlayerInputSource, which the PlayerInputHub in the scene attaches (no input
    /// component is needed on the prefab). Needs CoopRules.cs.
    /// </summary>
    [AddComponentMenu("Health System/Coop Health")]
    public class CoopHealth : Health
    {
        /// <summary>Every CoopHealth that is switched on right now (Player 2 is missing while they are switched off).</summary>
        public static readonly List<CoopHealth> Players = new List<CoopHealth>();

        /// <summary>Raised when the last player that counts dies.</summary>
        public static event Action TeamWiped;

        // True while a shared hit is being passed round, so it isn't passed round again forever.
        private static bool _mirroring;

        private PlayerInputSource _input;

        /// <summary>While true, damage does nothing (healing still works). Set by PlayerEffects, or by your own scripts.</summary>
        public bool Invincible { get; set; }

        /// <summary>While true, damage does nothing (healing still works). Set by TopDownJump while the player is in the air: no hurt box up there.</summary>
        public bool Airborne { get; set; }

        /// <summary>
        /// While true, damage does nothing (healing still works). Set by CoopDowned for a short moment after a revive or a respawn,
        /// so the player is not knocked straight back down by whatever hit them. A separate flag, so it can never switch the
        /// invincibility power-up (Invincible) or the jump (Airborne) off.
        /// </summary>
        public bool Grace { get; set; }

        /// <summary>
        /// While true, SplitScreenCamera keeps following this player even though their health is 0. CoopDowned no longer sets it: the camera
        /// now lets go of a knocked-down player and picks them up again by itself once their health is back (revive or respawn).
        /// Still here for any script of yours that wants the camera to stay on someone at 0 health.
        /// </summary>
        public bool KeepCameraOn { get; set; }

        /// <summary>
        /// Raise this while something will bring a team that is at 0 health back, so "everyone is at 0 health" is not the end of the game.
        /// While it is above 0, AllCountedDead stays false and TeamWiped is not raised. CoopDowned does NOT use it any more: everybody
        /// knocked down now ends the game (LightsOutScreen). Leave it at 0 unless you add your own rescue.
        /// </summary>
        public static int TeamWipeBlockers;

        /// <summary>
        /// Raises TeamWiped if every player that counts is at 0 health right now (and nothing holds it back). CoopDowned calls this as a
        /// safety net, so a team that is all down always reaches the game-over screen even if the last death was somehow missed.
        /// Does nothing while somebody is still alive.
        /// </summary>
        public static void RaiseTeamWiped()
        {
            if (AllCountedDead && TeamWiped != null) TeamWiped();
        }

        public int PlayerNumber
        {
            get
            {
                // A CoopHealth that sits on a CHILD of a player is that player, not a new one: it must never ask the PlayerInputHub for a slot
                // of its own (the hub would hand it the next free one, "Player 3").
                if (_input == null) _input = PlayerInputSource.For(PlayerObjectOf(this));
                return _input != null ? _input.playerNumber : 1;
            }
        }

        /// <summary>
        /// True when this component sits on a child of an object that already has a CoopHealth, for example an empty child object that got a
        /// CoopHealth or CoopDowned added by mistake. That is part of the player above it, not a player of its own, so it is not counted.
        /// </summary>
        public static bool IsNestedInPlayer(Component component)
        {
            if (component == null) return false;
            Transform parent = component.transform.parent;
            return parent != null && parent.GetComponentInParent<CoopHealth>(true) != null;
        }

        // The outermost object with a CoopHealth above (or at) this one: the real player.
        private static GameObject PlayerObjectOf(CoopHealth health)
        {
            GameObject owner = health.gameObject;
            Transform parent = health.transform.parent;
            while (parent != null)
            {
                CoopHealth up = parent.GetComponentInParent<CoopHealth>(true);
                if (up == null) break;
                owner = up.gameObject;
                parent = up.transform.parent;
            }
            return owner;
        }

        // ------------------------------------------------------------ damage

        public override void ApplyDamage(float damage)
        {
            // Not real damage (zero, or negative = healing), or already dead: leave it to Health.
            if (damage <= 0f || !IsAlive)
            {
                base.ApplyDamage(damage);
                return;
            }

            // Single player mode: the player who doesn't count can't be hurt. Neither can an invincible or a jumping one.
            if (!CoopRules.Counts(PlayerNumber)) return;
            if (Invincible || Airborne || Grace) return;

            base.ApplyDamage(damage);

            if (_mirroring || !CoopRules.SharedDamage) return;

            _mirroring = true;
            try
            {
                // The FULL hit goes to everyone else, even if it killed this player with damage to spare.
                foreach (CoopHealth other in Players.ToArray())
                {
                    if (other != null && other != this) other.ApplyDamage(damage);
                }
            }
            finally
            {
                _mirroring = false;
            }
        }

        // ------------------------------------------------------------ registry + deaths

        private void OnEnable()
        {
            if (IsNestedInPlayer(this))
            {
                // Not a player: counting it would add a phantom "Player 3" that never gets hurt, so the doors and the game-over screen would wait for it forever.
                Debug.LogWarning("CoopHealth on '" + name + "' sits inside the player '" + PlayerObjectOf(this).name + "', so it is not counted as a player of its own " +
                                 "and does nothing. You can remove this component (and any CoopDowned next to it) from '" + name + "'.", this);
                return;
            }

            if (!Players.Contains(this)) Players.Add(this);
            OnHealthEmpty += HandleEmpty;
        }

        private void OnDisable()
        {
            Players.Remove(this);
            OnHealthEmpty -= HandleEmpty;
        }

        private void HandleEmpty()
        {
            if (AllCountedDead && TeamWiped != null) TeamWiped();
        }

        /// <summary>
        /// True once every player that counts is at 0 health (and there is at least one), knocked down or not. Always false while
        /// TeamWipeBlockers is above 0, so TeamWiped is not raised either.
        /// </summary>
        public static bool AllCountedDead
        {
            get
            {
                if (TeamWipeBlockers > 0) return false;

                bool any = false;
                for (int i = 0; i < Players.Count; i++)
                {
                    CoopHealth p = Players[i];
                    if (!CoopRules.Counts(p.PlayerNumber)) continue;

                    any = true;
                    if (p.IsAlive) return false;
                }
                return any;
            }
        }

        // ------------------------------------------------------------ lookups

        /// <summary>The health of Player number 1, 2, ... or null if that player isn't in the game right now.</summary>
        public static CoopHealth Get(int playerNumber)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].PlayerNumber == playerNumber) return Players[i];
            }
            return null;
        }

        /// <summary>The closest player that counts to this position, or null. Only living players unless aliveOnly is false.</summary>
        public static CoopHealth Nearest(Vector3 position, bool aliveOnly = true)
        {
            CoopHealth best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < Players.Count; i++)
            {
                CoopHealth p = Players[i];
                if (!CoopRules.Counts(p.PlayerNumber)) continue;
                if (aliveOnly && !p.IsAlive) continue;

                float d = (p.transform.position - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Players.Clear();
            TeamWiped = null;
            TeamWipeBlockers = 0;
            _mirroring = false;
        }
    }
}
