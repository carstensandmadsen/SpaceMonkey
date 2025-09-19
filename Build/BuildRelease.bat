@echo off
cd /d "%~dp0"
rem ------------------------------------------------------------
rem Configuration
rem ------------------------------------------------------------
set SOLUTION="../SpaceMonkeyTP/SpaceMonkeyTP.sln"
rem Update the MSBUILD path if needed:
rem set MSBUILD="C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"

rem ------------------------------------------------------------
rem Clean and Build for Any CPU Release
rem ------------------------------------------------------------
echo Cleaning x64 Release...
rem %MSBUILD% %SOLUTION% /t:Clean /p:Configuration=Release /p:Platform="Any CPU"
dotnet clean %SOLUTION% -c Release
echo Building x64 Release...
rem %MSBUILD% %SOLUTION% /t:Build /p:Configuration=Release /p:Platform="Any CPU"
dotnet build %SOLUTION% -c Release

echo Build steps completed successfully.
exit /b 0

