using Unity.Mathematics;

namespace Ggj2026Game
{
  public struct NPCData
  {
    public float2 pos;
    public byte isAngry;
    public float throwCooldown;
  }

  public struct ChairData
  {
    public float2 pos;
    public float2 vel;
    public byte active;
    public int owner; // -1 = player
  }

  public struct PlayerData
  {
    public float2 pos;
  }
}
