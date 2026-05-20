using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine.UI;

// Pushman > 1. Setup Test Scene  — builds the SampleScene from scratch
// Pushman > 2. Save Prefabs      — bakes Arena + Player_RL prefabs from the active scene
// Pushman > 3. Build Training Scene — generates ML_Training_Scene with N×N arenas
public static class PushmanSetup
{
    private const int OBS_SIZE = 13; // selfKin(3)+selfStam(1)+selfState(1)+arena(3)+opp(5)
    private const string PREFAB_FOLDER = "Assets/Prefabs";
    private const string ARENA_PREFAB_PATH = PREFAB_FOLDER + "/Arena.prefab";
    private const string PLAYER_RL_PREFAB_PATH = PREFAB_FOLDER + "/Player_RL.prefab";
    private const string TRAINING_SCENE_PATH = "Assets/Scenes/ML_Training_Scene.unity";
    private const int GRID_SIZE = 8;          // GRID_SIZE × GRID_SIZE arenas
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

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] Test scene ready. Ctrl+S to save. " +
                  "Set Player2 > Behavior Parameters > Behavior Type to 'Default' before training.");
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
    // 4. Add UI to Player Prefab
    // -----------------------------------------------------------------------

    [MenuItem("Pushman/4. Add UI to Player Prefab")]
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

        // Wire to Player
        playerComp.staminaSlider = slider;

        EditorUtility.SetDirty(playerComp);
        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(slider);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[PushmanSetup] UI added to Player2. Save scene and re-run '2. Save Prefabs'.");
    }

    // Wire each arena instance's ArenaManager and RL brains to local scene references.
    private static void WireArena(GameObject arenaGO, ArenaManager am,
                                  ObservationProfile profile, BotPersonality personality)
    {
        // Rebuild lists from children (prefab instance has same hierarchy as prefab)
        am.allPlayers  = new List<Player>(arenaGO.GetComponentsInChildren<Player>());
        am.spawnPoints = new List<Transform>();
        am.arenaCenter = arenaGO.transform.Find("ArenaCenter");

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

        var player = go.AddComponent<Player>();
        player.stats = stats;

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
        bp.BehaviorName = "PushmanAgent";
        bp.BehaviorType = BehaviorType.HeuristicOnly;

        var so = new SerializedObject(bp);
        so.FindProperty("m_BrainParameters.m_VectorObservationSize")?.SetValue(OBS_SIZE);
        so.FindProperty("m_BrainParameters.m_NumStackedVectorObservations")?.SetValue(1);

        var actionSpec = so.FindProperty("m_BrainParameters.m_ActionSpec");
        if (actionSpec != null)
        {
            actionSpec.FindPropertyRelative("m_NumContinuousActions")?.SetValue(0);
            var branches = actionSpec.FindPropertyRelative("m_DiscreteBranches");
            if (branches != null)
            {
                branches.arraySize = 4;
                branches.GetArrayElementAtIndex(0).intValue = 3;
                branches.GetArrayElementAtIndex(1).intValue = 3;
                branches.GetArrayElementAtIndex(2).intValue = 3;
                branches.GetArrayElementAtIndex(3).intValue = 4;
            }
        }
        so.ApplyModifiedProperties();
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

    private static void InitCharacterStats(CharacterStats stats)
    {
        stats.weight                = 1f;
        stats.movementSpeed         = 6f;
        stats.maxStamina            = 100f;
        stats.staminaRegenRate      = 12f;
        stats.blockStaminaUsageRate = 18f;
        stats.dodgeForce            = 10f;
        stats.dodgeStamina          = 25f;
        stats.dodgeTime             = 0.25f;
        stats.pushForce             = 8f;
        stats.pushStamina           = 20f;
        stats.pushChargeMultiplier  = 1.5f;
        stats.pushChargeTime        = 1f;
        EditorUtility.SetDirty(stats);
    }
}

internal static class SerializedPropertyExtensions
{
    public static void SetValue(this SerializedProperty prop, int value)
    {
        if (prop != null) prop.intValue = value;
    }
}
