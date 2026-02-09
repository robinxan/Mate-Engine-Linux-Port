using UnityEngine;
using System.Collections.Generic;

public class MoveToPrimaryScreen : MonoBehaviour
{
    public void Action()
    {
        WindowManager.Instance.GetWindowRect(out Rect winRect);
        var targetScreen = FindMainMonitorRect(winRect);
        WindowManager.Instance.SetWindowPosition(targetScreen.x, targetScreen.y);
    }
    
    private Rect FindMainMonitorRect(Rect windowRect)
    {
        if (!WindowManager.Instance) return new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
        
        List<Rect> monitorRects = WindowManager.Instance.GetAllMonitors();
        return new(new((monitorRects[0].x + monitorRects[0].width) / 2f - windowRect.width / 2, (monitorRects[0].y + monitorRects[0].height) / 2f - windowRect.height / 2), monitorRects[0].size);
    }

}