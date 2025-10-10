using UnityEngine;

public class FunSun : MonoBehaviour
{
    [SerializeField] float rotationSpeed = 0.15f; // degrees per second
    private float currentXRot;

    void Start()
    {
        currentXRot = transform.rotation.eulerAngles.x;
    }

    // Update is called once per frame
    void Update()
    {
        currentXRot += rotationSpeed * Time.deltaTime;
        if (currentXRot >= 360f) { currentXRot -= 360f; }
        transform.rotation = Quaternion.Euler(currentXRot, 15, 0);
    }
}
