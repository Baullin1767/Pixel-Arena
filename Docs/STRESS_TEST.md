# Pixel Arena stress test

The stress test uses real headless Mirror clients. Each bot connects through SimpleWebTransport, joins the normal matchmaking queue, loads the arena, sends ordinary movement/combat/interaction commands, leaves and rejoins rooms, and periodically disconnects and reconnects.

## Build and run

Build the current Windows development player with **Pixel Arena > Build Windows MVP**, then run from the project root:

```powershell
.\Tools\Run-StressTest.ps1 -Bots 100 -Rooms 20 -DurationSeconds 600
```

For 100 bots and 20 rooms, the server sets room capacity to 5. Bot startup is staggered (10 processes/second by default) to avoid turning process creation into the only measured spike.

The server log and individual bot logs are written below `Builds/Windows/StressLogs/<run-id>/`. The server log contains only lifecycle events (`CONNECT`, `JOIN`, `LEAVE`, `DISCONNECT`) and a periodic status line. Gameplay actions are intentionally not logged. A compact `FPS` line reports the latest FPS from every bot.

Important status fields are connected bots, active players and rooms, total joins/leaves/reconnects, server FPS, normalized process CPU, working-set memory, and bot FPS min/average/max.

## Direct command-line modes

- `--stress-server`: start a headless server and launch local bot processes.
- `--stress-runner`: launch bot processes against an already running remote server. Add `--address <host>` and `--port <port>`.
- `--stress-bot`: internal single-bot mode; normally launched by the runner.

Common options: `--bots`, `--rooms`, `--duration` (0 means until stopped), `--spawn-rate`, `--bot-fps`, `--telemetry-interval`, `--min-session`, `--max-session`, `--address`, and `--port`.

Example remote load generator:

```powershell
PixelArena.exe -batchmode -nographics -logFile runner.log --stress-runner --bots 100 --rooms 20 --address 10.0.0.5 --port 7777 --duration 600
```

One Unity process is used per bot because Mirror's standard `NetworkClient` is static. This costs more load-generator CPU/RAM, but it exercises the same client serialization, scene loading, Commands and transport path as a real player. Run the bot launcher on a separate machine when measuring the server's CPU ceiling.
