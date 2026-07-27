using UnityEngine;
using Unity.Netcode;

public enum SkillType
{
    None,
    PlaceTrap,    // 陷阱
    Unchord,      // 中键技能
    RadarScan,     // 雷达
    HealSmall,    // 小治疗-纳米蜂群修复
    HealLarge,     // 大治疗-生物凝胶注射
    Ghost,       // 幽灵
    DataRollback, // 数据回滚
    CutWire,       // 切断电路
    BuildTower // 防御塔技能
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
            case SkillType.RadarScan: 
                return LogicRadarScan(player, x, y);
            case SkillType.HealSmall:
                // 参数：玩家, 治疗量(25), 能量消耗(20) -> 数值你可以自己调
                return LogicHeal(player, 25, 20); 
            case SkillType.HealLarge:
                // 参数：玩家, 治疗量(60), 能量消耗(50)
                return LogicHeal(player, 60, 50);
            case SkillType.Ghost:
                return LogicGhost(player, x, y);
            case SkillType.DataRollback:
                return LogicDataRollback(player, x, y);
            case SkillType.CutWire:
                return LogicCutWire(player, x, y);
            case SkillType.BuildTower:
                // 获取格子对象
                if (game.grid.TryGetCell(x, y, out Cell target))
                {
                    // 直接复用 Game.cs 里写好的造塔逻辑
                    // 注意：需要去 Game.cs 把 ProcessBuildTower 改成 public
                    return game.ProcessBuildTower(player, target);
                }
                return false;
            default:
                return false;
        }
    }

    private bool LogicPlaceTrap(Cell.Owner player, int x, int y)
    {
        if (!game.grid.TryGetCell(x, y, out Cell cell)) return false;

        // --- 1. 基础校验 ---
        // 布置陷阱的条件：必须是自己的领地，且目前没有塔、陷阱、也不是废墟
        if (cell.owner != player || cell.hasTower || cell.hasTrap || cell.isBedrock) 
        {
            Debug.Log("无法在此处部署陷阱：领地不符或已有设施。");
            return false;
        }

        // --- 2. 数量限制检查 ---
        // 这里会自动根据 player 是 A 还是 B 去取对应的陷阱计数
        int currentTraps = game.GetTrapCount(player); 
        if (currentTraps >= game.maxTrapsPerPlayer) 
        {
            Debug.Log("陷阱部署数量已达上限！");
            return false;
        }

        // --- 3. 能量消耗检查 ---
        int energyCost = 20; // 建议统一用变量控制
        if (!game.HasEnoughEnergy(player, energyCost)) return false;

        // --- 4. 执行逻辑 ---
        game.ModifyEnergy(player, -energyCost);
        
        cell.hasTrap = true;
        cell.trapOwner = player; // 【关键】这里赋值后，Board.cs 就会自动判断画红色还是蓝色 Tile
        
        game.IncrementTrapCount(player);
        
        // --- 5. 同步数据 ---
        // 必须调用 SyncCell，否则对方客户端不知道这里多了个陷阱（虽然对方看不见，但后端数据要同步）
        game.SyncCell(cell);
        
        Debug.Log($"玩家 {player} 部署了陷阱。");
        return true; 
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

    //雷达扫描（无伤占领地雷）
    private bool LogicRadarScan(Cell.Owner player, int x, int y)
    {
        // 1. 能量检查 (假设这种强力排雷需要 30 能量)
        if (!game.HasEnoughEnergy(player, 30)) return false;

        bool foundAnyMine = false;

        // 2. 遍历 3x3 区域
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (game.grid.TryGetCell(x + dx, y + dy, out Cell neighbor))
                {
                    // 只有该格子确实是雷，且还没被翻开/炸开时才处理
                    if (neighbor.type == Cell.Type.Mine && !neighbor.revealed)
                    {
                        // --- 核心逻辑：无伤占领地雷 ---
                        neighbor.revealed = true;
                        neighbor.exploded = false; // 标记为未爆炸（显示为地雷图块）
                        neighbor.owner = player;    // 归属于释放技能的玩家
                        
                        // 通知所有客户端同步这个格子的新状态
                        game.SyncCell(neighbor); 
                        foundAnyMine = true;
                    }
                }
            }
        }

        // 无论是否扫到雷，只要技能发动了就扣除能量
        game.ModifyEnergy(player, -30);
        
        // 如果扫到了雷，给个反馈
        if (foundAnyMine) {
            Debug.Log($"{player} 使用雷达精准捕获了地雷！");
        }

        return true; // 返回 true 表示消耗回合
    }

    // --- 通用治疗逻辑 ---
    private bool LogicHeal(Cell.Owner player, int healAmount, int energyCost)
    {
        // 1. 能量检查
        if (!game.HasEnoughEnergy(player, energyCost)) return false;

        // 2. 满血检查 (可选：如果是满血，不让用，避免浪费)
        int currentHP = (player == Cell.Owner.PlayerA) ? game.hpP1.Value : game.hpP2.Value;
        if (currentHP >= game.maxHealth) 
        {
            Debug.Log("生命值已满，无需治疗。");
            return false;
        }

        // 3. 扣除能量
        game.ModifyEnergy(player, -energyCost);

        // 4. 恢复生命 
        // 技巧：Game.cs 里的 ModifyHP 接收的是"伤害"，传入负数就是"治疗"
        // 比如传入 -25，也就是 hp - (-25) = hp + 25
        game.ModifyHP(player, -healAmount); 

        Debug.Log($"{player} 使用了治疗，恢复 {healAmount} 点生命。");
        return true; 
    }
    private bool LogicGhost(Cell.Owner player, int x, int y)
    {
        int cost = 70;
        if (!game.HasEnoughEnergy(player, cost)) return false;

        // 2. 扣能量
        game.ModifyEnergy(player, -cost);

        // 3. 【核心】设置无敌回合
        // 设为 2：代表 "我的当前回合剩余时间" + "敌人的一整个回合"。
        // 当我下一次行动开始时，无敌结束。
        // 如果想要 "敌人打我两轮我都无敌"，可以设为 3 或 4。
        if (player == Cell.Owner.PlayerA)
            game.immuneTurnsP1.Value = 4;
        else
            game.immuneTurnsP2.Value = 4;

        Debug.Log($"{player} 签订了幽灵协议，进入无敌状态！");
        return true;
    }

    //数据回滚
    private bool LogicDataRollback(Cell.Owner player, int x, int y)
    {
        // 1. 能量检查 (这是一个强力控制技能，建议 45-50 费)
        int cost = 70;
        if (!game.HasEnoughEnergy(player, cost)) return false;

        bool hasEffect = false; // 记录是否至少重置了一个格子

        // 2. 遍历 3x3 区域
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                int tx = x + i;
                int ty = y + j;

                if (game.grid.TryGetCell(tx, ty, out Cell cell))
                {
                    // 【核心规则】
                    // 只要格子被翻开了(revealed)，不管是谁的，都给它“回滚”
                    // 策略点：你可以回滚敌人的地盘，甚至可以回滚自己的（用来修补被炸坏的塔？）
                    // 这里我们设定为：主要用来攻击敌人，强制剥夺地盘
                    
                    if (cell.revealed) 
                    {
                        // 1. 剥夺所有权
                        cell.owner = Cell.Owner.None;
                        
                        // 2. 强制盖上迷雾
                        cell.revealed = false;
                        cell.isTowerRevealed = false; // 如果有塔，塔也会消失在迷雾中
                        
                        // 3. (可选) 是否要清除陷阱？
                        cell.hasTrap = false; // 如果你想让它彻底重置，取消注释这行
                        cell.hasTower = false; // 如果你想让它连塔都拆了，取消注释这行(太强了，不建议)
                        // 目前的逻辑是：塔还在，但变回了“未知”状态，敌人得重新探一遍

                        // 4. 同步数据
                        game.SyncCell(cell);
                        hasEffect = true;
                    }
                }
            }
        }

        // 3. 结算
        if (hasEffect)
        {
            game.ModifyEnergy(player, -cost);
            Debug.Log($"{player} 在 ({x},{y}) 执行了数据回滚，区域已重置！");
            return true;
        }
        else
        {
            Debug.Log("该区域已经是原始状态，无需回滚。");
            return false; // 省得浪费能量
        }
    }

    private bool LogicCutWire(Cell.Owner player, int x, int y)
    {
        // 1. 获取目标格子
        if (!game.grid.TryGetCell(x, y, out Cell cell)) return false;

        // 2. 只能阻断敌人的地盘 (如果是空地或者自己的，没意义)
        if (cell.owner == Cell.Owner.None || cell.owner == player) 
        {
            Debug.Log("只能阻断敌方已占领的节点！");
            return false;
        }

        bool isBaseP1 = (x == 0 && y == 0);
        bool isBaseP2 = (x == game.width - 1 && y == game.height - 1);

        if (isBaseP1 || isBaseP2)
        {
            Debug.Log("错误：无法阻断受到核心保护的基地节点！");
            return false; // 直接返回，不扣费
        }

        // 3. 能量消耗 (40费，这是一个关键的战术技能)
        int cost = 40;
        if (!game.HasEnoughEnergy(player, cost)) return false;

        game.ModifyEnergy(player, -cost);

        // 4. 施加阻断状态 (持续 3 回合)
        game.RegisterJam(x, y, 3);
        Debug.Log($"{player} 阻断了 ({x},{y}) 的信号连接！");
        return true;
    }
}