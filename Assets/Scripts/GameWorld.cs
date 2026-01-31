using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

namespace Ggj2026Game
{
  public class GameWorld : MonoBehaviour
  {
    [Header("UI")]
    public GameObject startMenu;
    public GameObject inGameMenu;
    public TMP_Text inGameScoreText;
    public GameObject endMenu;
    public TMP_Text endScoreText;

    [Header("Setup")]
    public Transform playerTransform;
    public GameObject npcPrefab;
    public GameObject chairPrefab;
    private Transform[] npcTransforms;
    private Transform[] chairTransforms;

    [Header("Settings")]
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
    NativeArray<float2> npcPos;
    NativeArray<byte> npcAngry;
    NativeArray<float> npcCooldown;

    NativeArray<float2> chairPos;
    NativeArray<float2> chairVel;
    NativeArray<byte> chairActive;
    NativeArray<int> chairOwner;
    NativeArray<byte> chairNearPlayer;
    NativeHashSet<int> playerNearbyChairs;
    NativeQueue<int> playerNearbyAddQueue;
    NativeQueue<int> playerNearbyRemoveQueue;

    float2 playerPos;
    float2 lastInputDir = new float2(0f, 1f); // Default forward

    int score = 0;
    int displayScore = 0;
    bool inGame = false;
    float playStartTime = 0;

    float countSpeed = 50f;
    Coroutine scoreRoutine;

    void Start()
    {
      startMenu.SetActive(true);
      inGameMenu.SetActive(false);
      endMenu.SetActive(false);
      inGame = false;
      // --- Spawn Player (optional if already in scene) ---
      playerPos = new float2(playerTransform.position.x, playerTransform.position.z);

      npcTransforms = new Transform[maxChairs];
      chairTransforms = new Transform[maxChairs];

      // --- Initialize NativeArrays ---
      playerNearbyChairs = new NativeHashSet<int>(maxChairs, Allocator.Persistent);
      playerNearbyAddQueue = new NativeQueue<int>(Allocator.Persistent);
      playerNearbyRemoveQueue = new NativeQueue<int>(Allocator.Persistent);
      npcPos = new NativeArray<float2>(maxChairs, Allocator.Persistent);
      npcAngry = new NativeArray<byte>(maxChairs, Allocator.Persistent);
      npcCooldown = new NativeArray<float>(maxChairs, Allocator.Persistent);

      chairPos = new NativeArray<float2>(maxChairs, Allocator.Persistent);
      chairVel = new NativeArray<float2>(maxChairs, Allocator.Persistent);
      chairActive = new NativeArray<byte>(maxChairs, Allocator.Persistent);
      chairOwner = new NativeArray<int>(maxChairs, Allocator.Persistent);
      chairNearPlayer = new NativeArray<byte>(maxChairs, Allocator.Persistent);

      for (int i = 0; i < maxChairs; i++)
      {
        chairActive[i] = 0;
        chairNearPlayer[i] = 0;

        // Random spawn position on XZ plane
        Vector3 pos = new Vector3(
            UnityEngine.Random.Range(-area, area),
            npcPrefab.transform.position.y,
            UnityEngine.Random.Range(-area, area)
        );

        // Instantiate prefab
        Transform npcInstance = Instantiate(npcPrefab, pos, Quaternion.identity).transform;
        npcTransforms[i] = npcInstance;

        // Initialize data
        npcPos[i] = new float2(pos.x, pos.z);
        npcAngry[i] = 0;
        npcCooldown[i] = 0f;

        // Spawn chairs at NPCs pos
        Transform chairInstance = Instantiate(chairPrefab, pos, Quaternion.identity).transform;
        chairTransforms[i] = chairInstance;

        chairActive[i] = 0;
        chairPos[i] = new float2(pos.x, pos.z);
        chairVel[i] = new float2(0f, 0f);
        chairOwner[i] = i;
      }

      // Last chair at Player pos
      chairTransforms[maxChairs - 1].position = Vector2.zero;
      chairActive[maxChairs - 1] = 0;
      chairPos[maxChairs - 1] = new float2(0, 0);
      chairOwner[maxChairs - 1] = -1;
    }

    void OnDestroy()
    {
      npcPos.Dispose();
      npcAngry.Dispose();
      npcCooldown.Dispose();

      chairPos.Dispose();
      chairVel.Dispose();
      chairActive.Dispose();
      chairOwner.Dispose();
      chairNearPlayer.Dispose();

      playerNearbyChairs.Dispose();
      playerNearbyAddQueue.Dispose();
      playerNearbyRemoveQueue.Dispose();
    }

    void Update()
    {
      if (!inGame)
      {
        return;
      }
      float dt = Time.deltaTime;
      Vector3 tempVec3 = Vector3.zero;
      float2 tempPos;

      // --- Player Input ---
      Vector2 inputVector = Vector2.zero;
      if (Keyboard.current != null)
      {
        // WASD keys
        inputVector.x = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
        inputVector.y = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
      }

      if (Gamepad.current != null)
      {
        // Left stick overrides keyboard if present
        inputVector = Gamepad.current.leftStick.ReadValue();
      }

      // Convert to float2 for data-oriented logic
      float2 input = new float2(inputVector.x, inputVector.y);

      if (math.lengthsq(input) > 0.001f)
      {
        input = math.normalize(input);
        playerPos += input * playerSpeed * dt;
        lastInputDir = input;
      }

      // Player throw (space key or gamepad south button)
      bool throwPressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                          (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

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
          chairVel[chairIndex] = lastInputDir * throwSpeed;
          chairActive[chairIndex] = 1;
          chairOwner[chairIndex] = -1;

          playerNearbyChairs.Remove(chairIndex);
        }
      }

      // --- Jobs ---
      NativeQueue<ChairHit> chairHitQueue = new NativeQueue<ChairHit>(Allocator.TempJob);
      var chairHitWriter = chairHitQueue.AsParallelWriter();

      var chairMoveJob = new ChairMoveJob
      {
        chairs = chairPos,
        chairVel = chairVel,
        chairActive = chairActive,
        dt = dt
      };

      var chairHitJob = new ChairHitJob
      {
        chairs = chairPos,
        chairVel = chairVel,
        chairActive = chairActive,
        chairOwner = chairOwner,
        chairNearPlayer = chairNearPlayer,
        npcs = npcPos,
        npcAngry = npcAngry,
        hitRadiusSq = chairHitRadius * chairHitRadius,
        playerPos = playerPos,
        playerNearbyAddQueue = playerNearbyAddQueue.AsParallelWriter(),
        playerNearbyRemoveQueue = playerNearbyRemoveQueue.AsParallelWriter(),
        hitWriter = chairHitWriter
      };

      var npcThrowQueue = new NativeQueue<ChairThrow>(Allocator.TempJob);
      var npcUpdateJob = new NPCUpdateJob
      {
        npcAngry = npcAngry,
        npcCooldown = npcCooldown,
        chairOwner = chairOwner,
        chairActive = chairActive,
        dt = Time.deltaTime,
        throwSpeed = throwSpeed,
        frameSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue),
        throwCooldown = npcThrowCooldown,
        throwQueue = npcThrowQueue.AsParallelWriter()
      };

      JobHandle moveHandle = chairMoveJob.Schedule(chairTransforms.Length, 64);
      JobHandle hitHandle = chairHitJob.Schedule(chairTransforms.Length, 64, moveHandle);
      JobHandle npcHandle = npcUpdateJob.Schedule(npcTransforms.Length, 64, hitHandle);
      npcHandle.Complete();

      // --- Chair Hit ---
      while (chairHitQueue.TryDequeue(out var hit))
      {
        score++;
        npcAngry[hit.npcIndex] = 1;

        // Chair is now "held" by NPC
        chairOwner[hit.chairIndex] = hit.npcIndex;
        chairActive[hit.chairIndex] = 0; // held, not flying

        // Optional: snap chair to NPC visually
        var t = chairTransforms[hit.chairIndex];
        tempPos = npcPos[hit.npcIndex];
        tempVec3.x = tempPos.x;
        tempVec3.z = tempPos.y;
        t.position = tempVec3;
      }
      chairHitQueue.Dispose();

      while (npcThrowQueue.TryDequeue(out var t))
      {
        chairActive[t.chairIndex] = 1;
        chairOwner[t.chairIndex] = -1;
        chairVel[t.chairIndex] = t.direction * t.speed;

        // Optional: snap to NPC position if desired
        var pos = npcPos[t.npcIndex] + t.direction * throwOffset;
        chairPos[t.chairIndex] = pos;
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
      for (int i = 0; i < maxChairs; i++)
      {
        tempPos = npcPos[i];
        tempVec3.x = tempPos.x;
        tempVec3.z = tempPos.y;
        npcTransforms[i].position = tempVec3;
        if (chairActive[i] == 1)
        {
          tempPos = chairPos[i];
          tempVec3.x = tempPos.x;
          tempVec3.z = tempPos.y;
          chairTransforms[i].position = tempVec3;
        }
      }
      TryAnimateScore();
      TryEndGame();
    }

    // --- Jobs ---

    [BurstCompile]
    struct ChairMoveJob : IJobParallelFor
    {
      public NativeArray<float2> chairs;
      public NativeArray<float2> chairVel;
      public NativeArray<byte> chairActive;
      public float dt;

      public void Execute(int i)
      {
        if (chairActive[i] == 0)
          return;
        chairs[i] += chairVel[i] * dt;
      }
    }

    [BurstCompile]
    struct ChairHitJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<float2> chairs;
      public NativeArray<float2> chairVel;
      public NativeArray<byte> chairActive;
      public NativeArray<int> chairOwner;
      public NativeArray<byte> chairNearPlayer;

      [ReadOnly] public NativeArray<float2> npcs;
      public NativeArray<byte> npcAngry;

      public float hitRadiusSq;

      // Player
      public float2 playerPos;
      public NativeQueue<int>.ParallelWriter playerNearbyAddQueue;
      public NativeQueue<int>.ParallelWriter playerNearbyRemoveQueue;

      // Hit reporting queue for main thread
      public NativeQueue<ChairHit>.ParallelWriter hitWriter;

      public void Execute(int c)
      {
        float2 chairPos = chairs[c];

        // -----------------------
        // Player nearby detection
        // -----------------------
        float2 playerDiff = chairPos - playerPos;
        bool inPlayerRange = math.dot(playerDiff, playerDiff) <= hitRadiusSq;
        bool wasNear = chairNearPlayer[c] == 1;

        if (inPlayerRange && !wasNear)
        {
          chairNearPlayer[c] = 1;
          playerNearbyAddQueue.Enqueue(c);
        }
        else if (!inPlayerRange && wasNear)
        {
          chairNearPlayer[c] = 0;
          playerNearbyRemoveQueue.Enqueue(c);
        }

        if (chairActive[c] == 0)
          return;

        // -----------------------
        // NPC hit detection
        // -----------------------
        for (int n = 0; n < npcs.Length; n++)
        {
          // Skip owner
          if (chairOwner[c] == n)
            continue;

          float2 diff = npcs[n] - chairPos;
          if (math.dot(diff, diff) < hitRadiusSq)
          {
            // Report hit
            hitWriter.Enqueue(new ChairHit
            {
              chairIndex = c,
              npcIndex = n
            });

            // Stop chair flight
            chairActive[c] = 0;
            chairVel[c] = float2.zero;

            break; // one NPC hit only
          }
        }
      }
    }

    [BurstCompile]
    struct NPCUpdateJob : IJobParallelFor
    {
      [ReadOnly] public NativeArray<byte> npcAngry;
      public NativeArray<float> npcCooldown;

      [ReadOnly] public NativeArray<int> chairOwner;
      [ReadOnly] public NativeArray<byte> chairActive;

      public float dt;
      public float throwCooldown;
      public float throwSpeed;
      public uint frameSeed;

      // Thread-safe queue for main thread processing
      public NativeQueue<ChairThrow>.ParallelWriter throwQueue;

      public void Execute(int i)
      {
        if (npcAngry[i] == 0)
          return;

        float cd = 0; //npcCooldown[i] - dt;

        if (cd <= 0f)
        {
          npcCooldown[i] = throwCooldown;

          // Find a chair owned by this NPC
          for (int c = 0; c < chairOwner.Length; c++)
          {
            if (chairOwner[c] == i && chairActive[c] == 0)
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
          npcCooldown[i] = cd;
        }
      }
    }

    // --- UI ---

    void TryEndGame()
    {
      if (playStartTime + maxPlayTime <= Time.time)
      {
        startMenu.SetActive(false);
        inGameMenu.SetActive(false);
        endMenu.SetActive(true);
        inGame = false;
        inGameScoreText.text = score.ToString();
        endScoreText.text = score.ToString();
      }
    }

    void TryAnimateScore()
    {
      if (displayScore == score)
      {
        return;
      }
      if (scoreRoutine != null)
      {
        return;
      }

      scoreRoutine = StartCoroutine(AnimateScore());
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

        inGameScoreText.text = displayScore.ToString();
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
      inGameScoreText.text = "0";
    }

    public void ReloadScene()
    {
      SceneManager.LoadScene(0);
    }
  }
}
