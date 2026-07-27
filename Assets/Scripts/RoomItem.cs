using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomItem : MonoBehaviour
{
    [Header("UI 组件 (请在 Prefab 里拖进去)")]
    public TMP_Text infoText;   // 显示 "房间名 (1/2)"
    public Button joinButton;   // 按钮组件
    public Image bgImage;       // 按钮背景图 (用于变色)

    // 初始化方法
    public void Setup(RoomData data, System.Action<string> onJoinClick)
    {
        // 1. 判断是否满员
        // 注意：Supabase 返回的 status 可能是 "FULL" 或 "WAITING"
        // player_count 是人数
        bool isFull = (data.status == "FULL") || (data.player_count >= 2);

        // 2. 设置文字
        if (isFull)
        {
            infoText.text = $"{data.room_name} <color=red>(已满)</color>";
            
            // --- 视觉设置：变红、不可点 ---
            if (bgImage) bgImage.color = new Color(1f, 0.6f, 0.6f); // 浅红色
            if (joinButton) joinButton.interactable = false; // 禁用点击
        }
        else
        {
            infoText.text = $"{data.room_name} <color=green>({data.player_count}/2)</color>";
            
            // --- 视觉设置：正常、可点 ---
            if (bgImage) bgImage.color = Color.white;
            if (joinButton) joinButton.interactable = true;

            // 3. 绑定点击事件
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => {
                onJoinClick(data.join_code);
            });
        }
    }
}