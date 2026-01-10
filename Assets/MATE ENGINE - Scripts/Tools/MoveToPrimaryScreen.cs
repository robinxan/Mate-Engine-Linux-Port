using UnityEngine;
using System.Collections.Generic;

public class MoveToPrimaryScreen : MonoBehaviour
{
    public void Action()
    {
        var displays = new List<DisplayInfo>();
        Screen.GetDisplayLayout(displays);
        var targetDisplay = displays[0];
        var centerPosition = new Vector2Int((targetDisplay.width - Screen.width) / 2, (targetDisplay.height - Screen.height) / 2);
        Screen.MoveMainWindowTo(targetDisplay, centerPosition);
    }
}