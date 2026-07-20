# Trickshot Multiplayer Framework

A transport-agnostic, **host-authoritative** netcode layer that is in the repo now and runs
today over an in-process loopback transport. Real Steam networking drops in behind the same
interface once the Steamworks SDK is added, with no changes to game code.

## Model

- **Host-authoritative.** One player (the host) runs the physics/ragdoll sim. Clients send
  their input each tick and receive world snapshots to interpolate toward. This is the only
  sane model for a ragdoll-physics game (deterministic lockstep across machines is not
  realistic with PhysX ragdolls).
- **Slots.** Slot 0 is the **keeper**; slots 1..N are **shooters** (`NetSession.MaxSlots = 8`,
  so 1 keeper + up to 7 shooters). Joining humans fill the lowest free shooter slot; any slot
  no human holds is filled by AI. This covers the target: *one keeper and however many people
  want to shoot around* — and works for **scrimmage** and **striker** mode alike.

## Files (`Assets/Scripts/Net/`)

| File | Role |
|------|------|
| `INetTransport.cs` | The seam. Host/join/send/poll + peer + connect events. `PeerId` is an opaque `ulong` (a `CSteamID` once wired). |
| `NetMessages.cs` | Wire types (`Hello`, `AssignSlot`, `PlayerInput`, `Snapshot`, `MatchEvent`) + a compact binary `NetWriter`/`NetReader` + `NetCodec` encode/decode. |
| `NetSession.cs` | Host-authoritative session: owns the slot table, routes messages, exposes per-slot input (host) and the latest snapshot (client). Does **not** run the sim. |
| `LocalTransport.cs` | In-process loopback. Lets the full host path run/test in single process today. |
| `SteamTransport.cs` | Steam P2P **stub** implementing `INetTransport`; real calls gated behind `TRICKSHOT_STEAM`. No-op without the SDK. |
| `Multiplayer.cs` | Global entry: `Multiplayer.Host(max)` / `Join(id)` / `End()` / `Poll()`. Picks Steam transport if built, else loopback. |

## How the mode drivers use it (next integration step)

`Multiplayer.Session` is `null` in single-player, so nothing changes there. To network a mode:

```csharp
// each frame
Multiplayer.Poll();

if (!Multiplayer.IsActive || Multiplayer.IsHost)
{
    // HOST / single-player: run the authoritative sim.
    // For each slot: if human, drive from NetSession.InputForSlot(slot); else AI.
    // The local player's own input:
    session.SetLocalInput(SampleInput());
    // ...advance physics...
    session.BroadcastSnapshot(BuildSnapshot());
}
else
{
    // CLIENT: send my input, apply the host's snapshot to the visible bodies.
    session.SetLocalInput(SampleInput());
    if (session.HasSnapshot) ApplySnapshot(session.LatestSnapshot);   // lerp bodies + ball
}
```

`ScrimmageGame` already has the pieces this maps onto (per-player `Footballer`, one human +
AI fill, a keeper). Networking it = feeding slot inputs instead of only the local one, and
publishing/consuming snapshots. Striker mode is the same with one shooter slot + the keeper.

## Wiring real Steam (when publishing)

1. **Add a Steamworks wrapper** to the project: [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)
   (MIT, low-level, matches the API names in the stub's TODOs) or
   [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) (friendlier).
   Import its DLLs/`steam_api` into `Assets/Plugins/`.
2. **App ID.** Put your Steam `appId` in `steam_appid.txt` at the project root for testing;
   call `SteamAPI.Init()` (Steamworks.NET) once at startup and `Shutdown()` on quit.
3. **Define the symbol.** Add `TRICKSHOT_STEAM` to *Project Settings → Player → Scripting
   Define Symbols*. That activates the guarded code in `SteamTransport.cs`.
4. **Fill the `TODO(steam)` blocks** in `SteamTransport.cs` — each is annotated with the
   exact Steamworks call:
   - `StartHost` → `SteamMatchmaking.CreateLobby`
   - `Join` → `SteamMatchmaking.JoinLobby` + open P2P to the owner
   - `Send`/`SendToAll` → `SteamNetworkingMessages.SendMessageToUser`
   - `Poll` → `SteamAPI.RunCallbacks()` + `ReceiveMessagesOnChannel`
   - peer ids: `CSteamID.m_SteamID` ↔ `PeerId.Value`
5. **Lobby UI.** Add Host / Join-friend / lobby-browser buttons to the menu that call
   `Multiplayer.Host(maxPlayers)` / `Multiplayer.Join(lobbyId)`.

Nothing above touches gameplay code — only `SteamTransport` and a bit of menu glue.

## Status

- Framework, messages, slot model, loopback transport: **done, runs now**.
- Steam transport: **stub with a documented fill-in path** (needs the SDK + an app ID, which
  can't be added or verified from this environment).
- Driver integration (feeding slot inputs + snapshot apply into ScrimmageGame / striker):
  **not yet wired** — the hooks exist (`SetLocalInput`, `InputForSlot`, `BroadcastSnapshot`,
  `LatestSnapshot`); say the word and it gets plumbed into the two modes.
