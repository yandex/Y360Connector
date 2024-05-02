#include "stdafx.h"
#include "OutlookUtils.h"

#include <shellapi.h>
#include <RestartManager.h>
#include <vector>

#pragma comment (lib, "Rstrtmgr.lib")

namespace OutlookUtils {

std::wstring GetOutlookExecutablePath()
{
    HKEY hKey = NULL;
    LRESULT result = ::RegOpenKeyEx(HKEY_LOCAL_MACHINE,
        L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\OUTLOOK.EXE", 0, KEY_READ, &hKey);
    if (result != ERROR_SUCCESS)
        return std::wstring();

    wchar_t buffer[MAX_PATH] = { 0 };
    DWORD size = sizeof(buffer);
    DWORD type = REG_SZ;
    result = ::RegQueryValueEx(hKey, L"", 0, &type, reinterpret_cast<LPBYTE>(buffer), &size);
    if (result != ERROR_SUCCESS)
        return std::wstring();

    return buffer;
}

int LaunchOutlook()
{
    return reinterpret_cast<int>(ShellExecute(NULL, NULL, L"outlook.exe", L"/recycle", NULL, SW_SHOWNORMAL));
}

std::unique_ptr<OutlookRestartSession> OutlookRestartSession::Create()
{
    auto outlookPath = GetOutlookExecutablePath();
    if (outlookPath.empty())
        return nullptr;

    WCHAR sessionKey[CCH_RM_SESSION_KEY + 1] = { 0 };
    DWORD sessionHandle = 0;
    DWORD rc = RmStartSession(&sessionHandle, 0, sessionKey);
    if (rc != ERROR_SUCCESS)
        return nullptr;

    std::vector<const wchar_t*> filesList = { outlookPath.data() };
    rc = RmRegisterResources(sessionHandle, filesList.size(), &filesList[0], 0, nullptr, 0, nullptr);
    if (rc != ERROR_SUCCESS)
    {
        ::RmEndSession(sessionHandle);
        return nullptr;
    }

    return std::unique_ptr<OutlookRestartSession>(new OutlookRestartSession(sessionHandle));
}

OutlookRestartSession::OutlookRestartSession(DWORD rmSessionHandle)
    : _rmSessionHandle(rmSessionHandle)
{
}


OutlookRestartSession::~OutlookRestartSession()
{
    Close();
}

bool OutlookRestartSession::IsOutlookRunning() const
{
    UINT processInfoCount = 0;
    UINT processInfoNeeded = 0;
    DWORD rebootReason = 0;

    DWORD rc = ::RmGetList(_rmSessionHandle, &processInfoNeeded, &processInfoCount, nullptr, &rebootReason);
    if (rc != ERROR_MORE_DATA && rc != ERROR_SUCCESS)
        return false;

    return (processInfoNeeded > 0);
}

void OutlookRestartSession::RestartOutlook()
{
    DWORD rc = RmShutdown(_rmSessionHandle, RmForceShutdown, NULL);
    if (rc == ERROR_SUCCESS)
        RmRestart(_rmSessionHandle, 0, NULL);
}

void OutlookRestartSession::ShutdownOutlook()
{
    RmShutdown(_rmSessionHandle, RmForceShutdown, NULL);
}

void OutlookRestartSession::Close()
{
    if (_rmSessionHandle != 0xffffffff)
        ::RmEndSession(_rmSessionHandle);
}

} // namespace OutlookUtils
