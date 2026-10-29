#include <windows.h>
#include <objbase.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <mutex>
#include <string>
#include <vector>
#include "PluginInterface.h"

namespace {
HMODULE moduleHandle{};
// Closed ONLY by the owning DLL. Never inherited by the collector.
HANDLE jobHandle{};
struct Display {
    wchar_t label[64];
    wchar_t value[128];
    wchar_t status[512];
    uint32_t alert;
};
struct Snapshot {
    uint32_t version;
    uint32_t collectorPid;
    uint64_t heartbeat;
    Display items[16];
};
static_assert(sizeof(Display) == 1412);
static_assert(sizeof(Snapshot) == 22608);

struct Handle {
    HANDLE value{};
    ~Handle() { reset(); }
    void reset(HANDLE next = nullptr) { if (value) CloseHandle(value); value = next; }
    Handle() = default;
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};

class Item final : public IPluginItem {
public:
    std::mutex guard;
    size_t slot{};
    std::wstring name, id, text{L"Stopped"}, label;
    std::wstring widthSample{L"~999999.99 CNY"};
    bool customDraw{};
    uint32_t alert{};
    COLORREF labelColor{CLR_INVALID}, valueColor{CLR_INVALID};
    bool taskbar{}, rightAlign{};
    bool IsCustomDraw() const override { return customDraw; }
    int GetItemWidth() const override { return 160; }
    int GetItemWidthEx(void* context) const override {
        auto& self = const_cast<Item&>(*this);
        std::lock_guard lock(self.guard);
        const auto dc = static_cast<HDC>(context);
        SIZE a{}, b{};
        const auto title = name + L": ";
        GetTextExtentPoint32W(dc, title.c_str(), static_cast<int>(title.size()), &a);
        GetTextExtentPoint32W(dc, widthSample.c_str(), static_cast<int>(widthSample.size()), &b);
        return a.cx + b.cx + MulDiv(8, GetDeviceCaps(dc, LOGPIXELSX), 96);
    }
    void DrawItem(void* context, int x, int y, int w, int h, bool dark) override {
        std::lock_guard lock(guard);
        const auto dc = static_cast<HDC>(context);
        int saved = SaveDC(dc);
        if (!saved) return;
        IntersectClipRect(dc, x, y, x + w, y + h);
        RECT area{x, y, x + w, y + h};
        COLORREF bg = CLR_INVALID;
        switch (alert) {
        case 1: bg = dark ? RGB(91, 77, 24) : RGB(255, 243, 176); break;
        case 2: bg = dark ? RGB(116, 75, 18) : RGB(255, 203, 105); break;
        case 3: bg = dark ? RGB(153, 51, 25) : RGB(255, 112, 67); break;
        case 4: bg = dark ? RGB(28, 91, 58) : RGB(183, 232, 196); break;
        }
        if (bg != CLR_INVALID) {
            HBRUSH brush = CreateSolidBrush(bg); FillRect(dc, &area, brush); DeleteObject(brush);
        }
        const auto normal = GetTextColor(dc);
        auto color = [&](COLORREF configured) { return bg != CLR_INVALID ? (dark ? RGB(255,255,255) : RGB(25,25,25)) : (configured == CLR_INVALID ? normal : configured); };
        SetBkMode(dc, TRANSPARENT);
        const int pad = MulDiv(4, GetDeviceCaps(dc, LOGPIXELSX), 96);
        area.left += pad; area.right -= pad;
        auto title = (label.empty() ? name : label) + L": ";
        SIZE size{}; GetTextExtentPoint32W(dc, title.c_str(), static_cast<int>(title.size()), &size);
        SetTextColor(dc, color(labelColor));
        DrawTextW(dc, title.c_str(), -1, &area, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
        area.left += size.cx;
        SetTextColor(dc, color(valueColor));
        DrawTextW(dc, text.c_str(), -1, &area, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | DT_END_ELLIPSIS | (taskbar && rightAlign ? DT_RIGHT : DT_LEFT));
        RestoreDC(dc, saved);
    }
    void setAlert(uint32_t next) { std::lock_guard lock(guard); alert = next <= 4 ? next : 0; }
    const wchar_t* GetItemName() const override {
        thread_local wchar_t buffer[16][64]{};
        auto& self = const_cast<Item&>(*this);
        std::lock_guard lock(self.guard);
        wcsncpy_s(buffer[slot], name.c_str(), _TRUNCATE); return buffer[slot];
    }
    const wchar_t* GetItemId() const override { return id.c_str(); }
    const wchar_t* GetItemLableText() const override {
        thread_local wchar_t buffer[16][64]{}; // POD: no destructor after DLL unload.
        auto& self = const_cast<Item&>(*this);
        std::lock_guard lock(self.guard);
        wcsncpy_s(buffer[slot], label.c_str(), _TRUNCATE); return buffer[slot];
    }
    const wchar_t* GetItemValueSampleText() const override {
        thread_local wchar_t buffer[16][64]{};
        auto& self = const_cast<Item&>(*this);
        std::lock_guard lock(self.guard);
        wcsncpy_s(buffer[slot], widthSample.c_str(), _TRUNCATE); return buffer[slot];
    }
    const wchar_t* GetItemValueText() const override {
        thread_local wchar_t buffer[16][128]{}; // Trivially destructible TLS only.
        auto& self = const_cast<Item&>(*this);
        std::lock_guard lock(self.guard);
        wcsncpy_s(buffer[slot], text.c_str(), _TRUNCATE); return buffer[slot];
    }
    void set(std::wstring next) {
        std::lock_guard lock(guard);
        text = std::move(next);
    }
    void setLabel(std::wstring next) { std::lock_guard lock(guard); label = std::move(next); }
    void setWidthSample(const wchar_t* next) { std::lock_guard lock(guard); widthSample = *next ? next : L"~999999.99 CNY"; }
    void setName(std::wstring next) { std::lock_guard lock(guard); name = next.empty() ? L"未配置" : std::move(next); }
};

class Plugin final : public ITMPlugin {
    std::array<Item, 16> items;
    std::array<bool, 16> configured{};
    std::vector<size_t> registeredSlots;
    bool enumerationFrozen{};
    std::mutex guard;
    std::filesystem::path config;
    std::wstring ipc, tooltip{L"Open DataPane options to configure sources."};
    void setAll(const wchar_t* text) { for (auto& item : items) { item.set(text); item.setAlert(0); } }
    Handle process, mapping, snapshotMutex, showEvent, stopEvent;
    ULONGLONG nextAttempt{};
    ULONGLONG nextNamesRead{};
    void readWidths() {
        if (config.empty()) return;
        Handle file;
        file.value = CreateFileW((config / L"display-widths.bin").c_str(), GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file.value == INVALID_HANDLE_VALUE) { file.value = nullptr; return; }
        struct Widths { uint32_t version; wchar_t samples[16][64]; } widths{};
        static_assert(sizeof(Widths) == 2052);
        LARGE_INTEGER length{};
        DWORD read{};
        if (!GetFileSizeEx(file.value, &length) || length.QuadPart != sizeof(widths) ||
            !ReadFile(file.value, &widths, sizeof(widths), &read, nullptr) || read != sizeof(widths) || widths.version != 0x31575044) return;
        for (const auto& sample : widths.samples) if (sample[63] != L'\0') return;
        for (size_t i = 0; i < items.size(); ++i) items[i].setWidthSample(widths.samples[i]);
    }
    void readNames() {
        readWidths();
        if (config.empty()) return;
        if (!enumerationFrozen) {
            Handle render;
            render.value = CreateFileW((config / L"display-render.bin").c_str(), GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (render.value == INVALID_HANDLE_VALUE) render.value = nullptr;
            struct Render { uint32_t version, mask; } info{};
            DWORD read{}; LARGE_INTEGER length{};
            if (render.value && GetFileSizeEx(render.value, &length) && length.QuadPart == sizeof(info) &&
                ReadFile(render.value, &info, sizeof(info), &read, nullptr) && read == sizeof(info) && info.version == 0x31525044)
                for (size_t i = 0; i < items.size(); ++i) items[i].customDraw = (info.mask & (1u << i)) != 0;
        }
        Handle file;
        file.value = CreateFileW((config / L"display-names.bin").c_str(), GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file.value == INVALID_HANDLE_VALUE) { file.value = nullptr; return; }
        struct Names { uint32_t version; wchar_t labels[16][64]; } names{};
        static_assert(sizeof(Names) == 2052);
        LARGE_INTEGER length{};
        DWORD read{};
        if (!GetFileSizeEx(file.value, &length) || length.QuadPart != sizeof(names) ||
            !ReadFile(file.value, &names, sizeof(names), &read, nullptr) || read != sizeof(names) || names.version != 0x314E5044) return;
        for (const auto& label : names.labels) if (label[63] != L'\0') return;
        for (size_t i = 0; i < items.size(); ++i) {
            configured[i] = names.labels[i][0] != L'\0';
            items[i].setName(names.labels[i]);
        }
    }
    void fail(const wchar_t* action) {
        const DWORD error = GetLastError();
        tooltip = std::wstring(action) + L" (Win32 " + std::to_wstring(error) + L")";
        setAll(L"Error");
    }
    bool alive() const { return process.value && WaitForSingleObject(process.value, 0) == WAIT_TIMEOUT; }
    bool enabled() const {
        return !config.empty() && GetFileAttributesW((config / L"collection.enabled").c_str()) != INVALID_FILE_ATTRIBUTES;
    }
    void start(bool show) {
        if (alive()) { if (show) SetEvent(showEvent.value); return; }
        if (config.empty()) return;
        if (jobHandle) { CloseHandle(jobHandle); jobHandle = nullptr; }
        process.reset();
        GUID id{};
        if (FAILED(CoCreateGuid(&id))) { setAll(L"IPC error"); return; }
        wchar_t guid[40]{};
        StringFromGUID2(id, guid, 40);
        ipc = L"Local\\DataPane-" + std::to_wstring(GetCurrentProcessId()) + L"-" + guid;
        mapping.reset(CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(Snapshot), (ipc + L"-snapshot").c_str()));
        snapshotMutex.reset(CreateMutexW(nullptr, FALSE, (ipc + L"-mutex").c_str()));
        showEvent.reset(CreateEventW(nullptr, FALSE, show, (ipc + L"-show").c_str()));
        stopEvent.reset(CreateEventW(nullptr, TRUE, FALSE, (ipc + L"-stop").c_str()));
        jobHandle = CreateJobObjectW(nullptr, nullptr);
        if (!mapping.value || !snapshotMutex.value || !showEvent.value || !stopEvent.value || !jobHandle) { fail(L"Create IPC/job"); return; }
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, &limits, sizeof(limits))) { fail(L"Configure job"); return; }

        // Atomic job assignment avoids the crash gap between CreateProcess and AssignProcessToJobObject.
        SIZE_T bytes{};
        InitializeProcThreadAttributeList(nullptr, 1, 0, &bytes);
        std::vector<unsigned char> buffer(bytes);
        auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(buffer.data());
        if (!InitializeProcThreadAttributeList(attributes, 1, 0, &bytes)) { fail(L"Initialize attributes"); return; }
        if (!UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_JOB_LIST, &jobHandle, sizeof(jobHandle), nullptr, nullptr)) {
            fail(L"Assign job at creation"); DeleteProcThreadAttributeList(attributes); return;
        }
        wchar_t dllPath[32768]{};
        GetModuleFileNameW(moduleHandle, dllPath, 32768);
        const auto exe = std::filesystem::path(dllPath).parent_path() / L"DataPane" / L"DataPane.Collector.exe";
        // Paths are Windows paths (cannot contain quotes); append a filename to avoid trailing backslash quoting.
        std::wstring command = L"\"" + exe.wstring() + L"\" --ipc \"" + ipc + L"\" --config \"" + (config / L"config.json").wstring() + L"\"";
        STARTUPINFOEXW startup{};
        startup.StartupInfo.cb = sizeof(startup);
        startup.lpAttributeList = attributes;
        PROCESS_INFORMATION child{};
        BOOL created = CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE,
            EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW, nullptr, exe.parent_path().c_str(), &startup.StartupInfo, &child);
        if (!created) fail(L"Start collector");
        DeleteProcThreadAttributeList(attributes);
        if (created) { process.reset(child.hProcess); CloseHandle(child.hThread); setAll(L"Starting"); }
    }
public:
    Plugin() {
        for (size_t i = 0; i < items.size(); ++i) {
            items[i].slot = i;
            items[i].name = L"未配置";
            items[i].id = L"DataPane.Slot." + std::to_wstring(i + 1);
        }
    }
    IPluginItem* GetItem(int index) override {
        std::lock_guard lock(guard);
        if (index < 0) return nullptr;
        // TM stores these pointers until unload. Freeze membership, not names or values.
        if (!enumerationFrozen) {
            for (size_t i = 0; i < items.size(); ++i) if (configured[i]) registeredSlots.push_back(i);
            enumerationFrozen = true;
        }
        return static_cast<size_t>(index) < registeredSlots.size() ? &items[registeredSlots[index]] : nullptr;
    }
    const wchar_t* GetInfo(PluginInfoIndex index) override {
        switch (index) {
        case TMI_NAME: return L"DataPane";
        case TMI_DESCRIPTION: return L"Usage, JSON and web data collector";
        case TMI_AUTHOR: return L"DataPane contributors";
        case TMI_VERSION: return L"0.4.0-dev";
        default: return L"";
        }
    }
    void OnExtenedInfo(ExtendedInfoIndex index, const wchar_t* data) override {
        if (!data) return;
        std::lock_guard lock(guard);
        if (index != EI_CONFIG_DIR) {
            const auto value = wcstoul(data, nullptr, 10);
            for (auto& item : items) {
                std::lock_guard itemLock(item.guard);
                if (index == EI_LABEL_TEXT_COLOR) item.labelColor = value;
                if (index == EI_VALUE_TEXT_COLOR) item.valueColor = value;
                if (index == EI_DRAW_TASKBAR_WND) item.taskbar = value != 0;
                if (index == EI_TASKBAR_WND_VALUE_RIGHT_ALIGN) item.rightAlign = value != 0;
            }
            return;
        }
        config = std::filesystem::path(data) / L"DataPane";
        readNames();
        // Directory creation is done by the collector only when the user opens settings.
    }
    void DataRequired() override {
        std::lock_guard lock(guard);
        try {
            const auto now = GetTickCount64();
            if (now >= nextNamesRead) { nextNamesRead = now + 1000; readNames(); }
            if (!alive()) {
                if (enabled() && now >= nextAttempt) { nextAttempt = now + 30000; start(false); }
                else if (!enabled()) setAll(L"Stopped");
                else setAll(L"Collector stopped");
                return;
            }
            const DWORD acquired = WaitForSingleObject(snapshotMutex.value, 0);
            if (acquired != WAIT_OBJECT_0 && acquired != WAIT_ABANDONED) return;
            Snapshot copy{};
            auto view = MapViewOfFile(mapping.value, FILE_MAP_READ, 0, 0, sizeof(copy));
            if (view) { memcpy(&copy, view, sizeof(copy)); UnmapViewOfFile(view); }
            ReleaseMutex(snapshotMutex.value);
            if (!view || acquired == WAIT_ABANDONED || copy.version != 3) { setAll(L"Snapshot unavailable"); return; }
            const bool stale = now < copy.heartbeat || now - copy.heartbeat > 5000;
            tooltip.clear();
            for (size_t i = 0; i < items.size(); ++i) {
                auto& entry = copy.items[i];
                entry.label[63] = 0; entry.value[127] = 0; entry.status[511] = 0;
                items[i].setLabel(entry.label);
                items[i].set(std::wstring(stale ? L"Stale: " : L"") + entry.value);
                items[i].setAlert(stale ? 0 : entry.alert);
                if (*entry.label) tooltip += std::wstring(entry.label) + L": " + entry.status + L"\n";
            }
        } catch (...) { setAll(L"Plugin error"); }
    }
    OptionReturn ShowOptionsDialog(void*) override {
        std::lock_guard lock(guard);
        try { start(true); } catch (...) { setAll(L"Start error"); }
        return OR_OPTION_UNCHANGED;
    }
    const wchar_t* GetTooltipInfo() override {
        std::lock_guard lock(guard);
        thread_local wchar_t buffer[8192]{};
        wcsncpy_s(buffer, tooltip.c_str(), _TRUNCATE); return buffer;
    }
};
Plugin plugin;
}

extern "C" __declspec(dllexport) ITMPlugin* TMPluginGetInstance() { return &plugin; }
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) moduleHandle = instance;
    if (reason == DLL_PROCESS_DETACH && jobHandle) {
        // No threads, waits, IPC, COM teardown, or UI under the loader lock.
        CloseHandle(jobHandle); jobHandle = nullptr;
    }
    return TRUE;
}
