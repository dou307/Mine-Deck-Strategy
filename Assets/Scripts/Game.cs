using System.Collections;
using UnityEngine;
using TMPro;

[DefaultExecutionOrder(-1)]
public class Game : MonoBehaviour
{
    public int width = 16;
    public int height = 16;
    public int mineCount = 32;

    [Header("能量系统")]
public int energy = 0;        // 当前能量
public int maxEnergy = 100;   // 能量上限
public int energyPerCell = 5; // 每点开一个格子给多少能量

// 引用 UI 组件（稍后会在 Unity 界面里关联）
public UnityEngine.UI.Slider energySlider; 
public TMPro.TextMeshProUGUI energyText;

    private Board board;
    private CellGrid grid;
    private bool gameover;
    private bool generated;

    private void OnValidate()
    {
        mineCount = Mathf.Clamp(mineCount, 0, width * height);
    }

    private void Awake()
    {
        Application.targetFrameRate = 60;
        board = GetComponentInChildren<Board>();
    }

    private void Start()
    {
        NewGame();
    }

    private void NewGame()
    {
        StopAllCoroutines();

        Camera.main.transform.position = new Vector3(width / 2f, height / 2f, -10f);

        gameover = false;
        generated = false;

        grid = new CellGrid(width, height);
        board.Draw(grid);
        energy = 0; // 能量清零
        AddEnergy(0); // 调用一下这个方法，用来刷新一次 UI 显示
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            NewGame();
            return;
        }

        if (!gameover)
        {
            if (Input.GetMouseButtonDown(0)) {
                Reveal();
            } else if (Input.GetMouseButtonDown(1)) {
                Flag();
            } else if (Input.GetMouseButton(2)) {
                Chord();
            } else if (Input.GetMouseButtonUp(2)) {
                Unchord();
            }
        }
    }

    private void Reveal()
    {
        if (TryGetCellAtMousePosition(out Cell cell))
        {
            if (!generated)
            {
                grid.GenerateMines(cell, mineCount);
                grid.GenerateNumbers();
                generated = true;
            }

            Reveal(cell);
        }
    }

    private void Reveal(Cell cell)
{
    if (cell.revealed) return;
    if (cell.flagged) return;

    switch (cell.type)
    {
        case Cell.Type.Mine:
            Explode(cell);
            break;

        case Cell.Type.Empty:
            // 只要不是雷，就加能量
            AddEnergy(energyPerCell); 
            StartCoroutine(Flood(cell));
            CheckWinCondition();
            break;

        default: // 这里通常是数字格
            AddEnergy(energyPerCell); 
            cell.revealed = true;
            CheckWinCondition();
            break;
    }

    board.Draw(grid);
}

    private IEnumerator Flood(Cell cell)
    {
        if (gameover) yield break;
        if (cell.revealed) yield break;
        if (cell.type == Cell.Type.Mine) yield break;

        cell.revealed = true;
        board.Draw(grid);

        yield return null;

        if (cell.type == Cell.Type.Empty)
        {
            if (grid.TryGetCell(cell.position.x - 1, cell.position.y, out Cell left)) {
                StartCoroutine(Flood(left));
            }
            if (grid.TryGetCell(cell.position.x + 1, cell.position.y, out Cell right)) {
                StartCoroutine(Flood(right));
            }
            if (grid.TryGetCell(cell.position.x, cell.position.y - 1, out Cell down)) {
                StartCoroutine(Flood(down));
            }
            if (grid.TryGetCell(cell.position.x, cell.position.y + 1, out Cell up)) {
                StartCoroutine(Flood(up));
            }
        }
    }

    private void Flag()
    {
        if (!TryGetCellAtMousePosition(out Cell cell)) return;
        if (cell.revealed) return;

        cell.flagged = !cell.flagged;
        board.Draw(grid);
    }

    private void Chord()
    {
        // unchord previous cells
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y].chorded = false;
            }
        }

        // chord new cells
        if (TryGetCellAtMousePosition(out Cell chord))
        {
            for (int adjacentX = -1; adjacentX <= 1; adjacentX++)
            {
                for (int adjacentY = -1; adjacentY <= 1; adjacentY++)
                {
                    int x = chord.position.x + adjacentX;
                    int y = chord.position.y + adjacentY;

                    if (grid.TryGetCell(x, y, out Cell cell)) {
                        cell.chorded = !cell.revealed && !cell.flagged;
                    }
                }
            }
        }

        board.Draw(grid);
    }

    private void Unchord()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Cell cell = grid[x, y];

                if (cell.chorded) {
                    Unchord(cell);
                }
            }
        }

        board.Draw(grid);
    }

    private void Unchord(Cell chord)
    {
        chord.chorded = false;

        for (int adjacentX = -1; adjacentX <= 1; adjacentX++)
        {
            for (int adjacentY = -1; adjacentY <= 1; adjacentY++)
            {
                if (adjacentX == 0 && adjacentY == 0) {
                    continue;
                }

                int x = chord.position.x + adjacentX;
                int y = chord.position.y + adjacentY;

                if (grid.TryGetCell(x, y, out Cell cell))
                {
                    if (cell.revealed && cell.type == Cell.Type.Number)
                    {
                        if (grid.CountAdjacentFlags(cell) >= cell.number)
                        {
                            Reveal(chord);
                            return;
                        }
                    }
                }
            }
        }
    }

    private void Explode(Cell cell)
    {
        gameover = true;

        // Set the mine as exploded
        cell.exploded = true;
        cell.revealed = true;

        // Reveal all other mines
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                cell = grid[x, y];

                if (cell.type == Cell.Type.Mine) {
                    cell.revealed = true;
                }
            }
        }
    }

    private void CheckWinCondition()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Cell cell = grid[x, y];

                // All non-mine cells must be revealed to have won
                if (cell.type != Cell.Type.Mine && !cell.revealed) {
                    return; // no win
                }
            }
        }

        gameover = true;

        // Flag all the mines
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Cell cell = grid[x, y];

                if (cell.type == Cell.Type.Mine) {
                    cell.flagged = true;
                }
            }
        }
    }

    private bool TryGetCellAtMousePosition(out Cell cell)
    {
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int cellPosition = board.tilemap.WorldToCell(worldPosition);
        return grid.TryGetCell(cellPosition.x, cellPosition.y, out cell);
    }

// 增加或减少能量的方法
    private void AddEnergy(int amount)
{
    energy += amount;
    // 确保能量不会超过上限，也不会低于 0
    energy = Mathf.Clamp(energy, 0, maxEnergy);

    // 更新 UI 显示
    if (energySlider != null) {
        energySlider.value = energy;
    }
    if (energyText != null) {
        energyText.text = $"Energy: {energy} / {maxEnergy}";
    }
}

// 技能1：雷达扫描 - 消耗30能量，随机标出一颗雷
public void UseRadarSkill()
{
    // 1. 检查能量够不够
    if (energy < 30)
    {
        Debug.Log("Energy is insufficient. You need 30 points of energy!");
        return;
    }

    // 2. 寻找一颗还没被标记、也没被翻开的雷
    for (int x = 0; x < width; x++)
    {
        for (int y = 0; y < height; y++)
        {
            Cell cell = grid[x, y];
            
            // 如果是雷，且玩家还没发现它
            if (cell.type == Cell.Type.Mine && !cell.revealed && !cell.flagged)
            {
                // 扣除能量
                AddEnergy(-30); 
                
                // 帮玩家插上旗子
                cell.flagged = true;
                
                // 刷新画面
                board.Draw(grid);
                
                Debug.Log("The radar has detected landmines!");
                return; // 发现一颗就结束函数
            }
        }
    }
    
    Debug.Log("There are no more undiscovered lightning strikes on the field.");
}

}
