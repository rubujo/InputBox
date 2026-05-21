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
    constexpr uint32_t InputBoxGameInputShimAbiVersion = 2;
    constexpr uint32_t InputBoxGameInputMaxExtraControlIndexes = 32;

    enum InputBoxGameInputModuleKind : uint32_t
    {
        InputBoxGameInputModuleUnknown = 0,
        InputBoxGameInputModuleSystemGameInput = 1,
        InputBoxGameInputModuleSystemGameInputRedist = 2,
        InputBoxGameInputModuleRegistryGameInputRedist = 3
    };

    struct InputBoxGameInputVersion
    {
        uint16_t major;
        uint16_t minor;
        uint16_t build;
        uint16_t revision;
    };

    struct InputBoxGameInputShimInfo
    {
        uint32_t abiVersion;
        uint32_t gameInputApiVersion;
        uint32_t loadedModuleKind;
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
    };

    using GameInputInitializeFn = HRESULT(WINAPI*)(
        _In_ REFIID riid,
        _COM_Outptr_ LPVOID* ppv);

    void CopyUtf8(
        char* destination,
        size_t destinationLength,
        const char* value) noexcept
    {
        if (destinationLength == 0)
        {
            return;
        }

        const char* source = value != nullptr ? value : "";
        strncpy_s(destination, destinationLength, source, _TRUNCATE);
    }

    void CopyWideAsUtf8(
        char* destination,
        size_t destinationLength,
        const wchar_t* value) noexcept
    {
        if (destinationLength == 0)
        {
            return;
        }

        destination[0] = '\0';

        if (value == nullptr ||
            value[0] == L'\0')
        {
            return;
        }

        int written = WideCharToMultiByte(
            CP_UTF8,
            0,
            value,
            -1,
            destination,
            static_cast<int>(destinationLength),
            nullptr,
            nullptr);

        if (written == 0)
        {
            destination[0] = '\0';
        }
    }

    void CopyDeviceId(
        char* destination,
        size_t destinationLength,
        const APP_LOCAL_DEVICE_ID& deviceId) noexcept
    {
        if (destinationLength < 2)
        {
            return;
        }

        destination[0] = '\0';

        const auto* bytes = reinterpret_cast<const uint8_t*>(&deviceId);
        const size_t byteCount = std::min(sizeof(APP_LOCAL_DEVICE_ID), (destinationLength - 1) / 2);

        for (size_t i = 0; i < byteCount; i++)
        {
            sprintf_s(destination + (i * 2), destinationLength - (i * 2), "%02X", bytes[i]);
        }
    }

    void CopyGuid(
        char* destination,
        size_t destinationLength,
        const GUID& value) noexcept
    {
        wchar_t buffer[39]{};
        int written = StringFromGUID2(value, buffer, static_cast<int>(std::size(buffer)));

        if (written <= 0)
        {
            CopyUtf8(destination, destinationLength, "");

            return;
        }

        CopyWideAsUtf8(destination, destinationLength, buffer);
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
        CopyDeviceId(destination->deviceId, sizeof(destination->deviceId), info->deviceId);
        CopyDeviceId(destination->deviceRootId, sizeof(destination->deviceRootId), info->deviceRootId);
        CopyGuid(destination->containerId, sizeof(destination->containerId), info->containerId);
        CopyUtf8(destination->displayName, sizeof(destination->displayName), info->displayName);
        CopyUtf8(destination->pnpPath, sizeof(destination->pnpPath), info->pnpPath);

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
        GameInputInitializeFn* initialize) noexcept
    {
        *module = nullptr;
        *initialize = nullptr;

        HMODULE candidate = LoadLibraryExW(path, nullptr, flags);

        if (candidate == nullptr)
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        FARPROC proc = GetProcAddress(candidate, "GameInputInitialize");

        if (proc == nullptr)
        {
            FreeLibrary(candidate);

            return HRESULT_FROM_WIN32(GetLastError());
        }

        *module = candidate;
        *initialize = reinterpret_cast<GameInputInitializeFn>(proc);

        return S_OK;
    }

    HRESULT TryLoadRedistFromRegistry(
        HMODULE* module,
        GameInputInitializeFn* initialize) noexcept
    {
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

        std::wstring path(redistDir.data());

        if (!path.empty() &&
            path.back() != L'\\')
        {
            path += L'\\';
        }

        path += L"GameInputRedist.dll";

        return TryLoadGameInputModule(path.c_str(), 0, module, initialize);
    }

    HRESULT LoadGameInput(
        HMODULE* module,
        uint32_t* moduleKind,
        char* modulePath,
        size_t modulePathLength,
        IGameInput** gameInput) noexcept
    {
        *module = nullptr;
        *moduleKind = InputBoxGameInputModuleUnknown;
        CopyUtf8(modulePath, modulePathLength, "");
        *gameInput = nullptr;

        GameInputInitializeFn initialize = nullptr;
        HRESULT hr = TryLoadGameInputModule(
            L"GameInput.dll",
            LOAD_LIBRARY_SEARCH_SYSTEM32,
            module,
            &initialize);

        if (SUCCEEDED(hr))
        {
            *moduleKind = InputBoxGameInputModuleSystemGameInput;
        }

        if (FAILED(hr))
        {
            hr = TryLoadGameInputModule(
                L"GameInputRedist.dll",
                LOAD_LIBRARY_SEARCH_SYSTEM32,
                module,
                &initialize);

            if (SUCCEEDED(hr))
            {
                *moduleKind = InputBoxGameInputModuleSystemGameInputRedist;
            }
        }

        if (FAILED(hr))
        {
            hr = TryLoadRedistFromRegistry(module, &initialize);

            if (SUCCEEDED(hr))
            {
                *moduleKind = InputBoxGameInputModuleRegistryGameInputRedist;
            }
        }

        if (FAILED(hr))
        {
            return hr;
        }

        std::array<wchar_t, 512> path{};
        DWORD pathLength = GetModuleFileNameW(*module, path.data(), static_cast<DWORD>(path.size()));
        if (pathLength > 0)
        {
            CopyWideAsUtf8(modulePath, modulePathLength, path.data());
        }

        hr = initialize(IID_IGameInput, reinterpret_cast<void**>(gameInput));

        if (FAILED(hr))
        {
            FreeLibrary(*module);
            *module = nullptr;
            *moduleKind = InputBoxGameInputModuleUnknown;
            CopyUtf8(modulePath, modulePathLength, "");
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

    HRESULT ResolveOptionalDevice(
        InputBoxGameInputContext* context,
        const char* deviceId,
        IGameInputDevice** device) noexcept
    {
        if (device == nullptr)
        {
            return E_INVALIDARG;
        }

        *device = nullptr;

        if (deviceId == nullptr ||
            deviceId[0] == '\0')
        {
            return S_OK;
        }

        DeviceEntry* entry = FindDevice(context, deviceId);

        if (entry == nullptr)
        {
            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        *device = entry->device.Get();

        return S_OK;
    }
}

extern "C"
{
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

    __declspec(dllexport) void __stdcall InputBoxGameInputDestroy(
        InputBoxGameInputContext* context) noexcept
    {
        if (context == nullptr)
        {
            return;
        }

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
        context->gameInput.Reset();

        if (context->module != nullptr)
        {
            FreeLibrary(context->module);
            context->module = nullptr;
        }

        delete context;
    }

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputGetShimInfo(
        InputBoxGameInputContext* context,
        InputBoxGameInputShimInfo* info) noexcept
    {
        if (info == nullptr)
        {
            return E_INVALIDARG;
        }

        *info = {};
        info->abiVersion = InputBoxGameInputShimAbiVersion;
        info->gameInputApiVersion = GAMEINPUT_API_VERSION;

        if (context != nullptr)
        {
            info->loadedModuleKind = context->moduleKind;
            CopyUtf8(info->loadedModulePath, sizeof(info->loadedModulePath), context->modulePath);
        }

        return S_OK;
    }

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputSetFocusPolicy(
        InputBoxGameInputContext* context,
        uint32_t policy) noexcept
    {
        if (context == nullptr ||
            context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        context->gameInput->SetFocusPolicy(static_cast<GameInputFocusPolicy>(policy));

        return S_OK;
    }

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputRefreshDevices(
        InputBoxGameInputContext* context) noexcept
    {
        if (context == nullptr ||
            context->gameInput == nullptr)
        {
            return E_INVALIDARG;
        }

        try
        {
            DeviceCollector collector;
            GameInputCallbackToken token = 0;
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

    __declspec(dllexport) int32_t __stdcall InputBoxGameInputGetDeviceCount(
        InputBoxGameInputContext* context) noexcept
    {
        if (context == nullptr)
        {
            return 0;
        }

        return static_cast<int32_t>(context->devices.size());
    }

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputGetDeviceInfo(
        InputBoxGameInputContext* context,
        int32_t index,
        InputBoxGameInputDeviceInfo* info) noexcept
    {
        if (context == nullptr ||
            info == nullptr ||
            index < 0 ||
            static_cast<size_t>(index) >= context->devices.size())
        {
            return E_INVALIDARG;
        }

        *info = context->devices[static_cast<size_t>(index)].info;

        return S_OK;
    }

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

        DeviceEntry* entry = FindDevice(context, deviceId);

        if (entry == nullptr)
        {
            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        *status = static_cast<uint32_t>(entry->device->GetDeviceStatus());

        return S_OK;
    }

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

        DeviceEntry* entry = FindDevice(context, deviceId);

        if (entry == nullptr)
        {
            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        if ((entry->device->GetDeviceStatus() & GameInputDeviceConnected) == 0)
        {
            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        ComPtr<IGameInputReading> reading;
        HRESULT hr = context->gameInput->GetCurrentReading(
            GameInputKindGamepad,
            entry->device.Get(),
            &reading);

        if (FAILED(hr) ||
            reading == nullptr)
        {
            return FAILED(hr) ? hr : InputBoxGameInputNoReading;
        }

        GameInputGamepadState gamepadState{};

        if (!reading->GetGamepadState(&gamepadState))
        {
            return InputBoxGameInputNoReading;
        }

        FillGamepadState(reading.Get(), gamepadState, state);

        return S_OK;
    }

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputRegisterReadingCallback(
        InputBoxGameInputContext* context,
        const char* deviceId,
        uint32_t kind,
        InputBoxGameInputReadingCallback callback,
        void* callbackContext,
        uint64_t* callbackToken) noexcept
    {
        if (context == nullptr ||
            context->gameInput == nullptr ||
            callback == nullptr ||
            callbackToken == nullptr)
        {
            return E_INVALIDARG;
        }

        *callbackToken = 0;

        IGameInputDevice* device = nullptr;
        HRESULT hr = ResolveOptionalDevice(context, deviceId, &device);

        if (FAILED(hr))
        {
            return hr;
        }

        auto registration = std::make_unique<CallbackRegistration>();
        registration->readingCallback = callback;
        registration->callbackContext = callbackContext;

        GameInputCallbackToken token = 0;
        hr = context->gameInput->RegisterReadingCallback(
            device,
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
        context->callbacks.push_back(std::move(registration));

        return S_OK;
    }

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
            context->gameInput == nullptr ||
            callback == nullptr ||
            callbackToken == nullptr)
        {
            return E_INVALIDARG;
        }

        *callbackToken = 0;

        IGameInputDevice* device = nullptr;
        HRESULT hr = ResolveOptionalDevice(context, deviceId, &device);

        if (FAILED(hr))
        {
            return hr;
        }

        auto registration = std::make_unique<CallbackRegistration>();
        registration->deviceCallback = callback;
        registration->callbackContext = callbackContext;

        GameInputCallbackToken token = 0;
        hr = context->gameInput->RegisterDeviceCallback(
            device,
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
        context->callbacks.push_back(std::move(registration));

        return S_OK;
    }

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputUnregisterCallback(
        InputBoxGameInputContext* context,
        uint64_t callbackToken) noexcept
    {
        if (context == nullptr ||
            context->gameInput == nullptr ||
            callbackToken == 0)
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

    __declspec(dllexport) HRESULT __stdcall InputBoxGameInputSetRumbleState(
        InputBoxGameInputContext* context,
        const char* deviceId,
        float lowFrequency,
        float highFrequency,
        float leftTrigger,
        float rightTrigger) noexcept
    {
        DeviceEntry* entry = FindDevice(context, deviceId);

        if (entry == nullptr)
        {
            return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        }

        GameInputRumbleParams rumble
        {
            lowFrequency,
            highFrequency,
            leftTrigger,
            rightTrigger
        };

        entry->device->SetRumbleState(&rumble);

        return S_OK;
    }
}
