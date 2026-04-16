# VrisingMods Documentation Summary

## Fetched Files

| File                     | Description                    | Size      |
| ------------------------ | ------------------------------ | --------- |
| `query-descriptions.md`  | EntityQuery reference guide    | 540 bytes |
| `queryDescriptions.json` | Raw query data from GitHub     | ~371KB    |
| `systems-tree.md`        | ECS system hierarchy guide     | 496 bytes |
| `systemsTree.json`       | Raw systems tree from GitHub   | ~76KB     |
| `ecs-entities.md`        | Comprehensive ECS entity guide | 8.5KB     |

## Key Findings

### 1. ECS Entities Guide (ecs-entities.md)

A comprehensive ~450 line tutorial covering:

- **EntityManager**: Central API accessed via `Core.EntityManager` or `Core.Server.EntityManager`
- **Reading Components**: `entity.Read<T>()` or `entityManager.GetComponentData<T>()`
- **Writing Components**: Must read-modify-write pattern; use `entity.Write()` or `entityManager.SetComponentData()`
- **Inline Modification**: `entity.With(ref T component => { ... })` for quick edits
- **Component Presence**: Check with `entity.Has<T>()` before reading
- **Structural Changes**: Adding/removing components requires archetype migration
- **DynamicBuffers**: Variable-length data like inventories, buffs, waypoints
- **Entity Creation**: `entityManager.CreateEntity(ComponentType[])`
- **EntityQuery**: Build queries with `EntityQueryDesc` - cache the query, dispose results
- **Query Options**: Default, IncludeDisabled, IncludePrefab, IncludeSpawnTag, IncludeAll
- **Query Filtering**: Use `All`, `None` arrays for component filters
- **EntityCommandBuffer (ECB)**: Queue structural changes for end-of-frame execution
- **Entity Validity**: Check with `entity == Entity.Null` and `entityManager.Exists()`

### 2. Query Descriptions (queryDescriptions.json)

- 1,000+ EntityQuery objects registered by game systems
- Each entry has: system name, property name, All/None/Any components, options
- Example: `ProjectM.AbilityCastStarted_SetupAbilityTargetSystem_Shared._BuffsQuery`
- Data generated from game version 1.1

### 3. Systems Tree (systemsTree.json)

- 1,075 ECS systems across 72 groups
- Main SimulationSystemGroup contains 927 systems
- System types: Group (containers), CSB (Component System Base), ISystem (interface-based)
- Group descendants indicate number of child systems

## Technical Notes

- Wiki pages use JavaScript rendering - fetched data from GitHub `_data/` folder instead
- Source repo: https://github.com/decaprime/VRising-Modding
