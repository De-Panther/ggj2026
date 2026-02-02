using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using TMPro;
using System.Collections;
using System.Globalization;

namespace Ggj2026Game
{
  public class GameWorld : MonoBehaviour
  {
    [Header("Input Actions")]
    public InputActionReference moveActionRef;
    public InputActionReference attackActionRef;

    [Header("UI")]
    public GameObject startMenu;
    public GameObject inGameMenu;
    public TMP_Text inGameScoreText;
    public TMP_Text inGameTimerText;
    public Image inGameMaskIcon;
    public GameObject endMenu;
    public TMP_Text endScoreText;
    public GameObject mobileCanvas;

    [Header("Setup")]
    public Transform playerTransform;
    public Transform furtherChair1;
    public Transform furtherChair2;
    public Transform cameraTransform;
    public GameObject npcPrefab;
    public GameObject chairPrefab;
    public Mesh npcMesh;
    public Material npcMaterial;
    public Mesh chairMesh;
    public Material chairMaterial;
    // private Transform[] npcTransforms;
    // private Transform[] chairTransforms;
    private NativeArray<Matrix4x4> npcMatrices;
    private ComputeBuffer npcMatrixBuffer;
    private NativeArray<Matrix4x4> chairMatrices;
    private ComputeBuffer chairMatrixBuffer;

    [Header("Settings")]
    public float cameraStart = 15f;
    public float cameraEnd = 200f;
    public float maxPlayTime = 60f;
    public int maxChairs = 100;
    public float playerSpeed = 5f;
    public float throwSpeed = 10f;
    public float chairHitRadius = 0.5f;
    public float area = 100f;
    private float throwOffset = 0.5f;

    [Header("Gameplay")]
    public float npcThrowCooldown = 0.5f;

    // Data
    NativeArray<NPCData> npcs;
    NativeArray<ChairData> chairs;
    NativeHashSet<int> playerNearbyChairs;
    NativeQueue<int> playerNearbyAddQueue;
    NativeQueue<int> playerNearbyRemoveQueue;
    // TransformAccessArray chairTransformAccess;
    NativeArray<int> npcGrid; // size = gridWidth * gridHeight

    float2 playerPos;
    float2 lastInputDir = new float2(0f, 1f); // Default forward

    int gridSize = 0;
    float2 worldMin = new float2();

    int score = 0;
    int displayScore = 0;
    bool inGame = false;
    float playStartTime = 0;
    int lastSeconds = -1;
    InputAction moveAction;
    InputAction attackAction;

    readonly float countSpeed = 50f;
    Coroutine scoreRoutine;

    void Start()
    {
      mobileCanvas.SetActive(Application.isMobilePlatform);
      startMenu.SetActive(true);
      inGameMenu.SetActive(false);
      endMenu.SetActive(false);
      inGame = false;
      // --- Spawn Player (optional if already in scene) ---
      playerPos = new float2(playerTransform.position.x, playerTransform.position.z);

      gridSize = (int)math.ceil(area * 2 / chairHitRadius);
      worldMin = new float2(-area, -area);

      // npcTransforms = new Transform[maxChairs];
      // chairTransforms = new Transform[maxChairs];
      // chairTransformAccess = new TransformAccessArray(chairTransforms.Length);
      npcMatrices = new NativeArray<Matrix4x4>(maxChairs, Allocator.Persistent);
      npcMatrixBuffer = new ComputeBuffer(maxChairs, 16 * 4); // 16 floats per Matrix4x4
      npcMaterial.enableInstancing = true;
      npcMaterial.SetBuffer("_Matrices", npcMatrixBuffer);
      chairMatrices = new NativeArray<Matrix4x4>(maxChairs, Allocator.Persistent);
      chairMatrixBuffer = new ComputeBuffer(maxChairs, 16 * 4); // 16 floats per Matrix4x4
      chairMaterial.enableInstancing = true;
      chairMaterial.SetBuffer("_Matrices", chairMatrixBuffer);

      // --- Initialize NativeArrays ---
      playerNearbyChairs = new NativeHashSet<int>(maxChairs, Allocator.Persistent);
      playerNearbyAddQueue = new NativeQueue<int>(Allocator.Persistent);
      playerNearbyRemoveQueue = new NativeQueue<int>(Allocator.Persistent);
      npcs = new NativeArray<NPCData>(maxChairs, Allocator.Persistent);
      chairs = new NativeArray<ChairData>(maxChairs, Allocator.Persistent);
      npcGrid = new NativeArray<int>(gridSize * gridSize, Allocator.Persistent);

      for (int i = 0; i < npcGrid.Length; i++)
        npcGrid[i] = -1;

      for (int i = 0; i < maxChairs; i++)
      {
        // Random spawn position on XZ plane
        Vector3 pos = new Vector3(
            UnityEngine.Random.Range(-area, area),
            npcPrefab.transform.position.y,
            UnityEngine.Random.Range(-area, area)
        );

        // Instantiate prefab
        // Transform npcInstance = Instantiate(npcPrefab, pos, Quaternion.identity).transform;
        // npcTransforms[i] = npcInstance;

        // Initialize data
        NPCData npc;
        npc.pos = new float2(pos.x, pos.z);
        npc.angry = 0;
        npc.cooldown = 0f;
        npcs[i] = npc;

        // Spawn chairs at NPCs pos
        // Transform chairInstance = Instantiate(chairPrefab, pos, Quaternion.identity).transform;
        // chairTransforms[i] = chairInstance;
        // chairTransformAccess.Add(chairInstance);

        ChairData chair;
        chair.active = 0;
        chair.pos = new float2(pos.x, pos.z);
        chair.vel = new float2(0f, 0f);
        chair.owner = i;
        chair.nearbyPlayer = 0;
        chairs[i] = chair;
        UpdateMatrix(i, npc.pos, npcMatrices);
        UpdateMatrix(i, chair.pos, chairMatrices);
      }

      // Last chair at Player pos
      // chairTransforms[maxChairs - 1].position = Vector2.zero;
      ChairData chairTemp = chairs[maxChairs - 1];
      chairTemp.active = 0;
      chairTemp.pos = new float2(0, 0);
      chairTemp.owner = -1;
      chairs[maxChairs - 1] = chairTemp;
      UpdateMatrix(maxChairs - 1, chairTemp.pos, chairMatrices);
    }

    void UpdateMatrix(int index, float2 pos, NativeArray<Matrix4x4> matrices)
    {
      Vector3 position = new Vector3(pos.x, -1, pos.y);
      Quaternion rot = Quaternion.Euler(270f, 0f, 0f);
      Vector3 scale = Vector3.one;

      matrices[index] = Matrix4x4.TRS(position, rot, scale);
    }

    void OnDestroy()
    {
      npcs.Dispose();

      chairs.Dispose();

      playerNearbyChairs.Dispose();
      playerNearbyAddQueue.Dispose();
      playerNearbyRemoveQueue.Dispose();
      // if (chairTransformAccess.isCreated)
      //   chairTransformAccess.Dispose();
      npcGrid.Dispose();
      if (npcMatrices.IsCreated)
        npcMatrices.Dispose();
      if (npcMatrixBuffer != null)
        npcMatrixBuffer.Release();
      if (chairMatrices.IsCreated)
        chairMatrices.Dispose();
      if (chairMatrixBuffer != null)
        chairMatrixBuffer.Release();
    }

    private void OnEnable()
    {
      // Get the actual InputActions from the references
      moveAction = moveActionRef.action;
      attackAction = attackActionRef.action;

      // Enable them
      moveAction.Enable();
      attackAction.Enable();
    }

    private void OnDisable()
    {
      // Disable actions to avoid memory leaks
      moveAction.Disable();
      attackAction.Disable();
    }

    void Update()
    {
      if (!inGame)
      {
        return;
      }
      float dt = Time.deltaTime;
      Vector3 tempVec3 = Vector3.zero;
      // float2 tempPos;

      // --- Player Input ---
      Vector2 inputVector = moveAction.ReadValue<Vector2>();
      // if (Keyboard.current != null)
      // {
      //   // WASD keys
      //   inputVector.x = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
      //   inputVector.y = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
      // }

      // if (Gamepad.current != null)
      // {
      //   // Left stick overrides keyboard if present
      //   inputVector = Gamepad.current.leftStick.ReadValue();
      // }

      // Convert to float2 for data-oriented logic
      float2 input = new float2(inputVector.x, inputVector.y);

      if (math.lengthsq(input) > 0.001f)
      {
        input = math.normalize(input);
        playerPos += input * playerSpeed * dt;
        lastInputDir = input;
      }

      // Player throw (space key or gamepad south button)
      // bool throwPressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
      //                     (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
      bool throwPressed = attackAction.WasPressedThisFrame();

      if (throwPressed && math.lengthsq(lastInputDir) > 0.001f && playerNearbyChairs.Count > 0)
      {
        // Pick the first chair in range (or random)
        int chairIndex = -1;
        foreach (int c in playerNearbyChairs)
        {
          chairIndex = c;
          break;
        }

        if (chairIndex >= 0)
        {
          var chair = chairs[chairIndex];
          chair.vel = lastInputDir * throwSpeed;
          chair.active = 1;
          chair.owner = -1;
          chairs[chairIndex] = chair;

          playerNearbyChairs.Remove(chairIndex);
        }
      }

      // --- Jobs ---
      var furthestPose = new NativeReference<float2>(Allocator.TempJob);
      var buildNPCGridJob = new BuildNPCGridJob
      {
        npcs = npcs,
        chairs = chairs,
        npcGrid = npcGrid,
        cellSize = chairHitRadius,
        gridSize = gridSize,
        worldMin = worldMin,
        furthestPose = furthestPose
      };

      NativeQueue<ChairHit> chairHitQueue = new NativeQueue<ChairHit>(Allocator.TempJob);
      var chairHitWriter = chairHitQueue.AsParallelWriter();

      var chairUpdateJob = new ChairUpdateJob
      {
        chairs = chairs,
        dt = dt,
        npcs = npcs,
        hitRadiusSq = chairHitRadius * chairHitRadius,
        playerPos = playerPos,
        playerNearbyAddQueue = playerNearbyAddQueue.AsParallelWriter(),
        playerNearbyRemoveQueue = playerNearbyRemoveQueue.AsParallelWriter(),
        hitWriter = chairHitWriter,
        npcGrid = npcGrid,
        cellSize = chairHitRadius,
        gridSize = gridSize,
        worldMin = worldMin,
        matrices = chairMatrices
      };

      var npcThrowQueue = new NativeQueue<ChairThrow>(Allocator.TempJob);
      var npcUpdateJob = new NPCUpdateJob
      {
        npcs = npcs,
        chairs = chairs,
        dt = Time.deltaTime,
        throwSpeed = throwSpeed,
        frameSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue),
        throwCooldown = npcThrowCooldown,
        throwQueue = npcThrowQueue.AsParallelWriter(),
        matrices = npcMatrices
      };

      JobHandle buildNPCGridHandle = buildNPCGridJob.Schedule();
      JobHandle chairUpdateHandle = chairUpdateJob.Schedule(maxChairs, 64, buildNPCGridHandle);
      JobHandle npcHandle = npcUpdateJob.Schedule(maxChairs, 64, chairUpdateHandle);
      npcHandle.Complete();

      float2 furthest = furthestPose.Value;
      tempVec3.x = furthest.x;
      tempVec3.z = furthest.y;
      furtherChair1.position = tempVec3;
      furtherChair2.position = -tempVec3;
      furthestPose.Dispose();
      // handle = new ChairTransformJob
      // {
      //   chairs = chairs
      // }.Schedule(chairTransformAccess, handle);

      // --- Chair Hit ---
      while (chairHitQueue.TryDequeue(out var hit))
      {
        score++;
        var npc = npcs[hit.npcIndex];
        npc.angry = 1;
        npcs[hit.npcIndex] = npc;

        // Chair is now "held" by NPC
        var chair = chairs[hit.chairIndex];
        chair.owner = hit.npcIndex;
        chair.active = 0; // held, not flying
        chairs[hit.chairIndex] = chair;

        // Optional: snap chair to NPC visually
        // var t = chairTransforms[hit.chairIndex];
        // tempPos = npc.pos;
        // tempVec3.x = tempPos.x;
        // tempVec3.z = tempPos.y;
        // t.position = tempVec3;
      }
      chairHitQueue.Dispose();

      while (npcThrowQueue.TryDequeue(out var t))
      {
        var chair = chairs[t.chairIndex];
        chair.active = 1;
        chair.owner = -1;
        chair.vel = t.direction * t.speed;

        // Optional: snap to NPC position if desired
        var npc = npcs[t.npcIndex];
        var pos = npc.pos + t.direction * throwOffset;
        chair.pos = pos;
        chairs[t.chairIndex] = chair;
      }

      npcThrowQueue.Dispose();

      // Add chairs to GameWorld.playerNearbyChairs
      while (playerNearbyAddQueue.TryDequeue(out int chairIndex))
      {
        playerNearbyChairs.Add(chairIndex);
      }

      // Remove chairs from GameWorld.playerNearbyChairs
      while (playerNearbyRemoveQueue.TryDequeue(out int chairIndex))
      {
        playerNearbyChairs.Remove(chairIndex);
      }

      // --- Sync Transforms ---
      tempVec3.x = playerPos.x;
      tempVec3.z = playerPos.y;
      playerTransform.position = tempVec3;
      // for (int i = 0; i < maxChairs; i++)
      // {
      //   // tempPos = npcs[i].pos;
      //   // tempVec3.x = tempPos.x;
      //   // tempVec3.z = tempPos.y;
      //   // npcTransforms[i].position = tempVec3;
      //   var chair = chairs[i];
      //   if (chair.active == 1)
      //   {
      //     tempPos = chair.pos;
      //     tempVec3.x = tempPos.x;
      //     tempVec3.z = tempPos.y;
      //     chairTransforms[i].position = tempVec3;
      //   }
      // }
      TryAnimateScore();
      UpdateTimer();
      TryEndGame();
    }

    void LateUpdate()
    {
      DrawInstances(npcMatrixBuffer, npcMatrices, npcMaterial, npcMesh);
      DrawInstances(chairMatrixBuffer, chairMatrices, chairMaterial, chairMesh);
    }

    void DrawInstances(ComputeBuffer matrixBuffer, NativeArray<Matrix4x4> matrices, Material material, Mesh mesh)
    {
      // Upload to GPU
      matrixBuffer.SetData(matrices);

      // Pass VP matrix
      Camera cam = Camera.main;
      Matrix4x4 proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
      Matrix4x4 vp = proj * cam.worldToCameraMatrix;
      material.SetMatrix("_VP", vp);

      // Draw procedural
      Graphics.DrawMeshInstancedProcedural(
          mesh,
          0,
          material,
          new Bounds(Vector3.zero, Vector3.one * 9999999),
          maxChairs
      );
    }

    // --- Jobs ---

    [BurstCompile]
    struct ChairUpdateJob : IJobParallelFor
    {
      public NativeArray<ChairData> chairs;
      [ReadOnly] public NativeArray<NPCData> npcs;

      public float hitRadiusSq;
      public float dt;

      // Player
      public float2 playerPos;
      public NativeQueue<int>.ParallelWriter playerNearbyAddQueue;
      public NativeQueue<int>.ParallelWriter playerNearbyRemoveQueue;

      // Hit reporting queue for main thread
      public NativeQueue<ChairHit>.ParallelWriter hitWriter;

      [ReadOnly] public NativeArray<int> npcGrid;

      public float cellSize;
      public int gridSize;
      public float2 worldMin;

      public NativeArray<Matrix4x4> matrices;

      public void Execute(int c)
      {
        ChairData chair = chairs[c];

        if (chair.active == 1)
        {
          chair.pos += chair.vel * dt;
          UpdateMatrix(c, chair);
        }

        // -----------------------
        // Player nearby detection
        // -----------------------
        float2 playerDiff = chair.pos - playerPos;
        bool inPlayerRange = math.dot(playerDiff, playerDiff) <= hitRadiusSq;
        bool wasNear = chair.nearbyPlayer == 1;

        if (inPlayerRange && !wasNear)
        {
          chair.nearbyPlayer = 1;
          playerNearbyAddQueue.Enqueue(c);
        }
        else if (!inPlayerRange && wasNear)
        {
          chair.nearbyPlayer = 0;
          playerNearbyRemoveQueue.Enqueue(c);
        }

        if (chair.active == 0)
        {
          chairs[c] = chair;
          return;
        }

        // -----------------------
        // NPC hit detection
        // -----------------------
        int cellX = (int)math.floor((chair.pos.x - worldMin.x) / cellSize);
        int cellY = (int)math.floor((chair.pos.y - worldMin.y) / cellSize);
        if ((uint)cellX < gridSize && (uint)cellY < gridSize)
        {
          int cellIndex = cellX + cellY * gridSize;
          int npcIndex = npcGrid[cellIndex];
          if (npcIndex > -1)
          {
            // Report hit
            hitWriter.Enqueue(new ChairHit
            {
              chairIndex = c,
              npcIndex = npcIndex
            });

            // Stop chair flight
            chair.active = 0;
            chair.vel = float2.zero;
            chairs[c] = chair;
          }
        }
        chairs[c] = chair;
      }

      void UpdateMatrix(int index, ChairData chair)
      {
        Vector3 pos = new Vector3(chair.pos.x, -1, chair.pos.y);
        Quaternion rot = Quaternion.Euler(270f, 0f, 0f);
        Vector3 scale = Vector3.one;

        matrices[index] = Matrix4x4.TRS(pos, rot, scale);
      }
    }

    [BurstCompile]
    struct NPCUpdateJob : IJobParallelFor
    {
      public NativeArray<NPCData> npcs;
      [ReadOnly] public NativeArray<ChairData> chairs;

      public float dt;
      public float throwCooldown;
      public float throwSpeed;
      public uint frameSeed;

      // Thread-safe queue for main thread processing
      public NativeQueue<ChairThrow>.ParallelWriter throwQueue;

      public NativeArray<Matrix4x4> matrices;

      public void Execute(int i)
      {
        NPCData npc = npcs[i];
        if (npc.angry == 0)
          return;

        float cd = npc.cooldown - dt;

        if (cd <= 0f)
        {
          npc.cooldown = throwCooldown;

          // Find a chair owned by this NPC
          for (int c = 0; c < chairs.Length; c++)
          {
            ChairData chair = chairs[c];
            if (chair.owner == i && chair.active == 0)
            {
              uint seed = (uint)(i * 9176 + c + 13 + frameSeed);
              var rng = new Unity.Mathematics.Random(seed);
              float angle = rng.NextFloat(0f, math.PI * 2f);
              float2 dir = new float2(math.cos(angle), math.sin(angle));

              // Enqueue a throw request
              throwQueue.Enqueue(new ChairThrow
              {
                npcIndex = i,
                chairIndex = c,
                direction = dir,
                speed = throwSpeed
              });

              break; // throw only one chair
            }
          }
        }
        else
        {
          npc.cooldown = cd;
        }

        npcs[i] = npc;
        UpdateMatrix(i, npc);
      }

      void UpdateMatrix(int index, NPCData npc)
      {
        Vector3 pos = new Vector3(npc.pos.x, -1, npc.pos.y);
        Quaternion rot = Quaternion.Euler(270f, 0f, 0f);
        Vector3 scale = Vector3.one;

        matrices[index] = Matrix4x4.TRS(pos, rot, scale);
      }
    }

    // [BurstCompile]
    // struct ChairTransformJob : IJobParallelForTransform
    // {
    //   [ReadOnly] public NativeArray<ChairData> chairs;

    //   public void Execute(int i, TransformAccess transform)
    //   {
    //     var chair = chairs[i];
    //     if (chair.active == 0)
    //       return;

    //     transform.position = new Vector3(
    //         chair.pos.x,
    //         0,
    //         chair.pos.y
    //     );
    //   }
    // }

    [BurstCompile]
    struct BuildNPCGridJob : IJob
    {
      public NativeArray<int> npcGrid;
      [ReadOnly] public NativeArray<NPCData> npcs;
      [ReadOnly] public NativeArray<ChairData> chairs;

      public float cellSize;
      public int gridSize;
      public float2 worldMin;
      public NativeReference<float2> furthestPose;

      public void Execute()
      {
        float maxDistSq = -1f;
        float2 furthest = float2.zero;
        // Clear grid
        for (int i = 0; i < npcGrid.Length; i++)
          npcGrid[i] = -1;

        // Fill grid
        for (int i = 0; i < npcs.Length; i++)
        {
          float2 pos = npcs[i].pos;

          int cellX = (int)math.floor((pos.x - worldMin.x) / cellSize);
          int cellY = (int)math.floor((pos.y - worldMin.y) / cellSize);

          if ((uint)cellX >= gridSize || (uint)cellY >= gridSize)
            continue;

          int cellIndex = cellX + cellY * gridSize;
          npcGrid[cellIndex] = i;

          pos = chairs[i].pos;
          float distSq = math.lengthsq(pos);
          if (distSq > maxDistSq)
          {
            maxDistSq = distSq;
            furthest = pos;
          }
        }
        furthestPose.Value = furthest;
      }
    }


    // --- UI ---

    void TryEndGame()
    {
      float partTime = (Time.time - playStartTime) / maxPlayTime;
      inGameMaskIcon.fillAmount = 1f - partTime;
      cameraTransform.position = new Vector3(0, Mathf.Lerp(cameraStart, cameraEnd, partTime), 0);
      if (playStartTime + maxPlayTime <= Time.time)
      {
        startMenu.SetActive(false);
        inGameMenu.SetActive(false);
        endMenu.SetActive(true);
        inGame = false;
        endScoreText.SetText(score.ToString("#,0", CultureInfo.InvariantCulture));
      }
    }

    void UpdateTimer()
    {
      float timeRemaining = playStartTime + maxPlayTime - Time.time;
      if (timeRemaining < 0f)
        timeRemaining = 0f;

      int seconds = Mathf.FloorToInt(timeRemaining);

      // Update text ONLY when seconds change
      if (seconds != lastSeconds)
      {
        lastSeconds = seconds;

        DrawTimer(seconds);
      }
    }

    void DrawTimer(int seconds)
    {
      seconds++;
      int minutes = seconds / 60;
      int secs = seconds % 60;
      inGameTimerText.SetText("{0:00}:{1:00}", minutes, secs);
    }

    void TryAnimateScore()
    {
      inGameScoreText.SetText(score.ToString("#,0", CultureInfo.InvariantCulture));
      // if (displayScore == score)
      // {
      //   return;
      // }
      // if (scoreRoutine != null)
      // {
      //   return;
      // }

      // scoreRoutine = StartCoroutine(AnimateScore());
    }

    IEnumerator AnimateScore()
    {
      while (displayScore != score)
      {
        displayScore = (int)Mathf.MoveTowards(
            displayScore,
            score,
            Mathf.CeilToInt(countSpeed * Time.deltaTime)
        );

        inGameScoreText.SetText(displayScore.ToString("#,0", CultureInfo.InvariantCulture));
        yield return null;
      }
      scoreRoutine = null;
    }

    public void PlayGame()
    {
      startMenu.SetActive(false);
      inGameMenu.SetActive(true);
      endMenu.SetActive(false);
      inGame = true;
      inGameScoreText.SetText("0");
      playStartTime = Time.time;
      lastSeconds = Mathf.FloorToInt(maxPlayTime);
      DrawTimer((int)maxPlayTime - 1);
    }

    public void ReloadScene()
    {
      SceneManager.LoadScene(0);
    }
  }
}
