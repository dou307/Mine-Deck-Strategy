using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkUI : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;

    private void Awake()
    {
        // 绑定按钮点击事件
        hostButton.onClick.AddListener(() => {
            NetworkManager.Singleton.StartHost();
            HideButtons();
        });

        clientButton.onClick.AddListener(() => {
            NetworkManager.Singleton.StartClient();
            HideButtons();
        });
    }

    private void HideButtons()
    {
        // 点击后隐藏按钮，防止重复点击
        gameObject.SetActive(false);
    }
}