#define UNICODE
#define _UNICODE
#include <windows.h>
#include <dwmapi.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "shell32.lib")

#ifndef DWMWA_USE_IMMERSIVE_DARK_MODE
#define DWMWA_USE_IMMERSIVE_DARK_MODE 20
#endif

static const int IDC_CLOSE_BUTTON = 1001;

static wchar_t g_gameName[256] = L"(Unspecified Game)";
static wchar_t g_processName[MAX_PATH] = L"DummyGameRunner.exe";
static DWORD g_processId = 0;
static HBRUSH g_bgBrush = NULL;
static HFONT g_titleFont = NULL;
static HFONT g_bodyFont = NULL;

static void ParseCommandLineGameName(void)
{
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv)
    {
        return;
    }

    for (int i = 1; i < argc - 1; ++i)
    {
        if (_wcsicmp(argv[i], L"--game") == 0 && argv[i + 1][0] != L'\0')
        {
            wcsncpy_s(g_gameName, _countof(g_gameName), argv[i + 1], _TRUNCATE);
            break;
        }
    }

    LocalFree(argv);
}

static void ResolveCurrentProcessName(void)
{
    wchar_t fullPath[MAX_PATH] = { 0 };
    DWORD len = GetModuleFileNameW(NULL, fullPath, MAX_PATH);
    if (len > 0 && len < MAX_PATH)
    {
        const wchar_t* lastSlash = wcsrchr(fullPath, L'\\');
        const wchar_t* fileName = lastSlash ? (lastSlash + 1) : fullPath;
        wcsncpy_s(g_processName, _countof(g_processName), fileName, _TRUNCATE);
    }

    g_processId = GetCurrentProcessId();
}

static LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        BOOL darkMode = TRUE;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &darkMode, sizeof(darkMode));

        g_titleFont = CreateFontW(
            -22, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");

        g_bodyFont = CreateFontW(
            -15, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");

        HWND btn = CreateWindowW(
            L"BUTTON",
            L"Close",
            WS_TABSTOP | WS_VISIBLE | WS_CHILD | BS_PUSHBUTTON,
            336, 174, 100, 34,
            hwnd,
            (HMENU)(INT_PTR)IDC_CLOSE_BUTTON,
            (HINSTANCE)GetWindowLongPtrW(hwnd, GWLP_HINSTANCE),
            NULL);

        if (btn && g_bodyFont)
        {
            SendMessageW(btn, WM_SETFONT, (WPARAM)g_bodyFont, TRUE);
        }
        return 0;
    }

    case WM_COMMAND:
        if (LOWORD(wParam) == IDC_CLOSE_BUTTON)
        {
            DestroyWindow(hwnd);
            return 0;
        }
        break;

    case WM_PAINT:
    {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);

        SetBkMode(hdc, TRANSPARENT);

        HFONT oldFont = (HFONT)SelectObject(hdc, g_titleFont);
        SetTextColor(hdc, RGB(245, 247, 250));
        RECT titleRect = { 24, 24, 436, 60 };
        DrawTextW(hdc, L"This is just dummy game", -1, &titleRect, DT_LEFT | DT_SINGLELINE);

        SelectObject(hdc, g_bodyFont);
        SetTextColor(hdc, RGB(192, 198, 210));

        wchar_t details[640];
        swprintf_s(
            details,
            _countof(details),
            L"Game: %s\nProcess Name: %s\nProcess ID: %lu",
            g_gameName,
            g_processName,
            (unsigned long)g_processId);

        RECT bodyRect = { 24, 72, 436, 160 };
        DrawTextW(hdc, details, -1, &bodyRect, DT_LEFT | DT_WORDBREAK);

        SelectObject(hdc, oldFont);
        EndPaint(hwnd, &ps);
        return 0;
    }

    case WM_DESTROY:
        if (g_titleFont) DeleteObject(g_titleFont);
        if (g_bodyFont) DeleteObject(g_bodyFont);
        if (g_bgBrush) DeleteObject(g_bgBrush);
        PostQuitMessage(0);
        return 0;
    }

    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, PWSTR pCmdLine, int nCmdShow)
{
    (void)hPrevInstance;
    (void)pCmdLine;

    ParseCommandLineGameName();
    ResolveCurrentProcessName();

    g_bgBrush = CreateSolidBrush(RGB(24, 26, 32));

    const wchar_t CLASS_NAME[] = L"VoltrexQuestDummyGameWindow";
    WNDCLASSEXW wc = { 0 };
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WindowProc;
    wc.hInstance = hInstance;
    wc.hCursor = LoadCursorW(NULL, IDC_ARROW);
    wc.hbrBackground = g_bgBrush;
    wc.lpszClassName = CLASS_NAME;

    if (!RegisterClassExW(&wc))
    {
        return 1;
    }

    RECT desired = { 0, 0, 460, 230 };
    DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
    AdjustWindowRect(&desired, style, FALSE);

    int width = desired.right - desired.left;
    int height = desired.bottom - desired.top;
    int screenW = GetSystemMetrics(SM_CXSCREEN);
    int screenH = GetSystemMetrics(SM_CYSCREEN);
    int posX = (screenW - width) / 2;
    int posY = (screenH - height) / 2;

    HWND hwnd = CreateWindowExW(
        0,
        CLASS_NAME,
        L"This is just dummy game",
        style,
        posX, posY, width, height,
        NULL, NULL, hInstance, NULL);

    if (!hwnd)
    {
        return 1;
    }

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0)
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    return (int)msg.wParam;
}

