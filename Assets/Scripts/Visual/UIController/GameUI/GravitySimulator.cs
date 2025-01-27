using UnityEngine;

//Literately just follow gravity. Forget everything else
public class GravitySimulator : MonoBehaviour
{
    public Vector3 velocity = Vector3.zero; // Initial velocity
    void Update()
    {
        velocity += Physics.gravity * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
    }
}