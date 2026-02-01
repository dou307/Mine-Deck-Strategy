using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ConnectionUI : MonoBehaviour
{
    public Button hostBtn;   // 房主按钮
    public Button clientBtn; // 客户端按钮
    public GameObject panel; // 用来在游戏开始后隐藏按钮

    private void Start()
    {
        // 绑定点击事件
        hostBtn.onClick.AddListener(() => {
            NetworkManager.Singleton.StartHost();
            HideUI();
        });

        clientBtn.onClick.AddListener(() => {
            NetworkManager.Singleton.StartClient();
            HideUI();
        });
    }

    private void HideUI()
    {
        if (panel != null) 
            panel.SetActive(false); // 点击后隐藏按钮，露出游戏画面
    }
}