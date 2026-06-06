using System.Collections.Generic;
using UnityEngine;

//It is common to create a class to contain all of your
//extension methods. This class must be static.
public static class ExtensionMethods
{
    public static void RandomizeList<T>( this List<T> inList )
    {
        List<T> tempList = new List<T>();

        int rnd;

        while(inList.Count > 0)
        {
            rnd = UnityEngine.Random.Range(0, inList.Count);
            T obj = inList[rnd];
            tempList.Add(obj);
            inList.Remove(obj);
        }

        //inList = tempList;

        foreach (T obj in tempList)
            inList.Add(obj);
    }

    public static List<T> CreateRandomizedList<T>(this List<T> inList)
    {
        List<T> randomList = new List<T>();
        List<T> tempList = new List<T>();

        foreach (T obj in inList)
            tempList.Add(obj);

        int rnd;

        while (tempList.Count > 0)
        {
            rnd = UnityEngine.Random.Range(0, tempList.Count);
            T obj = tempList[rnd];
            randomList.Add(obj);
            tempList.Remove(obj);
        }

        return randomList;
    }

    public static string ShowObjectPath(this Transform inTransform)
    {
        string pathString = inTransform.gameObject.name;

        Transform t = inTransform;

        while( t.parent != null)
        {
            pathString = t.parent.gameObject.name + "/" + pathString;
            t = t.parent;
        }

        return pathString;
    }
}