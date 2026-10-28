#include <windows.h>
#include <tlhelp32.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <set>
#include <vector>
#include "PluginInterface.h"

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
    if (std::wstring(argv[4]) == L"open") {
        plugin->ShowOptionsDialog(nullptr);
        plugin->ShowOptionsDialog(nullptr);
    }
    const ULONGLONG until = GetTickCount64() + seconds * 1000ULL;
    std::set<DWORD> children;
    while (GetTickCount64() < until) {
        plugin->DataRequired();
        const wchar_t* firstValue = plugin->GetItem(0)->GetItemValueText();
        const std::wstring savedValue(firstValue);
        plugin->GetItem(1)->GetItemValueText();
        if (savedValue != firstValue) { std::wcerr << L"Another slot overwrote the first slot's text"; FreeLibrary(dll); return 5; }
        ReportChildren(children);
        std::wcout << plugin->GetItem(0)->GetItemValueText() << L" | " << plugin->GetTooltipInfo() << std::endl;
        Sleep(250);
    }
    const auto before = GetTickCount64();
    FreeLibrary(dll);
    std::wcout << L"Unload milliseconds: " << GetTickCount64() - before << L'\n';
    return 0;
}
