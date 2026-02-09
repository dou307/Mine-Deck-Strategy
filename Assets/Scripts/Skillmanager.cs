using UnityEngine;
using Unity.Netcode;

public enum SkillType
{
    None,
    PlaceTrap,    // 陷阱
    Unchord,      // 中键技能
    RadarScan     // 预留卡牌技能
}
public class SkillManager : NetworkBehaviour
{
    private Game game;

    private void Awake()
    {
        game = GetComponent<Game>();
    }

    // 统一技能执行入口
    public bool ExecuteSkill(SkillType type, Cell.Owner player, int x, int y)
    {
        switch (type)
        {
            case SkillType.PlaceTrap:
                return LogicPlaceTrap(player, x, y);
            case SkillType.Unchord:
                return LogicUnchord(player, x, y);
            default:
                return false;
        }
    }

    private bool LogicPlaceTrap(Cell.Owner player, int x, int y)
    {
        if (!game.grid.TryGetCell(x, y, out Cell cell)) return false;

        // 布置陷阱的条件：必须是自己的领地，且目前没有塔或陷阱
        if (cell.owner != player || cell.hasTower || cell.hasTrap || cell.isBedrock) return false;

        // 数量限制检查
        int currentTraps = (player == Cell.Owner.PlayerA) ? game.GetTrapCount(Cell.Owner.PlayerA) : game.GetTrapCount(Cell.Owner.PlayerB);
        if (currentTraps >= game.maxTrapsPerPlayer) return false;

        // 能量消耗检查 (假设布置陷阱消耗 20 能量)
        if (!game.HasEnoughEnergy(player, 20)) return false;

        // 执行逻辑
        game.ModifyEnergy(player, -20);
        cell.hasTrap = true;
        cell.trapOwner = player;
        game.IncrementTrapCount(player);
        
        return true; // 返回 true 表示技能释放成功，消耗回合
    }

    public bool LogicUnchord(Cell.Owner player, int x, int y)
    {
        Cell center = game.grid.GetCell(x, y);
        
        // 1. 基础检查：只有已翻开的数字格可以触发
        if (center == null || !center.revealed || center.type != Cell.Type.Number) return false;

        // 2. 检查：操作者自己标记的旗子 + 塔的数量 是否等于 该格子的数字
        // 注意：CountAdjacentFlagsAndTowers 需要在 Game.cs 中改为 public
        if (CountAdjacentFlagsAndTowers(center, player) >= center.number)
        {
            bool anyChange = false;
            
            // 遍历周围 8 个格子进行尝试翻开
            for (int dx = -1; dx <= 1; dx++) 
            {
                for (int dy = -1; dy <= 1; dy++) 
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    if (game.grid.TryGetCell(center.position.x + dx, center.position.y + dy, out Cell neighbor)) 
                    {
                        if (neighbor.IsFlaggedBy(player)) continue;

                        // 2. 如果这格子已经有塔了，也跳过（虽然 ProcessReveal 会拦，但这里跳过更安全）
                        if (neighbor.hasTower) continue;

                        // --- 核心修复结束 ---

                        // 对剩下的没插旗的格子执行翻开
                        if (game.ProcessReveal(player, neighbor)) 
                        {
                            anyChange = true;
                        }
                    }
                }
            }
            return anyChange;
        }
        return false;
    }

    private int CountAdjacentFlagsAndTowers(Cell cell, Cell.Owner actor)
    {
        int count = 0;
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                if (x == 0 && y == 0) continue;
                if (game.grid.TryGetCell(cell.position.x + x, cell.position.y + y, out Cell neighbor)) {
                    
                    // 根据行动者身份判断他眼中的旗子
                    bool hasFlag = (actor == Cell.Owner.PlayerA) ? neighbor.flaggedP1 : neighbor.flaggedP2;
                    
                    if ((!neighbor.revealed && hasFlag) || neighbor.hasTower) {
                        count++;
                    }
                }
            }
        }
        return count;
    }
}