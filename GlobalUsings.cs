// ── System ───────────────────────────────────────────────────────────
global using System;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Reflection;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading.Tasks;

// ── BepInEx / Harmony ───────────────────────────────────────────────
global using BepInEx;
global using BepInEx.Logging;
global using BepInEx.Unity.IL2CPP;
global using HarmonyLib;

// ── HookDOTS ────────────────────────────────────────────────────────
global using HookDOTS;
global using HookDOTS.API;
global using HookDOTS.API.Attributes;

// ── VampireCommandFramework ─────────────────────────────────────────
global using VampireCommandFramework;

// ── ProjectM (V Rising) ─────────────────────────────────────────────
global using ProjectM;
global using ProjectM.CastleBuilding;
global using ProjectM.Gameplay.WarEvents;
global using ProjectM.Network;
global using ProjectM.Scripting;
global using ProjectM.Shared;

// ── Stunlock ────────────────────────────────────────────────────────
global using Stunlock.Core;

// ── Unity ───────────────────────────────────────────────────────────
global using Unity.Collections;
global using Unity.Entities;
global using Unity.Mathematics;
global using Unity.Transforms;
global using UnityEngine;
global using UnityEngine.InputSystem;

// ── Il2Cpp ──────────────────────────────────────────────────────────
global using Il2CppInterop.Runtime;
