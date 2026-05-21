using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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
                                   () => MakeCircleTexture(64));
        Sprite pushSprite    = GetOrCreateSprite("Assets/Sprites/PushHand.png",
                                   () => MakeRectTexture(24, 16));
        Sprite blockSprite   = GetOrCreateSprite("Assets/Sprites/BlockShield.png",
                                   () => MakeRectTexture(48, 10));
        Sprite ringSprite    = GetOrCreateSprite("Assets/Sprites/ArenaBoundary.png",
                                   () => MakeRingTexture(256, 8));
        if (circleSprite == null || pushSprite == null || blockSprite == null || ringSprite == null)
        {
            Debug.LogError("[PushmanSetup] Failed to create one or more sprite assets.");
            return;
        }

        // --- Wire up Player1 (green) ---
        GameObject p1GO = GameObject.Find("Arena/Player1");
        if (p1GO != null)
            ApplyPlayerSprites(p1GO, circleSprite, pushSprite, blockSprite,
                               new Color(0.2f, 0.8f, 0.2f));  // green

        // --- Wire up Player2 (red) ---
        GameObject p2GO = GameObject.Find("Arena/Player2");
        if (p2GO != null)
            ApplyPlayerSprites(p2GO, circleSprite, pushSprite, blockSprite,
                               new Color(0.8f, 0.2f, 0.2f));  // red

        // --- Screen-space stamina HUD (replaces old world-space bars) ---
        if (p1GO != null || p2GO != null) CreateStaminaHUD(p1GO, p2GO);

        // --- Arena boundary ring ---
        GameObject arenaGO = GameObject.Find("Arena");
        if (arenaGO != null)
            ApplyArenaBoundary(arenaGO, ringSprite, ringOutRadius: 10f);

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

        // Push hand — bright white fist in front of the player so it contrasts against the colored body
        var pushHand = MakeHandChild("PushHand", playerGO, pushSpr, Color.white,
                                    localPos: new Vector3(0f, 0.72f, 0f),
                                    localScale: new Vector3(0.45f, 0.30f, 1f),
                                    sortOrder: 2);
        pushHand.SetActive(false);

        // Block shield — cyan-tinted wide plate so it reads differently from the body
        var blockHand = MakeHandChild("BlockHand", playerGO, blockSpr, new Color(0.4f, 0.9f, 1f),
                                     localPos: new Vector3(0f, 0.60f, 0f),
                                     localScale: new Vector3(1.1f, 0.18f, 1f),
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

    // Create or refresh the ring outline that marks the ring-out boundary.
    private static void ApplyArenaBoundary(GameObject arenaGO, Sprite ring, float ringOutRadius)
    {
        DestroyChild(arenaGO, "ArenaBoundary");

        var go = new GameObject("ArenaBoundary");
        go.transform.SetParent(arenaGO.transform);
        go.transform.localPosition = Vector3.zero;

        // Ring texture is 256×256 px. At Unity's default 100 PPU it spans 2.56 units.
        // Scale so it matches ringOutRadius * 2.
        float scale = (ringOutRadius * 2f) / (256f / 100f);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = ring;
        sr.color        = new Color(1f, 1f, 1f, 0.45f);
        sr.sortingOrder = -1;  // behind players

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

        // P1 bar — bottom-left, green.
        Image p1Fill = CreateBarUI(hudGO.transform, isLeft: true,
                                   new Color(0.2f, 0.85f, 0.2f));   // green

        // P2 bar — bottom-right, red.
        Image p2Fill = CreateBarUI(hudGO.transform, isLeft: false,
                                   new Color(0.85f, 0.2f, 0.2f));   // red

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
            so.FindProperty("p1Fill").objectReferenceValue  = p1Fill;
            so.FindProperty("p2Fill").objectReferenceValue  = p2Fill;
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
        bgImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        // Fill image — Filled type drives bar width from left.
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(barGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2f, 2f);
        fillRT.offsetMax = new Vector2(-2f, -2f);
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color      = fillColor;
        fillImg.type       = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 1f;

        return fillImg;
    }

    // -----------------------------------------------------------------------
    // Sprite texture factories
    // -----------------------------------------------------------------------

    // Filled white circle on a transparent background.
    private static Texture2D MakeCircleTexture(int size)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float c = size * 0.5f;
        float r = c - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                pixels[y * size + x] = (dx * dx + dy * dy <= r * r) ? Color.white : Color.clear;
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // Ring (annulus) outline on a transparent background.
    private static Texture2D MakeRingTexture(int size, float borderPx)
    {
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        float c      = size * 0.5f;
        float outer  = c - 1f;
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

        // SpriteRenderer — sprite & color assigned later in AddSpritesToPlayers.
        go.AddComponent<SpriteRenderer>();

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
        if (bp == null) bp = go.AddComponent<BehaviorParameters>();

        bp.BehaviorName = "PushmanAgent";
        bp.BehaviorType = BehaviorType.HeuristicOnly;

        // Use direct API — avoids fragile SerializedObject property-path lookups.
        bp.BrainParameters.VectorObservationSize        = OBS_SIZE;
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

    private static void InitCharacterStats(CharacterStats stats)
    {
        stats.weight                = 1f;
        stats.movementSpeed         = 6f;
        stats.maxStamina            = 100f;
        stats.staminaRegenRate      = 8f;
        stats.blockStaminaUsageRate = 15f;
        stats.dodgeForce            = 18f;
        stats.dodgeStamina          = 40f;
        stats.dodgeTime             = 0.25f;
        stats.pushForce             = 8f;
        stats.pushStamina           = 20f;
        stats.pushChargeMultiplier  = 1.5f;
        stats.pushChargeTime        = 1f;
        EditorUtility.SetDirty(stats);
    }
}

