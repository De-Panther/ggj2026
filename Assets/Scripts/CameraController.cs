using System.Collections.Generic;
using UnityEngine;

namespace Ggj2026Game
{
  [RequireComponent(typeof(Camera))]
  public class CameraController : MonoBehaviour
  {
    [Header("Targets")]
    public Transform player;
    public List<Transform> npcs = new List<Transform>();

    [Header("Area Limits")]
    public BoxCollider areaBounds;

    [Header("Follow Settings")]
    public float followSpeed = 5f;
    public float height = 15f;
    public Vector3 offset = new Vector3(0, 0, -5);

    [Header("Zoom Settings (Perspective)")]
    public float minFOV = 35f;
    public float maxFOV = 70f;
    public float zoomSpeed = 3f;
    public float zoomFactor = 1.2f;
    public float npcInfluenceDistance = 30f;

    private Camera cam;

    void Awake()
    {
      cam = GetComponent<Camera>();
      cam.orthographic = false;
    }

    void LateUpdate()
    {
      if (!player || !areaBounds)
        return;

      Bounds targetsBounds = CalculateTargetsBounds();

      Vector3 targetPos = CalculateTargetPosition(targetsBounds);
      targetPos = ClampToArea(targetPos);

      transform.position = Vector3.Lerp(
          transform.position,
          targetPos,
          followSpeed * Time.deltaTime
      );

      float targetFOV = CalculateTargetFOV(targetsBounds);
      cam.fieldOfView = Mathf.Lerp(
          cam.fieldOfView,
          targetFOV,
          zoomSpeed * Time.deltaTime
      );
    }

    // -----------------------------
    // Bounds Calculation
    // -----------------------------
    Bounds CalculateTargetsBounds()
    {
      Bounds bounds = new Bounds(player.position, Vector3.zero);

      foreach (Transform npc in npcs)
      {
        if (!npc)
          continue;

        if (Vector3.Distance(player.position, npc.position) > npcInfluenceDistance)
          continue;

        bounds.Encapsulate(npc.position);
      }

      return bounds;
    }

    // -----------------------------
    // Position Calculation
    // -----------------------------
    Vector3 CalculateTargetPosition(Bounds bounds)
    {
      Vector3 center = bounds.center;

      Vector3 target = center + offset;
      target.y = height;

      return target;
    }

    // -----------------------------
    // Zoom (FOV)
    // -----------------------------
    float CalculateTargetFOV(Bounds bounds)
    {
      float spread = Mathf.Max(bounds.size.x, bounds.size.z);
      float fov = spread * zoomFactor;
      return Mathf.Clamp(fov, minFOV, maxFOV);
    }

    // -----------------------------
    // Clamp to Area
    // -----------------------------
    Vector3 ClampToArea(Vector3 target)
    {
      Bounds b = areaBounds.bounds;

      target.x = Mathf.Clamp(target.x, b.min.x, b.max.x);
      target.z = Mathf.Clamp(target.z, b.min.z, b.max.z);

      return target;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
      if (!player)
        return;

      Gizmos.color = Color.yellow;
      Gizmos.DrawWireSphere(player.position, npcInfluenceDistance);
    }
#endif
  }
}
