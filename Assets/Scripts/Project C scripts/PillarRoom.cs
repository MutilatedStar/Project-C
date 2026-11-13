using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One room's pillar puzzle. It owns a fixed set of altars and a fixed set of pillars, and nothing outside that set:
/// when THIS room's altars are done, only THIS room's pillars are removed. Every room is its own PillarRoom object.
///
/// SET UP, for each room:
///   1. Make an empty object for the room and put this script on it.
///   2. Drag the room's altars into Altars and the room's pillars into Pillars. (Leave a list EMPTY to use every
///      RoomAltar / RoomPillar under this object instead, if you prefer to build the room as a hierarchy.)
///   3. Drag this room into the Rooms list of ITS floor, in FloorController's Floors list. That is what ties the room to
///      a floor: only the rooms of the floor the team is on react to their altars. (A room that no floor lists is always live.)
///
/// An altar or pillar can belong to ONE room only. If two rooms list the same one, the first room keeps it and the Console
/// warns about the second, so a mix-up shows up straight away instead of removing the wrong pillars.
///
/// When the room's altars are done (see Requirement) every pillar of this room is removed (collision included) and the
/// optional Solved Sound plays once. It stays solved. PillarRoom.ResetRoom() puts everything back, for a retry.
/// </summary>
[DisallowMultipleComponent]
public class PillarRoom : MonoBehaviour
{
    public enum Requirement { AllAltars, AnyAltar }

    [Header("This room's members")]
    [Tooltip("The altars of THIS room. Leave EMPTY to use every RoomAltar under this object.")]
    [SerializeField] private RoomAltar[] altars;
    [Tooltip("The pillars of THIS room: the ones removed when it is solved. Leave EMPTY to use every RoomPillar under this object.")]
    [SerializeField] private RoomPillar[] pillars;

    [Header("Solving")]
    [Tooltip("All Altars: every altar of this room must be fully triggered (a room with one altar works the same either way). Any Altar: one fully triggered altar is enough.")]
    [SerializeField] private Requirement requirement = Requirement.AllAltars;
    [Tooltip("Optional. Plays once, for everybody, when the pillars go.")]
    [SerializeField] private AudioClip solvedSound;
    [SerializeField, Range(0f, 1f)] private float solvedVolume = 1f;
    [Tooltip("Prints a Console line when the room is solved, saying how many pillars went. Handy for checking which room is which.")]
    [SerializeField] private bool logEvents = true;

    /// <summary>True once this room's pillars have been removed.</summary>
    public bool IsSolved { get; private set; }

    /// <summary>False while the team is on another floor (set by FloorController). The room's altars ignore everything then.</summary>
    public bool IsFloorActive { get; private set; } = true;

    /// <summary>How many altars / pillars this room really owns.</summary>
    public int AltarCount { get { return ownedAltars.Count; } }
    public int PillarCount { get { return ownedPillars.Count; } }

    /// <summary>Raised once when the room is solved.</summary>
    public event Action<PillarRoom> Solved;

    private readonly List<RoomAltar> ownedAltars = new List<RoomAltar>();
    private readonly List<RoomPillar> ownedPillars = new List<RoomPillar>();
    private AudioSource source;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        RoomAltar[] altarSource = (altars != null && altars.Length > 0) ? altars : GetComponentsInChildren<RoomAltar>(true);
        for (int i = 0; i < altarSource.Length; i++)
        {
            RoomAltar a = altarSource[i];
            if (a == null || ownedAltars.Contains(a)) continue;

            if (a.Room != null && a.Room != this)
            {
                Debug.LogError("PillarRoom '" + name + "': altar '" + a.name + "' already belongs to room '" + a.Room.name +
                               "', so it is NOT used here. An altar can belong to one room only.", this);
                continue;
            }

            a.Room = this;
            a.Triggered += HandleAltarTriggered;
            ownedAltars.Add(a);
        }

        RoomPillar[] pillarSource = (pillars != null && pillars.Length > 0) ? pillars : GetComponentsInChildren<RoomPillar>(true);
        for (int i = 0; i < pillarSource.Length; i++)
        {
            RoomPillar p = pillarSource[i];
            if (p == null || ownedPillars.Contains(p)) continue;

            if (p.Room != null && p.Room != this)
            {
                Debug.LogError("PillarRoom '" + name + "': pillar '" + p.name + "' already belongs to room '" + p.Room.name +
                               "', so it is NOT used here. A pillar can belong to one room only.", this);
                continue;
            }

            p.Room = this;
            ownedPillars.Add(p);
        }

        if (ownedAltars.Count == 0) Debug.LogError("PillarRoom '" + name + "' has no altars, so it can never be solved. Fill in its Altars list " +
                                                   "(or put the altars under this object and leave the list empty).", this);
        if (ownedPillars.Count == 0) Debug.LogWarning("PillarRoom '" + name + "' has no pillars, so there is nothing for it to remove. Fill in its Pillars list.", this);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < ownedAltars.Count; i++)
        {
            if (ownedAltars[i] != null) ownedAltars[i].Triggered -= HandleAltarTriggered;
        }
    }

    /// <summary>Called by FloorController: whether the team is on this room's floor.</summary>
    public void SetFloorActive(bool active)
    {
        IsFloorActive = active;
    }

    // ------------------------------------------------------------ solving

    private void HandleAltarTriggered(RoomAltar altar)
    {
        if (IsSolved || !IsFloorActive) return;
        if (Satisfied()) Solve();
    }

    private bool Satisfied()
    {
        int total = 0;
        int done = 0;
        for (int i = 0; i < ownedAltars.Count; i++)
        {
            if (ownedAltars[i] == null) continue;
            total++;
            if (ownedAltars[i].IsTriggered) done++;
        }

        if (total == 0) return false;
        return requirement == Requirement.AnyAltar ? done > 0 : done == total;
    }

    private void Solve()
    {
        IsSolved = true;

        // Only the pillars THIS room owns.
        for (int i = 0; i < ownedPillars.Count; i++)
        {
            if (ownedPillars[i] != null) ownedPillars[i].Remove();
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

        if (logEvents) Debug.Log("PillarRoom '" + name + "': solved, " + ownedPillars.Count + " pillar(s) removed.", this);
        if (Solved != null) Solved(this);
    }

    /// <summary>Puts the room back: its pillars return and its altars are cleared, so the puzzle can be done again.</summary>
    public void ResetRoom()
    {
        IsSolved = false;
        for (int i = 0; i < ownedPillars.Count; i++)
        {
            if (ownedPillars[i] != null) ownedPillars[i].Restore();
        }
        for (int i = 0; i < ownedAltars.Count; i++)
        {
            if (ownedAltars[i] != null) ownedAltars[i].ResetAltar();
        }
    }
}
