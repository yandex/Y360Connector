#include "stdafx.h"

#include "OutlookUtils.h"


UINT WINAPI CheckOutlookRunningCA(MSIHANDLE hInstall)
{
    HRESULT hr = S_OK;
    UINT er = ERROR_SUCCESS;

    hr = WcaInitialize(hInstall, "CheckOutlookIsRunnning");
    if (FAILED(hr)) {
        WcaLogError(hr, "Failed to initialize");
        return WcaFinalize(ERROR_INSTALL_FAILURE);
    }

    auto restartSession = OutlookUtils::OutlookRestartSession::Create();
    if (restartSession != nullptr && restartSession->IsOutlookRunning()) {
        MsiSetProperty(hInstall, L"OUTLOOK_IS_RUNING", L"1");
    }

    return WcaFinalize(ERROR_SUCCESS);
}


UINT WINAPI RestartOutlookCA(MSIHANDLE hInstall)
{
    HRESULT hr = S_OK;
    UINT er = ERROR_SUCCESS;

    hr = WcaInitialize(hInstall, "RestartOutlook");
    if (FAILED(hr)) {
        WcaLogError(hr, "Failed to initialize");
        return WcaFinalize(ERROR_INSTALL_FAILURE);
    }

    auto restartSession = OutlookUtils::OutlookRestartSession::Create();
    if (restartSession != nullptr && restartSession->IsOutlookRunning()) {
        restartSession->ShutdownOutlook();
        restartSession.reset();

        OutlookUtils::LaunchOutlook();
    }

    return WcaFinalize(ERROR_SUCCESS);
}


UINT WINAPI LaunchOutlookCA(MSIHANDLE hInstall)
{
    HRESULT hr = S_OK;
    UINT er = ERROR_SUCCESS;

    hr = WcaInitialize(hInstall, "RestartOutlook");
    if (FAILED(hr)) {
        WcaLogError(hr, "Failed to initialize");
        return WcaFinalize(ERROR_INSTALL_FAILURE);
    }

    OutlookUtils::LaunchOutlook();

    return WcaFinalize(ERROR_SUCCESS);
}


// DllMain - Initialize and cleanup WiX custom action utils.
extern "C" BOOL WINAPI DllMain(HINSTANCE hInst, ULONG ulReason, LPVOID)
{
    switch (ulReason)
    {
    case DLL_PROCESS_ATTACH:
        WcaGlobalInitialize(hInst);
        break;

    case DLL_PROCESS_DETACH:
        WcaGlobalFinalize();
        break;
    }

    return TRUE;
}
