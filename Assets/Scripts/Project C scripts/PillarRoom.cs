using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One room's pillar puzzle. Put this on an empty object that holds the room's altars and pillars:
///
///     Room 1  (PillarRoom)
///       Altar A   (RoomAltar)
///       Altar B   (RoomAltar)
///       Pillar 1  (RoomPillar)
///       Pillar 2  (RoomPillar)
///     Room 2  (PillarRoom)
///       Altar    (RoomAltar)
///       Pillar   (RoomPillar)
///
/// Every altar and pillar belongs to the nearest PillarRoom above it, so each room is completely separate: fully
/// pressing room 1's altars removes room 1's pillars and nothing else. Make a room into a prefab to reuse it.
///
/// When the room's altars are done (see Requirement) every pillar in THIS room is removed (collision included) and the
/// optional Solved Sound plays once. It stays solved. PillarRoom.ResetRoom() puts everything back, for a retry.
/// </summary>
[DisallowMultipleComponent]
public class PillarRoom : MonoBehaviour
{
    public enum Requirement { AllAltars, AnyAltar }

    [Tooltip("All Altars: every altar in this room must be fully pressed (a room with one altar works the same either way). Any Altar: one fully pressed altar is enough.")]
    [SerializeField] private Requirement requirement = Requirement.AllAltars;
    [Tooltip("Optional. Plays once, for everybody, when the pillars go.")]
    [SerializeField] private AudioClip solvedSound;
    [SerializeField, Range(0f, 1f)] private float solvedVolume = 1f;
    [Tooltip("Prints a Console line when the room is solved, saying how many pillars went. Handy for checking which room is which.")]
    [SerializeField] private bool logEvents = true;

    /// <summary>True once this room's pillars have been removed.</summary>
    public bool IsSolved { get; private set; }

    /// <summary>Raised once when the room is solved.</summary>
    public event Action<PillarRoom> Solved;

    private readonly List<RoomAltar> altars = new List<RoomAltar>();
    private readonly List<RoomPillar> pillars = new List<RoomPillar>();
    private AudioSource source;

    // ------------------------------------------------------------ the room's members sign in here

    public void Register(RoomAltar altar)
    {
        if (altar != null && !altars.Contains(altar)) altars.Add(altar);
    }

    public void Unregister(RoomAltar altar)
    {
        altars.Remove(altar);
    }

    public void Register(RoomPillar pillar)
    {
        if (pillar == null) return;
        if (!pillars.Contains(pillar)) pillars.Add(pillar);
        if (IsSolved) pillar.Remove(true);   // switched on again (or made) after the room was solved: it must not come back
    }

    public void Unregister(RoomPillar pillar)
    {
        pillars.Remove(pillar);
    }

    // ------------------------------------------------------------ solving

    /// <summary>Called by an altar the moment it is fully pressed.</summary>
    public void AltarTriggered(RoomAltar altar)
    {
        if (IsSolved) return;
        if (Satisfied()) Solve();
    }

    private bool Satisfied()
    {
        int total = 0;
        int done = 0;
        for (int i = 0; i < altars.Count; i++)
        {
            if (altars[i] == null) continue;
            total++;
            if (altars[i].IsTriggered) done++;
        }

        if (total == 0) return false;
        return requirement == Requirement.AnyAltar ? done > 0 : done == total;
    }

    private void Solve()
    {
        IsSolved = true;

        for (int i = 0; i < pillars.Count; i++)
        {
            if (pillars[i] != null) pillars[i].Remove();
        }

        if (solvedSound != null)
        {
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;   // 2D: both split-screen players hear it
            }
            source.PlayOneShot(solvedSound, solvedVolume);
        }

        if (logEvents) Debug.Log("PillarRoom '" + name + "': solved, " + pillars.Count + " pillar(s) removed.", this);
        if (Solved != null) Solved(this);
    }

    /// <summary>Puts the room back: pillars return and every altar is cleared, so the puzzle can be done again.</summary>
    public void ResetRoom()
    {
        IsSolved = false;
        for (int i = 0; i < pillars.Count; i++)
        {
            if (pillars[i] != null) pillars[i].Restore();
        }
        for (int i = 0; i < altars.Count; i++)
        {
            if (altars[i] != null) altars[i].ResetAltar();
        }
    }
}
