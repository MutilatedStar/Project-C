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
    ///
    /// Helpers for other scripts:
    ///   CoopHealth.Nearest(position)   the closest living player that counts (foes: "who do I chase / hit?")
    ///   CoopHealth.Get(number)         Player 1's or Player 2's health, or null if they aren't in the game right now
    ///   CoopHealth.AllCountedDead      true once every player that counts is dead
    ///   CoopHealth.TeamWiped           event raised when the last player that counts dies (use it for the Failure Ending)
    ///
    /// Needs a PlayerInputSource on the same object (it reads the Player Number from it) and CoopRules.cs.
    /// </summary>
    [RequireComponent(typeof(PlayerInputSource))]
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

        public int PlayerNumber
        {
            get
            {
                if (_input == null) _input = GetComponent<PlayerInputSource>();
                return _input != null ? _input.playerNumber : 1;
            }
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

            // Single player mode: the player who doesn't count can't be hurt. Neither can an invincible one.
            if (!CoopRules.Counts(PlayerNumber)) return;
            if (Invincible) return;

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

        /// <summary>True once every player that counts is dead (and there is at least one).</summary>
        public static bool AllCountedDead
        {
            get
            {
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
            _mirroring = false;
        }
    }
}
