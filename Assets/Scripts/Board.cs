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

    public void Draw(CellGrid grid)
    {
        int width = grid.Width;
        int height = grid.Height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Cell cell = grid[x, y];
                Vector3Int pos = cell.position;
                tilemap.SetTile(pos, GetTile(cell));

                // --- 工作包A：处理颜色和锁定状态 ---
                tilemap.SetTileFlags(pos, TileFlags.None); // 允许修改颜色
                
                if (cell.lockTimer > 0) {
                    tilemap.SetColor(pos, colorLocked);
                } else {
                    switch (cell.owner) {
                        case Cell.Owner.PlayerA: tilemap.SetColor(pos, colorPlayerA); break;
                        case Cell.Owner.PlayerB: tilemap.SetColor(pos, colorPlayerB); break;
                        default: tilemap.SetColor(pos, colorNone); break;
                    }
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
