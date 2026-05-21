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
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <new>
#include <string>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;
using namespace GameInput::v3;

namespace
{
    const HRESULT InputBoxGameInputNoReading = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);

    struct InputBoxGameInputDeviceInfo
    {
        uint16_t vendorId;
        uint16_t productId;
        uint32_t supportedRumbleMotors;
        char deviceId[65];
        char displayName[256];
    };

    struct InputBoxGameInputGamepadState
    {
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

    struct InputBoxGameInputContext
    {
        HMODULE module = nullptr;
        ComPtr<IGameInput> gameInput;
        std::vector<DeviceEntry> devices;
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

        destination->vendorId = info->vendorId;
        destination->productId = info->productId;
        destination->supportedRumbleMotors = static_cast<uint32_t>(info->supportedRumbleMotors);
        CopyDeviceId(destination->deviceId, sizeof(destination->deviceId), info->deviceId);
        CopyUtf8(destination->displayName, sizeof(destination->displayName), info->displayName);

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
        IGameInput** gameInput) noexcept
    {
        *module = nullptr;
        *gameInput = nullptr;

        GameInputInitializeFn initialize = nullptr;
        HRESULT hr = TryLoadGameInputModule(
            L"GameInput.dll",
            LOAD_LIBRARY_SEARCH_SYSTEM32,
            module,
            &initialize);

        if (FAILED(hr))
        {
            hr = TryLoadGameInputModule(
                L"GameInputRedist.dll",
                LOAD_LIBRARY_SEARCH_SYSTEM32,
                module,
                &initialize);
        }

        if (FAILED(hr))
        {
            hr = TryLoadRedistFromRegistry(module, &initialize);
        }

        if (FAILED(hr))
        {
            return hr;
        }

        hr = initialize(IID_IGameInput, reinterpret_cast<void**>(gameInput));

        if (FAILED(hr))
        {
            FreeLibrary(*module);
            *module = nullptr;
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

        HRESULT hr = LoadGameInput(&created->module, &created->gameInput);

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

        context->devices.clear();
        context->gameInput.Reset();

        if (context->module != nullptr)
        {
            FreeLibrary(context->module);
            context->module = nullptr;
        }

        delete context;
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

        state->buttons = static_cast<uint32_t>(gamepadState.buttons);
        state->leftTrigger = gamepadState.leftTrigger;
        state->rightTrigger = gamepadState.rightTrigger;
        state->leftThumbstickX = gamepadState.leftThumbstickX;
        state->leftThumbstickY = gamepadState.leftThumbstickY;
        state->rightThumbstickX = gamepadState.rightThumbstickX;
        state->rightThumbstickY = gamepadState.rightThumbstickY;

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
