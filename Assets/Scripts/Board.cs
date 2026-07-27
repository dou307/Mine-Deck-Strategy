using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
public class Board : MonoBehaviour
{
    [Header("图块层引用 (按层级由低到高排列)")]
    public Tilemap tilemap;         // 1. 基础层 (数字、空地、迷雾)
    public Tilemap damageTilemap;   // 2. 受损层 (裂纹)
    public Tilemap trapTilemap;     // 3. 陷阱层 (己方陷阱高亮)
    public Tilemap jamTilemap;      // 4. 阻断层 (干扰信号)
    public Tilemap cooldownTilemap; // 5. 冷却层 (你提议的小号色块)

    [Header("基础图块")]
    public Tile tileUnknown;
    public Tile tileEmpty;
    public Tile tileMine;
    public Tile tileExploded;
    public Tile tileFlag;
    public Tile tileBedrock; // 新增：焦土/废墟图块 (黑色或碎石)
    public Tile tileTrap;    // 新增：己方可见的陷阱图块
    public Tile tileTrapBlue;
    public Tile tileJam;

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
    [Header("状态 Overlay 图块")]
    public Tile tileLockOverlay;    // 冷却锁定图标
    public Tile tileCrackedOverlay; // 耐久度损伤裂纹
    public Tile tileCooldownOverlay; // 【建议】在这里放入你说的“小菱形色块”

    [Header("颜色配置")]
    public Color colorPlayerA = new Color(1f, 0.5f, 0.5f); // 红
    public Color colorPlayerB = new Color(0.5f, 0.5f, 1f); // 蓝
    public Color colorNone = Color.white;
    public Color colorLocked = new Color(0.5f, 0.5f, 0.5f, 0.6f); // 冷却色块的半透明灰/蓝
    public Color colorDamaged = new Color(0.7f, 0.7f, 0.7f); // 耐久度下降变暗
    private void Awake()
    {
        //tilemap = GetComponent<Tilemap>();
    }

    public void Draw(CellGrid grid, Cell.Owner viewer, int currentTurn)
    {
        // 1. 清理所有层级
        tilemap.ClearAllTiles();
        if (damageTilemap) damageTilemap.ClearAllTiles();
        if (trapTilemap) trapTilemap.ClearAllTiles();
        if (jamTilemap) jamTilemap.ClearAllTiles();
        if (cooldownTilemap) cooldownTilemap.ClearAllTiles();

        int width = grid.Width;
        int height = grid.Height;
        int midX = width / 2;
        bool isFogPhase = currentTurn <= 6;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!grid.InBounds(x, y)) continue;

                Cell cell = grid[x, y];
                Vector3Int pos = new Vector3Int(x, y, 0);

                // --- 优先级 0: 报废/虚空 (全层清空) ---
                if (cell.isBedrock)
                {
                    tilemap.SetTile(pos, null); 
                    continue; 
                }

                // --- 判定迷雾 ---
                bool isFogged = false;
                if (isFogPhase)
                {
                    if (viewer == Cell.Owner.PlayerA && x >= midX) isFogged = true;
                    if (viewer == Cell.Owner.PlayerB && x < midX) isFogged = true;
                }

                // --- 1. 绘制基础层 (tilemap) ---
                Tile tileToDraw = tileUnknown;
                Color colorToDraw = colorNone;

                if (isFogged) { tileToDraw = tileUnknown; }
                else if (cell.revealed)
                {
                    bool isTowerVisible = cell.hasTower && (cell.towerOwner == viewer || cell.isTowerRevealed);
                    if (isTowerVisible || (cell.exploded && cell.owner == viewer))
                        tileToDraw = GetFullDetailTile(cell);
                    else if (cell.owner == viewer || cell.owner == Cell.Owner.None)
                        tileToDraw = GetFullDetailTile(cell);
                    else
                        tileToDraw = tileUnknown;
                }
                else
                {
                    tileToDraw = (cell.IsFlaggedBy(viewer)) ? tileFlag : tileUnknown;
                }

                // 染色逻辑
                if (!isFogged && cell.owner != Cell.Owner.None)
                {
                    colorToDraw = GetPlayerColor(cell.owner);
                    // 仅调节基础层的亮度来反映耐久，不再强行Lerp灰色
                    if (cell.currentDurability < cell.maxDurability)
                    {
                        float brightness = Mathf.Lerp(0.5f, 1.0f, (float)cell.currentDurability / cell.maxDurability);
                        colorToDraw.r *= brightness; colorToDraw.g *= brightness; colorToDraw.b *= brightness;
                    }
                }

                tilemap.SetTile(pos, tileToDraw);
                tilemap.SetTileFlags(pos, TileFlags.None);
                tilemap.SetColor(pos, colorToDraw);

                if (isFogged) continue; // 迷雾下不显示任何叠加

                // --- 2. 绘制受损层 (damageTilemap) - 最底层叠加 ---
                if (cell.revealed && cell.currentDurability <= 1 && damageTilemap)
                {
                    damageTilemap.SetTile(pos, tileCrackedOverlay);
                }

                // --- 3. 绘制陷阱层 (trapTilemap) ---
                if (cell.hasTrap && cell.trapOwner == viewer && trapTilemap)
                {
                    trapTilemap.SetTile(pos, (cell.trapOwner == Cell.Owner.PlayerB) ? tileTrapBlue : tileTrap);
                }

                // --- 4. 绘制阻断层 (jamTilemap) ---
                if (cell.jamTurns > 0 && jamTilemap)
                {
                    jamTilemap.SetTile(pos, tileJam);
                }

                // --- 5. 绘制冷却层 (cooldownTilemap) - 最顶层叠加 ---
                if (cell.unlockTurn > currentTurn && cooldownTilemap)
                {
                    cooldownTilemap.SetTile(pos, tileCooldownOverlay); // 你的小菱形图块
                    cooldownTilemap.SetTileFlags(pos, TileFlags.None);
                    cooldownTilemap.SetColor(pos, colorLocked); // 给小色块染色
                }
            }
        }
    }
    // 获取完全信息的图块 (自己视角)
    private Tile GetFullDetailTile(Cell cell)
    {
        if (cell.hasTower) return (cell.towerOwner == Cell.Owner.PlayerA) ? tileRedTower : tileBlueTower;
        // 如果是雷且翻开了
    if (cell.type == Cell.Type.Mine && cell.revealed)
    {
        // 如果炸了显示爆炸图块，如果没炸（被雷达扫出来）显示普通地雷图块
        return cell.exploded ? tileExploded : tileMine;
    }
        
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