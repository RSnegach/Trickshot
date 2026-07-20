# Trickshot Multiplayer Framework

A transport-agnostic, **host-authoritative** netcode layer. It runs over three interchangeable
transports behind one interface: **direct-IP UDP** (play with friends now, no Steam, no paid
hosting), in-process **loopback** (single-machine testing), and a **Steam** P2P stub that drops
in once the Steamworks SDK is added. Game code doesn't change between them.

## Play with friends right now (no Steam, no server)

One friend **hosts**; everyone else **joins by the host's IP**. The host's PC is the
authoritative server, so no rented server and no Steam.

1. **Build it** (see *Packaging* below) and send everyone the zip, or all run it from the editor.
2. **Host:** Multiplayer → Host a Session → pick the mode → Create. The lobby shows
   **"Friends join at: `<your IPs>` (port 7777)"**. Read the right address to your friends:
   - **Same house / wifi (LAN):** share the `192.168.x.y` address.
   - **Remote friends:** everyone installs [Tailscale](https://tailscale.com) (free), joins the
     same tailnet, and the host shares the `100.x` address marked **(Tailscale)**. This is a
     virtual LAN over the internet — no router setup, no port forwarding.
3. **Join:** Multiplayer → Find a Session → type the host's IP (`192.168.1.5` or
   `100.90.1.2:7777`) in the **"join by IP"** box → Join.
4. **Firewall:** the first time you host, Windows Defender asks to allow Trickshot on the
   network — **allow it** (Private networks). Friends joining don't get this prompt.

Notes: IPv4 only (type the Tailscale `100.x`, not the `fd7a:` IPv6). Default UDP port is 7777.
Port forwarding on the host's router also works if you'd rather not use Tailscale, but Tailscale
is easier and safer.

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
| `LocalTransport.cs` | In-process loopback. Lets the full host path run/test in single process. |
| `DirectIpTransport.cs` | **Direct-IP UDP** transport (LAN / Tailscale). Background receive thread → `ConcurrentQueue`, drained on the main thread in `Poll`; a small reliability layer (`ReliableChannel`) on the Reliable channel, raw UDP on Unreliable; keepalive + 5s timeout for disconnect detection. |
| `ReliableChannel.cs` | Per-peer seq / cumulative-ack / resend-until-acked + in-order reorder buffer + dedup, for the Reliable channel only (lobby/roster/score/replay). |
| `NetEndpoint.cs` | Encodes an IPv4 `ip:port` into the `ulong` Join handle (so `Join(ulong)` is unchanged), parses typed addresses, and lists the host's local IPv4s for the lobby. |
| `SteamTransport.cs` | Steam P2P **stub** implementing `INetTransport`; real calls gated behind `TRICKSHOT_STEAM`. No-op without the SDK. |
| `Multiplayer.cs` | Global entry: `Multiplayer.Host(max)` / `Join(handle)` / `End()` / `Poll()`. Transport priority: **Steam** (if `TRICKSHOT_STEAM`) → **direct-IP** → loopback (`UseDirectIp=false`). |

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

## Packaging (send it to friends)

1. In Unity: **File → Build Settings → Windows / Mac / Linux → Windows, x86_64 → Build**.
2. Pick an empty output folder. Unity produces `Trickshot.exe` + a `Trickshot_Data/` folder
   (+ some support files) in it.
3. **Zip that whole folder** and send it. Friends unzip and run `Trickshot.exe` — no installer.
4. Builds are per-OS: a Windows build runs on Windows only. Build a Mac/Linux target separately
   if a friend needs one.

## Status

- Framework, messages, slot model, loopback transport: **done, runs now**.
- **Direct-IP UDP transport (LAN / Tailscale): done.** Reliable+unreliable channels, join-by-IP
  UI, host address display, disconnect detection. This is the "play with friends without Steam"
  path. Source-verified; validate on two machines when you can run a build.
- Steam transport: **stub with a documented fill-in path** (needs the SDK + an app ID, which
  can't be added or verified from this environment). Wiring it does not disturb the direct-IP
  path — it just takes priority when built.
- Driver integration (slot inputs + snapshot apply): wired for **striker** (`NetStrikerMatch`)
  and **scrimmage** (`ScrimmageGame`).
