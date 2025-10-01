using System.Collections.Generic;
using UnityEngine;

public class ConsoleLogBuildPrint : MonoBehaviour
{
#if !UNITY_EDITOR
    private readonly Queue<string> logQueue = new Queue<string>();
    private const int maxLogs = 10;
    private Vector2 logScroll;

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logQueue.Count >= maxLogs)
            logQueue.Dequeue();
        logQueue.Enqueue(logString);
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.LowerLeft;

        GUILayout.BeginArea(new Rect(10, Screen.height - 10 - (maxLogs * 20), Screen.width / 2, maxLogs * 20));
        logScroll = GUILayout.BeginScrollView(logScroll, false, false, GUILayout.Height(maxLogs * 20));
        foreach (var log in logQueue)
        {
            GUILayout.Label(log, style);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
#endif
}
