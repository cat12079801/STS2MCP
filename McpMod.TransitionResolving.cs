namespace STS2_MCP;

public static partial class McpMod
{
    internal const string MapClosingReason = "map_closing";
    internal const string MapTravelingReason = "map_traveling";
    internal const string RunLoadingReason = "run_loading";
    internal const string RoomChangingReason = "room_changing";

    /// <summary>
    /// Why a <c>map</c> state is a screen in motion rather than one waiting for the player, or null
    /// when the map is idle. <c>map</c> is an input screen, so every other reason is cleared on it;
    /// this one is added afterwards and must therefore clear by itself.
    ///
    /// The game's own order (decompile <c>sts2-a671e9ef</c>):
    /// <list type="bullet">
    /// <item><b>Traveling.</b> <c>NMapScreen.OnMapPointSelectedLocally</c> enqueues a
    /// <c>VoteForMapCoordAction</c>; executing it (<c>MapSelectionSynchronizer.PlayerVotedForMapCoord</c>
    /// → <c>MoveToMapCoord</c>) enqueues a <c>MoveToMapCoordAction</c>, whose <c>ExecuteAction</c> awaits
    /// <c>NMapScreen.TravelToMapCoord</c>. That sets <c>IsTraveling = true</c> and <c>IsTravelEnabled = false</c>
    /// together (no await between them), plays the path animation and the fade, and awaits
    /// <c>RunManager.EnterMapCoord</c> → <c>EnterMapPointInternal</c>, which closes the map without an animation
    /// (<c>ClearScreens</c> → <c>NMapScreen.Close(animateOut: false)</c>) and, once <c>EnterRoom</c> returns, sets
    /// <c>IsTraveling = false</c>. The two actions cover the frames before <c>IsTraveling</c> is set.
    /// A map that has <c>IsTravelEnabled</c> back (<c>RunManager.Proceed</c> → <c>SetTravelEnabled(true)</c>) takes
    /// clicks again (<c>NMapPoint.IsTravelable</c> reads only <c>IsTravelEnabled</c> and the point's state), so
    /// <c>IsTraveling</c> counts only while travel is disabled: a flag left behind by a travel that never
    /// reached its room cannot keep an open map "resolving" after the next room ends.</item>
    /// <item><b>Closing.</b> <c>NMapScreen.Close(animateOut: true)</c> (the map's back arrow, the top bar's map
    /// button) sets <c>IsOpen = false</c> at once but leaves the screen visible until <c>AnimClose</c>'s tween
    /// finishes (0.1 s delay + 0.25 s) and hides it. <c>Open</c> kills that tween and sets <c>IsOpen</c> again,
    /// so a visible map that is not open is always one that is about to disappear.</item>
    /// </list>
    /// Kept free of game objects - it takes only the values read off them - so it can be tested
    /// without a running game.
    /// </summary>
    /// <param name="mapOpen"><c>NMapScreen.IsOpen</c>.</param>
    /// <param name="mapVisible">The map screen node is visible in the tree.</param>
    /// <param name="mapTraveling"><c>NMapScreen.IsTraveling</c>.</param>
    /// <param name="travelEnabled"><c>NMapScreen.IsTravelEnabled</c>.</param>
    /// <param name="travelActionPending">
    /// A <c>VoteForMapCoordAction</c> / <c>MoveToMapCoordAction</c> is running, or (outside combat, where a
    /// <c>NonCombat</c> action can run) waiting in the action queue.
    /// </param>
    internal static string? MapTransitionReason(
        bool mapOpen, bool mapVisible, bool mapTraveling, bool travelEnabled, bool travelActionPending)
    {
        if ((mapTraveling && !travelEnabled) || travelActionPending)
            return MapTravelingReason;
        if (!mapOpen && mapVisible)
            return MapClosingReason;
        return null;
    }

    /// <summary>
    /// Why an in-run <c>unknown</c> state (no screen the state builder recognises) is the game on its way
    /// somewhere, or null when there is no such sign.
    /// <list type="bullet">
    /// <item><c>RunManager.IsChangingRooms</c> is true from the start of <c>EnterMapPointInternal</c> (and
    /// <c>EnterRoomWithoutExitingCurrentRoom</c> / <c>EnterRoomDebug</c>) to its <c>finally</c>: the old room is
    /// exited (<c>ExitCurrentRooms</c>) and the new one entered. A continued run enters its saved room the same
    /// way (<c>NGame.LoadRun</c> → <c>RunManager.LoadIntoLatestMapCoord</c>).</item>
    /// <item>A run whose state exists but has no room yet is loading: <c>NMainMenu.OnContinueButtonPressedAsync</c>
    /// sets the run state first (<c>RunManager.SetUpSavedSingleplayer</c>, so <c>IsInProgress</c> is true), then fades
    /// out, preloads, creates <c>NRun</c> and generates the map (<c>NGame.LoadRun</c>) before any room is pushed.
    /// A new run does the same in <c>NGame.StartRun</c>. A failed load calls <c>RunManager.CleanUp</c>, which drops the
    /// run state (the state becomes <c>menu</c>), so the reason does not outlive the load.</item>
    /// </list>
    /// </summary>
    /// <param name="hasRoom"><c>RunState.CurrentRoom</c> is not null.</param>
    /// <param name="changingRooms"><c>RunManager.IsChangingRooms</c>.</param>
    internal static string? RunTransitionReason(bool hasRoom, bool changingRooms)
    {
        if (changingRooms)
            return RoomChangingReason;
        if (!hasRoom)
            return RunLoadingReason;
        return null;
    }

    /// <summary>
    /// Why <c>choose_map_node</c> must not press a node now, or null when the map takes a choice. It mirrors
    /// what the player's click needs (<c>NMapPoint.OnRelease</c> → <c>IsTravelable</c>: <c>IsTravelEnabled</c>, or
    /// <c>IsDebugTravelEnabled</c> while not traveling) on a map that is actually open, and refuses the windows
    /// <see cref="MapTransitionReason"/> reports as resolving.
    /// </summary>
    internal static string? ChooseMapNodeRefusal(
        bool mapOpen, bool mapVisible, bool mapTraveling, bool travelEnabled, bool debugTravelEnabled, bool travelActionPending)
    {
        if (!mapOpen)
            return mapVisible
                ? "Map screen is closing. Nothing was pressed; re-read state"
                : "Map screen is not open";
        if (MapTransitionReason(mapOpen, mapVisible, mapTraveling, travelEnabled, travelActionPending) == MapTravelingReason)
            return "The map is already traveling to a node. Nothing was pressed; re-read state";
        if (!travelEnabled && !debugTravelEnabled)
            return "Travel is not enabled on this map (it was opened before the current room was finished). Nothing was pressed";
        return null;
    }
}
