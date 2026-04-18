# BattleLuck

BattleLuck is a V Rising BepInEx plugin focused on competitive arena-style modes, player state snapshots, zone-driven match flow, optional AI assistance, Discord/webhook integrations, and server-side event control. all thrue configs u can change actions while u r in the game , some actions are working with ai like .ai servant send regionname some not 


## 📚 Documentation

**For comprehensive documentation, see [docs/README.md](docs/README.md)**

Covers: Installation, Configuration, Commands, APIs, Troubleshooting, and more.

**Additional Resources**:
- [System Architecture](docs/ARCHITECTURE.md) - Detailed system design
- [Audit Report](docs/AUDIT_REPORT.md) - Documentation audit summary
- [V Rising Modding](docs/vrisingmods/) - V Rising ECS reference

## 🚀 CI/CD

BattleLuck uses GitHub Actions for automated building and releasing.

### Build & Release

Automatically builds the mod on every push to `main`/`master` and creates GitHub releases with semantic versioning.

**Triggers**: Push to main/master, manual dispatch

### Thunderstore Release

Publishes releases to Thunderstore when a GitHub release is published.

**Setup Required**:
1. Add `THUNDERSTORE_KEY` secret to your repository settings
2. Create releases through GitHub UI or manually via workflow dispatch

**Getting Thunderstore API Key**:
1. Go to [Thunderstore](https://thunderstore.io/)
2. Sign in and go to Settings → API Keys
3. Create a new API key
4. Add it as `THUNDERSTORE_KEY` in your GitHub repository secrets

### Dependency Updates

Automatically checks for NuGet package updates weekly and creates pull requests.

**Triggers**: Weekly (Sundays), manual dispatch

### Semantic Versioning

Version numbers are automatically incremented based on commit messages:

- `+semver:major` or `+semver:breaking` → Major version bump (x.0.0)
- `+semver:minor` or `+semver:feature` → Minor version bump (0.x.0)
- `+semver:patch` or `+semver:fix` → Patch version bump (0.0.x)

**Example**: `git commit -m "add new game mode +semver:minor"`

## Modes

| Mode ID | Display Name | Purpose |
| --- | --- | --- |
| `bloodbath` | Bloodbath | Free-for-all PvP arena |
| `colosseum` | Colosseum | Duel/ELO-focused arena |
| `gauntlet` | Gauntlet | PvE wave survival |
| `siege` | Siege | Objective/team event mode |
| `trials` | Trials | Timed PvE challenge |
| `aievent` | AI Event Test | Deterministic AI-flow test mode |

## Commands

BattleLuck commands are registered through VampireCommandFramework.
🔒 = admin only.

### Admin Commands

| Command | Description |
| --- | --- |
| `.ai.event` 🔒 | Replay AI event flow (start, score, elimination, end) without entering a zone |
| `.ai.reload` 🔒 | Reload AI configuration and restart service |
| `.ai.status` 🔒 | Show detailed AI assistant status |
| `.ai.test` 🔒 | Test AI assistant with a sample query |
| `.autotrash` 🔒 | Toggle auto-trash for dropped items in mode zones |
| `.autotrash.status` 🔒 | Show auto-trash stats |
| `.debugabilities` 🔒 | Print all discovered AbilityGroup prefabs from the server |
| `.debugslots` 🔒 | Print combat key slot resolution status |
| `.event.clearburning` 🔒 | Remove burning penalty from all players |
| `.event.end` 🔒 | End all sessions for a mode and clear burning |
| `.event.endall` 🔒 | End ALL active sessions and clear all burning |
| `.event.forceenter` 🔒 | Force a player into a mode |
| `.event.forceexit` 🔒 | Force a player out of their current event |
| `.event.start` 🔒 | Start an event mode (teleports you in) |
| `.event.status` 🔒 | Show all active events and player counts |
| `.freebuild` 🔒 | Toggle building restrictions off/on |
| `.kick` 🔒 | Kick player from session |
| `.pause` 🔒 | Pause all active sessions |
| `.reload` 🔒 | Reload configs from disk |
| `.resume` 🔒 | Resume paused sessions |
| `..scanbufs [filter]` 🔒 | Scan live prefabs for buffs |
| `..scanitems <filter>` 🔒 | Scan live prefabs for items |
| `..scanprefabs <filter> [maxResults]` 🔒 | Scan live prefabs matching a filter |
| `.setwinner` 🔒 | Set winner and end session |
| `..spawntest <prefabGUID>` 🔒 | Test-spawn a unit at your position |
| `..spawnwave <tier> <count>` 🔒 | Test-spawn a wave of enemies |
| `..stashnpc <destNetId> [sourceNetId|allteam] [maxDistance] [sameTeam] [minStack] [maxStacks] [itemFilter]` 🔒 | Transfer items from NPC source(s) to destination entity |
| `.zoneinfo` 🔒 | Show zone stats and player counts |

### DataExport Commands

| Command | Description |
| --- | --- |
| `.discoverkits` 🔒 | Auto-discover the best weapon/armor/tile/ability prefabs from live game data and export kit.json template |
| `.exportmods` 🔒 | Export all loaded mod data (plugins, prefabs, APIs) to JSON |
| `.exportplugins` 🔒 | Export loaded BepInEx plugin info to JSON |
| `.exportprefabs` 🔒 | Export server live prefab collection to JSON |
| `.findtiles` 🔒 | Search live prefabs for tile/wall/floor building pieces |
| `.searchprefab` 🔒 | Search ALL live prefabs by name pattern (e.g. 'Item_Weapon_Sword', 'AB_Chaos', 'Item_Armor') |
| `.validateprefabs` 🔒 | Check if BattleLuck prefab GUIDs exist in the game's entity map |

### Mode Commands

| Command | Description |
| --- | --- |
| `.force` 🔒 | Teleport to mode's zone and auto-start session |
| `.modeend` 🔒 | Force-end all sessions for a mode |
| `.modeinfo` 🔒 | Show mode configuration details |
| `.modelist` 🔒 | List all registered game modes |
| `.modestart` 🔒 | Start a game mode manually |

### Mutator Commands

| Command | Description |
| --- | --- |
| `.mutatorclear` 🔒 | Clear all mutators |
| `.mutatordisable` 🔒 | Disable a mutator |
| `.mutatorenable` 🔒 | Enable a mutator |
| `.mutatorlist` | List available mutators |

### Player Commands

| Command | Description |
| --- | --- |
| `.actions` | Show valid actions for the current mode |
| `..ai <your question>` | Chat with the AI assistant |
| `.aistatus` | Show AI assistant status and settings |
| `.elo` | Show Elo ratings for Colosseum mode |
| `.exit` | Force exit current zone session |
| `.help` | Show available BattleLuck commands |
| `.kit` 🔒 | Apply full end-game kit to yourself |
| `.score` | Show current scoreboard |
| `.toggleenter` | Enter a zone session. Use: .toggleenter [modeName] |
| `.toggleleave` | Properly leave the current zone session |

### Team Commands

| Command | Description |
| --- | --- |
| `.teamaccept` | Accept team invite |
| `.teamcreate` | Create a team |
| `.teaminvite` | Invite player to your team |
| `.teamleave` | Leave your team |
| `.teamlist` | List all teams |


## Configuration Layout

BattleLuck reads config from `config/BattleLuck/`.

### Per-mode folders

Each mode folder contains:

- `session.json`
- `zones.json`
- `flow_enter.json`
- `flow_exit.json`
- `kit.json`

### Global files

- `ai_config.json`: Google AI + optional sidecar settings
- `ai_logger.json`: AI/event logging providers and routing
- `discord_bridge.json`: Discord interaction bridge server config
- `webhook.json`: BattleLuck webhook listener config
- `special_item.json`: Special item transformation behavior
- `kit_grant_rules.json`: Item-to-kit reward rules for craft completion hooks

## AI, Discord, and Webhooks

- The Discord bridge is optional and is disabled by default unless `discord_bridge.json` has `enabled: true`.
- The AI assistant can run in Gemini-only mode or with sidecar enrichment.
- The sidecar client expects an API root that exposes `GET /health` and `POST /api/query/enrich`.
- A Superuser chat page URL is not the same thing as a sidecar API base URL.
- Stripe-to-Discord relay support lives in the AI sidecar functions and uses `POST /stripe/discord`.

## Snapshot System

BattleLuck snapshots player state before mode entry and restores it on clean exit, rollback, or penalty-death recovery. The runtime currently captures and restores:

- Position
- Health
- Blood state
- Equipment level values
- Inventory items
- Derived equipped gear slots
- Weapon entries
- Ability slot replacements
- Passive buff-like abilities
- Active buffs

Snapshots are persisted under `BepInEx/data/BattleLuck/snapshots/`.

## Build

Release build output:

- `bin/Release/net6.0/BattleLuck.dll`

Typical build command:

```powershell
dotnet build BattleLuck.sln -c Release
```

### Optional: Build directly to server install

Set `VRISING_SERVER_ROOT` to your dedicated server path so post-build copy targets resolve cleanly.

Example PowerShell session:

```powershell
$env:VRISING_SERVER_ROOT = "C:\\Path\\To\\VRisingServer"
dotnet build BattleLuck.sln -c Release
```

## Thunderstore Packaging

This repository now includes a Thunderstore `manifest.json` template at the root.

Before publishing, ensure your package zip contains:

- `BattleLuck.dll`
- `manifest.json`
- `README.md`
- `icon.png` (256x256)
- `CHANGELOG.md` (recommended)

Follow upload guidance from the V Rising Mod Wiki:

- https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html

## Dependencies and Credits

BattleLuck depends on:

- BepInEx (mod loader)
- VampireCommandFramework (command registration and parsing)

Please keep dependency attributions and manifest dependency entries aligned with your released build.

## License

BattleLuck is licensed under MIT. See `LICENSE`.

Third-party dependency and runtime component notices are documented in `THIRD_PARTY_NOTICES.md`, including `VAutomationCore`, `VampireCommandFramework`, `BepInEx`, `Il2CppInterop`, and other referenced components.

## Notes

- Building restriction bypass is handled by debug-setting toggles in `BuildingRestrictionController`.
- `PlaceTileModelSystemPatch` only re-blocks castle heart placement while free-build is active.
- If prefab validation fails, prefer live prefab scanning/export over stale hardcoded GUIDs.
#
## Badges  
[![MIT License](https://img.shields.io/badge/License-MIT-green.svg)](https://choosealicense.com/licenses/mit/)  
[![GPLv3 License](https://img.shields.io/badge/License-GPL%20v3-yellow.svg)](https://choosealicense.com/licenses/gpl-3.0/)  
[![AGPL License](https://img.shields.io/badge/license-AGPL-blue.svg)](https://choosealicense.com/licenses/gpl-3.0/)  

## Features  
- Accessibility in VS Code  
- Download directly to project root  
- Live Previews    

## License  
[MIT](https://choosealicense.com/licenses/mit/)  

## Run Locally  
Clone the project  

~~~bash  
  git clone https://link-to-project
~~~

Go to the project directory  

~~~bash  
  cd my-project
~~~

Install dependencies  

~~~bash  
npm install
~~~

Start the server  

~~~bash  
npm run start
~~~  

## Screenshots  
![App Screenshot](https://lanecdr.org/wp-content/uploads/2019/08/placeholder.png)  
