@echo off
rem Raccourci vers la sonde FanOMax (compilée en Release). Exemples :
rem   probe inventory
rem   probe shadow-report --days 3
"%~dp0src\FanOMax.Probe\bin\Release\net10.0-windows\FanOMax.Probe.exe" %*
