using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

namespace Ggj2026Game
{
  public class GameWorld : MonoBehaviour
  {
    [Header("Setup")]
    public Transform playerTransform;
    public GameObject npcPrefab;
    public GameObject chairPrefab;
    private Transform[] npcTransforms;
    private Transform[] chairTransforms;

    public int maxChairs = 100;
    public float playerSpeed = 5f;
    public float throwSpeed = 10f;
    public float chairHitRadius = 0.5f;

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

    float2 playerPos;
    float2 lastInputDir = new float2(0f, 1f); // Default forward

    float throwOffset = 0.5f;

    void Start()
    {
      // --- Spawn Player (optional if already in scene) ---
      playerPos = new float2(playerTransform.position.x, playerTransform.position.z);

      int npcCount = maxChairs;
      int chairCount = maxChairs;
      npcTransforms = new Transform[npcCount];
      chairTransforms = new Transform[chairCount];

      // --- Initialize NativeArrays ---
      npcPos = new NativeArray<float2>(npcCount, Allocator.Persistent);
      npcAngry = new NativeArray<byte>(npcCount, Allocator.Persistent);
      npcCooldown = new NativeArray<float>(npcCount, Allocator.Persistent);

      chairPos = new NativeArray<float2>(chairCount, Allocator.Persistent);
      chairVel = new NativeArray<float2>(chairCount, Allocator.Persistent);
      chairActive = new NativeArray<byte>(chairCount, Allocator.Persistent);
      chairOwner = new NativeArray<int>(chairCount, Allocator.Persistent);

      for (int i = 0; i < chairCount; i++)
        chairActive[i] = 0;

      // --- Spawn NPCs from prefab ---
      for (int i = 0; i < npcCount; i++)
      {
        // Random spawn position on XZ plane
        Vector3 pos = new Vector3(
            UnityEngine.Random.Range(-100f, 100f),
            npcPrefab.transform.position.y,
            UnityEngine.Random.Range(-100f, 100f)
        );

        // Instantiate prefab
        Transform npcInstance = Instantiate(npcPrefab, pos, Quaternion.identity).transform;
        npcTransforms[i] = npcInstance;

        // Initialize data
        npcPos[i] = new float2(pos.x, pos.z);
        npcAngry[i] = 0;
        npcCooldown[i] = 0f;
      }

      // --- Spawn Chairs from prefab ---
      for (int i = 0; i < chairCount; i++)
      {
        // Spawn at origin initially; will be moved when thrown
        Transform chairInstance = Instantiate(chairPrefab, Vector3.zero, Quaternion.identity).transform;
        chairTransforms[i] = chairInstance;

        chairActive[i] = 0;
        chairPos[i] = new float2(0f, 0f);
        chairVel[i] = new float2(0f, 0f);
        chairOwner[i] = -1;
      }
    }

    void Update()
    {
      float dt = Time.deltaTime;

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

      if (throwPressed && math.lengthsq(lastInputDir) > 0.001f)
      {
        SpawnChair(playerPos, lastInputDir * throwSpeed, -1);
      }

      // --- Jobs ---
      NativeQueue<int> npcHitQueue = new NativeQueue<int>(Allocator.TempJob);
      var npcHitWriter = npcHitQueue.AsParallelWriter();

      var chairMoveJob = new ChairMoveJob
      {
        chairs = chairPos,
        chairVel = chairVel,
        chairActive = chairActive,
        dt = dt
      };

      var chairNPCHitJob = new ChairNPCHitJob
      {
        chairs = chairPos,
        chairActive = chairActive,
        npcs = npcPos,
        chairOwner = chairOwner,
        hitRadiusSq = chairHitRadius * chairHitRadius,
        npcHitWriter = npcHitWriter
      };

      var npcUpdateJob = new NPCUpdateJob
      {
        npcAngry = npcAngry,
        npcCooldown = npcCooldown,
        throwRequest = new NativeArray<byte>(npcAngry.Length, Allocator.TempJob),
        dt = dt,
        throwCooldown = npcThrowCooldown
      };

      JobHandle moveHandle = chairMoveJob.Schedule(chairActive.Length, 32);
      JobHandle hitHandle = chairNPCHitJob.Schedule(chairActive.Length, 32, moveHandle);
      JobHandle npcHandle = npcUpdateJob.Schedule(npcAngry.Length, 32, hitHandle);
      npcHandle.Complete();

      // --- Apply deferred NPC hits ---
      while (npcHitQueue.Count > 0)
      {
        int idx = npcHitQueue.Dequeue();
        npcAngry[idx] = 1;
        npcCooldown[idx] = 0.2f;
      }
      npcHitQueue.Dispose();

      // --- NPC Throws ---
      for (int i = 0; i < npcUpdateJob.throwRequest.Length; i++)
      {
        if (npcUpdateJob.throwRequest[i] == 1)
        {
          // Random direction
          float angle = UnityEngine.Random.Range(0f, math.PI * 2f);
          float2 dir = new float2(math.cos(angle), math.sin(angle));
          float2 spawnPos = npcPos[i] + dir * throwOffset;
          SpawnChair(spawnPos, dir * throwSpeed, i);
        }
      }
      npcUpdateJob.throwRequest.Dispose();

      // --- Sync Transforms ---
      playerTransform.position = new Vector3(playerPos.x, playerTransform.position.y, playerPos.y);

      for (int i = 0; i < npcTransforms.Length; i++)
      {
        npcTransforms[i].position = new Vector3(npcPos[i].x, npcTransforms[i].position.y, npcPos[i].y);
      }

      for (int i = 0; i < chairTransforms.Length; i++)
      {
        if (chairActive[i] == 1)
          chairTransforms[i].position = new Vector3(chairPos[i].x, chairTransforms[i].position.y, chairPos[i].y);
      }
    }


    void SpawnChair(float2 pos, float2 vel, int owner)
    {
      for (int i = 0; i < chairActive.Length; i++)
      {
        if (chairActive[i] == 0)
        {
          chairPos[i] = pos;
          chairVel[i] = vel;
          chairOwner[i] = owner;
          chairActive[i] = 1;

          if (i < chairTransforms.Length && chairTransforms[i] != null)
          {
            chairTransforms[i].position = new Vector3(pos.x, chairTransforms[i].position.y, pos.y);
          }
          break;
        }
      }
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
    struct ChairNPCHitJob : IJobParallelFor
    {
      // Chair data
      [ReadOnly] public NativeArray<float2> chairs;
      public NativeArray<byte> chairActive;

      // NPC data (read-only in job, write deferred)
      [ReadOnly] public NativeArray<float2> npcs;
      [ReadOnly] public NativeArray<int> chairOwner;

      // Distance squared for hit detection
      public float hitRadiusSq;

      // Queue for deferred writes to NPCs
      public NativeQueue<int>.ParallelWriter npcHitWriter;

      public void Execute(int c)
      {
        // Skip inactive chairs
        if (chairActive[c] == 0)
          return;

        float2 chairPos = chairs[c];

        // Check collision with all NPCs
        for (int n = 0; n < npcs.Length; n++)
        {
          float2 diff = npcs[n] - chairPos;
          if (math.dot(diff, diff) < hitRadiusSq)
          {
            // Skip self-hit
            if (chairOwner[c] == n)
              continue;

            // Safe deferred write: enqueue NPC index
            npcHitWriter.Enqueue(n);

            // Deactivate chair immediately (safe: writes to own index)
            chairActive[c] = 0;

            // Chair hits only one NPC
            break;
          }
        }
      }
    }

    [BurstCompile]
    struct NPCUpdateJob : IJobParallelFor
    {
      public NativeArray<byte> npcAngry;
      public NativeArray<float> npcCooldown;
      public NativeArray<byte> throwRequest;

      public float dt;
      public float throwCooldown;

      public void Execute(int i)
      {
        throwRequest[i] = 0;
        if (npcAngry[i] == 1)
        {
          npcCooldown[i] -= dt;
          if (npcCooldown[i] <= 0f)
          {
            throwRequest[i] = 1;
            npcCooldown[i] = throwCooldown;
          }
        }
      }
    }
  }

}
