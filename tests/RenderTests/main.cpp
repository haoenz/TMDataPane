#include "../../src/Plugin.Native/Plugin.cpp"
#include <iostream>

int main() {
    HDC dc = CreateCompatibleDC(nullptr);
    BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = 600; info.bmiHeader.biHeight = -100;
    info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32;
    void* pixels{};
    HBITMAP bitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
    if (!dc || !bitmap) return 1;
    auto old = SelectObject(dc, bitmap);
    Item item; item.setName(L"Codex 5h"); item.set(L"75%"); item.setWidthSample(L"~100%");
    item.customDraw = true;
    const COLORREF expected[] = {RGB(255,243,176), RGB(255,203,105), RGB(255,112,67), RGB(183,232,196)};
    for (int fontSize : {12, 18, 24}) {
        HFONT font = CreateFontW(-fontSize, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
            0, 0, 0, 0, L"Segoe UI");
        auto oldFont = SelectObject(dc, font);
        int width = item.GetItemWidthEx(dc);
        if (width < 80 || width > 500) return 2;
        for (uint32_t state = 0; state <= 4; ++state) {
            RECT all{0,0,600,100}; FillRect(dc, &all, static_cast<HBRUSH>(GetStockObject(WHITE_BRUSH)));
            SetTextColor(dc, RGB(12,34,56)); SetBkMode(dc, OPAQUE);
            item.setAlert(state); item.DrawItem(dc, 10, 10, width, 40, false);
            if (GetPixel(dc, 11, 11) != (state ? expected[state - 1] : RGB(255,255,255))) return 3;
            if (GetPixel(dc, 9, 10) != RGB(255,255,255) || GetTextColor(dc) != RGB(12,34,56) || GetBkMode(dc) != OPAQUE) return 4;
        }
        item.setAlert(3); item.DrawItem(dc, 10, 10, width, 40, true);
        if (GetPixel(dc,11,11) != RGB(153,51,25)) return 5;
        SelectObject(dc, oldFont); DeleteObject(font);
    }
    SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc);
    std::cout << "PASS: reminder backgrounds, orange-red, dark mode, clipping, font sizing, DC restoration\n";
}
