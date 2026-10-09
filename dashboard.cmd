@echo off
rem Tableau de bord FanOMax (lecture seule du journal du service, sans droits admin).
rem Compile si besoin, puis lance l'interface sans garder la console ouverte.
dotnet build "%~dp0src\FanOMax.App" -c Release -v quiet -nologo || exit /b 1
start "" "%~dp0src\FanOMax.App\bin\Release\net10.0-windows\FanOMax.App.exe"
