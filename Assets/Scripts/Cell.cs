using UnityEngine;

public class Cell
{
    public enum Type
    {
        Empty,
        Mine,   // 天然地雷
        Number,
    }

    public enum Owner
    {
        None,
        PlayerA,
        PlayerB,
    }

    public Vector3Int position;
    public Type type;
    public int number;
    
    // 基础状态
    public bool revealed;
    public bool exploded;
    public bool chorded;

    // 领地与冷却
    public Owner owner = Owner.None;
    public int unlockTurn = 0; // 解锁的回合数 (取代了 float timer)

    // 塔防
    public bool hasTower = false;
    public Owner towerOwner = Owner.None;

    // --- 新增：人造陷阱 ---
    public bool hasTrap = false;
    public Owner trapOwner = Owner.None;

    // --- 新增：焦土政策 (耐久度) ---
    public int maxDurability = 3;
    public int currentDurability = 3;
    public bool isBedrock = false; // 是否已变为废墟(不可通过，不可占领)

    public bool flaggedP1; // 玩家A的标记
    public bool flaggedP2; // 玩家B的标记

    // 为了方便逻辑调用，可以加一个快捷访问
    public bool IsFlaggedBy(Owner player) {
        if (player == Owner.PlayerA) return flaggedP1;
        if (player == Owner.PlayerB) return flaggedP2;
        return false;
    }

    public bool isTowerRevealed = false;
}