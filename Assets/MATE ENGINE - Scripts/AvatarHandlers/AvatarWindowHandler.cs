using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class AvatarWindowHandler : MonoBehaviour
{
    private static readonly int IsWindowSit = Animator.StringToHash("isWindowSit");
    private static readonly int IsTaskbarSit = Animator.StringToHash("isTaskbarSit");
    public float desktopScale = 1f;
    public int snapThreshold = 30;
    [Header("Window Sit BlendTree")]
    public int totalWindowSitAnimations = 4;
    private bool wasSitting;

    [Header("User Y-Offset Slider")]
    [Range(-0.015f, 0.015f)]
    public float windowSitYOffset;
    
    IntPtr _snappedHwnd = IntPtr.Zero;
    Rect _snappedRect;
    IntPtr _unityHwnd = IntPtr.Zero;
    Vector2 lastDesktopPosition;
    readonly List<WindowEntry> cachedWindows = new();

    Animator animator;
    AvatarAnimatorController controller;

    private float lastCacheUpdateTime;
    private const float CacheUpdateCooldown = 0.05f;
    
    private float horizontalOffset;

    void Start()
    {
        _unityHwnd = WindowManager.Instance.UnityWindow;
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        lastDesktopPosition = WindowManager.Instance.GetWindowPosition();
    }

    void Update()
    {
        if (_unityHwnd == IntPtr.Zero || !animator || !controller) return;
        if (!SaveLoadHandler.Instance.data.enableWindowSitting) return;

        bool isSittingNow = animator.GetBool("IsWindowSit");
        if (isSittingNow && !wasSitting)
        {
            int sitIdx = Random.Range(0, totalWindowSitAnimations);
            animator.SetFloat("WindowSitIndex", sitIdx);
        }
        wasSitting = isSittingNow;

        Vector2 unityPos = GetUnityWindowPosition();

        if (controller.isDragging)
        {
            if (!animator.GetBool("IsSitting"))
            {
                if (_snappedHwnd == IntPtr.Zero)
                    TrySnap(unityPos);
                else if (!IsStillNearSnappedWindow(unityPos))
                {
                    ExitWindowSitting();
                }
            }
            if (_snappedHwnd != IntPtr.Zero)
                horizontalOffset = unityPos.x - _snappedRect.x;
        }
        else if (_snappedHwnd != IntPtr.Zero)
        {
            FollowSnappedWindow();
        }

        if (_snappedHwnd != IntPtr.Zero)
        {
            if (WindowManager.Instance.IsWindowMaximized(_snappedHwnd) || IsWindowFullscreen(_snappedHwnd))
            {
                ExitWindowSitting();
                MoveMateToDesktopPosition();
            }
        }

        if (animator.GetBool("IsBigScreenAlarm"))
        {
            ExitWindowSitting();
        }
    }

    void TrySnap(Vector2 unityPos)
    {
        if (Time.time < lastCacheUpdateTime + CacheUpdateCooldown) return;
        UpdateCachedWindows();

        WindowManager.Instance.GetWindowRect(out Rect unityRect);

        foreach (var entry in cachedWindows)
        {
            if (entry.Hwnd == _unityHwnd) continue;
            
            Rect topBar = new Rect(entry.Rect.x, entry.Rect.y, entry.Rect.width, 5 * desktopScale);
            Rect snapRect = new Rect(unityRect.x, unityRect.y + unityRect.height, unityRect.width, snapThreshold * desktopScale);
            if (!snapRect.Overlaps(topBar)) continue;
            _snappedHwnd = entry.Hwnd;
            _snappedRect = entry.Rect;
            animator.SetBool(IsWindowSit, true);
            animator.SetBool(IsTaskbarSit, entry.IsDock);
            animator.Update(0f);
            lastDesktopPosition = unityPos;
            horizontalOffset = unityPos.x - entry.Rect.x;
        }
    }

    void FollowSnappedWindow()
    {
        if (!WindowManager.Instance.GetWindowRect(_snappedHwnd, out Rect winRect) || 
            !WindowManager.Instance.IsWindowVisible(_snappedHwnd))
        {
            ExitWindowSitting();
            return;
        }

        Vector2 unitySize = WindowManager.Instance.GetWindowSize();
        float targetY = winRect.y - unitySize.y + windowSitYOffset * unitySize.y;
        float targetX = winRect.x + horizontalOffset;

        WindowManager.Instance.SetWindowPosition(targetX, targetY);
    }

    bool IsStillNearSnappedWindow(Vector2 unityPos)
    {
        if (_snappedHwnd == IntPtr.Zero) return false;
        if (!WindowManager.Instance.GetWindowRect(_snappedHwnd, out Rect winRect)) return false;

        Vector2 size = WindowManager.Instance.GetWindowSize();
        float currentBottom = unityPos.y + size.y;
        float targetBottom = winRect.y;

        return Mathf.Abs(currentBottom - targetBottom) < snapThreshold;
    }

    void UpdateCachedWindows()
    {
        cachedWindows.Clear();
        var allWindows = WindowManager.Instance.GetAllVisibleWindows();
        foreach (var hWnd in allWindows)
        {
            if (!WindowManager.Instance.GetWindowRect(hWnd, out Rect r)) continue;
            string cls = WindowManager.Instance.GetClassName(hWnd);
            bool isDock = WindowManager.Instance.IsDock(hWnd);

            if (!isDock)
            {
                if (r.width < 100 || r.height < 100) continue;
                if (cls.Length == 0) continue;
                if (WindowManager.Instance.IsDesktop(hWnd)) continue;
            }

            cachedWindows.Add(new WindowEntry { Hwnd = hWnd, Rect = r, IsDock = isDock});
        }
        lastCacheUpdateTime = Time.time;
    }

    void ExitWindowSitting()
    {
        _snappedHwnd = IntPtr.Zero;
        animator.SetBool(IsWindowSit, false);
    }

    struct WindowEntry { public IntPtr Hwnd; public Rect Rect; public bool IsDock; }

    Vector2 GetUnityWindowPosition() => WindowManager.Instance.GetWindowPosition();

    bool IsWindowFullscreen(IntPtr hwnd)
    {
        if (!WindowManager.Instance.GetWindowRect(hwnd, out Rect rect)) return false;
        int screenWidth = Display.main.systemWidth;
        int screenHeight = Display.main.systemHeight;
        int tolerance = 2;
        return Mathf.Abs(rect.width - screenWidth) <= tolerance && 
               Mathf.Abs(rect.height - screenHeight) <= tolerance;
    }

    void MoveMateToDesktopPosition()
    {
        WindowManager.Instance.SetWindowPosition(lastDesktopPosition.x, lastDesktopPosition.y);
    }

    public void ForceExitWindowSitting()
    {
        ExitWindowSitting();
    }
}