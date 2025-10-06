using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.VisualScripting;

public class PlayerController : CharacterController
{
    public InputActionAsset inputActions;

    public InputActionReference move;
    public InputActionReference aim;
    public InputActionReference shoot;
    public Transform muzzle;

    private Vector2 moveDir;
    //private Vector2 aimDir;

    private float aimValue = 0;
    public float aimThreshold; //min value of holding the trigger before the gun is raised, 0 - 1

    public bool isAiming;

    void Update()
    {
        if (!IsOwner) { return; }

        //Movement
        float h = moveDir.x;
        float v = moveDir.y;

        /*
        Vector3 move = new Vector3(0, 0, v * Time.deltaTime * speed);
        if (move != Vector3.zero || rot != 0)
        {
            Vector3 newPos = transform.position + move.z * transform.forward;
            transform.position = newPos;
            transform.Rotate(new Vector3(0, rot, 0));
            UpdatePositionClientRpc(newPos, transform.rotation);
        }
        */
        ProcessMovement(v, h);

        //Aiming
        if (aimValue > aimThreshold)
        {
            if (!isAiming)
            {
                PullOutGunRpc();
                isAiming = true;
            }

        }
        else
        {
            if (isAiming)
            {
                PutAwayGunRpc();
                isAiming = false;
            }
        }
    }

    private void LateUpdate()
    {
        //Movement
        moveDir = move.action.ReadValue<Vector2>();
        //aimDir = aim.action.ReadValue<Vector2>();

        //Aiming
        aimValue = aim.action.ReadValue<float>();
    }

    private void OnEnable()
    {
        shoot.action.performed += Shoot;
    }

    private void OnDisable()
    {
        shoot.action.performed -= Shoot;
    }

    public void Shoot(InputAction.CallbackContext obj)
    {
        if (!isAiming) return;

        // Networked shooting logic
        ShootRpc();
        // Local raycast checking, and reporting hit to host
        RaycastHit hit;
        if (Physics.Raycast(muzzle.position, muzzle.forward, out hit, 10))
        {
            Debug.Log(hit.transform.gameObject.name);
            CharacterController hit_cc = hit.transform.GetComponent<CharacterController>();
            bool back_hit = false;
            bool npc_hit = false;
            if (hit_cc == null && hit.transform.tag == "back_target")
            {
                hit_cc = hit.transform.parent.GetComponent<CharacterController>();
                back_hit = true;
                Debug.Log("Back hit!");
            }
            if (hit_cc is CharacterController)
            {
                ulong hit_player_id = hit_cc.OwnerClientId;
                if (hit_cc is NPCController)
                {
                    Debug.Log("An NPC was hit! " + back_hit);
                    npc_hit = true;
                }
                else if (hit_cc is PlayerController)
                {
                    Debug.Log("A player was hit! " + back_hit);
                }
                BountyManager.Instance.CheckKill(npc_hit, OwnerClientId, hit_player_id, 
                    Vector3.Distance(transform.position, hit.transform.position), 
                    back_hit);
            }
        }
    }

    //Network Code
    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void UpdatePositionClientRpc(Vector3 pos, Quaternion rot)
    {
        transform.position = pos;
        transform.rotation = rot;
        Debug.Log("NOT_ME");
    }

    [Rpc(SendTo.Owner)]
    public void ParentCameraRpc()
    {
        if (!IsOwner) { Debug.LogError("BIG BAD!"); return; }
        Debug.Log("should be parenting camera");
        Camera.main.transform.SetParent(transform.GetChild(0));
        Camera.main.transform.localPosition = Vector3.zero;
        Camera.main.transform.localRotation = Quaternion.identity;
    }

    [Rpc(SendTo.Everyone)]
    public void PullOutGunRpc()
    {
        characterAnimator.Play("RaiseGun", 0, 0);
    }
    [Rpc(SendTo.Everyone)]
    public void PutAwayGunRpc()
    {
        characterAnimator.Play("Default", 0, 0);
    }

    [Rpc(SendTo.Everyone)]
    public void ShootRpc()
    {
        Debug.Log("BANG");
    }

}
