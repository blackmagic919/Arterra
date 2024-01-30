using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Destructor : MonoBehaviour
{
    public void DestroyObject()
    {
        Destroy(this.gameObject);
    }
}
