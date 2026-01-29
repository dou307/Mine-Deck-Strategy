using UnityEngine;

public class Cell
{
    public enum Type
    {
        Empty,
        Mine,
        Number,
    }

    //增加玩家枚举
    public enum Owner
    {
        None,
        PlayerA,
        PlayerB,
    }

    public Vector3Int position;
    public Type type;
    public int number;
    public bool revealed;
    public bool flagged;
    public bool exploded;
    public bool chorded;

    public Owner owner = Owner.None;
    public float lockTimer = 0f;//占领锁定倒计时

    public bool hasTower = false;       // 是否有塔
    public Owner towerOwner = Owner.None; // 塔归属于谁
}
