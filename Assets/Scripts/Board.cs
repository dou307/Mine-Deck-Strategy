using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
public class Board : MonoBehaviour
{
    public Tilemap tilemap { get; private set; }

    public Tile tileUnknown;
    public Tile tileEmpty;
    public Tile tileMine;
    public Tile tileExploded;
    public Tile tileFlag;
    public Tile tileNum1;
    public Tile tileNum2;
    public Tile tileNum3;
    public Tile tileNum4;
    public Tile tileNum5;
    public Tile tileNum6;
    public Tile tileNum7;
    public Tile tileNum8;

    public Tile tileRedTower;
    public Tile tileBlueTower;

    //定义玩家颜色
    public Color colorPlayerA = new Color(1f, 0.5f, 0.5f);//淡红色
    public Color colorPlayerB = new Color(0.5f, 0.5f, 1f);//淡蓝色
    public Color colorNone = Color.white;
    public Color colorLocked = Color.yellow;//黄色表示锁定中

    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    public void Draw(CellGrid grid, Cell.Owner viewer, int currentTurn)
    {
        int width = grid.Width;
        int height = grid.Height;
        int midX = width / 2; // 分界线

        // 【修正1】定义“前3个回合”：双方各动3次 = 总计6个Turn
        // 如果 currentTurn <= 6，说明还是盲眼阶段
        // 如果 currentTurn > 6，说明迷雾散开，进入“势力图”阶段
        bool isBlindPhase = currentTurn <= 6;

        for (int x = 0; x < width; x++)
        {
            for (int y = -3; y < height + 3; y++) // 渲染范围
            {
                if (!grid.InBounds(x, y)) continue;

                Cell cell = grid[x, y];
                Vector3Int pos = cell.position;
                
                // 1. 判断是否在敌方半场 (相对于观看者)
                bool isEnemySide = false;
                if (viewer == Cell.Owner.PlayerA && x >= midX) isEnemySide = true;      // P1看右边
                else if (viewer == Cell.Owner.PlayerB && x < midX) isEnemySide = true; // P2看左边

                // 2. 准备绘制参数
                Tile tileToDraw = null;
                Color colorToDraw = colorNone;

                // --- 逻辑分支 A：敌方半场 ---
                if (isEnemySide)
                {
                    if (isBlindPhase)
                    {
                        // 阶段一：完全看不见 (前3轮)
                        tileToDraw = null; 
                    }
                    else
                    {
                        // 阶段二：势力迷雾 (3轮后)
                        // 【修正2】不管下面有什么（塔、雷、数字），统统只画“未知方块”
                        tileToDraw = tileUnknown;

                        // 【修正2】只显示颜色（所有权）
                        // 如果对方占了，显示对方颜色；如果是无主的，显示白色
                        if (cell.owner == Cell.Owner.PlayerA) colorToDraw = colorPlayerA;
                        else if (cell.owner == Cell.Owner.PlayerB) colorToDraw = colorPlayerB;
                        else colorToDraw = colorNone;
                        
                        // 锁定状态依然可以显示（可选，如果想彻底保密也可以去掉）
                        if (cell.lockTimer > 0) colorToDraw = colorLocked;
                    }
                }
                // --- 逻辑分支 B：己方半场 (或者旁观者) ---
                else
                {
                    // 正常逻辑：显示所有细节（数字、塔、插旗等）
                    tileToDraw = GetTile(cell);
                    
                    // 正常颜色逻辑
                    if (cell.lockTimer > 0) colorToDraw = colorLocked;
                    else if (cell.owner == Cell.Owner.PlayerA) colorToDraw = colorPlayerA;
                    else if (cell.owner == Cell.Owner.PlayerB) colorToDraw = colorPlayerB;
                    else colorToDraw = colorNone;
                }

                // 3. 执行绘制
                tilemap.SetTile(pos, tileToDraw);
                if (tileToDraw != null) // 只有画了东西才设颜色
                {
                    tilemap.SetTileFlags(pos, TileFlags.None);
                    tilemap.SetColor(pos, colorToDraw);
                }
            }
        }
    }
     private Tile GetTile(Cell cell)
    {
        // --- 新增：防御塔渲染优先级最高 ---
        if (cell.hasTower) {
            return cell.towerOwner == Cell.Owner.PlayerA ? tileRedTower : tileBlueTower;
        }

        if (cell.revealed) {
            return GetRevealedTile(cell);
        } else if (cell.flagged) {
            return tileFlag;
        } else if (cell.chorded) {
            return tileEmpty;
        } else {
            return tileUnknown;
        }
    }

    private Tile GetRevealedTile(Cell cell)
    {
        switch (cell.type)
        {
            case Cell.Type.Empty: return tileEmpty;
            case Cell.Type.Mine: return cell.exploded ? tileExploded : tileMine;
            case Cell.Type.Number: return GetNumberTile(cell);
            default: return null;
        }
    }

    private Tile GetNumberTile(Cell cell)
    {
        switch (cell.number)
        {
            case 1: return tileNum1;
            case 2: return tileNum2;
            case 3: return tileNum3;
            case 4: return tileNum4;
            case 5: return tileNum5;
            case 6: return tileNum6;
            case 7: return tileNum7;
            case 8: return tileNum8;
            default: return null;
        }
    }

}
