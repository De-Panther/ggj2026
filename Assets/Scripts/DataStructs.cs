using Unity.Mathematics;

namespace Ggj2026Game
{
  public struct NPCData
  {
    public float2 pos;
    public float2 vel;
    public float cooldown;
    public byte angry;
  }

  public struct ChairData
  {
    public float2 pos;
    public float2 vel;
    public byte active;
    public byte nearbyPlayer; // Is there a nearby player
    public int prevCell;
  }

  public struct PlayerData
  {
    public float2 pos;
  }

  public struct ChairThrow
  {
    public int npcIndex;     // which NPC is throwing
    public int chairIndex;   // which chair to throw
    public float2 direction; // throw direction
    public float speed;      // optional: throw speed
  }

  public struct ChairHit
  {
    public int chairIndex;
    public int npcIndex;
  }
}
