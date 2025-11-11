using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.VisualScripting;
using Unity.Services.Matchmaker.Models;

public class PlayerController : BaseCharController, IObjectPoolable
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

    // Spatial grid tracking
    private Vector2Int currentCell;

    // Object Pooling
    public static ObjectPool<PlayerController> objectPool = new ObjectPool<PlayerController>(16);
    [SerializeField] bool _isPoolable = false;
    public bool IsPoolable { get { return _isPoolable; } set { _isPoolable = true; } }
    public bool IsPoolSpawned { get; set; } = false;
    protected override void Awake() {
        base.Awake();
        if (IsPoolable) objectPool.RegisterSpawnable(this); 
    }


    private void Start()
    {
        if (!IsOwner) return;

        // Register with spatial grid
        if (SpatialGrid.Instance != null)
        {
            currentCell = SpatialGrid.Instance.GetCellCoords(transform.position);
            SpatialGrid.Instance.RegisterCharacter(this, currentCell);
        }
    }

    void Update()
    {
        if (!IsOwner) { return; }


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


    void FixedUpdate()
    {
        //rb.linearVelocity = Vector3.zero;
        //rb.angularVelocity = Vector3.zero;
        if (!GameManager.Instance.ignoreNetwork && !IsOwner) { return; }

        // Update spatial grid cell if changed
        if (SpatialGrid.Instance != null)
        {
            Vector2Int newCell = SpatialGrid.Instance.GetCellCoords(transform.position);
            if (newCell != currentCell)
            {
                SpatialGrid.Instance.UpdateCharacter(this, currentCell, newCell);
                currentCell = newCell;
            }
        }

        //Movement
        float h = moveDir.x;
        float v = moveDir.y;

        ProcessMovement(v, h);
    }


    private void LateUpdate()
    {
        if (!GameManager.Instance.ignoreNetwork && !IsOwner) return;

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
            BaseCharController hit_cc = hit.transform.GetComponent<BaseCharController>();
            bool back_hit = false;
            bool npc_hit = false;
            if (hit_cc == null && hit.transform.tag == "back_target")
            {
                hit_cc = hit.transform.parent.GetComponent<BaseCharController>();
                back_hit = true;
                Debug.Log("Back hit!");
            }
            if (hit_cc is BaseCharController)
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
                BountyManager.Instance.CheckKillRpc(npc_hit, OwnerClientId, hit_player_id, 
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
    }

    [Rpc(SendTo.Owner)]
    public void ParentCameraRpc()
    {
        if (!IsOwner) { Debug.LogError("YOUR SUFFERING FROM A BIG BAD LACK OF CAMERA!"); return; }
        Camera.main.transform.SetParent(transform.GetChild(0));
        Camera.main.transform.localPosition = Vector3.zero;
        Camera.main.transform.localRotation = Quaternion.identity;
    }

    public void ParentCamera()
    {
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

    // Unregister from grid when destroyed or disabled
    private void OnDestroy()
    {
        if (IsOwner && SpatialGrid.Instance != null)
        {
            SpatialGrid.Instance.UnregisterCharacter(this, currentCell);
        }
    }
}
