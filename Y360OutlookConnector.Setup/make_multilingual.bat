:: https://www.hass.de/content/how-create-msi-packages-multilingual-user-interface-mui
:: https://www.geektieguy.com/2010/03/13/create-a-multi-lingual-multi-language-msi-using-wix-and-custom-build-scripts/

@echo off

set TargetDir=%~f1
set DevEnvDir=C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\Common7\IDE\

echo Creating a multilingual msi installation package
cd "%TargetDir%"

call "%DevEnvDir%..\Tools\VsDevCmd.bat" >nul
echo Path to WinSDK binaries: %WindowsSdkVerBinPath%

echo|set /p="Generating transfrom from en-US to ru-RU... " 
cscript "%WindowsSdkVerBinPath%x86\wilangid.vbs" //B "ru-RU\Y360ConnectorSetup_x86.msi" Product 1049
"%WindowsSdkVerBinPath%x86\MsiTran.exe" -g "en-US\Y360ConnectorSetup_x86.msi" "ru-RU\Y360ConnectorSetup_x86.msi" ru-RU.mst
echo.

echo|set /p="Adding transform to msi substorage... "
copy /B /Y "en-US\Y360ConnectorSetup_x86.msi" Y360ConnectorSetup_x86.msi >nul
cscript "%WindowsSdkVerBinPath%x86\wisubstg.vbs" //B Y360ConnectorSetup_x86.msi ru-RU.mst 1049
echo Done

echo|set /p="Updating msi package languages... "
cscript "%WindowsSdkVerBinPath%x86\wilangid.vbs" //B Y360ConnectorSetup_x86.msi Package 1033,1049
echo Done

