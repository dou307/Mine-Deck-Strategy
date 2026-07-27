using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System;

[Serializable]
public class RoomData
{
    public string join_code;
    public string room_name;
    public string status;      // 新增：WAITING 或 FULL
    public int player_count;   // 新增：当前人数
}

public class LobbyManager : MonoBehaviour
{
    // 填入你在 Supabase 获取的 URL 和 Key
    [Header("Supabase 配置")]
    public string supabaseUrl = "这里填你的Project URL"; 
    public string supabaseKey = "这里填你的anon public key";

    // 当获取到房间列表时触发的事件 (供 UI 监听)
    public System.Action<List<RoomData>> OnRoomListFetched;

    // --- 功能 1: 上传房间号 ---
    public void PostRoom(string joinCode, string roomName)
    {
        StartCoroutine(PostRoomCoroutine(joinCode, roomName));
    }

    private IEnumerator PostRoomCoroutine(string joinCode, string roomName)
    {
        // 构造 JSON 数据
        string json = $"{{\"join_code\": \"{joinCode}\", \"room_name\": \"{roomName}\", \"status\": \"WAITING\", \"player_count\": 1}}";
        
        string url = $"{supabaseUrl}/rest/v1/rooms";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            
            // 设置 Supabase 要求的请求头
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", supabaseKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseKey);
            // 告诉 Supabase 尽量返回刚才插入的数据（可选）
            request.SetRequestHeader("Prefer", "return=minimal"); 

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("【Lobby】房间创建成功，已上传至云端！");
            }
            else
            {
                Debug.LogError($"【Lobby】上传失败: {request.error}\n{request.downloadHandler.text}");
            }
        }
    }

    // --- 功能 2: 获取房间列表 ---
    public void FetchRooms()
    {
        StartCoroutine(FetchRoomsCoroutine());
    }

    private IEnumerator FetchRoomsCoroutine()
    {
        // 按照创建时间倒序排列，最新的在前面
        string url = $"{supabaseUrl}/rest/v1/rooms?select=join_code,room_name,status,player_count&order=created_at.desc";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("apikey", supabaseKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                Debug.Log($"【Lobby】获取列表: {json}");
                
                // 解析 JSON (使用简单的 JsonHelper)
                // 注意：Supabase 返回的是一个数组 [{}, {}]
                // Unity 自带的 JsonUtility 不能直接解析数组，需要包一层
                List<RoomData> rooms = ParseJsonArray<RoomData>(json);
                
                // 通知 UI 更新
                OnRoomListFetched?.Invoke(rooms);
            }
            else
            {
                Debug.LogError($"【Lobby】获取失败: {request.error}");
            }
        }
    }

    // --- 【新增】功能 3: 更新房间名称 ---
    public void UpdateRoomName(string joinCode, string newName)
    {
        StartCoroutine(UpdateRoomNameCoroutine(joinCode, newName));
    }

    private IEnumerator UpdateRoomNameCoroutine(string joinCode, string newName)
    {
        // 构造 JSON 数据：只修改 room_name
        string json = $"{{\"room_name\": \"{newName}\"}}";

        // URL 格式：.../rooms?join_code=eq.XXXXX (意思是：找到 join_code 等于 XXXXX 的那一行)
        string url = $"{supabaseUrl}/rest/v1/rooms?join_code=eq.{joinCode}";

        using (UnityWebRequest request = new UnityWebRequest(url, "PATCH"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            // 设置 Header
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", supabaseKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"【Lobby】改名成功！新名字: {newName}");
            }
            else
            {
                Debug.LogError($"【Lobby】改名失败: {request.error}\n{request.downloadHandler.text}");
            }
        }
    }

    // --- 新增功能: 更新房间状态 (比如有人进来了，或者游戏开始了) ---
    public void UpdateRoomStatus(string joinCode, string newStatus, int newCount)
    {
        StartCoroutine(UpdateRoomStatusCoroutine(joinCode, newStatus, newCount));
    }

    private IEnumerator UpdateRoomStatusCoroutine(string joinCode, string newStatus, int newCount)
    {
        // 构造 JSON 更新状态
        string json = $"{{\"status\": \"{newStatus}\", \"player_count\": {newCount}}}";
        string url = $"{supabaseUrl}/rest/v1/rooms?join_code=eq.{joinCode}";

        using (UnityWebRequest request = new UnityWebRequest(url, "PATCH"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", supabaseKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseKey);

            yield return request.SendWebRequest();
        }
    }

    // --- 简单的 JSON 数组解析工具 ---
    public static List<T> ParseJsonArray<T>(string json)
    {
        // 这是一个土办法：把 Supabase 返回的数组 "[]" 包装成 "{ \"Items\": [] }"
        // 这样 JsonUtility 就能解析了
        string newJson = "{ \"Items\": " + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
        return wrapper.Items;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public List<T> Items;
    }
}