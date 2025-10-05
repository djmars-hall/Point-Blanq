using UnityEngine;
using Unity.Netcode;

public class CharacterController : NetworkBehaviour
{
    [SerializeField] internal float speed = 3.5f;
    [SerializeField] internal float rotationSpeed = 60f;

    public Animator characterAnimator;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
