using System;
using UnityEngine;

public class OptimizeItem : MonoBehaviour
{
    public static GameObject Container;

    [SerializeField] private bool IsParent;

    private static int ObjectCount;

    public static void DestroyContainer()
    {
        if(Container != null)
            Destroy(Container);

        ObjectCount = 0;

        GC.Collect();
    }

    public void Awake()
    {
        if(Container == null)
        {
            Container = new GameObject();
            Container.name = "Optimize Container";
            Container.SetActive(false);
        }

        if(this.IsParent)
        {
            var children = this.GetComponentsInChildren<OptimizeItem>(true);

            foreach (var child in children)
            {
                if(child.transform != this.transform)
                {
                    ParentToOptimizeContainer(child.transform);
                    ++ObjectCount;
                }
            }

            Container.name = $"Optimize Container ({ObjectCount})";
        }
    }

    public void ParentToOptimizeContainer(Transform inTransform)
    {
        inTransform.SetParent(Container.transform);
    }

    public void ParentToOptimizeContainer()
    {
        this.transform.SetParent(Container.transform);
    }
}