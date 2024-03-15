#pragma once

#include <string>
#include <memory>

namespace OutlookUtils {

    std::wstring GetOutlookExecutablePath();

    int LaunchOutlook();

    class OutlookRestartSession
    {
    public:
        static std::unique_ptr<OutlookRestartSession> Create();
        ~OutlookRestartSession();

        bool IsOutlookRunning() const;
        void RestartOutlook();
        void ShutdownOutlook();
        void Close();

    private:
        DWORD _rmSessionHandle = 0xffffffff;

        OutlookRestartSession(DWORD rmSessionHandle);
    };

} // namespace OutlookUtils
