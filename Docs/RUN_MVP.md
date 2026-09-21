# Run Pixel Arena MVP

## Generated entry points

- Start scene: `Assets/PixelArena/Scenes/Menu.unity`.
- Additive room geometry: `Assets/PixelArena/Scenes/Arena.unity`.
- Registered assets: `Assets/PixelArena/Prefabs/Player.prefab`, `MatchManager.prefab`, `Projectile.prefab`.
- Editor builder: `Assets/PixelArena/Editor/MvpBuild.cs`.
- Transport: KCP, UDP port 7777. Room capacity: 5. Server connection limit: 100.
- Menu is first in build settings, Arena second. Existing SampleScene remains present and unchanged.

## Editor / LAN play

1. Open Menu and press Play.
2. Choose **Host**, then **Play / Find Match**.
3. Launch the Windows executable on another process/computer. Enter the host address (127.0.0.1 for the same computer), choose **Connect**, then **Play / Find Match**.
4. Escape opens the pause menu. **Leave Match** preserves the connection; Play joins again. **Stop Host** or **Disconnect** closes the connection.

Controls: WASD movement, mouse aim, Space jump, W/S in the ladder volume, E interact, LMB fire, R reload, 1–5 select weapons. No sprint. Bunker countdown is 30 seconds; reuse cooldown is 120 seconds after detonation; door hold is 2 seconds. These are editable prefab defaults.

## Rebuild generated assets

Save open scenes and exit Play Mode, then run **Pixel Arena > Rebuild MVP Scenes and Prefabs**. Equivalent MCP execute_code body:

```csharp
PixelArena.MvpBuild.Rebuild();
return "Rebuilt";
```

This deliberately regenerates the three MVP prefabs and two MVP scenes at the paths above. Preserve custom edits to generated scenes/prefabs elsewhere before rebuilding. Existing material assets are reused. SampleScene, original SimpleMatch prefab and unrelated assets are not regenerated.

## Build Windows

Run **Pixel Arena > Build Windows MVP**, or invoke `PixelArena.MvpBuild.BuildWindows()` through MCP. Output: `Builds/Windows/PixelArena.exe` (Development, Windows x64). The executable supports ordinary interactive client/host and headless server from the same build.

For unattended editor builds with the project closed in the interactive Editor, use the installed Unity executable with `-batchmode -quit -projectPath "D:\Unity\_Projects\Pixel Arena" -executeMethod PixelArena.MvpBuild.BuildWindows -logFile "D:\Unity\_Projects\Pixel Arena\Logs\build.log"`. Do not start a second Unity Editor against the same open project.

## Headless server

From the project directory in PowerShell:

```powershell
& '.\Builds\Windows\PixelArena.exe' -batchmode -nographics -logFile "$PWD\Logs\server.log"
```

Mirror's built-in AutoStartServer is configured on the menu NetManager. No custom `--server` flag is required. A normal launch shows the menu and does not auto-connect.

For an unattended Development-build client, use `PixelArena.exe --client 127.0.0.1 -logFile Logs/client.log` without `-batchmode`; it connects and joins automatically. Interactive players can continue to use Connect in the menu.

Validation-only arguments are `--room-capacity 1` on the server and `--validation-server`. They prove that two clients are assigned separate rooms and write room IDs/physics status to the server log; ordinary launches retain capacity 5.

For LAN, allow inbound UDP 7777 on the host. Internet access needs a reachable server/public endpoint and appropriate UDP routing/firewall configuration; this project does not provide relay, NAT traversal, authentication or a cloud hosting service.

## Integration evidence and limits

- Generated assets and references inspected inside Unity through MCP: exactly one RoomInterestManagement, KcpTransport, player gameplay components, match/projectile registration and combat projectile reference.
- Rebuild was repeated with the generated Menu scene already open; it restored Menu, retained build order/references and produced no console errors or warnings.
- Editor Host -> Play reached Playing, spawned a grounded player at (-19, 0, -19), HP 100, local camera, UI and local bunker controller.
- Leave returned None, removed the local player and retained the connection. Play again reached Playing with a new player.
- The Windows Development build completed with 0 errors. A separate `-batchmode -nographics` process listened on UDP 7777; an Editor client connected, entered Playing and received its player, 100 HP and bunker state without client console errors.
- Screenshot: `Captures/06-host.png` (pause overlay is visible because Editor did not own focused gameplay input).
- Task 07 stopped local Command producers before sending Leave; leave/rejoin was repeated with no late movement/combat warnings.
- Dedicated server plus two executable clients, capacity-one room isolation, all weapon slots, movement/jump/ladder, death/ragdoll/respawn and bunker outcomes were validated. See `Docs/MVP_VALIDATION.md` for evidence and remaining limits.
