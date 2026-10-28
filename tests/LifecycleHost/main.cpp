#include <windows.h>
#include <tlhelp32.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <set>
#include <vector>
#include "PluginInterface.h"

struct TestNameCache { uint32_t version{0x314E4255}; wchar_t labels[16][64]{}; };
static_assert(sizeof(TestNameCache) == 2052);
bool WriteNames(const std::filesystem::path& root, const TestNameCache& names) {
    const auto folder = root / L"UsageBridge";
    std::filesystem::create_directories(folder);
    const auto temp = folder / L"display-names.bin.tmp";
    { std::ofstream file(temp, std::ios::binary); file.write(reinterpret_cast<const char*>(&names), sizeof(names)); }
    return MoveFileExW(temp.c_str(), (folder / L"display-names.bin").c_str(), MOVEFILE_REPLACE_EXISTING) != FALSE;
}
int TestNames(ITMPlugin* plugin, const std::filesystem::path& root) {
    TestNameCache names;
    wcscpy_s(names.labels[0], L"余额"); wcscpy_s(names.labels[4], L"余额"); wcscpy_s(names.labels[15], L"Codex 周");
    if (!WriteNames(root, names)) return 10;
    plugin->OnExtenedInfo(ITMPlugin::EI_CONFIG_DIR, root.c_str());
    if (!plugin->GetItem(2) || plugin->GetItem(3) || plugin->GetItem(-1)) return 11;
    if (std::wstring(plugin->GetItem(0)->GetItemName()) != L"余额" || std::wstring(plugin->GetItem(1)->GetItemName()) != L"余额") return 12;
    const int slots[] = {1, 5, 16};
    for (int i = 0; i < 3; ++i)
        if (std::wstring(plugin->GetItem(i)->GetItemId()) != L"UsageBridge.Slot." + std::to_wstring(slots[i])) return 13;
    const wchar_t* first = plugin->GetItem(0)->GetItemName();
    plugin->GetItem(2)->GetItemName();
    if (std::wstring(first) != L"余额") return 14;
    IPluginItem* removed = plugin->GetItem(1);
    wcscpy_s(names.labels[0], L"网页余额"); names.labels[4][0] = L'\0'; wcscpy_s(names.labels[8], L"新增项目");
    if (!WriteNames(root, names)) return 15;
    plugin->DataRequired();
    // The host's existing pointers must remain valid until restart, even after add/delete.
    if (std::wstring(plugin->GetItem(0)->GetItemName()) != L"网页余额" || plugin->GetItem(1) != removed || plugin->GetItem(3)) return 16;
    if (std::wstring(removed->GetItemId()) != L"UsageBridge.Slot.5") return 17;
    names.version = 0;
    if (!WriteNames(root, names)) return 18;
    plugin->OnExtenedInfo(ITMPlugin::EI_CONFIG_DIR, root.c_str());
    if (std::wstring(plugin->GetItem(0)->GetItemName()) != L"网页余额") return 19;
    names.version = 0x314E4255;
    if (!WriteNames(root, names)) return 20;
    return 0;
}
void ReportChildren(std::set<DWORD>& reported) {
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return;
    std::vector<PROCESSENTRY32W> entries;
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry);
    if (Process32FirstW(snapshot, &entry)) do { entries.push_back(entry); } while (Process32NextW(snapshot, &entry));
    CloseHandle(snapshot);
    std::set<DWORD> descendants{GetCurrentProcessId()};
    bool changed;
    do {
        changed = false;
        for (const auto& process : entries)
            if (descendants.contains(process.th32ParentProcessID) && !descendants.contains(process.th32ProcessID)) {
                descendants.insert(process.th32ProcessID); changed = true;
                if (reported.insert(process.th32ProcessID).second)
                    std::wcout << L"CHILD " << process.th32ProcessID << L" " << process.szExeFile << std::endl;
            }
    } while (changed);
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 5) {
        std::wcerr << L"LifecycleHost DLL CONFIG_DIR SECONDS open|auto\n";
        return 2;
    }
    const int seconds = _wtoi(argv[3]);
    if (seconds < 1 || seconds > 3600) return 2;
    HMODULE dll = LoadLibraryW(argv[1]);
    if (!dll) { std::wcerr << L"LoadLibrary failed: " << GetLastError(); return 3; }
    auto get = reinterpret_cast<ITMPlugin*(*)()>(GetProcAddress(dll, "TMPluginGetInstance"));
    if (!get) { FreeLibrary(dll); return 4; }
    auto plugin = get();
    plugin->OnExtenedInfo(ITMPlugin::EI_CONFIG_DIR, argv[2]);
    plugin->OnInitialize(nullptr);
    if (std::wstring(argv[4]) == L"names") {
        int result = TestNames(plugin, argv[2]); FreeLibrary(dll);
        if (result != 0) return result;
        dll = LoadLibraryW(argv[1]);
        if (!dll) return 20;
        get = reinterpret_cast<ITMPlugin*(*)()>(GetProcAddress(dll, "TMPluginGetInstance"));
        plugin = get(); plugin->OnExtenedInfo(ITMPlugin::EI_CONFIG_DIR, argv[2]);
        result = plugin->GetItem(2) && !plugin->GetItem(3) && std::wstring(plugin->GetItem(0)->GetItemName()) == L"网页余额" &&
            std::wstring(plugin->GetItem(1)->GetItemId()) == L"UsageBridge.Slot.9" ? 0 : 21;
        FreeLibrary(dll); if (result) return result;
        TestNameCache empty;
        if (!WriteNames(argv[2], empty)) return 22;
        dll = LoadLibraryW(argv[1]); if (!dll) return 23;
        get = reinterpret_cast<ITMPlugin*(*)()>(GetProcAddress(dll, "TMPluginGetInstance"));
        plugin = get(); plugin->OnExtenedInfo(ITMPlugin::EI_CONFIG_DIR, argv[2]);
        if (plugin->GetItem(0) || plugin->GetItem(-1)) { FreeLibrary(dll); return 24; }
        wcscpy_s(empty.labels[8], L"首次添加");
        if (!WriteNames(argv[2], empty)) { FreeLibrary(dll); return 25; }
        plugin->DataRequired();
        if (plugin->GetItem(0)) { FreeLibrary(dll); return 26; }
        FreeLibrary(dll);
        dll = LoadLibraryW(argv[1]); if (!dll) return 27;
        get = reinterpret_cast<ITMPlugin*(*)()>(GetProcAddress(dll, "TMPluginGetInstance"));
        plugin = get(); plugin->OnExtenedInfo(ITMPlugin::EI_CONFIG_DIR, argv[2]);
        result = plugin->GetItem(0) && !plugin->GetItem(1) && std::wstring(plugin->GetItem(0)->GetItemId()) == L"UsageBridge.Slot.9" ? 0 : 28;
        FreeLibrary(dll);
        if (!result) std::wcout << L"PASS: sparse slots, duplicate names, stable IDs/pointers, rename, restart membership, empty config and first addition" << std::endl;
        return result;
    }    if (std::wstring(argv[4]) == L"open") {
        plugin->ShowOptionsDialog(nullptr);
        plugin->ShowOptionsDialog(nullptr);
    }
    const ULONGLONG until = GetTickCount64() + seconds * 1000ULL;
    std::set<DWORD> children;
    while (GetTickCount64() < until) {
        plugin->DataRequired();
        auto firstItem = plugin->GetItem(0);
        if (firstItem) {
            const wchar_t* firstValue = firstItem->GetItemValueText();
            const std::wstring savedValue(firstValue);
            if (auto second = plugin->GetItem(1)) second->GetItemValueText();
            if (savedValue != firstValue) { std::wcerr << L"Another slot overwrote the first slot's text"; FreeLibrary(dll); return 5; }
        }
        ReportChildren(children);
        std::wcout << (firstItem ? firstItem->GetItemValueText() : L"No registered items") << L" | " << plugin->GetTooltipInfo() << std::endl;        Sleep(250);
    }
    const auto before = GetTickCount64();
    FreeLibrary(dll);
    std::wcout << L"Unload milliseconds: " << GetTickCount64() - before << L'\n';
    return 0;
}
