using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.InferenceEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

// Pushman > 1. Setup Test Scene  — builds the SampleScene from scratch
// Pushman > 2. Save Prefabs      — bakes Arena + Player_RL prefabs from the active scene
// Pushman > 3. Build Training Scene — generates ML_Training_Scene with N×N arenas
public static class PushmanSetup
{
    // v3 obs space = 33 dims (Profile_A with full v3 obs set, 1 opponent):
    //   self: kin(3) + stam(1) + state(1) + chargeProg(1) + stats(5) + personality(5) = 16
    //   arena: bounds(3) + ringOutMargin(1) = 4
    //   per-opp: pos(2) + vel(2) + state-onehot(6) + facingSelf→Opp(1) + dist(1) + facingOpp→Self(1) = 13
    //   total: 16 + 4 + 13×1 = 33
    private const int OBS_SIZE = 33;
    private const string SHARED_BEHAVIOR_NAME = "Pushman"; // unified — all round-robin agents feed one network
    private const string PREFAB_FOLDER = "Assets/Prefabs";
    private const string ARENA_PREFAB_PATH = PREFAB_FOLDER + "/Arena.prefab";
    private const string PLAYER_RL_PREFAB_PATH = PREFAB_FOLDER + "/Player_RL.prefab";
    private const string TRAINING_SCENE_PATH = "Assets/Scenes/ML_Training_Scene.unity";
    private const string TRAINING_SCENE_SMALL_PATH = "Assets/Scenes/ML_Training_Scene_Small.unity";
    private const string TRAINING_SCENE_MIXED_PATH      = "Assets/Scenes/ML_Training_Mixed.unity";
    private const string TRAINING_SCENE_ROUNDROBIN_PATH = "Assets/Scenes/ML_Training_RoundRobin.unity";
    private const int GRID_SIZE = 8;           // GRID_SIZE × GRID_SIZE arenas (full training)
    private const int GRID_SIZE_SMALL = 4;    // smaller grid for fast/dev runs (4×4 = 16 arenas)
    private const int GRID_SIZE_MIXED = 6;    // 6×6=36 arenas (divisible by 3 for even bot distribution)
    private const float ARENA_SPACING = 25f;  // must be > ringOutRadius*2 so players can't cross arenas

    // -----------------------------------------------------------------------
    // 1. Setup Test Scene
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/1. Setup Test Scene")]
    public static void SetupTestScene()
    {
        EnsureFolders();

        var stats       = CreateOrLoad<CharacterStats>("Assets/ScriptableObjects/Characters/DefaultStats.asset");
        InitCharacterStats(stats);

        var profileA    = CreateOrLoad<ObservationProfile>("Assets/ScriptableObjects/Observation/Profile_A.asset");
        EditorUtility.SetDirty(profileA);

        var personality = CreateOrLoad<BotPersonality>("Assets/ScriptableObjects/Personalities/Aggressive.asset");
        EditorUtility.SetDirty(personality);

        AssetDatabase.SaveAssets();

        int playerLayer = GetOrAddLayer("Player");
        CleanupObject("Arena");

        GameObject arena  = new GameObject("Arena");
        GameObject center = CreateChild("ArenaCenter", arena);
        center.transform.localPosition = Vector3.zero;

        // 4 spawn points at cardinal positions inside the ring (ringOutRadius=10, spawn at 4 units)
        var spawnPositions = new Vector3[] {
            new Vector3(-4f,  0f, 0f),
            new Vector3( 4f,  0f, 0f),
            new Vector3( 0f,  4f, 0f),
            new Vector3( 0f, -4f, 0f),
        };
        var spawnGOs = new List<GameObject>();
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            var sp = CreateChild($"SpawnPoint_{i + 1}", arena);
            sp.transform.localPosition = spawnPositions[i];
            spawnGOs.Add(sp);
        }

        // Player 1 — HumanBrain
        GameObject p1GO = BuildPlayerGO("Player1", stats, playerLayer, arena);
        p1GO.transform.localPosition = spawnPositions[0];
        p1GO.AddComponent<HumanBrain>();
        Player p1 = p1GO.GetComponent<Player>();

        // Player 2 — RLAgentBrain
        GameObject p2GO = BuildPlayerGO("Player2", stats, playerLayer, arena);
        p2GO.transform.localPosition = spawnPositions[1];
        RLAgentBrain rlBrain = p2GO.AddComponent<RLAgentBrain>();
        rlBrain.MaxStep = 5000;
        ConfigureBehaviorParameters(p2GO);
        var dr = p2GO.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = 5;
        Player p2 = p2GO.GetComponent<Player>();

        int oppMask = 1 << playerLayer;
        p1.opponentLayer = oppMask;
        p2.opponentLayer = oppMask;

        ArenaManager am = arena.AddComponent<ArenaManager>();
        am.arenaCenter   = center.transform;
        am.ringOutRadius = 10f;
        am.allPlayers    = new List<Player> { p1, p2 };
        am.spawnPoints   = new List<Transform>();
        foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);

        rlBrain.arenaManager       = am;
        rlBrain.opponents          = new Player[] { p1 };
        rlBrain.observationProfile = profileA;
        rlBrain.personality        = personality;

        EditorUtility.SetDirty(am);
        EditorUtility.SetDirty(rlBrain);
        EditorUtility.SetDirty(p1);
        EditorUtility.SetDirty(p2);

        // Fixed orthographic camera sized to show the full arena (ring radius=10, diameter=20)
        // plus ~2u padding each side. No follow script needed — edge awareness is the core mechanic.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic     = true;
            cam.orthographicSize = 12f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] Test scene ready. Ctrl+S to save. " +
                  "Set Player2 > Behavior Parameters > Behavior Type to 'Default' before training.");
    }

    // -----------------------------------------------------------------------
    // 1b. Setup Bot Test Scene  — Player1=Human vs Player2=ChaseBotBrain
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/1b. Setup Bot Test Scene (Human vs ChaseBotBrain)")]
    public static void SetupBotTestScene()
    {
        // Run the full setup first to get a clean scene.
        SetupTestScene();

        // Swap Player2's brain from RLAgentBrain → ChaseBotBrain.
        GameObject p2GO = GameObject.Find("Arena/Player2");
        if (p2GO == null) { Debug.LogError("[PushmanSetup] Player2 not found."); return; }

        // Remove RL-specific components in dependency order (dependents first):
        //   DecisionRequester requires RLAgentBrain (Agent)
        //   RLAgentBrain (Agent) requires BehaviorParameters
        var dr = p2GO.GetComponent<DecisionRequester>();
        if (dr != null) Object.DestroyImmediate(dr);
        var rl = p2GO.GetComponent<RLAgentBrain>();
        if (rl != null) Object.DestroyImmediate(rl);
        var bp = p2GO.GetComponent<BehaviorParameters>();
        if (bp != null) Object.DestroyImmediate(bp);

        // Add the simple chase bot.
        p2GO.AddComponent<ChaseBotBrain>();

        // Player.Brain is set in Awake via GetComponent<IPlayerBrain>, so no rewiring needed.
        EditorUtility.SetDirty(p2GO);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] Bot test scene ready. Player1=Human (WASD+mouse), " +
                  "Player2=ChaseBotBrain. Run '4. Add Sprites to Players' then Ctrl+S.");
    }

    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // 6. Personality Showcase Scene
    //    5 arenas side-by-side, each showing a different personality matchup.
    //    All arenas use the shared Phase 3 v2 ONNX (Pushman_Shared/PushmanAgent_v2.onnx).
    //    Personality differentiation comes from BotPersonality SO + self-personality obs.
    //    Wide orthographic camera shows all 5 arenas at once.
    //
    //    Matchups chosen to illustrate the v2 win-rate matrix:
    //      Arena 0 — Evasive vs Aggressive   (Evasive wins 59%)
    //      Arena 1 — Aggressive vs Defensive (Aggressive wins 59%)
    //      Arena 2 — Defensive vs Balanced   (Defensive wins 62% — strongest asymmetry)
    //      Arena 3 — Counter vs Evasive      (Counter wins 52% — punisher vs dodger)
    //      Arena 4 — Balanced vs Counter     (~50/50 — the even fight)
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/6. Personality Showcase Scene")]
    public static void BuildPersonalityShowcaseScene()
    {
        const string SHOWCASE_SCENE_PATH = "Assets/Scenes/PersonalityShowcase.unity";
        const string MODELS_FOLDER       = "Assets/MLModels";
        const string SHARED_ONNX_PATH    = MODELS_FOLDER + "/Pushman_Shared/PushmanAgent_v39.onnx";

        // Force-import the shared ONNX before loading it.
        if (System.IO.File.Exists(SHARED_ONNX_PATH))
            AssetDatabase.ImportAsset(SHARED_ONNX_PATH, ImportAssetOptions.ForceSynchronousImport);

        // Single shared model — all arenas use v39 (33-dim obs, 45M steps, round-robin no self-play).
        var sharedModel = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SHARED_ONNX_PATH);
        if (sharedModel == null)
            Debug.LogWarning($"[Showcase] Shared ONNX missing at {SHARED_ONNX_PATH}. " +
                             "Copy results/Pushman_Shared_v39/Pushman/Pushman-45000114.onnx there first.");
        else
            Debug.Log($"[Showcase] Loaded shared model: PushmanAgent_v39.onnx ({sharedModel.GetType().Name})");

        string[] personalityNames = { "Aggressive", "Defensive", "Evasive", "Balanced", "Counter" };
        var personalities = new BotPersonality[5];

        for (int i = 0; i < 5; i++)
        {
            personalities[i] = AssetDatabase.LoadAssetAtPath<BotPersonality>(
                $"Assets/ScriptableObjects/Personalities/{personalityNames[i]}.asset");
            if (personalities[i] == null)
                Debug.LogWarning($"[Showcase] Personality asset missing: {personalityNames[i]}");
        }

        var profileA = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
            "Assets/ScriptableObjects/Observation/Profile_A.asset");
        var defaultStats = AssetDatabase.LoadAssetAtPath<CharacterStats>(
            "Assets/ScriptableObjects/Characters/DefaultStats.asset");

        // Indexes: 0=Aggressive  1=Defensive  2=Evasive  3=Balanced  4=Counter
        // Matchups chosen for v2 win-rate matrix: show the clearest asymmetries.
        var matchups = new (int a, int b, string label)[]
        {
            (2, 0, "Evasive vs Aggressive"),     // Evasive 59%
            (0, 1, "Aggressive vs Defensive"),   // Aggressive 59%
            (1, 3, "Defensive vs Balanced"),     // Defensive 62% — strongest
            (4, 2, "Counter vs Evasive"),        // Counter 52% — punisher vs dodger
            (3, 4, "Balanced vs Counter"),       // ~50/50 — the even fight
        };

        EnsureFolders();
        int playerLayer = GetOrAddLayer("Player");

        var circleSprite = GetOrCreateSprite("Assets/Sprites/PlayerCircle.png", () => MakeCircleTexture(128));
        var pushSprite   = GetOrCreateSprite("Assets/Sprites/PushHand.png",       () => MakeRectTexture(24, 16));
        var blockSprite  = GetOrCreateSprite("Assets/Sprites/BlockShield.png",    () => MakeRectTexture(48, 10));

        // Build in the current active scene (same pattern as BuildBotVsBotScene).
        // NewScene() unloads assets loaded above — avoid it.
        for (int k = 0; k < matchups.Length; k++)
            CleanupObject($"Arena_{k}_{matchups[k].label.Replace(" ", "_")}");
        // Also clean up any leftover single-arena objects from other builders.
        CleanupObject("Arena");
        CleanupObject("StaminaHUD");

        for (int i = 0; i < matchups.Length; i++)
        {
            var (pA, pB, label) = matchups[i];
            Vector3 offset = new Vector3(i * ARENA_SPACING, 0f, 0f);

            // Build the arena from scratch (not from prefab) so SerializedObject
            // model assignment works cleanly — same pattern as BuildBotVsBotScene.
            GameObject arenaGO = new GameObject($"Arena_{i}_{label.Replace(" ", "_")}");
            arenaGO.transform.position = offset;

            GameObject center = CreateChild("ArenaCenter", arenaGO);
            center.transform.localPosition = Vector3.zero;

            var spawnPositions = new Vector3[]
            {
                new Vector3(-4f,  0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(0f,   4f, 0f), new Vector3(0f, -4f, 0f),
            };
            var spawnGOs = new List<GameObject>();
            for (int s = 0; s < spawnPositions.Length; s++)
            {
                var sp = CreateChild($"SpawnPoint_{s + 1}", arenaGO);
                sp.transform.localPosition = spawnPositions[s];
                spawnGOs.Add(sp);
            }

            // Build the two players as fresh GOs so model assignment works.
            // Both use the shared Phase 3 v2 ONNX — personality comes from the SO + obs.
            var p1GO = BuildBotPlayer("Player1", defaultStats, playerLayer, arenaGO,
                                      spawnPositions[0], profileA, personalities[pA], sharedModel);
            var p2GO = BuildBotPlayer("Player2", defaultStats, playerLayer, arenaGO,
                                      spawnPositions[1], profileA, personalities[pB], sharedModel);

            // Both showcase players use the shared "Pushman" behavior so they can load the
            // v2 ONNX. Personality differentiation comes from the BotPersonality SO (rewards)
            // and the self-personality observation, not from the BehaviorName.
            p1GO.GetComponent<BehaviorParameters>().BehaviorName = SHARED_BEHAVIOR_NAME;
            p2GO.GetComponent<BehaviorParameters>().BehaviorName = SHARED_BEHAVIOR_NAME;

            Player p1 = p1GO.GetComponent<Player>();
            Player p2 = p2GO.GetComponent<Player>();
            int oppMask = 1 << playerLayer;
            p1.opponentLayer = oppMask;
            p2.opponentLayer = oppMask;

            // Wire ArenaManager
            ArenaManager am = arenaGO.AddComponent<ArenaManager>();
            am.arenaCenter     = center.transform;
            am.ringOutRadius   = 10f;
            am.timeUntilShrink = 10f;  // showcase: shrink starts after 10s so fast-cycling arenas always show it
            am.allPlayers      = new List<Player> { p1, p2 };
            am.spawnPoints   = new List<Transform>();
            foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);

            var rl1 = p1GO.GetComponent<RLAgentBrain>();
            var rl2 = p2GO.GetComponent<RLAgentBrain>();
            rl1.arenaManager = am; rl1.opponents = new Player[] { p2 };
            rl2.arenaManager = am; rl2.opponents = new Player[] { p1 };
            // Override personality assigned by BuildBotPlayer with the correct one
            rl1.personality = personalities[pA];
            rl2.personality = personalities[pB];

            // Per-personality body colors so viewer can match color to label.
            ApplyPlayerSprites(p1GO, circleSprite, pushSprite, blockSprite, PersonalityColor(personalityNames[pA]));
            ApplyPlayerSprites(p2GO, circleSprite, pushSprite, blockSprite, PersonalityColor(personalityNames[pB]));
            ApplyArenaBoundary(arenaGO, 10f);

            // Bigger push hand in showcase — ring radius 10u, camera far back, need impact to read.
            SetHandScale(p1GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p1GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));
            SetHandScale(p2GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p2GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));

            // Label above the ring: P1 name (p1 color) / vs / P2 name (p2 color).
            AddArenaLabel(arenaGO, personalityNames[pA], PersonalityColor(personalityNames[pA]),
                                   personalityNames[pB], PersonalityColor(personalityNames[pB]),
                                   yTop: 13.5f);

            EditorUtility.SetDirty(am);
            EditorUtility.SetDirty(p1GO);
            EditorUtility.SetDirty(p2GO);
            Debug.Log($"[Showcase] Arena {i}: {label}");
        }

        // Wide orthographic camera — all 5 arenas (x=0..100) plus labels above.
        // 5 arenas: half-width = (4×25)/2 + 10 + 5 padding = 65u
        // At 16:9: orthoSize = 65 / 1.778 = 36.6 → use 35.
        // Camera Y centered on content: ring bottom=-10, label top≈yTop+2.8=16.3 → center≈3.15
        float centerX = (matchups.Length - 1) * ARENA_SPACING * 0.5f;
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }
        cam.orthographic       = true;
        cam.orthographicSize   = 35f;
        cam.transform.position = new Vector3(centerX, 3.15f, -10f);
        cam.clearFlags         = CameraClearFlags.SolidColor;
        cam.backgroundColor    = new Color(0.067f, 0.063f, 0.035f);  // ContentKit Void — warm near-black
        EditorUtility.SetDirty(cam.gameObject);

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SHOWCASE_SCENE_PATH);

        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!buildScenes.Exists(s => s.path == SHOWCASE_SCENE_PATH))
            buildScenes.Add(new EditorBuildSettingsScene(SHOWCASE_SCENE_PATH, false));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log($"[Showcase] Saved → {SHOWCASE_SCENE_PATH}. Open it and press Play.\n" +
                  "  All arenas use PushmanAgent_v39.onnx (Phase 3.9 round-robin, 45M steps, 33-dim obs).\n" +
                  "  Arena 0 — Evasive vs Aggressive   (Evasive 59%)\n" +
                  "  Arena 1 — Aggressive vs Defensive (Aggressive 59%)\n" +
                  "  Arena 2 — Defensive vs Balanced   (Defensive 62% — strongest asymmetry)\n" +
                  "  Arena 3 — Counter vs Evasive      (Counter 52% — punisher vs dodger)\n" +
                  "  Arena 4 — Balanced vs Counter     (~50/50 — the even fight)");
    }

    // -----------------------------------------------------------------------
    // 7. Difficulty Tier Showcase Scene
    //    4 arenas side-by-side, each with a different humanization tier pinned.
    //    All arenas use the v2.5 ONNX and Aggressive vs Aggressive mirror match
    //    (mirrors isolate difficulty as the only variable). Use Profile_C since
    //    that's what v2.5 was trained against (noise + delay).
    //
    //    Tiers (humanization, noise σ, delay frames):
    //      Arena 0 — Expert (0.0, 0u,    0 frames /   0ms)
    //      Arena 1 — Hard   (0.33, 0.05, 4 frames /  80ms)
    //      Arena 2 — Medium (0.66, 0.10, 8 frames / 160ms)
    //      Arena 3 — Easy   (1.0,  0.15, 12 frames / 240ms)
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/7. Difficulty Tier Showcase Scene")]
    public static void BuildDifficultyShowcaseScene()
    {
        const string SHOWCASE_SCENE_PATH = "Assets/Scenes/DifficultyShowcase.unity";
        const string MODELS_FOLDER       = "Assets/MLModels";
        const string ONNX_PATH           = MODELS_FOLDER + "/Pushman_Shared/PushmanAgent_v39.onnx";

        if (System.IO.File.Exists(ONNX_PATH))
            AssetDatabase.ImportAsset(ONNX_PATH, ImportAssetOptions.ForceSynchronousImport);

        var model = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ONNX_PATH);
        if (model == null)
            Debug.LogWarning($"[DiffShowcase] v39 ONNX missing at {ONNX_PATH}. " +
                             "Copy results/Pushman_Shared_v39/Pushman/Pushman-45000114.onnx there first.");
        else
            Debug.Log($"[DiffShowcase] Loaded v39 model ({model.GetType().Name})");

        var profileC = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
            "Assets/ScriptableObjects/Observation/Profile_C.asset");
        var personality = AssetDatabase.LoadAssetAtPath<BotPersonality>(
            "Assets/ScriptableObjects/Personalities/Aggressive.asset");
        var defaultStats = AssetDatabase.LoadAssetAtPath<CharacterStats>(
            "Assets/ScriptableObjects/Characters/DefaultStats.asset");

        var tiers = new (float humanization, string label, Color color)[]
        {
            (0.0f,  "EXPERT", new Color(0.95f, 0.30f, 0.25f)),  // red   — hardest
            (0.33f, "HARD",   new Color(0.95f, 0.55f, 0.15f)),  // orange
            (0.66f, "MEDIUM", new Color(0.95f, 0.85f, 0.15f)),  // yellow
            (1.0f,  "EASY",   new Color(0.20f, 0.85f, 0.30f)),  // green — easiest
        };

        EnsureFolders();
        int playerLayer = GetOrAddLayer("Player");

        var circleSprite = GetOrCreateSprite("Assets/Sprites/PlayerCircle.png", () => MakeCircleTexture(128));
        var pushSprite   = GetOrCreateSprite("Assets/Sprites/PushHand.png",       () => MakeRectTexture(24, 16));
        var blockSprite  = GetOrCreateSprite("Assets/Sprites/BlockShield.png",    () => MakeRectTexture(48, 10));

        // Cleanup any prior arenas in the active scene.
        for (int k = 0; k < tiers.Length; k++)
            CleanupObject($"Arena_{k}_{tiers[k].label}");
        CleanupObject("Arena");
        CleanupObject("StaminaHUD");

        for (int i = 0; i < tiers.Length; i++)
        {
            var (hum, label, color) = tiers[i];
            Vector3 offset = new Vector3(i * ARENA_SPACING, 0f, 0f);

            GameObject arenaGO = new GameObject($"Arena_{i}_{label}");
            arenaGO.transform.position = offset;

            GameObject center = CreateChild("ArenaCenter", arenaGO);
            center.transform.localPosition = Vector3.zero;

            var spawnPositions = new Vector3[]
            {
                new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(0f, 4f, 0f), new Vector3(0f, -4f, 0f),
            };
            var spawnGOs = new List<GameObject>();
            for (int s = 0; s < spawnPositions.Length; s++)
            {
                var sp = CreateChild($"SpawnPoint_{s + 1}", arenaGO);
                sp.transform.localPosition = spawnPositions[s];
                spawnGOs.Add(sp);
            }

            // Mirror match: both players same personality, same stats. Only difference is the tier.
            var p1GO = BuildBotPlayer("Player1", defaultStats, playerLayer, arenaGO,
                                      spawnPositions[0], profileC, personality, model);
            var p2GO = BuildBotPlayer("Player2", defaultStats, playerLayer, arenaGO,
                                      spawnPositions[1], profileC, personality, model);

            p1GO.GetComponent<BehaviorParameters>().BehaviorName = SHARED_BEHAVIOR_NAME;
            p2GO.GetComponent<BehaviorParameters>().BehaviorName = SHARED_BEHAVIOR_NAME;

            // Pin the humanization tier — both players in this arena are at the same level.
            p1GO.GetComponent<RLAgentBrain>().runtimeHumanization = hum;
            p2GO.GetComponent<RLAgentBrain>().runtimeHumanization = hum;

            Player p1 = p1GO.GetComponent<Player>();
            Player p2 = p2GO.GetComponent<Player>();
            int oppMask = 1 << playerLayer;
            p1.opponentLayer = oppMask;
            p2.opponentLayer = oppMask;

            ArenaManager am = arenaGO.AddComponent<ArenaManager>();
            am.arenaCenter     = center.transform;
            am.ringOutRadius   = 10f;
            am.timeUntilShrink = 10f;
            am.allPlayers      = new List<Player> { p1, p2 };
            am.spawnPoints     = new List<Transform>();
            foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);

            var rl1 = p1GO.GetComponent<RLAgentBrain>();
            var rl2 = p2GO.GetComponent<RLAgentBrain>();
            rl1.arenaManager = am; rl1.opponents = new Player[] { p2 };
            rl2.arenaManager = am; rl2.opponents = new Player[] { p1 };
            rl1.personality = personality;
            rl2.personality = personality;

            // Same body color per arena = the tier color. Subtle alpha difference between
            // P1 and P2 so you can still tell them apart.
            Color p1Col = color;
            Color p2Col = new Color(color.r * 0.75f, color.g * 0.75f, color.b * 0.75f);
            ApplyPlayerSprites(p1GO, circleSprite, pushSprite, blockSprite, p1Col);
            ApplyPlayerSprites(p2GO, circleSprite, pushSprite, blockSprite, p2Col);
            ApplyArenaBoundary(arenaGO, 10f);

            SetHandScale(p1GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p1GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));
            SetHandScale(p2GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p2GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));

            // Label above the ring: tier name in tier color, with delay/noise stats below.
            int delayMs = Mathf.FloorToInt(hum * 12f) * 20;  // frames × 20ms (50Hz)
            float noiseSigma = hum * 0.15f;
            AddDifficultyLabel(arenaGO, label, color,
                               $"{delayMs}ms delay, σ={noiseSigma:F2}u noise",
                               yTop: 13.5f);

            EditorUtility.SetDirty(am);
            EditorUtility.SetDirty(p1GO);
            EditorUtility.SetDirty(p2GO);
            Debug.Log($"[DiffShowcase] Arena {i}: {label} (humanization={hum})");
        }

        // 4 arenas: half-width = (3×25)/2 + 10 + 5 padding = 52.5u
        // At 16:9: orthoSize = 52.5 / 1.778 = 29.5 → use 28.
        // Camera Y centered on content: ring bottom=-10, label top≈16.3 → center≈3.15
        float centerX = (tiers.Length - 1) * ARENA_SPACING * 0.5f;
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }
        cam.orthographic       = true;
        cam.orthographicSize   = 28f;
        cam.transform.position = new Vector3(centerX, 3.15f, -10f);
        cam.clearFlags         = CameraClearFlags.SolidColor;
        cam.backgroundColor    = new Color(0.067f, 0.063f, 0.035f);
        EditorUtility.SetDirty(cam.gameObject);

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SHOWCASE_SCENE_PATH);

        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!buildScenes.Exists(s => s.path == SHOWCASE_SCENE_PATH))
            buildScenes.Add(new EditorBuildSettingsScene(SHOWCASE_SCENE_PATH, false));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log($"[DiffShowcase] Saved → {SHOWCASE_SCENE_PATH}. Open it and press Play.\n" +
                  "  All arenas use PushmanAgent_v39.onnx (Aggressive vs Aggressive mirror, 33-dim obs).\n" +
                  "  Each arena pins both players to a specific humanization tier — only the\n" +
                  "  reaction time and observation noise differ between arenas.");
    }

    // -----------------------------------------------------------------------
    // 8. Character Showcase Scene
    //    5 arenas showing the three CharacterStats archetypes against each other.
    //    All use the Aggressive personality and the same ONNX so that only the
    //    physical stats (mass, speed, push/dodge force) differ between arenas.
    //
    //    Arenas:
    //      0 — Default    vs Heavyweight  (avg vs tank)
    //      1 — Default    vs Speedster    (avg vs glass cannon)
    //      2 — Heavyweight vs Speedster   (extreme contrast)
    //      3 — Heavyweight vs Heavyweight (tank mirror — how long do they take to ring each other out?)
    //      4 — Speedster  vs Speedster   (glass cannon mirror — how fast do they die?)
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/8. Character Showcase Scene")]
    public static void BuildCharacterShowcaseScene()
    {
        const string SHOWCASE_SCENE_PATH = "Assets/Scenes/CharacterShowcase.unity";
        const string MODELS_FOLDER       = "Assets/MLModels";
        const string SHARED_ONNX_PATH    = MODELS_FOLDER + "/Pushman_Shared/PushmanAgent_v39.onnx";

        if (System.IO.File.Exists(SHARED_ONNX_PATH))
            AssetDatabase.ImportAsset(SHARED_ONNX_PATH, ImportAssetOptions.ForceSynchronousImport);

        var sharedModel = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SHARED_ONNX_PATH);
        if (sharedModel == null)
            Debug.LogWarning($"[CharShowcase] Shared ONNX missing at {SHARED_ONNX_PATH}.");
        else
            Debug.Log($"[CharShowcase] Loaded model: {System.IO.Path.GetFileName(SHARED_ONNX_PATH)}");

        // Load all three CharacterStats SOs.
        var statsDefault   = AssetDatabase.LoadAssetAtPath<CharacterStats>(
            "Assets/ScriptableObjects/Characters/DefaultStats.asset");
        var statsHeavy     = AssetDatabase.LoadAssetAtPath<CharacterStats>(
            "Assets/ScriptableObjects/Characters/Heavyweight.asset");
        var statsSpeedster = AssetDatabase.LoadAssetAtPath<CharacterStats>(
            "Assets/ScriptableObjects/Characters/Speedster.asset");

        // All arenas use Aggressive personality — isolates CharacterStats as the only variable.
        var personality = AssetDatabase.LoadAssetAtPath<BotPersonality>(
            "Assets/ScriptableObjects/Personalities/Aggressive.asset");
        var profileA = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
            "Assets/ScriptableObjects/Observation/Profile_A.asset");

        // Color per archetype — ContentKit Bright palette, consistent across arenas.
        var colorDefault   = new Color(0.604f, 0.667f, 0.733f);  // Steel Bright  #9AAABB — neutral, versatile
        var colorHeavy     = new Color(0.863f, 0.596f, 0.471f);  // Terra Bright  #DC9878 — warm, heavy
        var colorSpeedster = new Color(0.604f, 0.733f, 0.525f);  // Sage Bright   #9ABB86 — light, fast

        // 3 cross-matchups only — mirrors (Heavy vs Heavy, Speedster vs Speedster) dropped.
        // Fewer arenas → smaller camera → arenas fill the screen.
        var matchups = new (CharacterStats a, string nameA, Color colA,
                            CharacterStats b, string nameB, Color colB)[]
        {
            (statsDefault,   "Default",    colorDefault,   statsHeavy,     "Heavyweight", colorHeavy),
            (statsDefault,   "Default",    colorDefault,   statsSpeedster, "Speedster",   colorSpeedster),
            (statsHeavy,     "Heavyweight",colorHeavy,     statsSpeedster, "Speedster",   colorSpeedster),
        };

        EnsureFolders();
        int playerLayer = GetOrAddLayer("Player");

        var circleSprite = GetOrCreateSprite("Assets/Sprites/PlayerCircle.png", () => MakeCircleTexture(128));
        var pushSprite   = GetOrCreateSprite("Assets/Sprites/PushHand.png",       () => MakeRectTexture(24, 16));
        var blockSprite  = GetOrCreateSprite("Assets/Sprites/BlockShield.png",    () => MakeRectTexture(48, 10));

        for (int k = 0; k < matchups.Length; k++)
            CleanupObject($"CharArena_{k}");
        CleanupObject("Arena");
        CleanupObject("StaminaHUD");

        for (int i = 0; i < matchups.Length; i++)
        {
            var (statsA, nameA, colA, statsB, nameB, colB) = matchups[i];
            Vector3 offset = new Vector3(i * ARENA_SPACING, 0f, 0f);

            GameObject arenaGO = new GameObject($"CharArena_{i}");
            arenaGO.transform.position = offset;

            GameObject center = CreateChild("ArenaCenter", arenaGO);
            center.transform.localPosition = Vector3.zero;

            var spawnPositions = new Vector3[]
            {
                new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3( 0f, 4f, 0f), new Vector3(0f, -4f, 0f),
            };
            var spawnGOs = new List<GameObject>();
            for (int s = 0; s < spawnPositions.Length; s++)
            {
                var sp = CreateChild($"SpawnPoint_{s + 1}", arenaGO);
                sp.transform.localPosition = spawnPositions[s];
                spawnGOs.Add(sp);
            }

            var p1GO = BuildBotPlayer("Player1", statsA, playerLayer, arenaGO,
                                      spawnPositions[0], profileA, personality, sharedModel);
            var p2GO = BuildBotPlayer("Player2", statsB, playerLayer, arenaGO,
                                      spawnPositions[1], profileA, personality, sharedModel);

            p1GO.GetComponent<BehaviorParameters>().BehaviorName = SHARED_BEHAVIOR_NAME;
            p2GO.GetComponent<BehaviorParameters>().BehaviorName = SHARED_BEHAVIOR_NAME;

            Player p1 = p1GO.GetComponent<Player>();
            Player p2 = p2GO.GetComponent<Player>();
            int oppMask = 1 << playerLayer;
            p1.opponentLayer = oppMask;
            p2.opponentLayer = oppMask;

            ArenaManager am = arenaGO.AddComponent<ArenaManager>();
            am.arenaCenter     = center.transform;
            am.ringOutRadius   = 10f;
            am.timeUntilShrink = 10f;
            am.allPlayers      = new List<Player> { p1, p2 };
            am.spawnPoints     = new List<Transform>();
            foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);

            var rl1 = p1GO.GetComponent<RLAgentBrain>();
            var rl2 = p2GO.GetComponent<RLAgentBrain>();
            rl1.arenaManager = am; rl1.opponents = new Player[] { p2 };
            rl2.arenaManager = am; rl2.opponents = new Player[] { p1 };
            rl1.personality  = personality;
            rl2.personality  = personality;

            ApplyPlayerSprites(p1GO, circleSprite, pushSprite, blockSprite, colA);
            ApplyPlayerSprites(p2GO, circleSprite, pushSprite, blockSprite, colB);
            ApplyArenaBoundary(arenaGO, 10f);

            SetHandScale(p1GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p1GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));
            SetHandScale(p2GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p2GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));

            // Label: names only. yTop=16 keeps bottom name 3.5u clear of the ring edge (y=10).
            AddCharacterLabel(arenaGO, nameA, colA, null, nameB, colB, null, yTop: 16.0f);

            EditorUtility.SetDirty(am);
            EditorUtility.SetDirty(p1GO);
            EditorUtility.SetDirty(p2GO);
            Debug.Log($"[CharShowcase] Arena {i}: {nameA} vs {nameB}");
        }

        // 3 arenas: half-width needed = (2×25)/2 + 10 + 5 padding = 40u
        // At 16:9 aspect: orthoSize = 40 / 1.778 = 22.5 → use 22.
        // Camera Y centered on content: ring bottom=-10, label top=yTop+2.5=18.5 → center=4.25
        float centerX = (matchups.Length - 1) * ARENA_SPACING * 0.5f;
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }
        cam.orthographic       = true;
        cam.orthographicSize   = 22f;
        cam.transform.position = new Vector3(centerX, 4.25f, -10f);
        cam.clearFlags         = CameraClearFlags.SolidColor;
        cam.backgroundColor    = new Color(0.067f, 0.063f, 0.035f);
        EditorUtility.SetDirty(cam.gameObject);

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SHOWCASE_SCENE_PATH);

        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!buildScenes.Exists(s => s.path == SHOWCASE_SCENE_PATH))
            buildScenes.Add(new EditorBuildSettingsScene(SHOWCASE_SCENE_PATH, false));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log($"[CharShowcase] Saved → {SHOWCASE_SCENE_PATH}. Open it and press Play.\n" +
                  "  All arenas: Aggressive vs Aggressive, same ONNX — only CharacterStats differ.\n" +
                  "  Steel = Default  |  Terra = Heavyweight  |  Sage = Speedster\n" +
                  "  Arena 0 — Default vs Heavyweight  (avg vs tank)\n" +
                  "  Arena 1 — Default vs Speedster    (avg vs glass cannon)\n" +
                  "  Arena 2 — Heavyweight vs Speedster (extreme contrast)\n" +
                  "  Arena 3 — Heavyweight mirror       (how long does tank vs tank take?)\n" +
                  "  Arena 4 — Speedster mirror         (how fast do glass cannons die?)");
    }

    // Label for Character Showcase: two character names in their colors, stat lines below each.
    private static void AddCharacterLabel(GameObject arenaGO,
                                          string nameA, Color colA, string statsA,
                                          string nameB, Color colB, string statsB,
                                          float yTop)
    {
        var dim = new Color(0.416f, 0.388f, 0.345f);  // Text Muted #6A6358
        // 3-line stack: nameA (large) / vs (small muted) / nameB (large). Stats dropped for clarity.
        MakeTMPWorldLabel(arenaGO, $"Label_{nameA}", nameA.ToUpper(), FontTitle, 3.5f, colA, new Vector3(0f, yTop + 2.5f, 0f));
        MakeTMPWorldLabel(arenaGO,  "Label_vs",      "vs",            FontBody,  1.4f, dim,  new Vector3(0f, yTop + 0.0f, 0f));
        MakeTMPWorldLabel(arenaGO, $"Label_{nameB}", nameB.ToUpper(), FontTitle, 3.5f, colB, new Vector3(0f, yTop - 2.5f, 0f));
    }

    // -----------------------------------------------------------------------
    // 9. Legacy Showcase Scene — historical "before" footage for video/article
    //
    //    Loads old 23-dim models that pre-date the v3 obs-space expansion.
    //    Creates a temporary in-memory Profile_Legacy (23 dims) so the obs
    //    count matches each model without crashing.
    //
    //    Three arenas, each a mirror match (same model on both sides) to
    //    isolate behavior — we want to see the AI, not who wins:
    //
    //      Arena 0 — Baby AI (Fast_v1, 500k steps)
    //                Near-random: spins, runs past opponent, pushes into walls.
    //      Arena 1 — Dodge-dominant collapse (RoundRobin_v1 Aggressive, 5M steps)
    //                All personalities converged here — spams dodge, never pushes.
    //                The failure mode that motivated the full reward redesign.
    //      Arena 2 — Phase 2 trained (Master_v1, 20M steps, Aggressive self-play)
    //                Correct RPS mechanics, real pushes — but single Aggressive
    //                personality and no aim obs (pushes in wrong direction often).
    //
    //    NOTE: these models use BehaviorName="PushmanAgent" (arenas 0+2) or
    //    "Aggressive" (arena 1). Both differ from the current "Pushman" name —
    //    that's expected and fine for InferenceOnly showcase.
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/9. Legacy Showcase Scene (v1 models)")]
    public static void BuildLegacyShowcaseScene()
    {
        const string SHOWCASE_SCENE_PATH = "Assets/Scenes/LegacyShowcase.unity";
        const string LEGACY_ASSET_DIR    = "Assets/MLModels/_Legacy";

        // Source paths in results/ (outside Assets — Unity can't load directly from there).
        // Destination paths are inside Assets/MLModels/_Legacy/ where AssetDatabase can reach them.
        var arenaModels = new (string srcPath, string assetPath, string behaviorName, int obsSize, string label, string subtitle)[]
        {
            (
                "results/Pushman_Fast_v1/PushmanAgent/PushmanAgent-499995.onnx",
                $"{LEGACY_ASSET_DIR}/Legacy_BabyAI.onnx",
                "PushmanAgent",
                13,   // ONNX verified: obs=[0,13], no LSTM
                "Baby AI",
                "500k steps — near random"
            ),
            (
                "results/Pushman_RoundRobin_v1/Aggressive/Aggressive-5021811.onnx",
                $"{LEGACY_ASSET_DIR}/Legacy_DodgeDominant.onnx",
                "Aggressive",
                18,   // ONNX verified: obs=[0,18], LSTM memory=128
                "Dodge Dominant",
                "5M steps — reward collapse"
            ),
            (
                "results/Pushman_Master_v1/PushmanAgent/PushmanAgent-20000528.onnx",
                $"{LEGACY_ASSET_DIR}/Legacy_Phase2.onnx",
                "PushmanAgent",
                18,   // ONNX verified: obs=[0,18], LSTM memory=128
                "Phase 2 Trained",
                "20M steps — fights but aim broken"
            ),
        };

        // Verify source files exist, then copy into Assets so Unity can import them.
        if (!AssetDatabase.IsValidFolder(LEGACY_ASSET_DIR))
            AssetDatabase.CreateFolder("Assets/MLModels", "_Legacy");

        foreach (var (srcPath, assetPath, _, _, label, _) in arenaModels)
        {
            if (!System.IO.File.Exists(srcPath))
            {
                Debug.LogError($"[LegacyShowcase] Missing source model for '{label}': {srcPath}\n" +
                               "  Ensure results/ folder is present from training runs.");
                return;
            }
            // Copy into Assets and force a synchronous import so LoadAssetAtPath works.
            System.IO.File.Copy(srcPath, assetPath, overwrite: true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        // Verified from ONNX input shapes (python onnx.load):
        //   Fast_v1 (Baby AI):               obs=[0,13] — no selfStats, no selfPersonalityId, no LSTM
        //   RoundRobin_v1 Aggressive (5M):   obs=[0,18] — selfStats added, no selfPersonalityId, LSTM=128
        //   Master_v1 (Phase 2, 20M):        obs=[0,18] — same as RoundRobin
        //
        // Profiles are saved as .asset files so they survive the Play-mode domain reload.
        // (CreateInstance<> objects lose their reference after domain reload — Unity serializes null.)
        const string PROFILE13_PATH = LEGACY_ASSET_DIR + "/Legacy_Profile13.asset";
        const string PROFILE18_PATH = LEGACY_ASSET_DIR + "/Legacy_Profile18.asset";

        bool p13Exists = AssetDatabase.LoadAssetAtPath<ObservationProfile>(PROFILE13_PATH) != null;
        ObservationProfile profile13 = p13Exists
            ? AssetDatabase.LoadAssetAtPath<ObservationProfile>(PROFILE13_PATH)
            : ScriptableObject.CreateInstance<ObservationProfile>();
        // kin(3)+stam(1)+state(1)+arena(3)+oppPos(2)+oppVel(2)+oppState(1) = 13
        profile13.selfKinematics        = true;
        profile13.selfStamina           = true;
        profile13.selfState             = true;
        profile13.selfChargeProgress    = false;
        profile13.selfRingOutMargin     = false;
        profile13.arenaBounds           = true;
        profile13.selfStats             = false;  // not yet added in Fast_v1 era
        profile13.selfPersonalityId     = false;  // not yet added in Fast_v1 era
        profile13.opponentPosition      = true;
        profile13.opponentVelocity      = true;
        profile13.opponentState         = true;
        profile13.opponentStateOneHot   = false;
        profile13.selfFacingOpponentDot = false;
        profile13.distanceToOpponent    = false;
        profile13.opponentFacingSelfDot = false;
        profile13.opponentStats         = false;
        profile13.humanization          = false;
        if (!p13Exists) AssetDatabase.CreateAsset(profile13, PROFILE13_PATH);
        else            EditorUtility.SetDirty(profile13);

        bool p18Exists = AssetDatabase.LoadAssetAtPath<ObservationProfile>(PROFILE18_PATH) != null;
        ObservationProfile profile18 = p18Exists
            ? AssetDatabase.LoadAssetAtPath<ObservationProfile>(PROFILE18_PATH)
            : ScriptableObject.CreateInstance<ObservationProfile>();
        // same as 13 + selfStats(5) = 18
        profile18.selfKinematics        = true;
        profile18.selfStamina           = true;
        profile18.selfState             = true;
        profile18.selfChargeProgress    = false;
        profile18.selfRingOutMargin     = false;
        profile18.arenaBounds           = true;
        profile18.selfStats             = true;   // added in RoundRobin era
        profile18.selfPersonalityId     = false;  // not yet (shared network came later)
        profile18.opponentPosition      = true;
        profile18.opponentVelocity      = true;
        profile18.opponentState         = true;
        profile18.opponentStateOneHot   = false;
        profile18.selfFacingOpponentDot = false;
        profile18.distanceToOpponent    = false;
        profile18.opponentFacingSelfDot = false;
        profile18.opponentStats         = false;
        profile18.humanization          = false;
        if (!p18Exists) AssetDatabase.CreateAsset(profile18, PROFILE18_PATH);
        else            EditorUtility.SetDirty(profile18);

        AssetDatabase.SaveAssets();
        Debug.Log($"[LegacyShowcase] Profile dims: profile13={profile13.ComputeSpaceSize(1)} (expect 13), " +
                  $"profile18={profile18.ComputeSpaceSize(1)} (expect 18)");

        var aggressive = AssetDatabase.LoadAssetAtPath<BotPersonality>(
            "Assets/ScriptableObjects/Personalities/Aggressive.asset");
        var defaultStats = AssetDatabase.LoadAssetAtPath<CharacterStats>(
            "Assets/ScriptableObjects/Characters/DefaultStats.asset");

        EnsureFolders();
        int playerLayer = GetOrAddLayer("Player");

        var circleSprite = GetOrCreateSprite("Assets/Sprites/PlayerCircle.png", () => MakeCircleTexture(128));
        var pushSprite   = GetOrCreateSprite("Assets/Sprites/PushHand.png",       () => MakeRectTexture(24, 16));
        var blockSprite  = GetOrCreateSprite("Assets/Sprites/BlockShield.png",    () => MakeRectTexture(48, 10));

        for (int k = 0; k < arenaModels.Length; k++)
            CleanupObject($"LegacyArena_{k}");
        CleanupObject("Arena");
        CleanupObject("StaminaHUD");

        // Era colors — ContentKit Bright palette, progression from rough → unstable → refined.
        var eraColors = new Color[]
        {
            new Color(0.863f, 0.596f, 0.471f),  // Arena 0 — Terra Bright  #DC9878 (Baby AI — rough/warm)
            new Color(0.722f, 0.596f, 0.800f),  // Arena 1 — Mauve Bright  #B898CC (Dodge Dominant — strange/unstable)
            new Color(0.604f, 0.667f, 0.733f),  // Arena 2 — Steel Bright  #9AAABB (Phase 2 — cooler, more refined)
        };

        for (int i = 0; i < arenaModels.Length; i++)
        {
            var (_, assetPath, behaviorName, obsSize, label, subtitle) = arenaModels[i];

            // Select the profile whose obs count matches what this ONNX was trained with.
            var obsProfile = obsSize == 13 ? profile13 : profile18;

            var model = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (model == null)
            {
                Debug.LogWarning($"[LegacyShowcase] Could not load model at {assetPath} — skipping arena {i}.");
                continue;
            }

            Vector3 offset = new Vector3(i * ARENA_SPACING, 0f, 0f);
            GameObject arenaGO = new GameObject($"LegacyArena_{i}");
            arenaGO.transform.position = offset;

            GameObject center = CreateChild("ArenaCenter", arenaGO);
            center.transform.localPosition = Vector3.zero;

            var spawnPositions = new Vector3[]
            {
                new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3( 0f, 4f, 0f), new Vector3(0f, -4f, 0f),
            };
            var spawnGOs = new List<GameObject>();
            for (int s = 0; s < spawnPositions.Length; s++)
            {
                var sp = CreateChild($"SpawnPoint_{s + 1}", arenaGO);
                sp.transform.localPosition = spawnPositions[s];
                spawnGOs.Add(sp);
            }

            // Mirror match — same model on both sides.
            var p1GO = BuildBotPlayer("Player1", defaultStats, playerLayer, arenaGO,
                                      spawnPositions[0], obsProfile, aggressive, model);
            var p2GO = BuildBotPlayer("Player2", defaultStats, playerLayer, arenaGO,
                                      spawnPositions[1], obsProfile, aggressive, model);

            // Override BehaviorName to match what each old model was trained as.
            p1GO.GetComponent<BehaviorParameters>().BehaviorName = behaviorName;
            p2GO.GetComponent<BehaviorParameters>().BehaviorName = behaviorName;

            Player p1 = p1GO.GetComponent<Player>();
            Player p2 = p2GO.GetComponent<Player>();
            int oppMask = 1 << playerLayer;
            p1.opponentLayer = oppMask;
            p2.opponentLayer = oppMask;

            ArenaManager am = arenaGO.AddComponent<ArenaManager>();
            am.arenaCenter     = center.transform;
            am.ringOutRadius   = 10f;
            am.timeUntilShrink = 30f;
            am.allPlayers      = new List<Player> { p1, p2 };
            am.spawnPoints     = new List<Transform>();
            foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);

            var rl1 = p1GO.GetComponent<RLAgentBrain>();
            var rl2 = p2GO.GetComponent<RLAgentBrain>();
            rl1.arenaManager = am; rl1.opponents = new Player[] { p2 };
            rl2.arenaManager = am; rl2.opponents = new Player[] { p1 };
            rl1.personality  = aggressive;
            rl2.personality  = aggressive;

            Color eraColor = eraColors[i];
            ApplyPlayerSprites(p1GO, circleSprite, pushSprite, blockSprite, eraColor);
            ApplyPlayerSprites(p2GO, circleSprite, pushSprite, blockSprite, eraColor * 0.7f);
            ApplyArenaBoundary(arenaGO, 10f);

            SetHandScale(p1GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p1GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));
            SetHandScale(p2GO, "PushHand",  new Vector3(2.8f, 2.8f, 1f));
            SetHandScale(p2GO, "BlockHand", new Vector3(2.2f, 2.6f, 1f));

            AddTitleSubtitleLabel(arenaGO, label, eraColor, subtitle,
                                  new Color(0.784f, 0.753f, 0.682f),  // Text Primary #C8C0AE
                                  yTop: 13.5f);

            EditorUtility.SetDirty(am);
            EditorUtility.SetDirty(p1GO);
            EditorUtility.SetDirty(p2GO);
            Debug.Log($"[LegacyShowcase] Arena {i}: {label} ({assetPath})");
        }

        float centerX = (arenaModels.Length - 1) * ARENA_SPACING * 0.5f;
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }
        cam.orthographic       = true;
        // 3 arenas: half-width = (2×25)/2 + 10 + 5 padding = 40u
        // At 16:9: orthoSize = 40 / 1.778 = 22.5 → use 22.
        // Camera Y centered on content: ring bottom=-10, label top≈15.5 → center≈2.75
        cam.orthographicSize   = 22f;
        cam.transform.position = new Vector3(centerX, 2.75f, -10f);
        cam.clearFlags         = CameraClearFlags.SolidColor;
        cam.backgroundColor    = new Color(0.067f, 0.063f, 0.035f);
        EditorUtility.SetDirty(cam.gameObject);

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), SHOWCASE_SCENE_PATH);

        var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!buildScenes.Exists(s => s.path == SHOWCASE_SCENE_PATH))
            buildScenes.Add(new EditorBuildSettingsScene(SHOWCASE_SCENE_PATH, false));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        // profiles are on-disk assets — do not DestroyImmediate
        AssetDatabase.Refresh();
        Debug.Log($"[LegacyShowcase] Saved → {SHOWCASE_SCENE_PATH}. Open it and press Play.\n" +
                  "  Arena 0 (Terra  #DC9878) — Baby AI: Fast_v1 at 500k steps\n" +
                  "  Arena 1 (Mauve  #B898CC) — Dodge Dominant: RoundRobin_v1 collapse\n" +
                  "  Arena 2 (Steel  #9AAABB) — Phase 2: Master_v1 correct mechanics, aim still broken");
    }

    // Two-line label with a subtitle beneath — used by Legacy Showcase.
    private static void AddTitleSubtitleLabel(GameObject arenaGO,
                                              string title, Color titleColor,
                                              string subtitle, Color subtitleColor,
                                              float yTop)
    {
        MakeTMPWorldLabel(arenaGO, "Label_title",    title.ToUpper(), FontTitle, 2.8f, titleColor,
                          new Vector3(0f, yTop + 2.0f, 0f));
        MakeTMPWorldLabel(arenaGO, "Label_subtitle", subtitle,        FontBody,  1.4f, subtitleColor,
                          new Vector3(0f, yTop - 0.5f, 0f));
    }

    // Two-line label: big tier name (colored, Rajdhani), small stats line (Inter, muted).
    private static void AddDifficultyLabel(GameObject arenaGO, string tierName, Color tierColor,
                                           string statsLine, float yTop)
    {
        MakeTMPWorldLabel(arenaGO, "Label_tier",  tierName.ToUpper(), FontTitle, 2.8f, tierColor,
                          new Vector3(0f, yTop + 2.0f, 0f));
        MakeTMPWorldLabel(arenaGO, "Label_stats", statsLine,          FontBody,  1.2f,
                          new Color(0.604f, 0.580f, 0.518f),  // Text Secondary #9A9484
                          new Vector3(0f, yTop - 0.5f, 0f));
    }

    // -----------------------------------------------------------------------
    // 10. ContentKit Scene Setup — Global Volume (Bloom + Vignette)
    //     Run this in any showcase scene after building it.
    //     Idempotent: removes the existing GlobalVolume GO first.
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/10. Apply ContentKit Scene Setup")]
    public static void ApplyContentKitSceneSetup()
    {
        // Remove any existing volume so this is idempotent.
        var existing = GameObject.Find("GlobalVolume");
        if (existing != null) Object.DestroyImmediate(existing);

        // Create the Global Volume GameObject.
        var volumeGO = new GameObject("GlobalVolume");
        var volume   = volumeGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.weight   = 1f;

        // Create or load the VolumeProfile asset.
        const string PROFILE_PATH = "Assets/Settings/ShowcaseVolumeProfile.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PROFILE_PATH);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PROFILE_PATH);
        }

        // Bloom — subtle glow on Amber Bright elements.
        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.threshold.Override(0.85f);  // only very bright pixels bloom
        bloom.intensity.Override(0.35f);  // gentle — not HDR-heavy
        bloom.scatter.Override(0.70f);    // diffuse spread
        bloom.tint.Override(new Color(1f, 0.92f, 0.75f));  // warm Amber tint on bloom

        // Vignette — subtle corner darkening focuses eye on the arena.
        if (!profile.TryGet<Vignette>(out var vignette))
            vignette = profile.Add<Vignette>(true);
        vignette.active = true;
        vignette.color.Override(Color.black);
        vignette.intensity.Override(0.25f);
        vignette.smoothness.Override(0.5f);
        vignette.rounded.Override(true);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volumeGO);

        // Remove any scene skybox — in URP the Lighting skybox overrides camera.clearFlags at
        // runtime even when clearFlags = SolidColor, causing Play mode to show the default blue.
        // Nulling it forces URP to fall back to the camera's solid background colour.
        // Null out the scene skybox so URP falls back to camera.backgroundColor at runtime.
        RenderSettings.skybox = null;

        // URP requires renderPostProcessing = true on the camera's UniversalAdditionalCameraData.
        // Without this the volume exists but URP ignores all post-processing effects at runtime.
        // Also lock the background to ContentKit Void so Play mode matches the Scene view.
        var cam = Camera.main;
        if (cam != null)
        {
            // Set editor-side values (Scene view / baked lighting).
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.067f, 0.063f, 0.035f);  // ContentKit Void #111009

            // URP 2D renderer can ignore clearFlags/backgroundColor set in the Editor at Play-mode
            // start. ContentKitCamera enforces them in Awake so Play mode always matches.
            if (cam.gameObject.GetComponent<ContentKitCamera>() == null)
                cam.gameObject.AddComponent<ContentKitCamera>();

            var camData = cam.GetComponent<UniversalAdditionalCameraData>()
                       ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            EditorUtility.SetDirty(cam.gameObject);
            Debug.Log("[PushmanSetup] ContentKit Void background + post-processing enabled on Main Camera.");
        }
        else
        {
            Debug.LogWarning("[PushmanSetup] No Main Camera found — set background colour and 'Post Processing' manually.");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] GlobalVolume applied (Bloom threshold=0.85 / intensity=0.35, Vignette=0.25). " +
                  "Re-run in each showcase scene after rebuilding.");
    }

    // -----------------------------------------------------------------------
    // 11. Game View resolution presets for recording
    //     Uses reflection against Unity's internal GameViewSizes API.
    //     Both add a custom fixed-resolution entry if it doesn't exist, then
    //     select it — idempotent on repeated calls.
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/11a. Set Game View — 4K (3840×2160)")]
    public static void SetGameView4K()    => SetGameViewResolution(3840, 2160, "4K UHD");

    [MenuItem("Pushman/11b. Set Game View — 1080p (1920×1080)")]
    public static void SetGameView1080p() => SetGameViewResolution(1920, 1080, "1080p HD");

    private static void SetGameViewResolution(int width, int height, string label)
    {
        var T = System.Type.GetType("UnityEditor.GameViewSizes,UnityEditor");
        if (T == null) { Debug.LogError("[PushmanSetup] GameViewSizes type not found."); return; }

        // 'instance' may be on the type or its ScriptableSingleton<T> base.
        var instanceProp = T.GetProperty("instance", BindingFlags.Static | BindingFlags.Public)
                        ?? T.BaseType?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
        if (instanceProp == null) { Debug.LogError("[PushmanSetup] GameViewSizes.instance not found."); return; }
        var sizes = instanceProp.GetValue(null);
        if (sizes == null) { Debug.LogError("[PushmanSetup] GameViewSizes instance is null."); return; }

        // GameViewSizeGroup methods are internal — must include NonPublic in binding flags.
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var getGroup = T.GetMethod("GetGroup", BF);
        if (getGroup == null) { Debug.LogError("[PushmanSetup] GetGroup method not found."); return; }
        var group = getGroup.Invoke(sizes, new object[] { (int)GameViewSizeGroupType.Standalone });
        if (group == null) { Debug.LogError("[PushmanSetup] GetGroup returned null."); return; }
        var gType = group.GetType();

        var getBuiltinCount  = gType.GetMethod("GetBuiltinCount",  BF);
        var getCustomCount   = gType.GetMethod("GetCustomCount",   BF);
        var getGameViewSize  = gType.GetMethod("GetGameViewSize",  BF);  // Unity 6: replaced GetCustom
        var addCustomSize    = gType.GetMethod("AddCustomSize",    BF);

        if (getBuiltinCount == null || getCustomCount == null || getGameViewSize == null)
        {
            var methods = string.Join(", ", System.Array.ConvertAll(gType.GetMethods(BF), m => m.Name));
            Debug.LogError($"[PushmanSetup] GameViewSizeGroup methods not found. Available: {methods}");
            return;
        }

        int builtinCount = (int)getBuiltinCount.Invoke(group, null);
        int customCount  = (int)getCustomCount.Invoke(group, null);

        // Unity 6: GetGameViewSize(totalIndex) replaces GetCustom(customIndex).
        // Total index = builtin count + custom offset.
        int targetIndex = -1;
        for (int i = 0; i < customCount; i++)
        {
            var s = getGameViewSize.Invoke(group, new object[] { builtinCount + i });
            if (s == null) continue;
            var wProp = s.GetType().GetProperty("width",  BF);
            var hProp = s.GetType().GetProperty("height", BF);
            if (wProp == null || hProp == null) continue;
            int w = (int)wProp.GetValue(s);
            int h = (int)hProp.GetValue(s);
            if (w == width && h == height) { targetIndex = builtinCount + i; break; }
        }

        if (targetIndex < 0)
        {
            var sizeT    = System.Type.GetType("UnityEditor.GameViewSize,UnityEditor");
            var sizeEnum = System.Type.GetType("UnityEditor.GameViewSizeType,UnityEditor");
            if (sizeT == null || sizeEnum == null) { Debug.LogError("[PushmanSetup] GameViewSize types not found."); return; }
            var fixedRes = System.Enum.Parse(sizeEnum, "FixedResolution");
            var ctor = sizeT.GetConstructor(new System.Type[] { sizeEnum, typeof(int), typeof(int), typeof(string) });
            if (ctor == null) { Debug.LogError("[PushmanSetup] GameViewSize constructor not found."); return; }
            if (addCustomSize == null) { Debug.LogError("[PushmanSetup] AddCustomSize not found."); return; }
            addCustomSize.Invoke(group, new object[] { ctor.Invoke(new object[] { fixedRes, width, height, label }) });
            customCount = (int)getCustomCount.Invoke(group, null);
            targetIndex = builtinCount + customCount - 1;
        }

        var gvT = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (gvT == null) { Debug.LogError("[PushmanSetup] GameView type not found."); return; }

        // Do NOT call GetWindow() inside a MenuItem — it triggers an assertion.
        // Find the existing Game view via FindObjectsOfTypeAll instead.
        var existing = Resources.FindObjectsOfTypeAll(gvT);
        if (existing == null || existing.Length == 0)
        {
            Debug.LogWarning("[PushmanSetup] Game view not open. Open the Game tab first, then re-run.");
            return;
        }
        var gv = (EditorWindow)existing[0];

        // Defer the actual selection one frame so we're outside the MenuItem call stack.
        int idx = targetIndex;
        EditorApplication.delayCall += () =>
        {
            var prop = gvT.GetProperty("selectedSizeIndex",
                           BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                prop.SetValue(gv, idx);
            }
            else
            {
                var cb = gvT.GetMethod("SizeSelectionCallback",
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cb != null) cb.Invoke(gv, new object[] { idx, null });
                else Debug.LogError("[PushmanSetup] Cannot set size — selectedSizeIndex and SizeSelectionCallback both missing.");
            }
            gv.Repaint();
            Debug.Log($"[PushmanSetup] Game view → {width}×{height} ({label}).");
        };
    }

    // -----------------------------------------------------------------------
    // 5. Bot vs Bot Scene — both players run ONNX inference, no human input
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/5. Bot vs Bot Scene")]
    public static void BuildBotVsBotScene()
    {
        // Find the most recently modified ONNX in Assets/MLModels/
        // Prefer the shared v2 model; fall back to newest if not found.
        const string MODELS_FOLDER = "Assets/MLModels";
        const string DEFAULT_ONNX  = MODELS_FOLDER + "/Pushman_Shared/PushmanAgent_v2.onnx";

        string onnxPath = DEFAULT_ONNX;
        var allOnnx = AssetDatabase.FindAssets("t:Object", new[] { MODELS_FOLDER });
        string newestPath = null;
        System.DateTime newestTime = System.DateTime.MinValue;
        foreach (var guid in allOnnx)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith(".onnx")) continue;
            System.DateTime t = File.GetLastWriteTime(p);
            if (t > newestTime) { newestTime = t; newestPath = p; }
        }
        if (newestPath != null) onnxPath = newestPath;

        var model = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(onnxPath);
        if (model == null)
            Debug.LogWarning($"[PushmanSetup] No ONNX found at {onnxPath}. " +
                             "Build the scene anyway — assign the model manually in BehaviorParameters.");
        else
            Debug.Log($"[PushmanSetup] Using model: {onnxPath}");

        EnsureFolders();
        var stats       = CreateOrLoad<CharacterStats>("Assets/ScriptableObjects/Characters/DefaultStats.asset");
        InitCharacterStats(stats);
        var profileA    = CreateOrLoad<ObservationProfile>("Assets/ScriptableObjects/Observation/Profile_A.asset");
        var personality = CreateOrLoad<BotPersonality>("Assets/ScriptableObjects/Personalities/Aggressive.asset");
        AssetDatabase.SaveAssets();

        int playerLayer = GetOrAddLayer("Player");
        CleanupObject("Arena");

        GameObject arena  = new GameObject("Arena");
        GameObject center = CreateChild("ArenaCenter", arena);
        center.transform.localPosition = Vector3.zero;

        var spawnPositions = new Vector3[] {
            new Vector3(-4f,  0f, 0f),
            new Vector3( 4f,  0f, 0f),
            new Vector3( 0f,  4f, 0f),
            new Vector3( 0f, -4f, 0f),
        };
        var spawnGOs = new List<GameObject>();
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            var sp = CreateChild($"SpawnPoint_{i + 1}", arena);
            sp.transform.localPosition = spawnPositions[i];
            spawnGOs.Add(sp);
        }

        // Both players — RLAgentBrain, InferenceOnly
        GameObject p1GO = BuildBotPlayer("Player1", stats, playerLayer, arena,
                                         spawnPositions[0], profileA, personality, model);
        GameObject p2GO = BuildBotPlayer("Player2", stats, playerLayer, arena,
                                         spawnPositions[1], profileA, personality, model);
        Player p1 = p1GO.GetComponent<Player>();
        Player p2 = p2GO.GetComponent<Player>();

        int oppMask = 1 << playerLayer;
        p1.opponentLayer = oppMask;
        p2.opponentLayer = oppMask;

        ArenaManager am = arena.AddComponent<ArenaManager>();
        am.arenaCenter   = center.transform;
        am.ringOutRadius = 10f;
        am.allPlayers    = new List<Player> { p1, p2 };
        am.spawnPoints   = new List<Transform>();
        foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);

        // Wire each brain's arena + opponent references
        foreach (var rl in arena.GetComponentsInChildren<RLAgentBrain>())
        {
            rl.arenaManager = am;
            Player self = rl.GetComponent<Player>();
            rl.opponents = self == p1 ? new Player[] { p2 } : new Player[] { p1 };
            EditorUtility.SetDirty(rl);
        }

        var cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic       = true;
            cam.orthographicSize   = 12f;
            cam.clearFlags         = CameraClearFlags.SolidColor;
            cam.backgroundColor    = new Color(0.067f, 0.063f, 0.035f);  // ContentKit Void #111009
            cam.transform.position = new Vector3(0f, 0f, -10f);
            EditorUtility.SetDirty(cam);
        }

        EditorUtility.SetDirty(am);
        EditorUtility.SetDirty(p1);
        EditorUtility.SetDirty(p2);

        // Sprites + HUD (reuse existing step 4)
        AddSpritesToPlayers();

        // Save to a dedicated scene file and add to Build Settings
        const string BOT_VS_BOT_SCENE_PATH = "Assets/Scenes/BotVsBot.unity";
        EnsureFolders();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), BOT_VS_BOT_SCENE_PATH);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == BOT_VS_BOT_SCENE_PATH))
            scenes.Add(new EditorBuildSettingsScene(BOT_VS_BOT_SCENE_PATH, false));
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log("[PushmanSetup] Bot vs Bot scene saved → Assets/Scenes/BotVsBot.unity. " +
                  "Press Play to watch. To swap models: select a player → BehaviorParameters → Model.");
    }

    // Builds a player GO fully wired for RL inference.
    private static GameObject BuildBotPlayer(string playerName, CharacterStats stats,
                                              int layer, GameObject arena, Vector3 spawnPos,
                                              ObservationProfile profile, BotPersonality personality,
                                              UnityEngine.Object onnxModel)
    {
        GameObject go = BuildPlayerGO(playerName, stats, layer, arena);
        go.transform.localPosition = spawnPos;

        var rl = go.AddComponent<RLAgentBrain>();
        rl.MaxStep            = 5000;
        rl.observationProfile = profile;
        rl.personality        = personality;

        ConfigureBehaviorParameters(go);
        var bp = go.GetComponent<BehaviorParameters>();
        bp.BehaviorType = BehaviorType.InferenceOnly;
        // Override VectorObservationSize — ConfigureBehaviorParameters always loads Profile_A (33 dims)
        // but legacy models need 13 or 18 dims depending on when they were trained.
        bp.BrainParameters.VectorObservationSize = profile != null ? profile.ComputeSpaceSize(1) : OBS_SIZE;

        // Assign ONNX model. ModelAsset comes from Unity.InferenceEngine (Unity 6's renamed Sentis).
        // The public Model setter handles UpdateAgentPolicy() correctly; SerializedObject fallback
        // covers older ML-Agents versions that may not have exposed the property.
        if (onnxModel != null)
        {
            var modelAsset = onnxModel as ModelAsset;
            if (modelAsset != null)
            {
                bp.Model = modelAsset;
            }
            else
            {
                var so = new SerializedObject(bp);
                var modelProp = so.FindProperty("m_Model");
                if (modelProp != null)
                {
                    modelProp.objectReferenceValue = onnxModel;
                    so.ApplyModifiedProperties();
                }
            }
            if (bp.Model == null)
                Debug.LogError($"[BuildBotPlayer] Failed to wire model onto {playerName}.");
            EditorUtility.SetDirty(bp);
        }

        var dr = go.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = 5;

        EditorUtility.SetDirty(go);
        return go;
    }

    // -----------------------------------------------------------------------
    // 2. Save Prefabs
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/2. Save Prefabs")]
    public static void SavePrefabs()
    {
        EnsureFolders();

        GameObject arena = GameObject.Find("Arena");
        if (arena == null)
        {
            Debug.LogError("[PushmanSetup] No 'Arena' object found. Run '1. Setup Test Scene' first.");
            return;
        }

        // Temporarily detach players — save Arena prefab without player scene objects,
        // because the training scene will instantiate its own player instances.
        PrefabUtility.SaveAsPrefabAsset(arena, ARENA_PREFAB_PATH);
        Debug.Log($"[PushmanSetup] Saved Arena prefab → {ARENA_PREFAB_PATH}");

        // Save Player_RL prefab from Player2 (the RL-configured player).
        GameObject p2GO = GameObject.Find("Arena/Player2");
        if (p2GO != null)
        {
            PrefabUtility.SaveAsPrefabAsset(p2GO, PLAYER_RL_PREFAB_PATH);
            Debug.Log($"[PushmanSetup] Saved Player_RL prefab → {PLAYER_RL_PREFAB_PATH}");
        }
        else
        {
            Debug.LogWarning("[PushmanSetup] Could not find Arena/Player2 — Player_RL prefab not saved.");
        }

        AssetDatabase.Refresh();
        Debug.Log("[PushmanSetup] Prefabs saved.");
    }

    // -----------------------------------------------------------------------
    // 3. Build Training Scene
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/3. Build Training Scene")]
    public static void BuildTrainingScene()
    {
        var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ARENA_PREFAB_PATH);
        if (arenaPrefab == null)
        {
            Debug.LogError("[PushmanSetup] Arena prefab not found. Run '2. Save Prefabs' first.");
            return;
        }

        var profileA    = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
                            "Assets/ScriptableObjects/Observation/Profile_A.asset");
        var personality = AssetDatabase.LoadAssetAtPath<BotPersonality>(
                            "Assets/ScriptableObjects/Personalities/Aggressive.asset");

        var trainingScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        int total = GRID_SIZE * GRID_SIZE;
        int arenaIndex = 0;

        for (int row = 0; row < GRID_SIZE; row++)
        {
            for (int col = 0; col < GRID_SIZE; col++)
            {
                Vector3 offset = new Vector3(col * ARENA_SPACING, row * ARENA_SPACING, 0f);
                GameObject arenaGO = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
                arenaGO.name = $"Arena_{arenaIndex}";
                arenaGO.transform.position = offset;

                // Wire RL brain references so each arena is self-contained
                ArenaManager am = arenaGO.GetComponent<ArenaManager>();
                if (am != null)
                {
                    WireArena(arenaGO, am, profileA, personality);
                }

                arenaIndex++;
                EditorUtility.DisplayProgressBar("Building Training Scene",
                    $"Arena {arenaIndex}/{total}", (float)arenaIndex / total);
            }
        }

        EditorUtility.ClearProgressBar();
        EnsureFolders();
        EditorSceneManager.SaveScene(trainingScene, TRAINING_SCENE_PATH);

        // Add to Build Settings
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
        {
            new EditorBuildSettingsScene(TRAINING_SCENE_PATH, true)
        };
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log($"[PushmanSetup] Training scene built: {GRID_SIZE}×{GRID_SIZE} = {total} arenas " +
                  $"({ARENA_SPACING}u spacing). Saved → {TRAINING_SCENE_PATH}");
        Debug.Log("[PushmanSetup] Note: arenas are isolated by distance — players ring out before " +
                  "they can reach a neighbouring arena (ringOutRadius=10, spacing=25).");
    }

    // -----------------------------------------------------------------------
    // 3b. Build Small Training Scene — 4×4 = 16 arenas for fast/dev runs
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/3b. Build Small Training Scene (4x4, fast)")]
    public static void BuildSmallTrainingScene()
    {
        var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ARENA_PREFAB_PATH);
        if (arenaPrefab == null)
        {
            Debug.LogError("[PushmanSetup] Arena prefab not found. Run '2. Save Prefabs' first.");
            return;
        }

        var profileA    = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
                            "Assets/ScriptableObjects/Observation/Profile_A.asset");
        var personality = AssetDatabase.LoadAssetAtPath<BotPersonality>(
                            "Assets/ScriptableObjects/Personalities/Aggressive.asset");

        var trainingScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        int total = GRID_SIZE_SMALL * GRID_SIZE_SMALL;
        int arenaIndex = 0;

        for (int row = 0; row < GRID_SIZE_SMALL; row++)
        {
            for (int col = 0; col < GRID_SIZE_SMALL; col++)
            {
                Vector3 offset = new Vector3(col * ARENA_SPACING, row * ARENA_SPACING, 0f);
                GameObject arenaGO = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
                arenaGO.name = $"Arena_{arenaIndex}";
                arenaGO.transform.position = offset;

                ArenaManager am = arenaGO.GetComponent<ArenaManager>();
                if (am != null)
                    WireArena(arenaGO, am, profileA, personality);

                arenaIndex++;
            }
        }

        EnsureFolders();
        EditorSceneManager.SaveScene(trainingScene, TRAINING_SCENE_SMALL_PATH);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == TRAINING_SCENE_SMALL_PATH))
            scenes.Add(new EditorBuildSettingsScene(TRAINING_SCENE_SMALL_PATH, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log($"[PushmanSetup] Small training scene built: {GRID_SIZE_SMALL}×{GRID_SIZE_SMALL} = {total} arenas. " +
                  $"Saved → {TRAINING_SCENE_SMALL_PATH}");
    }

    // -----------------------------------------------------------------------
    // 3c. Build Mixed-Opponent Training Scene — 6×6 = 36 arenas
    //     Player1 = master RLAgentBrain; Player2 rotates through 3 scripted bots
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/3c. Build Mixed-Opponent Training Scene (6x6)")]
    public static void BuildMixedTrainingScene()
    {
        var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ARENA_PREFAB_PATH);
        if (arenaPrefab == null)
        {
            Debug.LogError("[PushmanSetup] Arena prefab not found. Run '2. Save Prefabs' first.");
            return;
        }

        var profileA    = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
                            "Assets/ScriptableObjects/Observation/Profile_A.asset");
        var personality = AssetDatabase.LoadAssetAtPath<BotPersonality>(
                            "Assets/ScriptableObjects/Personalities/Aggressive.asset");
        var defaultStats    = AssetDatabase.LoadAssetAtPath<CharacterStats>(
                            "Assets/ScriptableObjects/Characters/DefaultStats.asset");
        var heavyStats      = AssetDatabase.LoadAssetAtPath<CharacterStats>(
                            "Assets/ScriptableObjects/Characters/Heavyweight.asset");
        var speedsterStats  = AssetDatabase.LoadAssetAtPath<CharacterStats>(
                            "Assets/ScriptableObjects/Characters/Speedster.asset");

        var trainingScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        int total = GRID_SIZE_MIXED * GRID_SIZE_MIXED;
        int arenaIndex = 0;

        for (int row = 0; row < GRID_SIZE_MIXED; row++)
        {
            for (int col = 0; col < GRID_SIZE_MIXED; col++)
            {
                Vector3 offset = new Vector3(col * ARENA_SPACING, row * ARENA_SPACING, 0f);
                GameObject arenaGO = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
                arenaGO.name = $"Arena_{arenaIndex}";
                arenaGO.transform.position = offset;

                ArenaManager am = arenaGO.GetComponent<ArenaManager>();
                if (am != null)
                {
                    // Populate statsPool so stats randomise every round.
                    am.statsPool = new List<CharacterStats>();
                    if (defaultStats   != null) am.statsPool.Add(defaultStats);
                    if (heavyStats     != null) am.statsPool.Add(heavyStats);
                    if (speedsterStats != null) am.statsPool.Add(speedsterStats);

                    WireMixedArena(arenaGO, am, profileA, personality, arenaIndex);
                }

                arenaIndex++;
                EditorUtility.DisplayProgressBar("Building Mixed Training Scene",
                    $"Arena {arenaIndex}/{total}", (float)arenaIndex / total);
            }
        }

        EditorUtility.ClearProgressBar();
        EnsureFolders();
        EditorSceneManager.SaveScene(trainingScene, TRAINING_SCENE_MIXED_PATH);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == TRAINING_SCENE_MIXED_PATH))
            scenes.Add(new EditorBuildSettingsScene(TRAINING_SCENE_MIXED_PATH, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log($"[PushmanSetup] Mixed training scene: {GRID_SIZE_MIXED}×{GRID_SIZE_MIXED} = {total} arenas " +
                  $"(12× ChaseBotBrain | 12× StandingBotBrain | 12× DodgingBotBrain). " +
                  $"Saved → {TRAINING_SCENE_MIXED_PATH}");
    }

    // -----------------------------------------------------------------------
    // 3e. Build Round-Robin Training Scene — 15 pairings × 3 arenas = 45 total
    //     All 5 personalities train simultaneously against each other.
    //     Pairings: 5 mirrors (self vs self) + 10 cross (C(5,2)).
    //     Grid: 9 columns × 5 rows. pairing = arenaIndex % 15.
    //     IMPORTANT: makes round-robin scene the ONLY enabled Build Settings scene.
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/3e. Build Round-Robin Training Scene (15 pairings x3)")]
    public static void BuildRoundRobinTrainingScene()
    {
        var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ARENA_PREFAB_PATH);
        if (arenaPrefab == null)
        {
            Debug.LogError("[PushmanSetup] Arena prefab not found. Run '2. Save Prefabs' first.");
            return;
        }

        // Phase 3.5 uses Profile_C (noise + delay + humanization obs, 24 dims).
        // Switch back to Profile_A here if rebuilding for a clean v2-style run.
        var profileC = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
            "Assets/ScriptableObjects/Observation/Profile_C.asset");
        if (profileC == null)
            Debug.LogWarning("[PushmanSetup] Profile_C not found — falling back to Profile_A.");
        var profile = profileC ?? AssetDatabase.LoadAssetAtPath<ObservationProfile>(
            "Assets/ScriptableObjects/Observation/Profile_A.asset");

        string[] personalityNames = { "Aggressive", "Defensive", "Evasive", "Balanced", "Counter" };
        var personalities = new BotPersonality[5];
        for (int i = 0; i < 5; i++)
        {
            personalities[i] = AssetDatabase.LoadAssetAtPath<BotPersonality>(
                $"Assets/ScriptableObjects/Personalities/{personalityNames[i]}.asset");
            if (personalities[i] == null)
                Debug.LogWarning($"[PushmanSetup] Personality asset not found: {personalityNames[i]}.asset");
        }

        var defaultStats   = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/DefaultStats.asset");
        var heavyStats     = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/Heavyweight.asset");
        var speedsterStats = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/Speedster.asset");

        // Build pairing table: 5 mirrors + 10 cross pairings = 15 total
        var pairings = new List<(int a, int b)>();
        for (int i = 0; i < 5; i++) pairings.Add((i, i));                           // mirrors
        for (int i = 0; i < 5; i++) for (int j = i + 1; j < 5; j++) pairings.Add((i, j)); // cross

        const int TOTAL_ARENAS = 45;    // 15 pairings × 3
        const int GRID_COLS    = 9;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        for (int idx = 0; idx < TOTAL_ARENAS; idx++)
        {
            int col = idx % GRID_COLS;
            int row = idx / GRID_COLS;
            Vector3 offset = new Vector3(col * ARENA_SPACING, row * ARENA_SPACING, 0f);

            GameObject arenaGO = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
            arenaGO.name = $"Arena_{idx}";
            arenaGO.transform.position = offset;

            ArenaManager am = arenaGO.GetComponent<ArenaManager>();
            if (am != null)
            {
                am.statsPool = new List<CharacterStats>();
                if (defaultStats   != null) am.statsPool.Add(defaultStats);
                if (heavyStats     != null) am.statsPool.Add(heavyStats);
                if (speedsterStats != null) am.statsPool.Add(speedsterStats);

                var (pA, pB) = pairings[idx % 15];
                WireRoundRobinArena(arenaGO, am, profile, personalities, personalityNames, pA, pB);
            }

            EditorUtility.DisplayProgressBar("Building Round-Robin Training Scene",
                $"Arena {idx + 1}/{TOTAL_ARENAS}", (float)(idx + 1) / TOTAL_ARENAS);
        }

        EditorUtility.ClearProgressBar();
        EnsureFolders();
        EditorSceneManager.SaveScene(scene, TRAINING_SCENE_ROUNDROBIN_PATH);

        // Make round-robin the ONLY enabled scene in Build Settings so the standalone
        // build doesn't silently train the wrong scene.
        var existingScenes = EditorBuildSettings.scenes;
        var newScenes = new List<EditorBuildSettingsScene>();
        bool found = false;
        foreach (var s in existingScenes)
        {
            bool isRR = s.path == TRAINING_SCENE_ROUNDROBIN_PATH;
            newScenes.Add(new EditorBuildSettingsScene(s.path, isRR));
            if (isRR) found = true;
        }
        if (!found)
            newScenes.Insert(0, new EditorBuildSettingsScene(TRAINING_SCENE_ROUNDROBIN_PATH, true));
        EditorBuildSettings.scenes = newScenes.ToArray();

        AssetDatabase.Refresh();

        // Coverage summary
        var coverage = new int[5, 5];
        for (int idx = 0; idx < TOTAL_ARENAS; idx++)
        {
            var (pA, pB) = pairings[idx % 15];
            coverage[pA, pB]++;
        }
        string coverageLog = "[PushmanSetup] Pairing arena counts:";
        for (int i = 0; i < 5; i++)
            for (int j = i; j < 5; j++)
                coverageLog += $"\n  {personalityNames[i]} vs {personalityNames[j]}: {coverage[i, j]}";
        Debug.Log(coverageLog);

        Debug.Log($"[PushmanSetup] Round-Robin scene: 45 arenas (9×5), 15 pairings × 3. " +
                  $"Saved → {TRAINING_SCENE_ROUNDROBIN_PATH}. " +
                  $"Round-robin is now the only ENABLED Build Settings scene — rebuild standalone before training.");
    }

    // Wire a round-robin arena: both players are RLAgentBrain, each assigned a pairing personality
    // and a behavior name matching the ppo_roundrobin.yaml behaviors: block.
    private static void WireRoundRobinArena(GameObject arenaGO, ArenaManager am,
                                             ObservationProfile profile,
                                             BotPersonality[] personalities,
                                             string[] personalityNames,
                                             int pA, int pB)
    {
        am.allPlayers  = new List<Player>(arenaGO.GetComponentsInChildren<Player>());
        am.spawnPoints = new List<Transform>();
        am.arenaCenter = arenaGO.transform.Find("ArenaCenter");

        for (int i = 1; i <= 4; i++)
        {
            Transform sp = arenaGO.transform.Find($"SpawnPoint_{i}");
            if (sp != null) am.spawnPoints.Add(sp);
        }

        var p1GO = arenaGO.transform.Find("Player1")?.gameObject;
        var p2GO = arenaGO.transform.Find("Player2")?.gameObject;
        if (p1GO == null || p2GO == null)
        {
            Debug.LogWarning($"[PushmanSetup] {arenaGO.name}: Player1 or Player2 missing — skipping.");
            return;
        }

        WireRLPlayerForRR(p1GO, p2GO, am, profile, personalities[pA], personalityNames[pA]);
        WireRLPlayerForRR(p2GO, p1GO, am, profile, personalities[pB], personalityNames[pB]);

        // Both players on team 0 — required for PPO round-robin training without self-play.
        // When self-play is active (v37/v38), set Player2 TeamId = 1 manually or re-add here.
        // With different TeamIds and no self_play: block, PPO silently skips team1 gradient updates.
        var bp1 = p1GO.GetComponent<BehaviorParameters>();
        var bp2 = p2GO.GetComponent<BehaviorParameters>();
        if (bp1 != null) { bp1.TeamId = 0; EditorUtility.SetDirty(bp1); }
        if (bp2 != null) { bp2.TeamId = 0; EditorUtility.SetDirty(bp2); }

        EditorUtility.SetDirty(am);
        EditorUtility.SetDirty(arenaGO);
    }

    private static void WireRLPlayerForRR(GameObject playerGO, GameObject opponentGO,
                                           ArenaManager am, ObservationProfile profile,
                                           BotPersonality personality, string behaviorName)
    {
        var rl = playerGO.GetComponent<RLAgentBrain>();
        if (rl == null) { Debug.LogWarning($"[PushmanSetup] {playerGO.name} has no RLAgentBrain."); return; }

        rl.arenaManager       = am;
        rl.observationProfile = profile;
        rl.personality        = personality;
        rl.opponents          = new Player[] { opponentGO.GetComponent<Player>() };

        // Override serialized prefab values with updated mechanics — prefab may have old defaults.
        var playerComp = playerGO.GetComponent<Player>();
        if (playerComp != null) { playerComp.pushHitRadius = 0.8f; EditorUtility.SetDirty(playerComp); }

        var bp = playerGO.GetComponent<BehaviorParameters>();
        if (bp == null) bp = playerGO.AddComponent<BehaviorParameters>();
        // Phase 3 v2 shared network: ALL agents use the same BehaviorName so experience
        // flows into ONE network. Personality differentiation comes from the per-agent
        // BotPersonality SO (rewards) + the self-personality observation.
        // `behaviorName` arg is kept for API compatibility but ignored here.
        bp.BehaviorName = SHARED_BEHAVIOR_NAME;
        bp.BehaviorType = BehaviorType.Default;

        int obsSize = profile != null ? profile.ComputeSpaceSize(1) : OBS_SIZE;
        bp.BrainParameters.VectorObservationSize          = obsSize;
        bp.BrainParameters.NumStackedVectorObservations   = 1;
        bp.BrainParameters.ActionSpec                     = ActionSpec.MakeDiscrete(3, 3, 3, 4);

        EditorUtility.SetDirty(rl);
        EditorUtility.SetDirty(bp);
    }

    // Wire a mixed arena: Player1 stays as RLAgentBrain; Player2 is replaced by a scripted bot
    // rotating through Chase → Standing → Dodging based on arenaIndex % 3.
    private static void WireMixedArena(GameObject arenaGO, ArenaManager am,
                                       ObservationProfile profile, BotPersonality personality,
                                       int arenaIndex)
    {
        am.allPlayers  = new List<Player>(arenaGO.GetComponentsInChildren<Player>());
        am.spawnPoints = new List<Transform>();
        am.arenaCenter = arenaGO.transform.Find("ArenaCenter");

        for (int i = 1; i <= 4; i++)
        {
            Transform sp = arenaGO.transform.Find($"SpawnPoint_{i}");
            if (sp != null) am.spawnPoints.Add(sp);
        }

        var p1GO = arenaGO.transform.Find("Player1")?.gameObject;
        var p2GO = arenaGO.transform.Find("Player2")?.gameObject;

        if (p1GO == null || p2GO == null)
        {
            Debug.LogWarning($"[PushmanSetup] Arena_{arenaIndex}: Player1 or Player2 not found — skipping.");
            return;
        }

        // Wire Player1 as the master RL agent.
        var rl1 = p1GO.GetComponent<RLAgentBrain>();
        if (rl1 != null)
        {
            rl1.arenaManager       = am;
            rl1.observationProfile = profile;
            rl1.personality        = personality;
            rl1.opponents          = new Player[] { p2GO.GetComponent<Player>() };
            EditorUtility.SetDirty(rl1);
        }

        // Strip Player2's ML-Agents components (order matters: dependents first).
        var dr2 = p2GO.GetComponent<DecisionRequester>();
        if (dr2 != null) Object.DestroyImmediate(dr2);
        var rl2 = p2GO.GetComponent<RLAgentBrain>();
        if (rl2 != null) Object.DestroyImmediate(rl2);
        var bp2 = p2GO.GetComponent<BehaviorParameters>();
        if (bp2 != null) Object.DestroyImmediate(bp2);

        // Add the scripted brain — cycles 0→Chase, 1→Standing, 2→Dodging.
        switch (arenaIndex % 3)
        {
            case 0: p2GO.AddComponent<ChaseBotBrain>();    break;
            case 1: p2GO.AddComponent<StandingBotBrain>(); break;
            case 2: p2GO.AddComponent<DodgingBotBrain>();  break;
        }

        EditorUtility.SetDirty(am);
        EditorUtility.SetDirty(arenaGO);
    }

    // -----------------------------------------------------------------------
    // 4. Add UI to Player Prefab
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // 4. Add Sprites to Players  (circle body + hand rects + arena ring)
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/4. Add Sprites to Players")]
    public static void AddSpritesToPlayers()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Sprites"))
            AssetDatabase.CreateFolder("Assets", "Sprites");

        // --- Create sprite assets ---
        Sprite circleSprite  = GetOrCreateSprite("Assets/Sprites/PlayerCircle.png",
                                   () => MakeCircleTexture(128));
        Sprite pushSprite    = GetOrCreateSprite("Assets/Sprites/PushHand.png",
                                   () => MakeRectTexture(24, 16));
        Sprite blockSprite   = GetOrCreateSprite("Assets/Sprites/BlockShield.png",
                                   () => MakeRectTexture(48, 10));
        if (circleSprite == null || pushSprite == null || blockSprite == null)
        {
            Debug.LogError("[PushmanSetup] Failed to create one or more sprite assets.");
            return;
        }

        // --- Wire up Player1 (Mauve Bright) ---
        GameObject p1GO = GameObject.Find("Arena/Player1");
        if (p1GO != null)
            ApplyPlayerSprites(p1GO, circleSprite, pushSprite, blockSprite,
                               new Color(0.722f, 0.596f, 0.800f));  // Mauve Bright #B898CC

        // --- Wire up Player2 (Terra Bright) ---
        GameObject p2GO = GameObject.Find("Arena/Player2");
        if (p2GO != null)
            ApplyPlayerSprites(p2GO, circleSprite, pushSprite, blockSprite,
                               new Color(0.863f, 0.596f, 0.471f));  // Terra Bright #DC9878

        // --- Screen-space stamina HUD (replaces old world-space bars) ---
        if (p1GO != null || p2GO != null) CreateStaminaHUD(p1GO, p2GO);

        // --- Arena boundary ring ---
        GameObject arenaGO = GameObject.Find("Arena");
        if (arenaGO != null)
            ApplyArenaBoundary(arenaGO, ringOutRadius: 10f);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] Sprites + screen-space HUD applied. Green=P1 (bottom-left), Red=P2 (bottom-right).");
    }

    // Assign body circle, create hand children, wire PlayerVisuals.
    private static void ApplyPlayerSprites(GameObject playerGO, Sprite body,
                                           Sprite pushSpr, Sprite blockSpr, Color tint)
    {
        // Body
        var sr = playerGO.GetComponent<SpriteRenderer>();
        if (sr == null) sr = playerGO.AddComponent<SpriteRenderer>();
        sr.sprite       = body;
        sr.color        = tint;
        sr.sortingOrder = 0;

        // Remove stale children
        DestroyChild(playerGO, "PushHand");
        DestroyChild(playerGO, "BlockHand");
        DestroyChild(playerGO, "StaminaBarBG");    // old world-space bars
        DestroyChild(playerGO, "StaminaBarFill");
        DestroyChild(playerGO, "StaminaCanvas");

        // Push hand — bright-white fist held in front of the player body (radius=0.64u @ 128px).
        // localPos y=0.75 puts it just outside the body edge so it's visible, not buried inside.
        var pushHand = MakeHandChild("PushHand", playerGO, pushSpr, Color.white,
                                    localPos: new Vector3(0f, 0.75f, 0f),
                                    localScale: new Vector3(1.6f, 1.6f, 1f),
                                    sortOrder: 2);
        pushHand.SetActive(false);

        // Block shield — large cyan plate hugging the body front; size makes the block state
        // unmistakable. (Sprite native 0.48×0.10u → 0.72×0.18u — was a 0.018u hairline.)
        var blockHand = MakeHandChild("BlockHand", playerGO, blockSpr, new Color(0.35f, 0.8f, 1f),
                                     localPos: new Vector3(0f, 0.62f, 0f),
                                     localScale: new Vector3(1.5f, 1.8f, 1f),
                                     sortOrder: 2);
        blockHand.SetActive(false);

        // Wire hand references directly on Player (no extra component needed).
        var player = playerGO.GetComponent<Player>();
        if (player != null)
        {
            var so = new SerializedObject(player);
            so.FindProperty("pushHand").objectReferenceValue  = pushHand.GetComponent<SpriteRenderer>();
            so.FindProperty("blockHand").objectReferenceValue = blockHand.GetComponent<SpriteRenderer>();
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(player);
        }
        EditorUtility.SetDirty(playerGO);
    }

    // Create or refresh the arena visual: dark filled floor disc + Amber ring border.
    // Both sprites are 512×512 px at 100 PPU = 5.12 units native; scaled to ringOutRadius*2.
    private static void ApplyArenaBoundary(GameObject arenaGO, float ringOutRadius)
    {
        // Remove legacy single-sprite child and any prior floor/border children.
        DestroyChild(arenaGO, "ArenaBoundary");
        DestroyChild(arenaGO, "ArenaFloor");
        DestroyChild(arenaGO, "ArenaBorder");

        var floorSprite  = GetOrCreateSprite("Assets/Sprites/ArenaFloor.png",
                               () => MakeArenaFloorTexture(512));
        var borderSprite = GetOrCreateSprite("Assets/Sprites/ArenaBorder.png",
                               () => MakeRingTexture(512, 24));

        const float PPU = 100f;
        const float TEX = 512f;
        float scale = (ringOutRadius * 2f) / (TEX / PPU);

        // Floor disc — dark surface, rendered behind everything.
        MakeBoundaryChild(arenaGO, "ArenaFloor",  floorSprite,
                          Color.white, scale, sortOrder: -2);

        // Amber Bright ring border — brighter variant for dark-bg contrast.
        MakeBoundaryChild(arenaGO, "ArenaBorder", borderSprite,
                          new Color(0.910f, 0.753f, 0.408f), scale, sortOrder: -1);  // #E8C068
    }

    private static void MakeBoundaryChild(GameObject parent, string name,
                                          Sprite sprite, Color color,
                                          float scale, int sortOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale    = new Vector3(scale, scale, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.color        = color;
        sr.sortingOrder = sortOrder;
        EditorUtility.SetDirty(go);
    }

    // Screen-space overlay HUD: P1 stamina bar bottom-left (green), P2 bottom-right (red).
    // Uses Image.Type.Filled so fill amount drives the bar width with no scale tricks.
    private static void CreateStaminaHUD(GameObject p1GO, GameObject p2GO)
    {
        // Idempotent: remove any existing HUD canvas.
        var existing = GameObject.Find("StaminaHUD");
        if (existing != null) Object.DestroyImmediate(existing);

        // Root HUD object — Screen Space Overlay canvas.
        var hudGO = new GameObject("StaminaHUD");
        var canvas = hudGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        hudGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        hudGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // ContentKit HUD palette
        var p1Color = new Color(0.910f, 0.753f, 0.408f);  // Amber Bright #E8C068 — P1
        var p2Color = new Color(0.604f, 0.667f, 0.733f);  // Steel Bright #9AAABB — P2

        // P1 bar — bottom-left, Amber Bright.
        Image p1Fill = CreateBarUI(hudGO.transform, isLeft: true,  p1Color);

        // P2 bar — bottom-right, Steel Bright.
        Image p2Fill = CreateBarUI(hudGO.transform, isLeft: false, p2Color);

        // Score labels — top corners, matching player colors, TMP.
        TextMeshProUGUI p1Score = CreateScoreText(hudGO.transform, isLeft: true,  p1Color);
        TextMeshProUGUI p2Score = CreateScoreText(hudGO.transform, isLeft: false, p2Color);

        // Wire StaminaHUD via reflection — avoids a hard compile-time editor→runtime dependency.
        // StaminaHUD.Start() also auto-discovers players by name as a fallback.
        System.Type hudType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try { hudType = asm.GetType("StaminaHUD"); } catch { }
            if (hudType != null) break;
        }

        if (hudType != null)
        {
            var hud = hudGO.AddComponent(hudType);
            var so  = new SerializedObject(hud);
            if (p1GO != null)
                so.FindProperty("player1").objectReferenceValue = p1GO.GetComponent<Player>();
            if (p2GO != null)
                so.FindProperty("player2").objectReferenceValue = p2GO.GetComponent<Player>();
            so.FindProperty("p1Fill").objectReferenceValue      = p1Fill;
            so.FindProperty("p2Fill").objectReferenceValue      = p2Fill;
            so.FindProperty("p1ScoreText").objectReferenceValue = p1Score;
            so.FindProperty("p2ScoreText").objectReferenceValue = p2Score;
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[PushmanSetup] StaminaHUD type not found in loaded assemblies — " +
                             "HUD canvas created without the component. It will auto-wire at runtime.");
        }

        EditorUtility.SetDirty(hudGO);
    }

    // Creates one stamina bar anchored to the bottom-left (isLeft=true) or bottom-right.
    // Returns the fill Image so StaminaHUD can update it each frame.
    private static Image CreateBarUI(Transform hudParent, bool isLeft, Color fillColor)
    {
        const float BAR_W  = 300f;
        const float BAR_H  = 28f;
        const float MARGIN = 20f;

        // Container
        var barGO = new GameObject(isLeft ? "P1Bar" : "P2Bar");
        barGO.transform.SetParent(hudParent, false);
        var barRT = barGO.AddComponent<RectTransform>();

        if (isLeft)
        {
            barRT.anchorMin        = new Vector2(0f, 0f);
            barRT.anchorMax        = new Vector2(0f, 0f);
            barRT.pivot            = new Vector2(0f, 0f);
            barRT.anchoredPosition = new Vector2(MARGIN, MARGIN);
        }
        else
        {
            barRT.anchorMin        = new Vector2(1f, 0f);
            barRT.anchorMax        = new Vector2(1f, 0f);
            barRT.pivot            = new Vector2(1f, 0f);
            barRT.anchoredPosition = new Vector2(-MARGIN, MARGIN);
        }
        barRT.sizeDelta = new Vector2(BAR_W, BAR_H);

        // Background image
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(barGO.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.118f, 0.110f, 0.086f, 0.92f);  // ContentKit Surface #1E1C16

        // Fill image — Filled type drives bar width from left.
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(barGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2f, 2f);
        fillRT.offsetMax = new Vector2(-2f, -2f);
        var fillImg = fillGO.AddComponent<Image>();
        // Image.Type.Filled requires a sprite — without one fillAmount is ignored
        // and it just renders a solid color at full size regardless of stamina.
        var barSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/StaminaBar.png");
        if (barSprite == null)   // create it if first run
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px  = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes("Assets/Sprites/StaminaBar.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset("Assets/Sprites/StaminaBar.png");
            var imp = AssetImporter.GetAtPath("Assets/Sprites/StaminaBar.png") as TextureImporter;
            if (imp != null) { imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; AssetDatabase.ImportAsset("Assets/Sprites/StaminaBar.png", ImportAssetOptions.ForceUpdate); }
            barSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/StaminaBar.png");
        }
        fillImg.sprite     = barSprite;
        fillImg.color      = fillColor;
        fillImg.type       = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;

        return fillImg;
    }

    // Score label anchored to a top corner. Uses TextMeshProUGUI with Rajdhani font.
    private static TextMeshProUGUI CreateScoreText(Transform hudParent, bool isLeft, Color color)
    {
        const float MARGIN = 32f;  // inset from screen edge
        const float W      = 120f;
        const float H      = 90f;

        // Outer container (anchors + positioning)
        var container = new GameObject(isLeft ? "P1ScoreContainer" : "P2ScoreContainer");
        container.transform.SetParent(hudParent, false);
        var crt = container.AddComponent<RectTransform>();
        if (isLeft)
        {
            crt.anchorMin        = new Vector2(0f, 1f);
            crt.anchorMax        = new Vector2(0f, 1f);
            crt.pivot            = new Vector2(0f, 1f);
            crt.anchoredPosition = new Vector2(MARGIN, -MARGIN);
        }
        else
        {
            crt.anchorMin        = new Vector2(1f, 1f);
            crt.anchorMax        = new Vector2(1f, 1f);
            crt.pivot            = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-MARGIN, -MARGIN);
        }
        crt.sizeDelta = new Vector2(W, H);

        // Score text — no backing panel, floats directly in the corner
        var go = new GameObject(isLeft ? "P1Score" : "P2Score");
        go.transform.SetParent(container.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text             = "0";
        txt.fontSize         = 72;
        txt.fontStyle        = FontStyles.Bold;
        txt.color            = color;
        txt.alignment        = isLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode     = TextOverflowModes.Overflow;
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Rajdhani-Medium SDF.asset");
        if (font != null) txt.font = font;

        return txt;
    }

    // -----------------------------------------------------------------------
    // ContentKit font helpers (world-space TextMeshPro labels)
    // -----------------------------------------------------------------------

    // Cached font assets — loaded once per editor session from Assets/Fonts/.
    private static TMP_FontAsset s_fontTitle;  // Rajdhani-Medium SDF  (headings)
    private static TMP_FontAsset s_fontBody;   // Inter-Regular SDF    (body / stats)

    private static TMP_FontAsset FontTitle =>
        s_fontTitle != null ? s_fontTitle :
        (s_fontTitle = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Fonts/Rajdhani-Medium SDF.asset"));

    private static TMP_FontAsset FontBody =>
        s_fontBody != null ? s_fontBody :
        (s_fontBody = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Fonts/Inter-Regular SDF.asset"));

    // TMP 3D renders text at ~fontSize × 0.127 world units (measured empirically from mesh bounds).
    // Scaling the label GO by the inverse makes fontSize values specify world-unit height directly:
    //   fontSize=2.8 → label GO scale 7.84 → text appears 2.8 world units tall in the scene.
    private const float TMP_WORLD_CORRECTION = 7.84f;

    /// <summary>
    /// Creates a world-space TextMeshPro label as a child of <paramref name="parent"/>.
    /// <paramref name="fontSize"/> is the desired text height in world units.
    /// A scale correction (TMP_WORLD_CORRECTION) is applied to the GO so the fontSize
    /// directly equals world-unit height (TMP 3D otherwise renders at ~0.127× the specified pt size).
    /// </summary>
    private static void MakeTMPWorldLabel(GameObject parent, string goName, string text,
                                           TMP_FontAsset font, float fontSize, Color color,
                                           Vector3 localPos, float rectWidth = 24f)
    {
        var go = new GameObject(goName);
        // Set parent first so hierarchy is correct before adding RectTransform.
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;

        // Pre-create RectTransform before adding TextMeshPro.
        // Without this TMP creates a zero-size rect in edit mode, clipping all text.
        var rt = go.AddComponent<RectTransform>();
        rt.localPosition = localPos;   // re-apply — AddComponent may reset it
        // Scale correction: makes fontSize == desired world-unit height.
        rt.localScale    = new Vector3(TMP_WORLD_CORRECTION, TMP_WORLD_CORRECTION, 1f);
        // sizeDelta in LOCAL (pre-scale) units; wide/tall enough to never clip (Overflow mode).
        rt.sizeDelta     = new Vector2(rectWidth  / TMP_WORLD_CORRECTION,
                                       Mathf.Max(0.5f, fontSize * 1.5f / TMP_WORLD_CORRECTION));

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text             = text;
        tmp.fontSize         = fontSize;
        tmp.color            = color;
        tmp.alignment        = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode     = TextOverflowModes.Overflow;
        if (font != null)
            tmp.font = font;
        else
            Debug.LogWarning($"[PushmanSetup] Font asset null for '{goName}' — check Assets/Fonts/*.asset paths.");

        // Render above arena visuals; TMP MeshRenderer is on the same GO.
        var mr = go.GetComponent<Renderer>();
        if (mr != null) mr.sortingOrder = 5;

        EditorUtility.SetDirty(go);
    }

    // -----------------------------------------------------------------------
    // Personality helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// ContentKit data-series colors — one distinct hue per personality.
    /// Amber / Steel / Mauve / Sage / Terra from the design system palette.
    /// </summary>
    // ContentKit "Bright" variants of the five data-series colors.
    // The base data-series colors (#C49A3C etc.) are calibrated for 1px chart lines on
    // medium backgrounds; the Bright variants have the luminosity needed for filled
    // shapes on the near-black Void (#111009) background.
    // Amber Bright + Steel Bright are spec'd in ContentKit; Sage/Mauve/Terra Bright are
    // computed at the same +~20% lightness offset to maintain palette consistency.
    private static Color PersonalityColor(string name) => name switch
    {
        "Aggressive" => new Color(0.910f, 0.753f, 0.408f),  // Amber Bright  #E8C068
        "Defensive"  => new Color(0.604f, 0.667f, 0.733f),  // Steel Bright  #9AAABB
        "Balanced"   => new Color(0.604f, 0.733f, 0.525f),  // Sage Bright   #9ABB86
        "Evasive"    => new Color(0.722f, 0.596f, 0.800f),  // Mauve Bright  #B898CC
        "Counter"    => new Color(0.863f, 0.596f, 0.471f),  // Terra Bright  #DC9878
        _            => Color.white,
    };

    /// <summary>
    /// Adds a three-line TMP label above the arena: P1 name (Rajdhani, personality color) /
    /// "VS" (Inter, muted) / P2 name (Rajdhani, personality color).
    /// fontSize values are world-unit heights (TMP_WORLD_CORRECTION handles the TMP internal scale).
    /// Vertical spacing must be > text height to avoid overlap; title=2.8u, VS=1.4u.
    /// </summary>
    private static void AddArenaLabel(GameObject arenaGO, string p1Name, Color p1Color,
                                       string p2Name, Color p2Color, float yTop)
    {
        MakeTMPWorldLabel(arenaGO, $"Label_{p1Name}", p1Name.ToUpper(),
                          FontTitle, 2.8f, p1Color,
                          new Vector3(0f, yTop + 3.0f, 0f));
        MakeTMPWorldLabel(arenaGO, "Label_vs", "VS",
                          FontBody, 1.4f, new Color(0.416f, 0.388f, 0.345f),  // Text Muted #6A6358
                          new Vector3(0f, yTop + 0.5f, 0f));
        MakeTMPWorldLabel(arenaGO, $"Label_{p2Name}", p2Name.ToUpper(),
                          FontTitle, 2.8f, p2Color,
                          new Vector3(0f, yTop - 2.0f, 0f));
    }

    // -----------------------------------------------------------------------
    // Sprite texture factories
    // -----------------------------------------------------------------------

    // 128×128 player disc with a directional chevron baked in.
    // Body pixels = (0.72, 0.72, 0.72, 1) so they appear slightly dimmed after tinting.
    // Chevron pixels = (1, 1, 1, 1) — full brightness, points toward sprite +Y (player forward).
    // The SpriteRenderer tint (personality color) multiplies uniformly; the chevron stays
    // visibly brighter than the body, making facing direction legible from the overhead camera.
    private static Texture2D MakeCircleTexture(int size)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float c = size * 0.5f;
        float r = c - 1.5f;

        // Chevron geometry in (dx, dy) space centered at circle origin.
        // Tip at (0, 0.78r), base corners at (±0.25r, 0.30r).
        // Any point inside the filled triangle is part of the chevron.
        float chevTipY  = 0.78f * r;
        float chevBaseY = 0.30f * r;
        float chevHW    = 0.25f * r;   // half-width at base

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;   // positive dy = upward in texture = sprite +Y

                // Outside the disc — transparent.
                if (dx * dx + dy * dy > r * r) { pixels[y * size + x] = Color.clear; continue; }

                // Inside chevron triangle?
                bool inChev = dy >= chevBaseY && dy <= chevTipY &&
                              Mathf.Abs(dx) <= chevHW * (1f - (dy - chevBaseY) / (chevTipY - chevBaseY));

                pixels[y * size + x] = inChev
                    ? new Color(1.00f, 1.00f, 1.00f, 1f)   // chevron — full white (tint × 1.0 = personality color)
                    : new Color(0.80f, 0.80f, 0.80f, 1f);  // body — 80% so chevron reads 25% brighter
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // Filled disc in ContentKit Surface color (#1E1C16) — used as the arena floor.
    private static Texture2D MakeArenaFloorTexture(int size)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float c    = size * 0.5f;
        float r    = c - 1.5f;
        var floor  = new Color(0.118f, 0.110f, 0.086f, 1f);  // Surface #1E1C16 — ContentKit surface color

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                pixels[y * size + x] = (dx * dx + dy * dy <= r * r) ? floor : Color.clear;
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // Ring (annulus) outline on a transparent background.
    // Tinted to ContentKit Amber at the call site (ApplyArenaBoundary).
    private static Texture2D MakeRingTexture(int size, float borderPx)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float c      = size * 0.5f;
        float outer  = c - 1.5f;
        float inner  = outer - borderPx;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float dSq = dx * dx + dy * dy;
                pixels[y * size + x] =
                    (dSq <= outer * outer && dSq >= inner * inner) ? Color.white : Color.clear;
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // Solid white rectangle.
    private static Texture2D MakeRectTexture(int w, int h)
    {
        var tex    = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // Write texture to disk and import as a Sprite asset; return loaded Sprite.
    // Checks the physical file first — avoids returning a stale cached asset when
    // the PNG was deleted from disk but AssetDatabase hasn't refreshed yet.
    private static Sprite GetOrCreateSprite(string assetPath, System.Func<Texture2D> makeTexture)
    {
        if (File.Exists(assetPath))
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (existing != null) return existing;
        }

        var tex  = makeTexture();
        var png  = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(assetPath, png);

        AssetDatabase.ImportAsset(assetPath);
        var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp != null)
        {
            imp.textureType          = TextureImporterType.Sprite;
            imp.spriteImportMode     = SpriteImportMode.Single;
            imp.filterMode           = FilterMode.Bilinear;
            imp.mipmapEnabled        = false;
            imp.alphaIsTransparency  = true;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // -----------------------------------------------------------------------
    // Small helpers
    // -----------------------------------------------------------------------

    private static GameObject MakeHandChild(string childName, GameObject parent,
                                            Sprite sprite, Color color,
                                            Vector3 localPos, Vector3 localScale, int sortOrder)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = localPos;
        go.transform.localScale    = localScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.color        = color;
        sr.sortingOrder = sortOrder;
        return go;
    }

    private static void SetHandScale(GameObject playerGO, string childName, Vector3 scale)
    {
        var t = playerGO.transform.Find(childName);
        if (t != null) { t.localScale = scale; EditorUtility.SetDirty(t.gameObject); }
    }

    private static void DestroyChild(GameObject parent, string childName)
    {
        var existing = parent.transform.Find(childName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
    }

    [MenuItem("Pushman/4b. Add UI to Player Prefab")]
    public static void AddUIToPlayer()
    {
        GameObject p2 = GameObject.Find("Arena/Player2");
        if (p2 == null)
        {
            Debug.LogError("[PushmanSetup] Player2 not found. Run '1. Setup Test Scene' first.");
            return;
        }

        Player playerComp = p2.GetComponent<Player>();
        if (playerComp == null)
        {
            Debug.LogError("[PushmanSetup] Player component not found on Player2.");
            return;
        }

        // Create Canvas (world-space, positioned above player)
        GameObject canvasGO = new GameObject("StaminaCanvas");
        canvasGO.transform.SetParent(p2.transform);
        canvasGO.transform.localPosition = new Vector3(0f, 1.2f, 0f);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRectTransform = canvasGO.GetComponent<RectTransform>();
        canvasRectTransform.sizeDelta = new Vector2(2f, 0.3f);
        canvasRectTransform.localScale = Vector3.one * 0.01f;

        // Create Slider (stamina bar)
        GameObject sliderGO = new GameObject("StaminaSlider");
        sliderGO.transform.SetParent(canvasGO.transform);
        sliderGO.transform.localPosition = Vector3.zero;

        var sliderRectTransform = sliderGO.AddComponent<RectTransform>();
        sliderRectTransform.anchorMin = Vector2.zero;
        sliderRectTransform.anchorMax = Vector2.one;
        sliderRectTransform.offsetMin = Vector2.zero;
        sliderRectTransform.offsetMax = Vector2.zero;

        var slider = sliderGO.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;
        slider.interactable = false;
        slider.direction = Slider.Direction.LeftToRight;

        // Create background image for slider
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform);
        bgGO.transform.localPosition = Vector3.zero;
        var bgRect = bgGO.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);  // dark gray background

        // Create fill image for slider
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(sliderGO.transform);
        fillGO.transform.localPosition = Vector3.zero;
        var fillRect = fillGO.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        var fillImage = fillGO.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.8f, 0.2f, 0.9f);  // green fill

        slider.fillRect = fillRect;

        // NOTE: staminaSlider is no longer used — Player now uses staminaFill (SpriteRenderer).
        // This legacy menu item is kept for reference only. Use '4. Add Sprites to Players' instead.
        EditorUtility.SetDirty(playerComp);
        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(slider);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] (Legacy) UI added. Prefer '4. Add Sprites to Players' instead.");
    }

    // Wire each arena instance's ArenaManager and RL brains to local scene references.
    private static void WireArena(GameObject arenaGO, ArenaManager am,
                                  ObservationProfile profile, BotPersonality personality)
    {
        // Rebuild lists from children (prefab instance has same hierarchy as prefab)
        am.allPlayers  = new List<Player>(arenaGO.GetComponentsInChildren<Player>());
        am.spawnPoints = new List<Transform>();
        am.arenaCenter = arenaGO.transform.Find("ArenaCenter");

        // Populate stats pool so agents train across all three character variants.
        var defaultStats   = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/DefaultStats.asset");
        var heavyStats     = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/Heavyweight.asset");
        var speedsterStats = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/Speedster.asset");
        am.statsPool = new List<CharacterStats>();
        if (defaultStats   != null) am.statsPool.Add(defaultStats);
        if (heavyStats     != null) am.statsPool.Add(heavyStats);
        if (speedsterStats != null) am.statsPool.Add(speedsterStats);

        for (int i = 1; i <= 4; i++)
        {
            Transform sp = arenaGO.transform.Find($"SpawnPoint_{i}");
            if (sp != null) am.spawnPoints.Add(sp);
        }

        // Wire each RL brain to its local arena and opponents
        var rlBrains = arenaGO.GetComponentsInChildren<RLAgentBrain>();
        var allPlayers = am.allPlayers;
        foreach (var rl in rlBrains)
        {
            rl.arenaManager       = am;
            rl.observationProfile = profile;
            rl.personality        = personality;

            // Opponents = every player except self
            var opp = new List<Player>();
            Player self = rl.GetComponent<Player>();
            foreach (var p in allPlayers) if (p != self) opp.Add(p);
            rl.opponents = opp.ToArray();

            // Update BehaviorParameters with correct observation space size
            ConfigureBehaviorParameters(rl.gameObject);
        }

        EditorUtility.SetDirty(am);
        EditorUtility.SetDirty(arenaGO);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static GameObject BuildPlayerGO(string name, CharacterStats stats, int layer, GameObject parent)
    {
        var go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent.transform);

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale   = 0f;
        rb.linearDamping  = 2f;
        rb.angularDamping = 10f;

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.5f;

        // SpriteRenderer — sprite & color assigned later in AddSpritesToPlayers.
        go.AddComponent<SpriteRenderer>();

        var player = go.AddComponent<Player>();
        player.stats = stats;
        player.pushHitRadius = 0.8f;   // larger than prefab default — makes push easier to land

        go.AddComponent<PlayerMovingState>();
        go.AddComponent<PlayerChargingState>();
        go.AddComponent<PlayerBlockingState>();
        go.AddComponent<PlayerPushingState>();
        go.AddComponent<PlayerDodgingState>();
        go.AddComponent<PlayerStunnedState>();

        EditorUtility.SetDirty(player);
        return go;
    }

    private static void ConfigureBehaviorParameters(GameObject go)
    {
        var bp = go.GetComponent<BehaviorParameters>();
        if (bp == null) bp = go.AddComponent<BehaviorParameters>();

        // Match the shared-network behavior name used by Phase 3 v2 training so test
        // and inference scenes can load the same ONNX.
        bp.BehaviorName = SHARED_BEHAVIOR_NAME;
        bp.BehaviorType = BehaviorType.Default; // Default = trainer controls; change to HeuristicOnly for manual play

        // Derive space size from the ObservationProfile asset so it stays in sync
        // if anyone toggles profile flags — avoids hard ML-Agents runtime errors.
        var profile = AssetDatabase.LoadAssetAtPath<ObservationProfile>(
            "Assets/ScriptableObjects/Observation/Profile_A.asset");
        int obsSize = profile != null ? profile.ComputeSpaceSize(1) : OBS_SIZE;
        bp.BrainParameters.VectorObservationSize = obsSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;
        // 4 discrete branches: move-H(3), move-V(3), rotate(3), action(4=none/push/block/dodge)
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(3, 3, 3, 4);

        EditorUtility.SetDirty(bp);
    }

    private static int GetOrAddLayer(string layerName)
    {
        for (int i = 0; i < 32; i++)
            if (LayerMask.LayerToName(i) == layerName) return i;

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            var entry = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(entry.stringValue))
            {
                entry.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[PushmanSetup] Added layer '{layerName}' at index {i}");
                return i;
            }
        }
        Debug.LogError($"[PushmanSetup] Could not add layer '{layerName}': no free slots.");
        return 0;
    }

    private static void CleanupObject(string name)
    {
        var go = GameObject.Find(name);
        if (go != null) Object.DestroyImmediate(go);
    }

    private static GameObject CreateChild(string name, GameObject parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = Vector3.zero;
        return go;
    }

    private static void EnsureFolders()
    {
        string[] folders = {
            "Assets/Prefabs",
            "Assets/Scenes",
            "Assets/ScriptableObjects",
            "Assets/ScriptableObjects/Characters",
            "Assets/ScriptableObjects/Observation",
            "Assets/ScriptableObjects/Personalities",
        };
        foreach (var path in folders)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                string folder = Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }

    private static T CreateOrLoad<T>(string path) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        var so = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();
        return so;
    }

    // -----------------------------------------------------------------------
    // 12a. Build Main Menu Scene
    //      Creates Assets/Scenes/MainMenu.unity with a Canvas + MainMenuController
    //      wired to all CharacterStats and BotPersonality SOs.
    //      Run Pushman/10 afterwards if you want ContentKit bg on the camera.
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/12a. Build Main Menu Scene")]
    public static void BuildMainMenuScene()
    {
        EnsureFolders();

        // Load or create the SOs we need for card data
        var defaultStats   = CreateOrLoad<CharacterStats>("Assets/ScriptableObjects/Characters/DefaultStats.asset");
        var heavyStats     = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/Heavyweight.asset");
        var speedsterStats = AssetDatabase.LoadAssetAtPath<CharacterStats>("Assets/ScriptableObjects/Characters/Speedster.asset");

        var aggressive  = AssetDatabase.LoadAssetAtPath<BotPersonality>("Assets/ScriptableObjects/Personalities/Aggressive.asset");
        var defensive   = AssetDatabase.LoadAssetAtPath<BotPersonality>("Assets/ScriptableObjects/Personalities/Defensive.asset");
        var evasive     = AssetDatabase.LoadAssetAtPath<BotPersonality>("Assets/ScriptableObjects/Personalities/Evasive.asset");
        var balanced    = AssetDatabase.LoadAssetAtPath<BotPersonality>("Assets/ScriptableObjects/Personalities/Balanced.asset");
        var counter     = AssetDatabase.LoadAssetAtPath<BotPersonality>("Assets/ScriptableObjects/Personalities/Counter.asset");

        // Create a fresh scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Camera — ContentKit Void background
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.067f, 0.063f, 0.035f);
            if (cam.gameObject.GetComponent<ContentKitCamera>() == null)
                cam.gameObject.AddComponent<ContentKitCamera>();
            EditorUtility.SetDirty(cam.gameObject);
        }

        // EventSystem — use InputSystemUIInputModule (new Input System) if available;
        // fall back to StandaloneInputModule only when the package isn't installed.
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var newInputModuleType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputModuleType != null)
                esGO.AddComponent(newInputModuleType);
            else
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Canvas + MainMenuController
        var canvasGO = new GameObject("MainMenu");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler  = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Resolve MainMenuController type at runtime to avoid hard editor dependency
        System.Type mmcType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try { mmcType = asm.GetType("MainMenuController"); } catch { }
            if (mmcType != null) break;
        }

        if (mmcType == null)
        {
            Debug.LogWarning("[PushmanSetup] MainMenuController type not found — save the scene and recompile first.");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/MainMenu.unity");
            return;
        }

        var mmc = canvasGO.AddComponent(mmcType);
        var so  = new SerializedObject(mmc);

        // ContentKit accent colours
        var kAmber = new Color(0.910f, 0.753f, 0.408f);
        var kMauve = new Color(0.722f, 0.596f, 0.800f);
        var kSteel = new Color(0.604f, 0.667f, 0.733f);
        var kTerra = new Color(0.863f, 0.596f, 0.471f);

        // Helper: write a CardDef array into a SerializedProperty
        void WriteCards(string propName, (string label, string desc, Color accent, Object data, float fval)[] defs)
        {
            var arr = so.FindProperty(propName);
            if (arr == null) { Debug.LogWarning($"[PushmanSetup] Property '{propName}' not found on MainMenuController."); return; }
            arr.arraySize = defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var el   = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("label"      ).stringValue          = defs[i].label;
                el.FindPropertyRelative("description").stringValue          = defs[i].desc;
                el.FindPropertyRelative("accentColor").colorValue           = defs[i].accent;
                el.FindPropertyRelative("data"       ).objectReferenceValue = defs[i].data;
                el.FindPropertyRelative("floatValue" ).floatValue           = defs[i].fval;
            }
        }

        // Player character cards (Amber)
        WriteCards("playerCharCards", new[] {
            ("DEFAULT",     "Balanced stats",            kAmber, (Object)defaultStats,   0f),
            ("HEAVYWEIGHT", "Slow but hits like a truck",kAmber, (Object)heavyStats,     0f),
            ("SPEEDSTER",   "Light and fast",            kAmber, (Object)speedsterStats, 0f),
        });

        // Personality cards (Mauve)
        WriteCards("personalityCards", new[] {
            ("AGGRESSIVE", "Push first, push hard",        kMauve, (Object)aggressive, 0f),
            ("DEFENSIVE",  "Block and punish",             kMauve, (Object)defensive,  0f),
            ("EVASIVE",    "Dodge everything",             kMauve, (Object)evasive,    0f),
            ("BALANCED",   "Adapts to the situation",      kMauve, (Object)balanced,   0f),
            ("COUNTER",    "Reads and punishes patterns",  kMauve, (Object)counter,    0f),
        });

        // Difficulty cards (Steel) — floatValue = runtimeHumanization (0=Expert, 1=Easy)
        WriteCards("difficultyCards", new[] {
            ("EXPERT", "No mercy",               kSteel, (Object)null, 0.00f),
            ("HARD",   "Tough but beatable",     kSteel, (Object)null, 0.33f),
            ("MEDIUM", "Fair challenge",         kSteel, (Object)null, 0.66f),
            ("EASY",   "Learning the ropes",    kSteel, (Object)null, 1.00f),
        });

        // Opponent character cards (Terra)
        WriteCards("opponentCharCards", new[] {
            ("DEFAULT",     "Balanced stats",            kTerra, (Object)defaultStats,   0f),
            ("HEAVYWEIGHT", "Slow but hits like a truck",kTerra, (Object)heavyStats,     0f),
            ("SPEEDSTER",   "Light and fast",            kTerra, (Object)speedsterStats, 0f),
        });

        // Wire scene name
        var sceneNameProp = so.FindProperty("gameSceneName");
        if (sceneNameProp != null) sceneNameProp.stringValue = "Game";

        // Wire TMP font — search for LiberationSans SDF (TMP's default built-in font)
        TMP_FontAsset defaultTMPFont = null;
        foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            var fp = AssetDatabase.GUIDToAssetPath(guid);
            if (fp.Contains("LiberationSans SDF"))
            {
                defaultTMPFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fp);
                if (defaultTMPFont != null) break;
            }
        }
        // Fall back to any TMP_FontAsset if LiberationSans isn't found
        if (defaultTMPFont == null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                defaultTMPFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (defaultTMPFont != null) break;
            }
        }
        if (defaultTMPFont != null)
        {
            var fontTitleProp = so.FindProperty("fontTitle");
            var fontBodyProp  = so.FindProperty("fontBody");
            if (fontTitleProp != null) fontTitleProp.objectReferenceValue = defaultTMPFont;
            if (fontBodyProp  != null) fontBodyProp.objectReferenceValue  = defaultTMPFont;
            Debug.Log($"[PushmanSetup] TMP font wired: {defaultTMPFont.name}");
        }
        else
        {
            Debug.LogWarning("[PushmanSetup] No TMP_FontAsset found — import TextMeshPro Essentials " +
                             "via Window > TextMeshPro > Import TMP Essential Resources, then re-run 12a.");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(canvasGO);

        // Save and register
        const string MAIN_MENU_PATH = "Assets/Scenes/MainMenu.unity";
        EditorSceneManager.SaveScene(scene, MAIN_MENU_PATH);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == MAIN_MENU_PATH))
            scenes.Insert(0, new EditorBuildSettingsScene(MAIN_MENU_PATH, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log("[PushmanSetup] Main Menu scene saved → Assets/Scenes/MainMenu.unity. " +
                  "Run Pushman/10 to apply ContentKit background.");
    }

    // -----------------------------------------------------------------------
    // 12b. Build Game Scene (Human vs Bot)
    //      Creates Assets/Scenes/Game.unity — Human P1 vs v39 RLAgentBrain P2.
    //      GameSceneController reads GameConfig at runtime to apply menu selections.
    //      SeriesManager tracks wins and shows series-end overlay.
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/12b. Build Game Scene (Human vs Bot)")]
    public static void BuildGameScene()
    {
        const string GAME_SCENE_PATH  = "Assets/Scenes/Game.unity";
        const string MODELS_FOLDER    = "Assets/MLModels";
        const string DEFAULT_ONNX     = MODELS_FOLDER + "/Pushman_Shared/PushmanAgent_v39.onnx";

        EnsureFolders();

        // Load SOs
        var stats    = CreateOrLoad<CharacterStats>("Assets/ScriptableObjects/Characters/DefaultStats.asset");
        InitCharacterStats(stats);
        var profileA    = CreateOrLoad<ObservationProfile>("Assets/ScriptableObjects/Observation/Profile_A.asset");
        var personality = AssetDatabase.LoadAssetAtPath<BotPersonality>("Assets/ScriptableObjects/Personalities/Aggressive.asset")
                       ?? CreateOrLoad<BotPersonality>("Assets/ScriptableObjects/Personalities/Aggressive.asset");
        AssetDatabase.SaveAssets();

        // Resolve ONNX — prefer v39, fall back to newest
        string onnxPath = DEFAULT_ONNX;
        var allOnnx = AssetDatabase.FindAssets("t:Object", new[] { MODELS_FOLDER });
        string newestPath = null; System.DateTime newestTime = System.DateTime.MinValue;
        foreach (var guid in allOnnx)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith(".onnx")) continue;
            System.DateTime t = File.GetLastWriteTime(p);
            if (t > newestTime) { newestTime = t; newestPath = p; }
        }
        if (!File.Exists(onnxPath) && newestPath != null) onnxPath = newestPath;

        // New scene FIRST — EditorSceneManager.NewScene triggers UnloadUnusedAssets which
        // would null out any asset reference we held before this point.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Now load the model — the reference will survive until BuildBotPlayer.
        var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(onnxPath);
        if (model == null) model = AssetDatabase.LoadMainAssetAtPath(onnxPath) as ModelAsset;
        if (model == null)
            Debug.LogError($"[PushmanSetup] Failed to load model at {onnxPath} after scene creation.");
        else
            Debug.Log($"[PushmanSetup] Game scene using model: {model.name} ({model.GetType().Name})");

        // EventSystem — must use InputSystemUIInputModule (new Input System project)
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var newInputModuleType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputModuleType != null)
                esGO.AddComponent(newInputModuleType);
            else
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        int playerLayer = GetOrAddLayer("Player");

        // Arena
        var arena  = new GameObject("Arena");
        var center = CreateChild("ArenaCenter", arena);
        center.transform.localPosition = Vector3.zero;

        var spawnPositions = new Vector3[] {
            new Vector3(-4f,  0f, 0f),
            new Vector3( 4f,  0f, 0f),
            new Vector3( 0f,  4f, 0f),
            new Vector3( 0f, -4f, 0f),
        };
        var spawnGOs = new List<GameObject>();
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            var sp = CreateChild($"SpawnPoint_{i + 1}", arena);
            sp.transform.localPosition = spawnPositions[i];
            spawnGOs.Add(sp);
        }

        // Player 1 — HumanBrain
        var p1GO = BuildPlayerGO("Player1", stats, playerLayer, arena);
        p1GO.transform.localPosition = spawnPositions[0];
        p1GO.AddComponent<HumanBrain>();
        var p1 = p1GO.GetComponent<Player>();

        // Player 2 — RLAgentBrain (InferenceOnly, v39)
        var p2GO = BuildBotPlayer("Player2", stats, playerLayer, arena,
                                  spawnPositions[1], profileA, personality, model);
        var p2 = p2GO.GetComponent<Player>();
        // Default to Hard difficulty (0.33 humanization)
        var p2rl = p2GO.GetComponent<RLAgentBrain>();
        if (p2rl != null) p2rl.runtimeHumanization = 0.33f;

        // Verify the model actually got wired onto BehaviorParameters.
        // BuildBotPlayer uses SerializedObject "m_Model"; if Sentis renamed it, this catches it.
        var p2bp = p2GO.GetComponent<BehaviorParameters>();
        if (p2bp != null)
        {
            if (p2bp.Model != null)
                Debug.Log($"[PushmanSetup] ✓ Bot model wired: {p2bp.Model.name}");
            else
            {
                Debug.LogError($"[PushmanSetup] ✗ Bot model FAILED to wire (BehaviorParameters.Model is null). " +
                               $"Falling back to HeuristicOnly so the scene doesn't crash at runtime.");
                p2bp.BehaviorType = BehaviorType.HeuristicOnly;
            }
        }

        int oppMask = 1 << playerLayer;
        p1.opponentLayer = oppMask;
        p2.opponentLayer = oppMask;

        // ArenaManager — autoRestart starts true; SeriesManager will set it false at series end
        var am = arena.AddComponent<ArenaManager>();
        am.arenaCenter   = center.transform;
        am.ringOutRadius = 10f;
        am.allPlayers    = new List<Player> { p1, p2 };
        am.spawnPoints   = new List<Transform>();
        foreach (var sp in spawnGOs) am.spawnPoints.Add(sp.transform);
        am.autoRestart   = true;

        // Wire RL brain
        if (p2rl != null)
        {
            p2rl.arenaManager = am;
            p2rl.opponents    = new Player[] { p1 };
        }

        // Camera
        var cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic     = true;
            cam.orthographicSize = 12f;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.067f, 0.063f, 0.035f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            if (cam.gameObject.GetComponent<ContentKitCamera>() == null)
                cam.gameObject.AddComponent<ContentKitCamera>();
            EditorUtility.SetDirty(cam.gameObject);
        }

        // Sprites + HUD + arena boundary ring (all idempotent)
        AddSpritesToPlayers();

        // GameSceneController
        System.Type gscType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try { gscType = asm.GetType("GameSceneController"); } catch { }
            if (gscType != null) break;
        }
        if (gscType != null)
        {
            var gscGO = new GameObject("GameSceneController");
            var gsc   = gscGO.AddComponent(gscType);
            var gscSO = new SerializedObject(gsc);
            var p1Prop = gscSO.FindProperty("player1");
            var p2Prop = gscSO.FindProperty("player2");
            if (p1Prop != null) p1Prop.objectReferenceValue = p1;
            if (p2Prop != null) p2Prop.objectReferenceValue = p2;
            gscSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(gscGO);
        }
        else
        {
            Debug.LogWarning("[PushmanSetup] GameSceneController type not found — recompile first.");
        }

        // SeriesManager
        System.Type smType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try { smType = asm.GetType("SeriesManager"); } catch { }
            if (smType != null) break;
        }
        if (smType != null)
        {
            var smGO = new GameObject("SeriesManager");
            var sm   = smGO.AddComponent(smType);
            var smSO = new SerializedObject(sm);
            var amProp = smSO.FindProperty("arenaManager");
            if (amProp != null) amProp.objectReferenceValue = am;
            var mnProp = smSO.FindProperty("mainMenuSceneName");
            if (mnProp != null) mnProp.stringValue = "MainMenu";
            smSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(smGO);
        }
        else
        {
            Debug.LogWarning("[PushmanSetup] SeriesManager type not found — recompile first.");
        }

        EditorUtility.SetDirty(am);
        EditorUtility.SetDirty(p1);
        EditorUtility.SetDirty(p2);

        // Save + register
        EditorSceneManager.SaveScene(scene, GAME_SCENE_PATH);
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == GAME_SCENE_PATH))
            scenes.Add(new EditorBuildSettingsScene(GAME_SCENE_PATH, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        AssetDatabase.Refresh();
        Debug.Log("[PushmanSetup] Game scene saved → Assets/Scenes/Game.unity. " +
                  "Run Pushman/10 to apply ContentKit bg and post-processing.");
    }

    // -----------------------------------------------------------------------

    private static void InitCharacterStats(CharacterStats stats)
    {
        stats.weight                = 1f;
        stats.movementSpeed         = 6f;
        stats.maxStamina            = 100f;
        stats.staminaRegenRate      = 8f;
        stats.blockStaminaUsageRate = 15f;
        stats.regenBoostThreshold   = 0.75f;
        stats.regenBoostMultiplier  = 2.5f;
        stats.dodgeForce            = 9f;   // reduced: dodge is evasive, not a ring-out tool
        stats.dodgeStamina          = 40f;
        stats.dodgeTime             = 0.25f;
        stats.pushForce             = 12f;  // increased: push is the ring-out tool
        stats.pushStamina           = 20f;
        stats.pushChargeMultiplier  = 1.5f;
        stats.pushChargeTime        = 0.25f;  // short charge: less vulnerability window, tighter credit assignment
        EditorUtility.SetDirty(stats);
    }
}

