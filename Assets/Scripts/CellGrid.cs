using UnityEngine;

public class CellGrid
{
    private readonly Cell[,] cells;

    public int Width => cells.GetLength(0);
    public int Height => cells.GetLength(1);

    public Cell this[int x, int y] => cells[x, y];

    public CellGrid(int width, int height)
    {
        cells = new Cell[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                cells[x, y] = new Cell
                {
                    position = new Vector3Int(x, y, 0),
                    type = Cell.Type.Empty
                };
            }
        }
    }

// 在 CellGrid 类中

public void GenerateMines(Cell firstClickCell, int totalMines)
{
    // --- 1. 参数定义 ---
    int startZoneSize = 4; // 起步区大小 4x4
    int width = Width;     // 32
    int height = Height;   // 16
    int midX = width / 2;  // 16, 分界线

    // 预设起步区雷数 (4x4=16格，建议放 2-3 个雷，不宜太多)
    int minesInStartZone = 2; 
    
    // 计算剩余雷数，分配给左右腹地
    // 剩余雷数 = 总数 - (起步区x2)
    int remainingMines = totalMines - (minesInStartZone * 2);
    int minesPerSide = remainingMines / 2;

    // --- 2. 生成起步区 (完全镜像) ---
    GenerateSymmetricalStartZones(startZoneSize, minesInStartZone);

    // --- 3. 生成左半区 (随机) ---
    // 范围: x[0, 15], 排除左下起步区
    GenerateRandomMinesInRect(
        new RectInt(0, 0, midX, height), 
        minesPerSide, 
        excludeZone: new RectInt(0, 0, startZoneSize, startZoneSize) // 假设P1在左下(0,0)
    );

    // --- 4. 生成右半区 (随机) ---
    // 范围: x[16, 31], 排除右上起步区
    GenerateRandomMinesInRect(
        new RectInt(midX, 0, width - midX, height), 
        minesPerSide, 
        excludeZone: new RectInt(width - startZoneSize, height - startZoneSize, startZoneSize, startZoneSize) // P2在右上
    );
    
    Debug.Log($"地图生成完毕: 起步区各{minesInStartZone}雷(镜像), 左右腹地各{minesPerSide}雷(随机).");
}

// --- 辅助函数：生成镜像起步区 ---
private void GenerateSymmetricalStartZones(int size, int count)
{
    int placed = 0;
    // P1 起步区范围 (左下)
    // 注意：我们要保证 (0,0) 及其周围绝对安全，这里简单处理为保证 (0,0), (0,1), (1,0), (1,1) 无雷
    // 既然是 4x4，我们可以更精细控制
    
    while (placed < count)
    {
        int x = Random.Range(0, size);
        int y = Random.Range(0, size);

        // 1. 强制保护：出生点 (0,0) 及其紧邻的 3 格 (0,1), (1,0), (1,1) 不放雷
        // 这样保证第一下点击必定是 0 (泛洪)
        if (x <= 1 && y <= 1) continue;

        Cell cellP1 = cells[x, y];

        // 如果已经有雷，重试
        if (cellP1.type == Cell.Type.Mine) continue;

        // 2. 放置 P1 的雷
        cellP1.type = Cell.Type.Mine;

        // 3. 放置 P2 的雷 (中心对称 / 旋转180度镜像)
        // P2 区域在 (Width-1, Height-1) 向内收缩
        int p2X = Width - 1 - x;
        int p2Y = Height - 1 - y;
        
        Cell cellP2 = cells[p2X, p2Y];
        cellP2.type = Cell.Type.Mine;

        placed++;
    }
}

// --- 辅助函数：在指定矩形区域内随机布雷 ---
private void GenerateRandomMinesInRect(RectInt area, int count, RectInt excludeZone)
{
    int placed = 0;
    int safetyLoop = 0; // 防止死循环

    while (placed < count && safetyLoop < 10000)
    {
        safetyLoop++;
        
        // 在区域内随机取点
        int x = Random.Range(area.x, area.x + area.width);
        int y = Random.Range(area.y, area.y + area.height);

        // 1. 检查是否在排除区 (起步区) 内
        if (excludeZone.Contains(new Vector2Int(x, y))) continue;

        // 2. 检查是否越界 (双重保险)
        if (x >= Width || y >= Height) continue;

        Cell cell = cells[x, y];

        // 3. 检查是否已经有雷
        if (cell.type == Cell.Type.Mine) continue;

        // 4. 布雷
        cell.type = Cell.Type.Mine;
        placed++;
    }
}

    public void GenerateNumbers()
    {
        int width = Width;
        int height = Height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Cell cell = cells[x, y];

                if (cell.type == Cell.Type.Mine) {
                    continue;
                }

                cell.number = CountAdjacentMines(cell);
                cell.type = cell.number > 0 ? Cell.Type.Number : Cell.Type.Empty;
            }
        }
    }

    public int CountAdjacentMines(Cell cell)
    {
        int count = 0;

        for (int adjacentX = -1; adjacentX <= 1; adjacentX++)
        {
            for (int adjacentY = -1; adjacentY <= 1; adjacentY++)
            {
                if (adjacentX == 0 && adjacentY == 0) {
                    continue;
                }

                int x = cell.position.x + adjacentX;
                int y = cell.position.y + adjacentY;

                if (TryGetCell(x, y, out Cell adjacent) && adjacent.type == Cell.Type.Mine) {
                    count++;
                }
            }
        }

        return count;
    }

    public int CountAdjacentFlags(Cell cell)
    {
        int count = 0;

        for (int adjacentX = -1; adjacentX <= 1; adjacentX++)
        {
            for (int adjacentY = -1; adjacentY <= 1; adjacentY++)
            {
                if (adjacentX == 0 && adjacentY == 0) {
                    continue;
                }

                int x = cell.position.x + adjacentX;
                int y = cell.position.y + adjacentY;

                if (TryGetCell(x, y, out Cell adjacent) && !adjacent.revealed && adjacent.flagged) {
                    count++;
                }
            }
        }

        return count;
    }

    public Cell GetCell(int x, int y)
    {
        if (InBounds(x, y)) {
            return cells[x, y];
        } else {
            return null;
        }
    }

    public bool TryGetCell(int x, int y, out Cell cell)
    {
        cell = GetCell(x, y);
        return cell != null;
    }

    public bool InBounds(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    public bool IsAdjacent(Cell a, Cell b)
    {
        return Mathf.Abs(a.position.x - b.position.x) <= 1 &&
               Mathf.Abs(a.position.y - b.position.y) <= 1;
    }

}
