using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
public class Board : MonoBehaviour
{
    public Tilemap tilemap { get; private set; }

    [Header("基础图块")]
    public Tile tileUnknown;
    public Tile tileEmpty;
    public Tile tileMine;
    public Tile tileExploded;
    public Tile tileFlag;
    public Tile tileBedrock; // 新增：焦土/废墟图块 (黑色或碎石)
    public Tile tileTrap;    // 新增：己方可见的陷阱图块

    [Header("数字图块")]
    public Tile tileNum1;
    public Tile tileNum2;
    public Tile tileNum3;
    public Tile tileNum4;
    public Tile tileNum5;
    public Tile tileNum6;
    public Tile tileNum7;
    public Tile tileNum8;

    [Header("建筑图块")]
    public Tile tileRedTower;
    public Tile tileBlueTower;

    [Header("颜色配置")]
    public Color colorPlayerA = new Color(1f, 0.5f, 0.5f); // 红
    public Color colorPlayerB = new Color(0.5f, 0.5f, 1f); // 蓝
    public Color colorNone = Color.white;
    public Color colorLocked = Color.gray; // 冷却中变灰
    public Color colorDamaged = new Color(0.7f, 0.7f, 0.7f); // 耐久度下降变暗
    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    public void Draw(CellGrid grid, Cell.Owner viewer, int currentTurn)
    {
        int width = grid.Width;
        int height = grid.Height;
        int midX = width / 2;
        bool isFogPhase = currentTurn <= 6;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) // 修正了y的范围
            {
                if (!grid.InBounds(x, y)) continue;

                Cell cell = grid[x, y];
                Vector3Int pos = cell.position;
                
                // 1. 迷雾判断
                bool isFogged = false;
                if (isFogPhase)
                {
                    if (viewer == Cell.Owner.PlayerA && x >= midX) isFogged = true;
                    if (viewer == Cell.Owner.PlayerB && x < midX) isFogged = true;
                }

                Tile tileToDraw = tileUnknown;
                Color colorToDraw = colorNone;

                // --- A. 废墟判断 (最高优先级) ---
                if (cell.isBedrock)
                {
                    tilemap.SetTile(pos, tileBedrock);
                    tilemap.SetTileFlags(pos, TileFlags.None);
                    tilemap.SetColor(pos, Color.white);
                    continue; 
                }

                // --- B. 确定防御塔可见性 ---
                // 逻辑：自己的塔始终可见，敌人的塔只有在 isTowerRevealed 时可见
                bool canSeeTower = false;
                if (cell.hasTower)
                {
                    if (cell.towerOwner == viewer || cell.isTowerRevealed)
                        canSeeTower = true;
                }

                if (isFogged)
                {
                    tileToDraw = tileUnknown;
                    colorToDraw = colorNone;
                }
                // --- C. 防御塔显示 (高优先级，覆盖插旗) ---
                else if (canSeeTower)
                {
                    tileToDraw = (cell.towerOwner == Cell.Owner.PlayerA) ? tileRedTower : tileBlueTower;
                    colorToDraw = GetPlayerColor(cell.owner);
                }
                // --- D. 已翻开格子的显示 ---
                else if (cell.revealed)
                {
                    if (cell.owner == viewer)
                    {
                        // 自己的地盘：看到完整信息（数字/陷阱等）
                        tileToDraw = GetFullDetailTile(cell);
                    }
                    else if (cell.owner != Cell.Owner.None)
                    {
                        // 敌人的地盘：如果塔没被发现（到这一步说明 canSeeTower 为 false）
                        // 则隐藏数字和塔，显示为“未知”图块，但保留占领颜色
                        tileToDraw = tileUnknown;
                    }
                    else
                    {
                        // 无主之地
                        tileToDraw = GetFullDetailTile(cell);
                    }
                    colorToDraw = GetPlayerColor(cell.owner);
                }
                // --- E. 未翻开格子的显示 (含插旗判断) ---
                else
                {
                    // 只有在不显示塔的情况下，才去判断插旗
                    bool myFlag = (viewer == Cell.Owner.PlayerA) ? cell.flaggedP1 : cell.flaggedP2;
                    
                    if (myFlag) tileToDraw = tileFlag;
                    else tileToDraw = tileUnknown;

                    // 未翻开的格子如果是被占领状态（比如盲注建塔后），也要显示颜色
                    colorToDraw = GetPlayerColor(cell.owner);
                }

                // --- F. 状态修饰 (冷却与耐久) ---
                if (!isFogged)
                {
                    if (cell.unlockTurn > currentTurn)
                        colorToDraw = Color.Lerp(colorToDraw, colorLocked, 0.5f);

                    if (cell.currentDurability < cell.maxDurability) 
                    {
                        float damageRatio = 1f - ((float)cell.currentDurability / cell.maxDurability);
                        colorToDraw = Color.Lerp(colorToDraw, Color.black, damageRatio * 0.4f);
                    }

                    // --- 添加：己方陷阱的高亮表现 ---
                    if (cell.hasTrap && cell.trapOwner == viewer)
                    {
                        // 在原有颜色基础上叠加一层紫色调，代表这里有陷阱
                        colorToDraw = Color.Lerp(colorToDraw,Color.green, 0.5f);
                    }
                }

                // 执行绘制
                tilemap.SetTile(pos, tileToDraw);
                if (tileToDraw != null)
                {
                    tilemap.SetTileFlags(pos, TileFlags.None);
                    tilemap.SetColor(pos, colorToDraw);
                }
            }
        }
    }
    // 获取完全信息的图块 (自己视角)
    private Tile GetFullDetailTile(Cell cell)
    {
        if (cell.hasTower) return (cell.towerOwner == Cell.Owner.PlayerA) ? tileRedTower : tileBlueTower;
        if (cell.exploded) return tileExploded;
        if (cell.type == Cell.Type.Mine) return tileMine; // 正常游戏不显示，调试用
        
        switch (cell.type)
        {
            case Cell.Type.Empty: return tileEmpty;
            case Cell.Type.Number: return GetNumberTile(cell.number);
            default: return tileUnknown;
        }
    }

    private Color GetPlayerColor(Cell.Owner owner)
    {
        if (owner == Cell.Owner.PlayerA) return colorPlayerA;
        if (owner == Cell.Owner.PlayerB) return colorPlayerB;
        return colorNone;
    }

    private Tile GetNumberTile(int number)
    {
        switch (number)
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