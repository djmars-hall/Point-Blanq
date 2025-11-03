using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

public interface IObjectPoolable
{
    public bool IsPoolable { get; set; }
    public bool IsPoolSpawned { get; set; }
}

public class ObjectPool<T> where T : MonoBehaviour, IObjectPoolable
{
    public T[] pool { get; set; }

    public ObjectPool(int poolSize)
    {
        pool = new T[poolSize];
    }

    public T Spawn(Vector3 pos, Vector3 rot)
    {
        T to_spawn = null;
        foreach (T o in pool)
        {
            if (o == null) continue;
            if (!o.IsPoolSpawned)
            {
                to_spawn = o;
                break;
            }
        }
        if (to_spawn == null)
        {
            Debug.Log("Could not find an object from the pool to spawn!");
            return null;
        }
        to_spawn.gameObject.SetActive(true);
        to_spawn.transform.position = pos;
        to_spawn.transform.eulerAngles = rot;
        return to_spawn;
    }

    public int FindFirstNull()
    {
        return Array.IndexOf(pool, null);
    }
    public int RegisterSpawnable(T obj)
    {
        int ind = FindFirstNull();
        if (ind == -1)
        {
            Debug.Log("There was no space in the object pool for " + obj.ToString() + "! Destroying...");
            GameObject.Destroy(obj);
            return -1;
        }
        pool[ind] = obj;
        obj.gameObject.SetActive(false);
        return ind;
    }

}
