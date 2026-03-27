using UnityEngine;

public abstract class PlayerStateBase : MonoBehaviour
{
    protected Player player;

    protected virtual void Awake()
    {
        player = GetComponent<Player>();
    }

    public abstract void BeginState();
    public abstract void UpdateState();
    public abstract void EndState();
}