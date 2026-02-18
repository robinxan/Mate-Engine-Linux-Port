using UnityEngine;
using System;

public class AvatarHideHandler : MonoBehaviour
{
    public int snapThresholdPx = 12;
    public int unsnapThresholdPx = 24;
    public int edgeInsetPx = 0;
    public bool enableSmoothing = true;
    [Range(0.01f, 0.5f)] public float smoothingTime = 0.10f;
    public float smoothingMaxSpeed = 6000f;
    public bool keepTopmostWhileSnapped = true;
    public float unsnapGraceTime = 0.12f;

    Animator animator;
    AvatarAnimatorController controller;
    IntPtr unityHWND;

    Transform leftHand;
    Transform rightHand;
    Camera cam;

    enum Side { None, Left, Right }
    Side snappedSide = Side.None;

    float cursorOffsetY;
    float windowW, windowH;
    float velX, velY;
    bool smoothingActive;
    bool wasDragging;
    float snappedAt;

    void Start()
    {
        unityHWND = WindowManager.Instance.UnityWindow;
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        if (animator != null && animator.isHuman && animator.avatar != null)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
    }

    void OnDisable()
    {
        SetHide(false, false);
        snappedSide = Side.None;
    }

    void Update()
    {
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;

        if (controller.isDragging && !wasDragging)
        {
            if (WindowManager.Instance.GetWindowRect(unityHWND, out Rect wr) && WindowManager.Instance.GetMousePosition(out Vector2 cp))
            {
                windowW = Math.Max(1, wr.width);
                windowH = Math.Max(1, wr.height);
                cursorOffsetY = cp.y - wr.y;
                smoothingActive = false;
                velX = velY = 0f;
            }
        }

        if (controller.isDragging)
        {
            if (!WindowManager.Instance.GetMousePosition(out Vector2 cp)) { wasDragging = controller.isDragging; return; }
            if (!WindowManager.Instance.GetWindowRect(unityHWND, out Rect wrCur)) { wasDragging = controller.isDragging; return; }
            Rect mon = GetCurrentMonitorRect(cp);

            float anchorLeftDesk = GetAnchorDesktopX(Side.Left);
            float anchorRightDesk = GetAnchorDesktopX(Side.Right);
            if (anchorLeftDesk < 0) anchorLeftDesk = wrCur.x + Math.Max(1, wrCur.width / 2);
            if (anchorRightDesk < 0) anchorRightDesk = wrCur.x + Math.Max(1, wrCur.width / 2);

            bool nearLeft = anchorLeftDesk - mon.x <= Math.Max(1, snapThresholdPx);
            bool nearRight = mon.x + mon.width - anchorRightDesk <= Math.Max(1, snapThresholdPx);

            if (snappedSide == Side.None)
            {
                if (nearLeft) SnapTo(Side.Left, cp, mon);
                else if (nearRight) SnapTo(Side.Right, cp, mon);
            }
            else
            {
                if (Time.unscaledTime >= snappedAt + unsnapGraceTime)
                {
                    if (snappedSide == Side.Left && (cp.x - mon.x) > Math.Max(1, unsnapThresholdPx)) Unsnap();
                    else if (snappedSide == Side.Right && (mon.x + mon.width - cp.x) > Math.Max(1, unsnapThresholdPx)) Unsnap();
                }
            }

            if (snappedSide != Side.None)
            {
                if (!WindowManager.Instance.GetWindowRect(unityHWND, out Rect wr2)) { wasDragging = controller.isDragging; return; }
                Rect monNow = GetCurrentMonitorRect(cp);

                float anchorDesk = GetAnchorDesktopX(snappedSide);
                if (anchorDesk < 0) anchorDesk = wr2.x + Math.Max(1, wr2.width / 2);
                float anchorWinX = Mathf.Clamp(anchorDesk - wr2.x, 0, Math.Max(1, wr2.width));

                float desiredAnchorDesk = snappedSide == Side.Left ? monNow.x + edgeInsetPx : monNow.x + monNow.width - edgeInsetPx;
                float tx = desiredAnchorDesk - anchorWinX;

                float ty = cp.y - cursorOffsetY;

                MoveSmooth(wr2.x, wr2.y, tx, ty, wr2.width, wr2.height);
                if (keepTopmostWhileSnapped) SetTopMost(true);
            }
        }
        else
        {
            if (snappedSide != Side.None)
            {
                if (!WindowManager.Instance.GetWindowRect(unityHWND, out Rect wr)) return;
                Rect mon = GetMonitorFromWindow(unityHWND);

                float anchorDesk = GetAnchorDesktopX(snappedSide);
                if (anchorDesk < 0) anchorDesk = wr.x + Math.Max(1, wr.width / 2);
                float anchorWinX = Mathf.Clamp(anchorDesk - wr.x, 0, Math.Max(1, wr.width));

                float desiredAnchorDesk = snappedSide == Side.Left ? mon.x + edgeInsetPx : mon.x + mon.width - edgeInsetPx;
                float tx = desiredAnchorDesk - anchorWinX;

                float ty = wr.y;

                MoveSmooth(wr.x, wr.y, tx, ty, wr.width, wr.height);
                if (keepTopmostWhileSnapped) SetTopMost(true);
            }
        }

        wasDragging = controller.isDragging;
    }
    
    float GetAnchorDesktopX(Side side)
    {
        Transform t = side == Side.Left ? leftHand : rightHand;
        if (t == null || cam == null) return -1;
        if (!GetUnityClientRect(out Rect uCli)) return -1;

        Vector3 sp = cam.WorldToScreenPoint(t.position);
        if (sp.z < 0.01f) return -1;

        float clientW = Mathf.Max(1f, uCli.x + uCli.width - uCli.x);
        float pxW = Mathf.Max(1, cam.pixelWidth);
        float sx = Mathf.Clamp(sp.x, 0, cam.pixelWidth) * (clientW / pxW);
        float desktopX = uCli.x + Mathf.RoundToInt(sx);
        return desktopX;
    }

    void SnapTo(Side side, Vector2 cp, Rect mon)
    {
        if (!WindowManager.Instance.GetWindowRect(unityHWND, out Rect wr)) return;

        windowW = Math.Max(1, wr.width);
        windowH = Math.Max(1, wr.height);
        cursorOffsetY = cp.y - wr.y;
        snappedSide = side;
        SetHide(side == Side.Left, side == Side.Right);

        float anchorDesk = GetAnchorDesktopX(side);
        if (anchorDesk < 0) anchorDesk = wr.x + Math.Max(1, (wr.width) / 2);
        float anchorWinX = Mathf.Clamp(anchorDesk - wr.x, 0, Math.Max(1, wr.width));

        float desiredAnchorDesk = side == Side.Left ? mon.x + edgeInsetPx : mon.x + mon.width - edgeInsetPx;
        float tx = desiredAnchorDesk - anchorWinX;

        float ty = cp.y - cursorOffsetY;

        WindowManager.Instance.SetWindowPosition(tx, ty);
        WindowManager.Instance.SetWindowSize(windowW, windowH);
        smoothingActive = enableSmoothing;
        velX = velY = 0f;
        snappedAt = Time.unscaledTime;
        if (keepTopmostWhileSnapped) SetTopMost(true);
    }

    void Unsnap()
    {
        snappedSide = Side.None;
        SetHide(false, false);
        smoothingActive = false;
        velX = velY = 0f;
        SetTopMost(false);
    }

    void SetHide(bool left, bool right)
    {
        animator.SetBool("HideLeft", left);
        animator.SetBool("HideRight", right);
    }

    void MoveSmooth(float curX, float curY, float targetX, float targetY, float w, float h)
    {
        if (!enableSmoothing || !smoothingActive)
        {
            if (curX != targetX || curY != targetY)
            {
                WindowManager.Instance.SetWindowPosition(targetX, targetY);
                WindowManager.Instance.SetWindowSize(w, h);
            }
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float nx = Mathf.SmoothDamp(curX, targetX, ref velX, smoothingTime, smoothingMaxSpeed, dt);
        float ny = Mathf.SmoothDamp(curY, targetY, ref velY, smoothingTime, smoothingMaxSpeed, dt);
        float ix = Mathf.RoundToInt(nx);
        float iy = Mathf.RoundToInt(ny);
        if (Mathf.Abs(targetX - ix) <= 1 && Mathf.Abs(targetY - iy) <= 1)
        {
            ix = targetX; iy = targetY; smoothingActive = false; velX = velY = 0f;
        }

        if (ix != curX || iy != curY)
        {
            WindowManager.Instance.SetWindowPosition(ix, iy);
            WindowManager.Instance.SetWindowSize(w, h);
        }
    }

    Rect GetCurrentMonitorRect(Vector2 cp)
    {
        return WindowManager.Instance.GetMonitorRectFromPoint(cp);
    }

    Rect GetMonitorFromWindow(IntPtr hwnd)
    {
        return WindowManager.Instance.GetMonitorRectFromWindow(hwnd);
    }

    Rect GetVirtualScreenRect()
    {
        return new Rect(Vector2.zero, WindowManager.Instance.GetTotalDisplaySize());
    }

    bool GetUnityClientRect(out Rect r)
    {
        return WindowManager.Instance.GetWindowRect(out r);
    }

    void SetTopMost(bool on)
    {
        WindowManager.Instance.SetTopmost(on);
    }
}