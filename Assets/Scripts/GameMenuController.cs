using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode; // 记得引用 Netcode

public class GameMenuController : MonoBehaviour
{
    [Header("面板引用")]
    public GameObject settingsPanel;
    public GameObject quitConfirmPanel;

    [Header("按钮引用 (可选，用于代码绑定)")]
    public Button btnHome;
    public Button btnSettings;
    public Button btnConfirmQuit;
    public Button btnCancelQuit;
    public Button btnCloseSettings;

    private void Start()
    {
        // 初始状态：确保面板是关的
        if (settingsPanel) settingsPanel.SetActive(false);
        if (quitConfirmPanel) quitConfirmPanel.SetActive(false);

        // 绑定事件 (也可以在 Inspector 里拖拽)
        if (btnHome) btnHome.onClick.AddListener(OnHomeClicked);
        if (btnSettings) btnSettings.onClick.AddListener(OnSettingsClicked);
        
        if (btnConfirmQuit) btnConfirmQuit.onClick.AddListener(OnConfirmQuit);
        if (btnCancelQuit) btnCancelQuit.onClick.AddListener(OnCancelQuit);
        if (btnCloseSettings) btnCloseSettings.onClick.AddListener(OnCloseSettings);
    }

    // --- 按钮逻辑 ---

    public void OnSettingsClicked()
    {
        // 打开/关闭 设置面板
        if (settingsPanel) 
            settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    public void OnCloseSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
    }

    public void OnHomeClicked()
    {
        // 打开二次确认弹窗
        if (quitConfirmPanel) 
            quitConfirmPanel.SetActive(true);
    }

    public void OnCancelQuit()
    {
        // 关闭弹窗
        if (quitConfirmPanel) 
            quitConfirmPanel.SetActive(false);
    }

    // --- 核心：退出逻辑 ---
    public void OnConfirmQuit()
    {
        Debug.Log("正在退出战局...");

        // 1. 断开网络连接
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 2. 加载回主菜单场景
        // 假设你的主菜单场景名字叫 "Menu" 或 "Lobby"
        // 自动获取“当前正在运行的场景名字”，不管它叫什么都能重载
        string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        UnityEngine.SceneManagement.SceneManager.LoadScene(currentSceneName);
    }
    // GameMenuController.cs 补充

    public void OnGameOver(Cell.Owner winner)
    {
        // 显示结算面板
        if (quitConfirmPanel) quitConfirmPanel.SetActive(true); 
        // 这里应该显示专门的 GameOverPanel，包含 "Rematch" 和 "Exit"
    }

    // --- 按钮 1: 退出到大厅 (Exit) ---
    // 逻辑：所有人都断开，各回各家，回到 Lobby 列表
    public void OnExitToLobby()
    {
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name); // 重载场景
    }

    // --- 按钮 2: 再来一局 (Rematch) ---
    // 逻辑：保持连接，重置游戏数据 (Game.NewGame)
    // 只有 Host 能点这个按钮，Client 只能等待
    public void OnRematch()
    {
        if (!NetworkManager.Singleton.IsServer) return; // 只有房主能重开

        // 调用 Game.cs 里的 NewGame()
        FindObjectOfType<Game>().ServerRematch(); 
    }
}