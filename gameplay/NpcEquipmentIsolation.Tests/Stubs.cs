// Engine-free fixture types for executing the production inventory-isolation code.
namespace UnityEngine { public sealed class GameObject { } }
public static class ItemDrop
{
    public sealed class ItemData
    {
        public UnityEngine.GameObject? m_dropPrefab;
        public int m_quality = 1;
        public ItemData Clone() => (ItemData)MemberwiseClone();
    }
}
public static class Humanoid
{
    public sealed class ItemSet { public UnityEngine.GameObject[] m_items = []; }
}
