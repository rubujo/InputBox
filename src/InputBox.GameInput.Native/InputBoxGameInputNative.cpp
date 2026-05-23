#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>
#include <GameInput.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <new>
#include <string>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;
using namespace GameInput::v3;

namespace
{
    const HRESULT InputBoxGameInputNoReading = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    constexpr uint32_t InputBoxGameInputShimAbiVersion = 3;
    constexpr uint32_t InputBoxGameInputMaxExtraControlIndexes = 32;

    enum InputBoxGameInputModuleKind : uint32_t
    {
        InputBoxGameInputModuleUnknown = 0,
        InputBoxGameInputModuleSystemGameInput = 1,
        InputBoxGameInputModuleSystemGameInputRedist = 2,
        InputBoxGameInputModuleRegistryGameInputRedist = 3
    };

    enum InputBoxGameInputStringTruncationFlags : uint32_t
    {
        InputBoxGameInputStringTruncatedNone = 0x00000000,
        InputBoxGameInputStringTruncatedDeviceId = 0x00000001,
        InputBoxGameInputStringTruncatedDeviceRootId = 0x00000002,
        InputBoxGameInputStringTruncatedContainerId = 0x00000004,
        InputBoxGameInputStringTruncatedDisplayName = 0x00000008,
        InputBoxGameInputStringTruncatedPnpPath = 0x00000010,
        InputBoxGameInputStringTruncatedAttemptedModulePath = 0x00000020,
        InputBoxGameInputStringTruncatedLoadedModulePath = 0x00000040
    };

    struct InputBoxGameInputVersion
    {
        uint16_t major;
        uint16_t minor;
        uint16_t build;
        uint16_t revision;
    };

    // 下列 C ABI 結構必須與 GameInputPrimitives.cs 的受控端結構保持版面相容。
    // 欄位異動時，請同步更新 InputBoxGameInputShimAbiVersion 與受控端大小檢查。
    struct InputBoxGameInputShimInfo
    {
        uint32_t abiVersion;
        uint32_t gameInputApiVersion;
        uint32_t pointerSize;
        uint32_t shimInfoSize;
        uint32_t runtimeProbeInfoSize;
        uint32_t deviceInfoSize;
        uint32_t gamepadStateSize;
        uint32_t diagnosticsSnapshotSize;
        uint32_t loadedModuleKind;
        char loadedModulePath[512];
    };

    struct InputBoxGameInputRuntimeProbeInfo
    {
        uint32_t abiVersion;
        uint32_t gameInputApiVersion;
        uint32_t pointerSize;
        uint32_t shimInfoSize;
        uint32_t runtimeProbeInfoSize;
        uint32_t deviceInfoSize;
        uint32_t gamepadStateSize;
        uint32_t diagnosticsSnapshotSize;
        uint32_t attemptedModuleKind;
        uint32_t loadedModuleKind;
        int32_t loadLibraryHResult;
        int32_t getProcAddressHResult;
        int32_t initializeHResult;
        int32_t finalHResult;
        uint32_t loadLibraryWin32Error;
        uint32_t getProcAddressWin32Error;
        uint32_t initializeWin32Error;
        uint32_t stringTruncationFlags;
        char attemptedModulePath[512];
        char loadedModulePath[512];
    };

    struct InputBoxGameInputDeviceInfo
    {
        uint16_t vendorId;
        uint16_t productId;
        uint16_t revisionNumber;
        uint16_t usagePage;
        uint16_t usageId;
        uint16_t reserved;
        uint32_t deviceFamily;
        uint32_t supportedInput;
        uint32_t supportedRumbleMotors;
        uint32_t supportedSystemButtons;
        uint32_t gamepadSupportedLayout;
        uint32_t gamepadExtraButtonCount;
        uint32_t gamepadExtraAxisCount;
        uint32_t forceFeedbackMotorCount;
        uint32_t inputReportCount;
        uint32_t outputReportCount;
        uint32_t extraButtonCount;
        uint32_t extraAxisCount;
        uint32_t extraButtonIndexCount;
        uint32_t extraAxisIndexCount;
        uint32_t hasInputMapper;
        uint32_t stringTruncationFlags;
        InputBoxGameInputVersion hardwareVersion;
        InputBoxGameInputVersion firmwareVersion;
        uint8_t extraButtonIndexes[InputBoxGameInputMaxExtraControlIndexes];
        uint8_t extraAxisIndexes[InputBoxGameInputMaxExtraControlIndexes];
        char deviceId[65];
        char deviceRootId[65];
        char containerId[39];
        char displayName[256];
        char pnpPath[512];
    };

    struct InputBoxGameInputGamepadState
    {
        uint64_t timestamp;
        uint32_t inputKind;
        uint32_t buttons;
        float leftTrigger;
        float rightTrigger;
        float leftThumbstickX;
        float leftThumbstickY;
        float rightThumbstickX;
        float rightThumbstickY;
    };

    struct InputBoxGameInputDiagnosticsSnapshot
    {
        uint64_t missingReadingCount;
        uint64_t repeatedTimestampCount;
        uint64_t backwardTimestampCount;
        uint64_t deviceUnavailableRefreshCount;
        uint64_t lastReadingTimestamp;
        int32_t lastReadHResult;
        uint32_t lastReadDeviceStatus;
        uint32_t reserved;
    };

    struct DeviceEntry
    {
        ComPtr<IGameInputDevice> device;
        InputBoxGameInputDeviceInfo info{};
    };

    using InputBoxGameInputReadingCallback = void(__stdcall*)(
        void* context,
        const InputBoxGameInputGamepadState* state);

    using InputBoxGameInputDeviceCallback = void(__stdcall*)(
        void* context,
        const char* deviceId,
        uint64_t timestamp,
        uint32_t currentStatus,
        uint32_t previousStatus);

    struct CallbackRegistration
    {
        GameInputCallbackToken token = 0;
        InputBoxGameInputReadingCallback readingCallback = nullptr;
        InputBoxGameInputDeviceCallback deviceCallback = nullptr;
        void* callbackContext = nullptr;
        std::atomic<bool> active = true;
    };

    struct InputBoxGameInputContext
    {
        HMODULE module = nullptr;
        uint32_t moduleKind = InputBoxGameInputModuleUnknown;
        char modulePath[512]{};
        ComPtr<IGameInput> gameInput;
        std::vector<DeviceEntry> devices;
        std::vector<std::unique_ptr<CallbackRegistration>> callbacks;
        // 保護 GameInput COM 存取，以及銷毀期間的裝置、回呼與診斷狀態。
        // 持有此鎖時不得呼叫受控端回呼。
        SRWLOCK lock = SRWLOCK_INIT;
        uint64_t lastReadingTimestamp = 0;
        uint64_t missingReadingCount = 0;
        uint64_t repeatedTimestampCount = 0;
        uint64_t backwardTimestampCount = 0;
        uint64_t deviceUnavailableRefreshCount = 0;
        int32_t lastReadHResult = S_OK;
        uint32_t lastReadDeviceStatus = 0;
    };

    using GameInputInitializeFn = HRESULT(WINAPI*)(
        _In_ REFIID riid,
        _COM_Outptr_ LPVOID* ppv);

    class SharedContextLock
    {
    public:
        explicit SharedContextLock(SRWLOCK& lock) noexcept
            : _lock(lock)
        {
            AcquireSRWLockShared(&_lock);
        }

        SharedContextLock(const SharedContextLock&) = delete;
        SharedContextLock& operator=(const SharedContextLock&) = delete;

        ~SharedContextLock() noexcept
        {
            ReleaseSRWLockShared(&_lock);
        }

    private:
        SRWLOCK& _lock;
    };

    class ExclusiveContextLock
    {
    public:
        explicit ExclusiveContextLock(SRWLOCK& lock) noexcept
            : _lock(lock)
        {
            AcquireSRWLockExclusive(&_lock);
        }

        ExclusiveContextLock(const ExclusiveContextLock&) = delete;
        ExclusiveContextLock& operator=(const ExclusiveContextLock&) = delete;

        ~ExclusiveContextLock() noexcept
        {
            ReleaseSRWLockExclusive(&_lock);
        }

    private:
        SRWLOCK& _lock;
    };

    bool CopyUtf8(
        char* destination,
        size_t destinationLength,
        const char* value) noexcept
    {
        if (destinationLength == 0)
        {
            return false;
        }

        const char* source = value != nullptr ? value : "";
        bool truncated = strlen(source) >= destinationLength;
        strncpy_s(destination, destinationLength, source, _TRUNCATE);

        return truncated;
    }

    bool CopyWideAsUtf8(
        char* destination,
        size_t destinationLength,
        const wchar_t* value) noexcept
    {
        if (destinationLength == 0)
        {
            return false;
        }

        destination[0] = '\0';

        if (value == nullptr ||
            value[0] == L'\0')
        {
            return false;
        }

        int required = WideCharToMultiByte(
            CP_UTF8,
            0,
            value,
            -1,
            nullptr,
            0,
            nullptr,
            nullptr);

        if (required <= 0)
        {
            return false;
        }

        std::vector<char> converted(static_cast<size_t>(required));
        int written = WideCharToMultiByte(
            CP_UTF8,
            0,
            value,
            -1,
            converted.data(),
            required,
            nullptr,
            nullptr);

        return written > 0 &&
            CopyUtf8(destination, destinationLength, converted.data());
    }

    bool CopyDeviceId(
        char* destination,
        size_t destinationLength,
        const APP_LOCAL_DEVICE_ID& deviceId) noexcept
    {
        if (destinationLength < 2)
        {
            return true;
        }

        destination[0] = '\0';

        const auto* bytes = reinterpret_cast<const uint8_t*>(&deviceId);
        const size_t byteCount = std::min(sizeof(APP_LOCAL_DEVICE_ID), (destinationLength - 1) / 2);

        for (size_t i = 0; i < byteCount; i++)
        {
            sprintf_s(destination + (i * 2), destinationLength - (i * 2), "%02X", bytes[i]);
        }

        return byteCount < sizeof(APP_LOCAL_DEVICE_ID);
    }

    bool CopyGuid(
        char* destination,
        size_t destinationLength,
        const GUID& value) noexcept
    {
        wchar_t buffer[39]{};
        int written = StringFromGUID2(value, buffer, static_cast<int>(std::size(buffer)));

        if (written <= 0)
        {
            return CopyUtf8(destination, destinationLength, "");
        }

        return CopyWideAsUtf8(destination, destinationLength, buffer);
    }

    void FillAbiSizes(
        uint32_t& pointerSize,
        uint32_t& shimInfoSize,
        uint32_t& runtimeProbeInfoSize,
        uint32_t& deviceInfoSize,
        uint32_t& gamepadStateSize,
        uint32_t& diagnosticsSnapshotSize) noexcept
    {
        pointerSize = static_cast<uint32_t>(sizeof(void*));
        shimInfoSize = static_cast<uint32_t>(sizeof(InputBoxGameInputShimInfo));
        runtimeProbeInfoSize = static_cast<uint32_t>(sizeof(InputBoxGameInputRuntimeProbeInfo));
        deviceInfoSize = static_cast<uint32_t>(sizeof(InputBoxGameInputDeviceInfo));
        gamepadStateSize = static_cast<uint32_t>(sizeof(InputBoxGameInputGamepadState));
        diagnosticsSnapshotSize = static_cast<uint32_t>(sizeof(InputBoxGameInputDiagnosticsSnapshot));
    }

    void FillShimInfoCommon(InputBoxGameInputShimInfo* info) noexcept
    {
        info->abiVersion = InputBoxGameInputShimAbiVersion;
        info->gameInputApiVersion = GAMEINPUT_API_VERSION;
        FillAbiSizes(
            info->pointerSize,
            info->shimInfoSize,
            info->runtimeProbeInfoSize,
            info->deviceInfoSize,
            info->gamepadStateSize,
            info->diagnosticsSnapshotSize);
    }

    void FillRuntimeProbeCommon(InputBoxGameInputRuntimeProbeInfo* info) noexcept
    {
        info->abiVersion = InputBoxGameInputShimAbiVersion;
        info->gameInputApiVersion = GAMEINPUT_API_VERSION;
        FillAbiSizes(
            info->pointerSize,
            info->shimInfoSize,
            info->runtimeProbeInfoSize,
            info->deviceInfoSize,
            info->gamepadStateSize,
            info->diagnosticsSnapshotSize);
        info->loadLibraryHResult = E_FAIL;
        info->getProcAddressHResult = E_FAIL;
        info->initializeHResult = E_FAIL;
        info->finalHResult = E_FAIL;
    }

    InputBoxGameInputVersion ToInputBoxVersion(const GameInputVersion& version) noexcept
    {
        return InputBoxGameInputVersion
        {
            version.major,
            version.minor,
            version.build,
            version.revision
        };
    }

    void FillGamepadState(
        IGameInputReading* reading,
        const GameInputGamepadState& source,
        InputBoxGameInputGamepadState* destination) noexcept
    {
        destination->timestamp = reading != nullptr ? reading->GetTimestamp() : 0;
        destination->inputKind = reading != nullptr ? static_cast<uint32_t>(reading->GetInputKind()) : 0;
        destination->buttons = static_cast<uint32_t>(source.buttons);
        destination->leftTrigger = source.leftTrigger;
        destination->rightTrigger = source.rightTrigger;
        destination->leftThumbstickX = source.leftThumbstickX;
        destination->leftThumbstickY = source.leftThumbstickY;
        destination->rightThumbstickX = source.rightThumbstickX;
        destination->rightThumbstickY = source.rightThumbstickY;
    }

    HRESULT FillDeviceInfo(
        IGameInputDevice* device,
        InputBoxGameInputDeviceInfo* destination) noexcept
    {
        if (device == nullptr ||
            destination == nullptr)
        {
            return E_INVALIDARG;
        }

        const GameInputDeviceInfo* info = nullptr;
        HRESULT hr = device->GetDeviceInfo(&info);

        if (FAILED(hr))
        {
            return hr;
        }

        *destination = {};

        destination->vendorId = info->vendorId;
        destination->productId = info->productId;
        destination->revisionNumber = info->revisionNumber;
        destination->usagePage = info->usage.page;
        destination->usageId = info->usage.id;
        destination->deviceFamily = static_cast<uint32_t>(info->deviceFamily);
        destination->supportedInput = static_cast<uint32_t>(info->supportedInput);
        destination->supportedRumbleMotors = static_cast<uint32_t>(info->supportedRumbleMotors);
        destination->supportedSystemButtons = static_cast<uint32_t>(info->supportedSystemButtons);
        destination->forceFeedbackMotorCount = info->forceFeedbackMotorCount;
        destination->inputReportCount = info->inputReportCount;
        destination->outputReportCount = info->outputReportCount;
        destination->hardwareVersion = ToInputBoxVersion(info->hardwareVersion);
        destination->firmwareVersion = ToInputBoxVersion(info->firmwareVersion);

        if (CopyDeviceId(destination->deviceId, sizeof(destination->deviceId), info->deviceId))
        {
            destination->stringTruncationFlags |= InputBoxGameInputStringTruncatedDeviceId;
        }

        if (CopyDeviceId(destination->deviceRootId, sizeof(destination->deviceRootId), info->deviceRootId))
        {
            destination->stringTruncationFlags |= InputBoxGameInputStringTruncatedDeviceRootId;
        }

        if (CopyGuid(destination->containerId, sizeof(destination->containerId), info->containerId))
        {
            destination->stringTruncationFlags |= InputBoxGameInputStringTruncatedContainerId;
        }

        if (CopyUtf8(destination->displayName, sizeof(destination->displayName), info->displayName))
        {
            destination->stringTruncationFlags |= InputBoxGameInputStringTruncatedDisplayName;
        }

        if (CopyUtf8(destination->pnpPath, sizeof(destination->pnpPath), info->pnpPath))
        {
            destination->stringTruncationFlags |= InputBoxGameInputStringTruncatedPnpPath;
        }

        if (info->gamepadInfo != nullptr)
        {
            destination->gamepadSupportedLayout = static_cast<uint32_t>(info->gamepadInfo->supportedLayout);
            destination->gamepadExtraButtonCount = info->gamepadInfo->extraButtonCount;
            destination->gamepadExtraAxisCount = info->gamepadInfo->extraAxisCount;
        }

        ComPtr<IGameInputMapper> mapper;
        if (SUCCEEDED(device->CreateInputMapper(&mapper)) &&
            mapper != nullptr)
        {
            destination->hasInputMapper = 1;
        }

        uint32_t extraButtonCount = 0;
        if (SUCCEEDED(device->GetExtraButtonCount(GameInputKindGamepad, &extraButtonCount)))
        {
            destination->extraButtonCount = extraButtonCount;
            destination->extraButtonIndexCount = std::min(extraButtonCount, InputBoxGameInputMaxExtraControlIndexes);

            if (destination->extraButtonIndexCount > 0)
            {
                std::vector<uint8_t> indexes(extraButtonCount);

                if (SUCCEEDED(device->GetExtraButtonIndexes(GameInputKindGamepad, extraButtonCount, indexes.data())))
                {
                    std::copy_n(indexes.data(), destination->extraButtonIndexCount, destination->extraButtonIndexes);
                }
                else
                {
                    destination->extraButtonIndexCount = 0;
                }
            }
        }

        uint32_t extraAxisCount = 0;
        if (SUCCEEDED(device->GetExtraAxisCount(GameInputKindGamepad, &extraAxisCount)))
        {
            destination->extraAxisCount = extraAxisCount;
            destination->extraAxisIndexCount = std::min(extraAxisCount, InputBoxGameInputMaxExtraControlIndexes);

            if (destination->extraAxisIndexCount > 0)
            {
                std::vector<uint8_t> indexes(extraAxisCount);

                if (SUCCEEDED(device->GetExtraAxisIndexes(GameInputKindGamepad, extraAxisCount, indexes.data())))
                {
                    std::copy_n(indexes.data(), destination->extraAxisIndexCount, destination->extraAxisIndexes);
                }
                else
                {
                    destination->extraAxisIndexCount = 0;
                }
            }
        }

        return S_OK;
    }

    HRESULT TryLoadGameInputModule(
        const wchar_t* path,
        DWORD flags,
        HMODULE* module,
        GameInputInitializeFn* initialize,
        HRESULT* loadLibraryHResult = nullptr,
        DWORD* loadLibraryWin32Error = nullptr,
        HRESULT* getProcAddressHResult = nullptr,
        DWORD* getProcAddressWin32Error = nullptr) noexcept
    {
        *module = nullptr;
        *initialize = nullptr;
        if (loadLibraryHResult != nullptr)
        {
            *loadLibraryHResult = E_FAIL;
        }

        if (loadLibraryWin32Error != nullptr)
        {
            *loadLibraryWin32Error = ERROR_SUCCESS;
        }

        if (getProcAddressHResult != nullptr)
        {
            *getProcAddressHResult = E_FAIL;
        }

        if (getProcAddressWin32Error != nullptr)
        {
            *getProcAddressWin32Error = ERROR_SUCCESS;
        }

        HMODULE candidate = LoadLibraryExW(path, nullptr, flags);

        if (candidate == nullptr)
        {
            DWORD error = GetLastError();
            HRESULT hr = HRESULT_FROM_WIN32(error);

            if (loadLibraryHResult != nullptr)
            {
                *loadLibraryHResult = hr;
            }

            if (loadLibraryWin32Error != nullptr)
            {
                *loadLibraryWin32Error = error;
            }

            return hr;
        }

        if (loadLibraryHResult != nullptr)
        {
            *loadLibraryHResult = S_OK;
        }

        FARPROC proc = GetProcAddress(candidate, "GameInputInitialize");

        if (proc == nullptr)
        {
            DWORD error = GetLastError();
            HRESULT hr = HRESULT_FROM_WIN32(error);
            FreeLibrary(candidate);

            if (getProcAddressHResult != nullptr)
            {
                *getProcAddressHResult = hr;
            }

            if (getProcAddressWin32Error != nullptr)
            {
                *getProcAddressWin32Error = error;
            }

            return hr;
        }

        if (getProcAddressHResult != nullptr)
        {
            *getProcAddressHResult = S_OK;
        }

        *module = candidate;
        *initialize = reinterpret_cast<GameInputInitializeFn>(proc);

        return S_OK;
    }

    HRESULT TryGetRedistPathFromRegistry(std::wstring* path) noexcept
    {
        if (path == nullptr)
        {
            return E_INVALIDARG;
        }

        path->clear();

        std::array<wchar_t, MAX_PATH> redistDir{};
        DWORD redistDirSize = static_cast<DWORD>(redistDir.size() * sizeof(wchar_t));

        LSTATUS status = RegGetValueW(
            HKEY_LOCAL_MACHINE,
            L"SOFTWARE\\Microsoft\\GameInput",
            L"RedistDir",
            RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY,
            nullptr,
            redistDir.data(),
            &redistDirSize);

        if (status != ERROR_SUCCESS)
        {
            return HRESULT_FROM_WIN32(status);
        }

        *path = redistDir.data();

        if (!path->empty() &&
            path->back() != L'\\')
        {
            *path += L'\\';
        }

        *path += L"GameInputRedist.dll";

        return S_OK;
    }

    HRESULT TryLoadGameInputCandidate(
        const wchar_t* path,
        DWORD flags,
        uint32_t moduleKind,
        HMODULE* module,
        GameInputInitializeFn* initialize,
        InputBoxGameInputRuntimeProbeInfo* probe) noexcept
    {
        // 保留每個候選載入嘗試的診斷資訊。GameInput 退避到 XInput 時，
        // 這些欄位是判斷 DLL 缺失、export 缺失或初始化失敗的主要線索。
        if (probe != nullptr)
        {
            probe->attemptedModuleKind = moduleKind;
            if (CopyWideAsUtf8(probe->attemptedModulePath, sizeof(probe->attemptedModulePath), path))
            {
                probe->stringTruncationFlags |= InputBoxGameInputStringTruncatedAttemptedModulePath;
            }
        }

        HRESULT loadHr = E_FAIL;
        HRESULT procHr = E_FAIL;
        DWORD loadError = ERROR_SUCCESS;
        DWORD procError = ERROR_SUCCESS;
        HRESULT hr = TryLoadGameInputModule(
            path,
            flags,
            module,
            initialize,
            &loadHr,
            &loadError,
            &procHr,
            &procError);

        if (probe != nullptr)
        {
            probe->loadLibraryHResult = loadHr;
            probe->loadLibraryWin32Error = loadError;
            probe->getProcAddressHResult = procHr;
            probe->getProcAddressWin32Error = procError;

            if (SUCCEEDED(hr))
            {
                probe->loadedModuleKind = moduleKind;

                std::array<wchar_t, 512> loadedPath{};
                DWORD pathLength = GetModuleFileNameW(*module, loadedPath.data(), static_cast<DWORD>(loadedPath.size()));

                if (pathLength > 0 &&
                    CopyWideAsUtf8(probe->loadedModulePath, sizeof(probe->loadedModulePath), loadedPath.data()))
                {
                    probe->stringTruncationFlags |= InputBoxGameInputStringTruncatedLoadedModulePath;
                }
            }
        }

        return hr;
    }

    HRESULT LoadGameInput(
        HMODULE* module,
        uint32_t* moduleKind,
        char* modulePath,
        size_t modulePathLength,
        IGameInput** gameInput,
        InputBoxGameInputRuntimeProbeInfo* probe = nullptr) noexcept
    {
        if (probe != nullptr)
        {
            *probe = {};
            FillRuntimeProbeCommon(probe);
        }

        *module = nullptr;
        *moduleKind = InputBoxGameInputModuleUnknown;
        CopyUtf8(modulePath, modulePathLength, "");
        *gameInput = nullptr;

        GameInputInitializeFn initialize = nullptr;
        // 優先使用 System32 內的系統 GameInput。登錄檔 redist 路徑排在最後，
        // 並使用 DLL_LOAD_DIR + SYSTEM32，避免相依 DLL 從目前工作目錄解析。
        HRESULT hr = TryLoadGameInputCandidate(
            L"GameInput.dll",
            LOAD_LIBRARY_SEARCH_SYSTEM32,
            InputBoxGameInputModuleSystemGameInput,
            module,
            &initialize,
            probe);

        if (SUCCEEDED(hr))
        {
            *moduleKind = InputBoxGameInputModuleSystemGameInput;
        }

        if (FAILED(hr))
        {
            hr = TryLoadGameInputCandidate(
                L"GameInputRedist.dll",
                LOAD_LIBRARY_SEARCH_SYSTEM32,
                InputBoxGameInputModuleSystemGameInputRedist,
                module,
                &initialize,
                probe);

            if (SUCCEEDED(hr))
            {
                *moduleKind = InputBoxGameInputModuleSystemGameInputRedist;
            }
        }

        if (FAILED(hr))
        {
            std::wstring redistPath;
            HRESULT pathHr = TryGetRedistPathFromRegistry(&redistPath);

            if (SUCCEEDED(pathHr))
            {
                hr = TryLoadGameInputCandidate(
                    redistPath.c_str(),
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32,
                    InputBoxGameInputModuleRegistryGameInputRedist,
                    module,
                    &initialize,
                    probe);

                if (SUCCEEDED(hr))
                {
                    *moduleKind = InputBoxGameInputModuleRegistryGameInputRedist;
                }
            }
            else
            {
                hr = pathHr;

                if (probe != nullptr)
                {
                    probe->attemptedModuleKind = InputBoxGameInputModuleRegistryGameInputRedist;
                    probe->loadLibraryHResult = pathHr;
                    probe->loadLibraryWin32Error = HRESULT_FACILITY(pathHr) == FACILITY_WIN32
                        ? HRESULT_CODE(pathHr)
                        : ERROR_SUCCESS;
                }
            }
        }

        if (FAILED(hr))
        {
            if (probe != nullptr)
            {
                probe->finalHResult = hr;
            }

            return hr;
        }

        std::array<wchar_t, 512> path{};
        DWORD pathLength = GetModuleFileNameW(*module, path.data(), static_cast<DWORD>(path.size()));
        if (pathLength > 0)
        {
            bool truncated = CopyWideAsUtf8(modulePath, modulePathLength, path.data());

            if (probe != nullptr &&
                truncated)
            {
                probe->stringTruncationFlags |= InputBoxGameInputStringTruncatedLoadedModulePath;
            }
        }

        hr = initialize(IID_IGameInput, reinterpret_cast<void**>(gameInput));

        if (probe != nullptr)
        {
            probe->initializeHResult = hr;
            probe->initializeWin32Error = FAILED(hr) && HRESULT_FACILITY(hr) == FACILITY_WIN32
                ? HRESULT_CODE(hr)
                : ERROR_SUCCESS;
            probe->finalHResult = hr;
        }

        if (FAILED(hr))
        {
            FreeLibrary(*module);
            *module = nullptr;
            *moduleKind = InputBoxGameInputModuleUnknown;
            CopyUtf8(modulePath, modulePathLength, "");
        }
        else if (probe != nullptr)
        {
            probe->finalHResult = S_OK;
        }

        return hr;
    }

    DeviceEntry* FindDevice(
        InputBoxGameInputContext* context,
        const char* deviceId) noexcept
    {
        if (context == nullptr ||
            deviceId == nullptr)
        {
            return nullptr;
        }

        for (DeviceEntry& entry : context->devices)
        {
            if (strcmp(entry.info.deviceId, deviceId) == 0)
            {
                return &entry;
            }
        }

        return nullptr;
    }

    HRESULT CopyDeviceLocked(
        InputBoxGameInputContext* context,
        const char* deviceId,
        ComPtr<IGameInputDevice>* device,
        InputBoxGameInputDeviceInfo* info = nullptr) noexcept
    {
        // 呼叫端必須先持有 context->lock。回傳的 ComPtr 可讓裝置離開 vector 後仍存活，
        // 但 vector 查找本身仍必須同步。
        if (context == nullptr ||
            deviceId == nullptr ||
            device == nullptr)
        {
            return E_INVALIDARG;
        }

        device->Reset();

        DeviceEntry* entry = FindDevice(context, deviceId);

        if (entry == nullptr)
        {
            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        *device = entry->device;

        if (info != nullptr)
        {
            *info = entry->info;
        }

        return S_OK;
    }

    HRESULT CopyDevice(
        InputBoxGameInputContext* context,
        const char* deviceId,
        ComPtr<IGameInputDevice>* device,
        InputBoxGameInputDeviceInfo* info = nullptr) noexcept
    {
        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        SharedContextLock lock(context->lock);

        return CopyDeviceLocked(context, deviceId, device, info);
    }

    void RecordReadResultLocked(
        InputBoxGameInputContext* context,
        HRESULT hr,
        uint32_t deviceStatus,
        uint64_t timestamp) noexcept
    {
        if (context == nullptr)
        {
            return;
        }

        // 這些計數器僅供診斷；受控端邊緣偵測與中立閘門
        // 不應依據它們改變行為。
        context->lastReadHResult = hr;
        context->lastReadDeviceStatus = deviceStatus;

        if (FAILED(hr))
        {
            if (hr == InputBoxGameInputNoReading)
            {
                context->missingReadingCount++;
            }

            return;
        }

        if (timestamp != 0)
        {
            if (context->lastReadingTimestamp == timestamp)
            {
                context->repeatedTimestampCount++;
            }
            else if (context->lastReadingTimestamp > timestamp)
            {
                context->backwardTimestampCount++;
            }

            context->lastReadingTimestamp = timestamp;
        }
    }

    void RecordReadResult(
        InputBoxGameInputContext* context,
        HRESULT hr,
        uint32_t deviceStatus,
        uint64_t timestamp) noexcept
    {
        if (context == nullptr)
        {
            return;
        }

        ExclusiveContextLock lock(context->lock);
        RecordReadResultLocked(context, hr, deviceStatus, timestamp);
    }

    void RecordDeviceUnavailableLocked(InputBoxGameInputContext* context, uint32_t status) noexcept
    {
        if (context == nullptr)
        {
            return;
        }

        context->deviceUnavailableRefreshCount++;
        context->lastReadDeviceStatus = status;
        context->lastReadHResult = HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
    }

    void RecordDeviceUnavailable(InputBoxGameInputContext* context, uint32_t status) noexcept
    {
        if (context == nullptr)
        {
            return;
        }

        ExclusiveContextLock lock(context->lock);
        RecordDeviceUnavailableLocked(context, status);
    }

    struct DeviceCollector
    {
        std::vector<ComPtr<IGameInputDevice>> devices;
    };

    void CALLBACK OnDeviceEnumerated(
        _In_ GameInputCallbackToken,
        _In_ void* context,
        _In_ IGameInputDevice* device,
        _In_ uint64_t,
        _In_ GameInputDeviceStatus currentStatus,
        _In_ GameInputDeviceStatus)
    {
        if (context == nullptr ||
            device == nullptr ||
            (currentStatus & GameInputDeviceConnected) == 0)
        {
            return;
        }

        auto* collector = static_cast<DeviceCollector*>(context);
        collector->devices.emplace_back(device);
    }

    void CALLBACK OnReadingCallback(
        _In_ GameInputCallbackToken,
        _In_ void* context,
        _In_ IGameInputReading* reading)
    {
        // 回呼路徑只作為喚醒與診斷的輔助通道。這裡只把 reading 轉成 POD，
        // 是否重新整理則交由受控端輪詢迴圈決定。
        auto* registration = static_cast<CallbackRegistration*>(context);

        if (registration == nullptr ||
            !registration->active.load() ||
            registration->readingCallback == nullptr ||
            reading == nullptr)
        {
            return;
        }

        GameInputGamepadState gamepadState{};
        if (!reading->GetGamepadState(&gamepadState))
        {
            return;
        }

        InputBoxGameInputGamepadState state{};
        FillGamepadState(reading, gamepadState, &state);

        registration->readingCallback(registration->callbackContext, &state);
    }

    void CALLBACK OnDeviceCallback(
        _In_ GameInputCallbackToken,
        _In_ void* context,
        _In_ IGameInputDevice* device,
        _In_ uint64_t timestamp,
        _In_ GameInputDeviceStatus currentStatus,
        _In_ GameInputDeviceStatus previousStatus)
    {
        // 不要把 IGameInputDevice 跨過 C ABI 傳給受控端。受控層只接收穩定識別與狀態位元，
        // 再透過輪詢執行緒重新整理。
        auto* registration = static_cast<CallbackRegistration*>(context);

        if (registration == nullptr ||
            !registration->active.load() ||
            registration->deviceCallback == nullptr)
        {
            return;
        }

        char deviceId[65]{};

        if (device != nullptr)
        {
            const GameInputDeviceInfo* info = nullptr;
            if (SUCCEEDED(device->GetDeviceInfo(&info)) &&
                info != nullptr)
            {
                CopyDeviceId(deviceId, sizeof(deviceId), info->deviceId);
            }
        }

        registration->deviceCallback(
            registration->callbackContext,
            deviceId,
            timestamp,
            static_cast<uint32_t>(currentStatus),
            static_cast<uint32_t>(previousStatus));
    }

    HRESULT ResolveOptionalDeviceLocked(
        InputBoxGameInputContext* context,
        const char* deviceId,
        ComPtr<IGameInputDevice>* device) noexcept
    {
        if (device == nullptr)
        {
            return E_INVALIDARG;
        }

        device->Reset();

        if (deviceId == nullptr ||
            deviceId[0] == '\0')
        {
            return S_OK;
        }

        return CopyDeviceLocked(context, deviceId, device);
    }

    HRESULT ResolveOptionalDevice(
        InputBoxGameInputContext* context,
        const char* deviceId,
        ComPtr<IGameInputDevice>* device) noexcept
    {
        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        SharedContextLock lock(context->lock);

        return ResolveOptionalDeviceLocked(context, deviceId, device);
    }
}

extern "C"
{
    /**
     * @brief 探測 GameInput runtime 是否可用,不建立持久 context。
     *
     * 啟動期供 managed 層分類 LoadLibrary、GetProcAddress、GameInputInitialize 的
     * 失敗來源;呼叫後立即釋放暫時建立的 IGameInput 與 module 控制代碼,不會留下
     * callback 註冊。
     *
     * @param[out] info 回傳的 runtime probe 資訊(ABI 版本、模組種類、嘗試路徑等);
     *                  不可為 nullptr。
     * @return Native HRESULT;S_OK 表示 runtime 可用,失敗時 info 仍會包含
     *         可供日誌觀察的部分欄位。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputProbeRuntime(
        InputBoxGameInputRuntimeProbeInfo* info) noexcept
    {
        if (info == nullptr)
        {
            return E_INVALIDARG;
        }

        HMODULE module = nullptr;
        uint32_t moduleKind = InputBoxGameInputModuleUnknown;
        char modulePath[512]{};
        ComPtr<IGameInput> gameInput;

        // Probe 只建立短生命週期的 IGameInput，用來分類執行階段載入失敗原因。
        // 不得留下持久 context 或回呼註冊。
        HRESULT hr = LoadGameInput(
            &module,
            &moduleKind,
            modulePath,
            sizeof(modulePath),
            &gameInput,
            info);

        gameInput.Reset();

        if (module != nullptr)
        {
            FreeLibrary(module);
        }

        return hr;
    }

    /**
     * @brief 建立 GameInput shim context;成功時透過 out 指標傳回原生 context。
     *
     * 內部呼叫 LoadGameInput 取得 IGameInput 與 module handle,並儲存於新配置的
     * InputBoxGameInputContext 中。Managed 端應將回傳的指標包裝為
     * SafeGameInputContextHandle,確保在 GC/Dispose 時呼叫 InputBoxGameInputDestroy。
     *
     * @param[out] context 回傳新建立的 context 指標;失敗時設為 nullptr。
     * @return Native HRESULT;失敗時呼叫端應走退避 XInput 路徑。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputCreate(
        InputBoxGameInputContext** context) noexcept
    {
        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        *context = nullptr;

        auto created = new (std::nothrow) InputBoxGameInputContext();

        if (created == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT hr = LoadGameInput(
            &created->module,
            &created->moduleKind,
            created->modulePath,
            sizeof(created->modulePath),
            &created->gameInput);

        if (FAILED(hr))
        {
            delete created;

            return hr;
        }

        *context = created;

        return S_OK;
    }

    /**
     * @brief 釋放 GameInput shim context 與所有附屬資源。
     *
     * 釋放順序:先停止所有 callback(避免回呼觀察到半銷毀的原生狀態)→ 清空裝置
     * 與 callback 清單 → 重置 IGameInput → FreeLibrary 卸載已載入的 module →
     * delete context。通常由 SafeGameInputContextHandle::ReleaseHandle 呼叫。
     *
     * @param context 由 InputBoxGameInputCreate 建立的 context;nullptr 為合法輸入。
     */
    __declspec(dllexport) void __stdcall InputBoxGameInputDestroy(
        InputBoxGameInputContext* context) noexcept
    {
        if (context == nullptr)
        {
            return;
        }

        {
            ExclusiveContextLock lock(context->lock);

            // 先停止回呼，再釋放 vector/module，避免回呼觀察到半銷毀的原生狀態。
            if (context->gameInput != nullptr)
            {
                for (const std::unique_ptr<CallbackRegistration>& registration : context->callbacks)
                {
                    if (registration != nullptr &&
                        registration->token != 0)
                    {
                        registration->active.store(false);
                        context->gameInput->StopCallback(registration->token);
                        context->gameInput->UnregisterCallback(registration->token);
                    }
                }
            }

            context->callbacks.clear();
            context->devices.clear();
        }

        context->gameInput.Reset();

        if (context->module != nullptr)
        {
            FreeLibrary(context->module);
            context->module = nullptr;
        }

        delete context;
    }

    /**
     * @brief 取得 shim 自身與已載入 GameInput runtime 的版本與 ABI 資訊。
     *
     * 回傳的結構包含 shim ABI 版本、GAMEINPUT_API_VERSION、pointer size、所有跨邊界
     * struct 的 size,以及實際載入的 GameInput module kind 與路徑。Managed 端必須以
     * Marshal.SizeOf<T>() 比對 struct size,不符即視為 shim 載錯並退避 XInput。
     *
     * @param context 已建立的 context;允許 nullptr,此時僅回傳 shim 本身資訊。
     * @param[out] info 回傳的 shim/runtime 資訊結構;不可為 nullptr。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputGetShimInfo(
        InputBoxGameInputContext* context,
        InputBoxGameInputShimInfo* info) noexcept
    {
        if (info == nullptr)
        {
            return E_INVALIDARG;
        }

        *info = {};
        FillShimInfoCommon(info);

        if (context != nullptr)
        {
            SharedContextLock lock(context->lock);

            info->loadedModuleKind = context->moduleKind;
            CopyUtf8(info->loadedModulePath, sizeof(info->loadedModulePath), context->modulePath);
        }

        return S_OK;
    }

    /**
     * @brief 取得 shim 累積的診斷快照(missing reading、stale/backward timestamp、
     *        device unavailable refresh 等計數)。
     *
     * 此快照僅供日誌、測試或未來診斷儀表使用,不可直接影響 edge detection、
     * Pause/Resume neutral gate 或任何 UI 命令。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param[out] snapshot 回傳的診斷計數結構;不可為 nullptr。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputGetDiagnosticsSnapshot(
        InputBoxGameInputContext* context,
        InputBoxGameInputDiagnosticsSnapshot* snapshot) noexcept
    {
        if (context == nullptr ||
            snapshot == nullptr)
        {
            return E_INVALIDARG;
        }

        SharedContextLock lock(context->lock);

        snapshot->missingReadingCount = context->missingReadingCount;
        snapshot->repeatedTimestampCount = context->repeatedTimestampCount;
        snapshot->backwardTimestampCount = context->backwardTimestampCount;
        snapshot->deviceUnavailableRefreshCount = context->deviceUnavailableRefreshCount;
        snapshot->lastReadingTimestamp = context->lastReadingTimestamp;
        snapshot->lastReadHResult = context->lastReadHResult;
        snapshot->lastReadDeviceStatus = context->lastReadDeviceStatus;
        snapshot->reserved = 0;

        return S_OK;
    }

    /**
     * @brief 設定 IGameInput::SetFocusPolicy,控制應用程式失去焦點時是否仍接收輸入。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param policy GameInputFocusPolicy 位元旗標(以 uint32_t 跨 ABI 傳遞)。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputSetFocusPolicy(
        InputBoxGameInputContext* context,
        uint32_t policy) noexcept
    {
        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        SharedContextLock lock(context->lock);

        if (context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        context->gameInput->SetFocusPolicy(static_cast<GameInputFocusPolicy>(policy));

        return S_OK;
    }

    /**
     * @brief 強制 shim 對 GameInput runtime 進行裝置重新列舉,並更新內部快取清單。
     *
     * 使用 GameInputBlockingEnumeration 取得目前所有已連線 gamepad 的同步快照,
     * 暫時註冊一個列舉用的 callback,完成後立即解除。整個流程持有 exclusive lock,
     * 避免與 polling 端讀取裝置清單競態。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @return Native HRESULT;發生例外時回傳 E_FAIL。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputRefreshDevices(
        InputBoxGameInputContext* context) noexcept
    {
        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        try
        {
            ExclusiveContextLock lock(context->lock);

            if (context->gameInput == nullptr)
            {
                return E_INVALIDARG;
            }

            DeviceCollector collector;
            GameInputCallbackToken token = 0;
            // 阻塞列舉提供 60 FPS 輪詢迴圈需要的完整快照。
            // 這個回呼註冊是暫時的，替換裝置清單前會先解除註冊。
            HRESULT hr = context->gameInput->RegisterDeviceCallback(
                nullptr,
                GameInputKindGamepad,
                GameInputDeviceConnected,
                GameInputBlockingEnumeration,
                &collector,
                OnDeviceEnumerated,
                &token);

            if (FAILED(hr))
            {
                return hr;
            }

            context->gameInput->UnregisterCallback(token);

            std::vector<DeviceEntry> refreshed;
            refreshed.reserve(collector.devices.size());

            for (const ComPtr<IGameInputDevice>& device : collector.devices)
            {
                DeviceEntry entry;
                entry.device = device;

                hr = FillDeviceInfo(entry.device.Get(), &entry.info);

                if (SUCCEEDED(hr))
                {
                    refreshed.push_back(entry);
                }
            }

            context->devices = std::move(refreshed);

            return S_OK;
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    /**
     * @brief 回傳 shim 目前所知的裝置總數(以 RefreshDevices 結果為準)。
     *
     * @param context 已建立的 context;nullptr 時回傳 0。
     * @return 裝置數;以 int32_t 跨 ABI 傳遞。
     */
    __declspec(dllexport) int32_t __stdcall InputBoxGameInputGetDeviceCount(
        InputBoxGameInputContext* context) noexcept
    {
        if (context == nullptr)
        {
            return 0;
        }

        SharedContextLock lock(context->lock);

        return static_cast<int32_t>(context->devices.size());
    }

    /**
     * @brief 依索引取得單一裝置的中繼資訊(VID/PID、displayName、capabilities、
     *        支援馬達等)。
     *
     * 索引以與 InputBoxGameInputGetDeviceCount 同一回合的計數為準;不要與下一次
     * RefreshDevices 之間共用。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param index 裝置索引(0-based);超出範圍會回傳 E_INVALIDARG。
     * @param[out] info 回傳的裝置資訊結構;不可為 nullptr。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputGetDeviceInfo(
        InputBoxGameInputContext* context,
        int32_t index,
        InputBoxGameInputDeviceInfo* info) noexcept
    {
        if (context == nullptr ||
            info == nullptr ||
            index < 0)
        {
            return E_INVALIDARG;
        }

        SharedContextLock lock(context->lock);

        if (static_cast<size_t>(index) >= context->devices.size())
        {
            return E_INVALIDARG;
        }

        *info = context->devices[static_cast<size_t>(index)].info;

        return S_OK;
    }

    /**
     * @brief 依穩定 deviceId 查詢目前裝置狀態旗標。
     *
     * 若裝置目前未連線會額外更新 deviceUnavailableRefreshCount 診斷計數,但不會
     * 自動觸發重列舉(由 managed 端 polling 邏輯依連續缺幀數決定)。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param deviceId 穩定裝置識別字串(UTF-8);可為 nullptr,此時依 shim 規則選擇預設裝置。
     * @param[out] status 回傳 GameInputDeviceStatus 旗標;不可為 nullptr。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputGetDeviceStatus(
        InputBoxGameInputContext* context,
        const char* deviceId,
        uint32_t* status) noexcept
    {
        if (status == nullptr)
        {
            return E_INVALIDARG;
        }

        *status = 0;

        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        ExclusiveContextLock lock(context->lock);

        if (context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        ComPtr<IGameInputDevice> device;
        HRESULT hr = CopyDeviceLocked(context, deviceId, &device);

        if (FAILED(hr))
        {
            return hr;
        }

        *status = static_cast<uint32_t>(device->GetDeviceStatus());

        if ((*status & GameInputDeviceConnected) == 0)
        {
            RecordDeviceUnavailableLocked(context, *status);
        }

        return S_OK;
    }

    /**
     * @brief 同步讀取指定裝置目前的 gamepad reading 快照。
     *
     * 流程:取得裝置 → 檢查 GameInputDeviceConnected → GetCurrentReading →
     * 抽取 GameInputGamepadState 並填入 InputBoxGameInputGamepadState(含 timestamp
     * 與診斷 metadata)。途中任何失敗都會更新對應的 read result 診斷計數。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param deviceId 穩定裝置識別字串(UTF-8);可為 nullptr。
     * @param[out] state 回傳的 gamepad 狀態快照;不可為 nullptr,內容會先被清零。
     * @return Native HRESULT;InputBoxGameInputNoReading 代表暫無可用 reading;
     *         ERROR_DEVICE_NOT_CONNECTED 代表裝置已斷線。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputReadGamepadState(
        InputBoxGameInputContext* context,
        const char* deviceId,
        InputBoxGameInputGamepadState* state) noexcept
    {
        if (state == nullptr)
        {
            return E_INVALIDARG;
        }

        *state = {};

        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        ExclusiveContextLock lock(context->lock);

        if (context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        ComPtr<IGameInputDevice> device;
        HRESULT hr = CopyDeviceLocked(context, deviceId, &device);

        if (FAILED(hr))
        {
            RecordReadResultLocked(context, hr, 0, 0);

            return hr;
        }

        uint32_t deviceStatus = static_cast<uint32_t>(device->GetDeviceStatus());

        if ((deviceStatus & GameInputDeviceConnected) == 0)
        {
            RecordDeviceUnavailableLocked(context, deviceStatus);

            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        ComPtr<IGameInputReading> reading;
        hr = context->gameInput->GetCurrentReading(
            GameInputKindGamepad,
            device.Get(),
            &reading);

        if (FAILED(hr) ||
            reading == nullptr)
        {
            HRESULT result = FAILED(hr) ? hr : InputBoxGameInputNoReading;
            RecordReadResultLocked(context, result, deviceStatus, 0);

            return result;
        }

        GameInputGamepadState gamepadState{};

        if (!reading->GetGamepadState(&gamepadState))
        {
            RecordReadResultLocked(context, InputBoxGameInputNoReading, deviceStatus, 0);

            return InputBoxGameInputNoReading;
        }

        FillGamepadState(reading.Get(), gamepadState, state);
        RecordReadResultLocked(context, S_OK, deviceStatus, state->timestamp);

        return S_OK;
    }

    /**
     * @brief 註冊 reading callback;GameInput runtime 在推送新 gamepad reading 時
     *        通知 managed 端喚醒 MTA polling thread。
     *
     * 註冊本身不持有 context lock 以避免 GameInput 在註冊期間呼叫回呼時造成死結;
     * 但會在 token 發布前重新檢查 context 是否仍存活,避免 destroy 與註冊競態。
     * callback 僅可用於要求重新整理或喚醒診斷路徑,不得直接觸發 UI 或輸入命令。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param deviceId 穩定裝置識別字串(UTF-8);nullptr 表示訂閱全部裝置。
     * @param kind 必須為 GameInputKindGamepad。
     * @param callback Managed 端建立、需 keep-alive 的回呼函式指標。
     * @param callbackContext 回呼時傳回的 user context(通常為 GCHandle)。
     * @param[out] callbackToken 回傳的回呼識別 token,用於 InputBoxGameInputUnregisterCallback。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputRegisterReadingCallback(
        InputBoxGameInputContext* context,
        const char* deviceId,
        uint32_t kind,
        InputBoxGameInputReadingCallback callback,
        void* callbackContext,
        uint64_t* callbackToken) noexcept
    {
        if (context == nullptr ||
            kind != static_cast<uint32_t>(GameInputKindGamepad) ||
            callback == nullptr ||
            callbackToken == nullptr)
        {
            return E_INVALIDARG;
        }

        *callbackToken = 0;

        ComPtr<IGameInput> gameInput;
        ComPtr<IGameInputDevice> device;

        {
            SharedContextLock lock(context->lock);

            if (context->gameInput == nullptr)
            {
                return E_INVALIDARG;
            }

            // 在 shared lock 內複製 COM 參考，接著不持有 context lock 進行註冊。
            // GameInput 可能在註冊期間呼叫回呼。
            gameInput = context->gameInput;
            HRESULT hr = ResolveOptionalDeviceLocked(context, deviceId, &device);

            if (FAILED(hr))
            {
                return hr;
            }
        }

        auto registration = std::make_unique<CallbackRegistration>();
        registration->readingCallback = callback;
        registration->callbackContext = callbackContext;

        GameInputCallbackToken token = 0;
        HRESULT hr = gameInput->RegisterReadingCallback(
            device.Get(),
            static_cast<GameInputKind>(kind),
            registration.get(),
            OnReadingCallback,
            &token);

        if (FAILED(hr))
        {
            return hr;
        }

        registration->token = token;
        *callbackToken = token;

        {
            ExclusiveContextLock lock(context->lock);
            if (context->gameInput == nullptr)
            {
                // 原生註冊成功後 destroy 搶先完成。這裡立即解除註冊並回報失敗，
                // 避免受控端保留這個 token。
                registration->active.store(false);
                gameInput->StopCallback(token);
                gameInput->UnregisterCallback(token);
                *callbackToken = 0;

                return E_INVALIDARG;
            }

            context->callbacks.push_back(std::move(registration));
        }

        return S_OK;
    }

    /**
     * @brief 註冊 device callback;裝置連線/斷線/狀態變更時通知 managed 端排程
     *        裝置重列舉。
     *
     * 與 RegisterReadingCallback 相同的鎖規則:註冊期間不持有 context lock,
     * 並在 token 發布前重新檢查 context 是否仍存活。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param deviceId 穩定裝置識別字串(UTF-8);nullptr 表示訂閱全部裝置。
     * @param kind 必須為 GameInputKindGamepad。
     * @param statusFilter 關注的 GameInputDeviceStatus 旗標。
     * @param enumerationKind GameInputEnumerationKind(Blocking / Async 等)。
     * @param callback Managed 端建立、需 keep-alive 的回呼函式指標。
     * @param callbackContext 回呼時傳回的 user context(通常為 GCHandle)。
     * @param[out] callbackToken 回傳的回呼識別 token。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputRegisterDeviceCallback(
        InputBoxGameInputContext* context,
        const char* deviceId,
        uint32_t kind,
        uint32_t statusFilter,
        uint32_t enumerationKind,
        InputBoxGameInputDeviceCallback callback,
        void* callbackContext,
        uint64_t* callbackToken) noexcept
    {
        if (context == nullptr ||
            kind != static_cast<uint32_t>(GameInputKindGamepad) ||
            callback == nullptr ||
            callbackToken == nullptr)
        {
            return E_INVALIDARG;
        }

        *callbackToken = 0;

        ComPtr<IGameInput> gameInput;
        ComPtr<IGameInputDevice> device;

        {
            SharedContextLock lock(context->lock);

            if (context->gameInput == nullptr)
            {
                return E_INVALIDARG;
            }

            // 參考 RegisterReadingCallback：回呼註冊不持有 context lock，
            // 並且只在 context 仍存活時發布 token。
            gameInput = context->gameInput;
            HRESULT hr = ResolveOptionalDeviceLocked(context, deviceId, &device);

            if (FAILED(hr))
            {
                return hr;
            }
        }

        auto registration = std::make_unique<CallbackRegistration>();
        registration->deviceCallback = callback;
        registration->callbackContext = callbackContext;

        GameInputCallbackToken token = 0;
        HRESULT hr = gameInput->RegisterDeviceCallback(
            device.Get(),
            static_cast<GameInputKind>(kind),
            static_cast<GameInputDeviceStatus>(statusFilter),
            static_cast<GameInputEnumerationKind>(enumerationKind),
            registration.get(),
            OnDeviceCallback,
            &token);

        if (FAILED(hr))
        {
            return hr;
        }

        registration->token = token;
        *callbackToken = token;

        {
            ExclusiveContextLock lock(context->lock);
            if (context->gameInput == nullptr)
            {
                // context 銷毀與註冊發生競態；回傳前先停止並解除註冊。
                registration->active.store(false);
                gameInput->StopCallback(token);
                gameInput->UnregisterCallback(token);
                *callbackToken = 0;

                return E_INVALIDARG;
            }

            context->callbacks.push_back(std::move(registration));
        }

        return S_OK;
    }

    /**
     * @brief 註銷先前以 RegisterReadingCallback 或 RegisterDeviceCallback 取得的
     *        callback token。
     *
     * 流程:標記 registration 為非 active(讓正在執行的回呼盡快返回) → StopCallback
     * → UnregisterCallback → 從 context 移除註冊紀錄。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param callbackToken 先前回傳的 callback token(非 0)。
     * @return Native HRESULT;找不到對應註冊時回傳 HRESULT_FROM_WIN32(ERROR_NOT_FOUND)。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputUnregisterCallback(
        InputBoxGameInputContext* context,
        uint64_t callbackToken) noexcept
    {
        if (context == nullptr ||
            callbackToken == 0)
        {
            return E_INVALIDARG;
        }

        ExclusiveContextLock lock(context->lock);

        if (context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        auto found = std::find_if(
            context->callbacks.begin(),
            context->callbacks.end(),
            [callbackToken](const std::unique_ptr<CallbackRegistration>& registration)
            {
                return registration != nullptr &&
                    registration->token == callbackToken;
            });

        if (found == context->callbacks.end())
        {
            return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        }

        (*found)->active.store(false);
        context->gameInput->StopCallback(callbackToken);
        context->gameInput->UnregisterCallback(callbackToken);
        context->callbacks.erase(found);

        return S_OK;
    }

    /**
     * @brief 套用震動參數到指定裝置;四個馬達強度均為 [0.0, 1.0] 正規化值。
     *
     * 不支援的馬達會被 GameInput runtime 忽略(例如 PC 控制器多半無 trigger 馬達);
     * 由 managed 端依 GameInputRumbleMotors 旗標決定是否傳遞非零值。
     *
     * @param context 已建立的 context;不可為 nullptr。
     * @param deviceId 穩定裝置識別字串(UTF-8);可為 nullptr。
     * @param lowFrequency 低頻主馬達強度。
     * @param highFrequency 高頻主馬達強度。
     * @param leftTrigger 左扳機馬達強度(不支援時忽略)。
     * @param rightTrigger 右扳機馬達強度(不支援時忽略)。
     * @return Native HRESULT。
     */
    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputSetRumbleState(
        InputBoxGameInputContext* context,
        const char* deviceId,
        float lowFrequency,
        float highFrequency,
        float leftTrigger,
        float rightTrigger) noexcept
    {
        if (context == nullptr)
        {
            return E_INVALIDARG;
        }

        SharedContextLock lock(context->lock);

        if (context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        ComPtr<IGameInputDevice> device;
        HRESULT hr = CopyDeviceLocked(context, deviceId, &device);

        if (FAILED(hr))
        {
            return hr;
        }

        GameInputRumbleParams rumble
        {
            lowFrequency,
            highFrequency,
            leftTrigger,
            rightTrigger
        };

        device->SetRumbleState(&rumble);

        return S_OK;
    }
}
