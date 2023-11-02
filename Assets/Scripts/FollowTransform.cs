using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    [SerializeField]
    private Transform follow;

    public void Update()
    {
        this.transform.position = follow.position;
    }
}
