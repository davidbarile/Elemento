using Lean.Pool;
using UnityEngine;

public class DespawnParticle : MonoBehaviour
{
    public void SetDespawn(float inDelay)
    {
        Invoke(nameof(Despawn), inDelay);
    }

    private void Despawn()
    {
        LeanPool.Despawn(this);
    }
}